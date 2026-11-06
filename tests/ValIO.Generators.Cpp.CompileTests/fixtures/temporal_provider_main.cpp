#include <fstream>
#include <iostream>
#include <string>
#include <type_traits>
#include "Models/TemporalProbe.h"
#include "Serializers/TemporalProbeJson.h"
#include "Runtime/ValIOJsonTypes.h"

namespace {
int failures = 0;

void Check(const bool success, const std::string& label) {
    if (!success) { ++failures; std::cerr << "Temporal provider failure: " << label << '\n'; }
}

template<class T>
bool Rejects(const nlohmann::json& value, const ValIO::JsonStringFormat format) {
    try { (void)ValIO::ReadJson<T>(value, format); }
    catch (const nlohmann::json::exception&) { return true; }
    return false;
}

template<class T>
void RoundTrip(const nlohmann::json& values, const ValIO::JsonStringFormat format, const std::string& label) {
    for (const auto& value : values) {
        try {
            const auto parsed = ValIO::ReadJson<T>(value, format);
            Check(ValIO::WriteJson(parsed, format) == value, label + " " + value.dump());
        } catch (const std::exception& error) {
            Check(false, label + " " + value.dump() + " " + error.what());
        }
    }
}
}

int RunTemporalProviderChecks(const std::string& fixturePath) {
    using namespace ScalarProviders;
    using Format = ValIO::JsonStringFormat;
    using Date = decltype(TemporalProbe{}.DateValue);
    using Time = decltype(TemporalProbe{}.TimeValue);
    using Timestamp = decltype(TemporalProbe{}.Timestamp);
    using Interval = decltype(TemporalProbe{}.Interval);
    using TimeSpan = decltype(TemporalProbe{}.TimeSpan);
    std::ifstream stream(fixturePath);
    const auto fixture = nlohmann::json::parse(stream);
    try {
        const auto model = ValIO::ReadJson<TemporalProbe>(fixture.at("model"));
        Check(ValIO::WriteJson(model) == fixture.at("model"), "formatted model, nullable arrays, dictionary, optional null");
        Check(!model.OptionalTime.has_value(), "omitted optional time remains absent");
        Check(model.OptionalDate.has_value() && !model.OptionalDate->has_value(), "explicit null remains distinct from absent property");
        auto present = fixture.at("model");
        present["optional_time"] = "09:10:11Z";
        const auto withTime = ValIO::ReadJson<TemporalProbe>(present);
        Check(withTime.OptionalTime.has_value(), "present optional time is decoded");
        Check(ValIO::WriteJson(withTime) == present, "present optional time round-trips");
    } catch (const std::exception& error) { Check(false, std::string("model codec ") + error.what()); }
    for (const auto& date : fixture.at("dates")) {
#if VALIO_TEMPORAL_BACKEND == 3
        if (date == "0000-01-01") { Check(Rejects<Date>(date, Format::Date), "Boost rejects year outside actual calendar range"); continue; }
#endif
        RoundTrip<Date>(nlohmann::json::array({date}), Format::Date, "date");
    }
    RoundTrip<Time>(fixture.at("times"), Format::Time, "time offset and exact fraction");
    RoundTrip<Timestamp>(fixture.at("timestamps"), Format::DateTime, "timestamp calendar range and lower-case syntax");
    RoundTrip<Interval>(fixture.at("durations"), Format::Duration, "duration");
    RoundTrip<TimeSpan>(fixture.at("timeSpans"), Format::TimeSpan, "time-span");
    for (const auto& value : fixture.at("invalidDates")) Check(Rejects<Date>(value, Format::Date), "invalid date " + value.dump());
    for (const auto& value : fixture.at("invalidTimes")) Check(Rejects<Time>(value, Format::Time), "invalid time " + value.dump());
    for (const auto& value : fixture.at("invalidDurations")) Check(Rejects<Interval>(value, Format::Duration), "invalid duration " + value.dump());
    for (const auto& value : fixture.at("invalidTimeSpans")) Check(Rejects<TimeSpan>(value, Format::TimeSpan), "invalid time-span " + value.dump());
    Check(Rejects<Date>(123, Format::Date), "date requires JSON string");
    Check(Rejects<Time>(true, Format::Time), "time requires JSON string");
    Check(Rejects<Interval>(nullptr, Format::Duration), "required interval rejects null");
    auto invalid = fixture.at("model");
    invalid["dates"][0] = "2023-02-29";
    Check(Rejects<TemporalProbe>(invalid, Format::Plain), "nested nullable date array validates each value");
    invalid = fixture.at("model"); invalid["times"]["start"] = 12;
    Check(Rejects<TemporalProbe>(invalid, Format::Plain), "nested dictionary rejects nonstring temporal leaf");
    invalid = fixture.at("model"); invalid.erase("date");
    Check(Rejects<TemporalProbe>(invalid, Format::Plain), "required date presence");
#if VALIO_TEMPORAL_BACKEND == 0 || VALIO_TEMPORAL_BACKEND == 4
    RoundTrip<Time>(nlohmann::json::array({"23:59:60Z", "12:30:45.12345678901234567890Z"}), Format::Time, "lossless leap second and arbitrary fractions");
    RoundTrip<Interval>(nlohmann::json::array({"P1Y2M3DT4H5M6.12345678901234567890S"}), Format::Duration, "calendar years/months stay calendar units");
#else
    Check(Rejects<Time>("23:59:60Z", Format::Time), "native clock cannot silently normalize leap second");
    Check(Rejects<Time>("12:30:45.1234567891Z", Format::Time), "native clock rejects discarded fraction digits");
    Check(Rejects<Interval>("P1Y", Format::Duration), "fixed duration rejects calendar year");
    Check(Rejects<Interval>("P1M", Format::Duration), "fixed duration rejects calendar month");
    Check(Rejects<Interval>("P9999999999999999999D", Format::Duration), "fixed duration rejects overflow");
#endif
#if VALIO_TEMPORAL_BACKEND == 3
    if (ValIO::NativeTime::ticks_per_second() == 1000000) {
        Check(Rejects<Time>("12:30:45.1234567Z", Format::Time), "Boost microseconds reject submicrosecond loss");
        Check(Rejects<Interval>("PT0.1234567S", Format::Duration), "Boost interval precision check");
    }
#endif
#if VALIO_TEMPORAL_BACKEND == 0
    RoundTrip<Interval>(nlohmann::json::array({"P999999999999999999999999999999Y"}), Format::Duration, "validated strings preserve arbitrary lexical component range");
#endif
    return failures;
}
