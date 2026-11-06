// Scalar-only client checks expose missing native provider includes and unchecked wire formatting.
#include "Clients/ProviderScalarsClient.h"
#include "Clients/ProviderFormsClient.h"
#include "Models/TemporalProbe.h"
#include "Models/UuidProbe.h"
#include "Runtime/ValIOJson.h"
#include <httplib.h>
#include <cstdio>
#include <string>
#include <thread>
#include <vector>

/// @brief Exercises scalar and collection values through generated HTTP path, query, header, and form codecs.
/// @return The number of failed acceptance checks.
int RunProviderClientChecks()
{
    int failures = 0;
    const auto check = [&](const bool valid, const char* description) {
        std::printf("%s: %s\n", valid ? "ok" : "FAIL", description);
        if (!valid) ++failures;
    };
    const std::string uuidText = "00112233-4455-6677-8899-aabbccddeeff";
    const std::string dateText = "2024-02-29";
    const std::string timeText = "09:10:11+02:30";
    const std::string timestampText = "2024-02-29T09:10:11+02:30";
    const std::string intervalText = "PT1H2M3S";
    const std::string timeSpanText = "01:02:03";
    using UuidValue = decltype(ScalarProviders::UuidProbe{}.Id);
    using DateValue = decltype(ScalarProviders::TemporalProbe{}.DateValue);
    using TimeValue = decltype(ScalarProviders::TemporalProbe{}.TimeValue);
    using TimestampValue = decltype(ScalarProviders::TemporalProbe{}.Timestamp);
    using IntervalValue = decltype(ScalarProviders::TemporalProbe{}.Interval);
    using TimeSpanValue = decltype(ScalarProviders::TemporalProbe{}.TimeSpan);
    const auto uuid = ValIO::ReadJson<UuidValue>(nlohmann::json(uuidText), ValIO::JsonStringFormat::Uuid);
    const auto date = ValIO::ReadJson<DateValue>(nlohmann::json(dateText), ValIO::JsonStringFormat::Date);
    const auto time = ValIO::ReadJson<TimeValue>(nlohmann::json(timeText), ValIO::JsonStringFormat::Time);
    const auto timestamp = ValIO::ReadJson<TimestampValue>(nlohmann::json(timestampText), ValIO::JsonStringFormat::DateTime);
    const auto interval = ValIO::ReadJson<IntervalValue>(nlohmann::json(intervalText), ValIO::JsonStringFormat::Duration);
    const auto timeSpan = ValIO::ReadJson<TimeSpanValue>(nlohmann::json(timeSpanText), ValIO::JsonStringFormat::TimeSpan);
    const std::string nextUuidText = "ffeeddcc-bbaa-9988-7766-554433221100";
    const auto nextUuid = ValIO::ReadJson<UuidValue>(nlohmann::json(nextUuidText), ValIO::JsonStringFormat::Uuid);
    const std::string nextDateText = "2024-03-01";
    const std::vector<UuidValue> uuids{uuid, nextUuid};
    const std::vector<DateValue> dates{date, ValIO::ReadJson<DateValue>(nlohmann::json(nextDateText), ValIO::JsonStringFormat::Date)};
    const std::string expectedUuids = uuidText + "," + nextUuidText;
    const std::string expectedDates = dateText + "," + nextDateText;
    const std::string expectedItems = nlohmann::json{{"name", "alpha"}}.dump() + "," + nlohmann::json{{"name", "beta"}}.dump();
    const std::string expectedMaps = nlohmann::json{{"first", uuidText}}.dump() + "," + nlohmann::json{{"second", nextUuidText}}.dump();
    httplib::Server server;
    server.Get(R"(/provider/.*)", [&](const httplib::Request& request, httplib::Response& response) {
        const bool valid = request.path == "/provider/" + uuidText + "/" + dateText
            && request.get_param_value("time") == timeText
            && request.get_param_value("timestamp") == timestampText
            && request.get_param_value("interval") == intervalText
            && request.get_param_value("time-span") == timeSpanText
            && request.get_header_value("X-UUID") == uuidText
            && request.get_header_value("X-Date") == dateText
            && request.get_header_value("X-Time") == timeText
            && request.get_header_value("X-Timestamp") == timestampText
            && request.get_header_value("X-Interval") == intervalText
            && request.get_header_value("X-Time-Span") == timeSpanText;
        response.set_content(valid ? "true" : "false", "application/json");
    });
    server.Post("/provider/form", [&](const httplib::Request& request, httplib::Response& response) {
        const bool valid = request.get_header_value("Content-Type") == "application/x-www-form-urlencoded"
            && request.target == "/provider/form?q_uuids=" + uuidText + "&q_uuids=" + nextUuidText
                + "&q_dates=" + dateText + "&q_dates=" + nextDateText
            && request.get_param_value("uuids") == expectedUuids && request.get_param_value("dates") == expectedDates
            && request.get_param_value("items") == expectedItems && request.get_param_value("maps") == expectedMaps
            && request.get_param_value_count("q_uuids") == 2
            && request.get_param_value("q_uuids", 0) == uuidText && request.get_param_value("q_uuids", 1) == nextUuidText
            && request.get_param_value_count("q_dates") == 2
            && request.get_param_value("q_dates", 0) == dateText && request.get_param_value("q_dates", 1) == nextDateText
            && request.get_header_value("X-Uuids") == expectedUuids && request.get_header_value("X-Dates") == expectedDates;
        if (!valid) {
            std::printf("FORM MISMATCH target='%s' contentType='%s' body='%s'\n", request.target.c_str(),
                request.get_header_value("Content-Type").c_str(), request.body.c_str());
            for (const auto& parameter : request.params)
                std::printf("FORM PARAM '%s'='%s'\n", parameter.first.c_str(), parameter.second.c_str());
            std::printf("FORM HEADERS X-Uuids='%s' X-Dates='%s'\n", request.get_header_value("X-Uuids").c_str(),
                request.get_header_value("X-Dates").c_str());
            std::printf("FORM EXPECTED uuids='%s' dates='%s' items='%s' maps='%s'\n", expectedUuids.c_str(),
                expectedDates.c_str(), expectedItems.c_str(), expectedMaps.c_str());
        }
        response.set_content(valid ? "true" : "false", "application/json");
    });
    const int port = server.bind_to_any_port("127.0.0.1");
    if (port <= 0) {
        check(false, "scalar provider HTTP server binds to a local port");
        return failures;
    }
    std::thread serverThread([&] { server.listen_after_bind(); });
    ValIO::HttpConfig config;
    config.BaseUrl = "http://127.0.0.1:" + std::to_string(port);
    ScalarProviders::ProviderScalarsClient client(config);
    const auto result = client.RoundTripValues(uuid, date, time, timestamp, interval, timeSpan,
        uuid, date, time, timestamp, interval, timeSpan);
    check(result.has_value() && *result, "generated scalar-only client preserves native UUID and temporal path, query, and header values");
    ScalarProviders::ProviderFormValues form;
    form.Uuids = uuids;
    form.Dates = dates;
    form.Items = {{"alpha"}, {"beta"}};
    form.Maps.emplace_back();
    form.Maps.back().emplace("first", uuid);
    form.Maps.emplace_back();
    form.Maps.back().emplace("second", nextUuid);
    ScalarProviders::ProviderFormsClient formClient(config);
    const auto formResult = formClient.PostValues(uuids, dates, uuids, dates, form);
    if (!formResult)
        std::printf("FORM ERROR status=%d message='%s' body='%s'\n", formResult.error().StatusCode,
            formResult.error().Message.c_str(), formResult.error().Body.c_str());
    check(formResult.has_value() && *formResult,
        "generated form POST formats native UUID/date arrays and model/dictionary leaves with repeated query and CSV header values");
    server.stop();
    serverThread.join();
    return failures;
}
