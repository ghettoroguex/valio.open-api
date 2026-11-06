// Native checks use generated codecs and the same checked helper as generated HTTP clients.
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <limits>
#include <optional>
#include <string>
#include <unordered_map>
#include <vector>

#include "Models/CodecFormats.h"
#include "Runtime/ValIOJson.h"
#include "Serializers/CodecFormatsJson.h"
#include "Serializers/LevelJson.h"
#include "Serializers/OrderStatusJson.h"

namespace {

template<class Action>
bool Rejects(Action&& action) {
    try { action(); }
    catch (const nlohmann::json::exception&) { return true; }
    return false;
}

} // namespace

int RunCheckedCodecSmoke() {
    int failures = 0;
    const auto check = [&failures](const bool condition, const char* message) {
        std::printf("%s checked-codec: %s\n", condition ? "ok:  " : "FAIL:", message);
        if (!condition) ++failures;
    };
    using Json = nlohmann::json;
    using Format = ValIO::JsonStringFormat;

    check(ValIO::ReadJson<std::int32_t>(Json(2147483647)) == (std::numeric_limits<std::int32_t>::max)(), "int32 maximum is exact");
    check(ValIO::ReadJson<std::int64_t>(Json((std::numeric_limits<std::int64_t>::min)())) == (std::numeric_limits<std::int64_t>::min)(), "int64 minimum is exact");
    check(ValIO::ReadJson<std::int64_t>(Json((std::numeric_limits<std::int64_t>::max)())) == (std::numeric_limits<std::int64_t>::max)(), "int64 maximum is exact");
    check(ValIO::ReadJson<std::uint64_t>(Json((std::numeric_limits<std::uint64_t>::max)())) == (std::numeric_limits<std::uint64_t>::max)(), "uint64 maximum is exact");
    check(ValIO::ReadJson<std::int32_t>(Json(2.0)) == 2, "mathematically integral JSON decimal is accepted");
    check(Rejects([] { (void)ValIO::ReadJson<std::int32_t>(Json(1.5)); }), "fractional JSON cannot truncate to integer");
    check(Rejects([] { (void)ValIO::ReadJson<std::int32_t>(Json(2147483648LL)); }), "int32 overflow is rejected");
    check(Rejects([] { (void)ValIO::ReadJson<std::int64_t>(Json(9223372036854775808ULL)); }), "unsigned value beyond int64 is rejected");
    check(Rejects([] { (void)ValIO::ReadJson<std::int64_t>(Json(std::ldexp(1.0, 63))); }), "double boundary rounded to 2^63 is rejected");
    check(Rejects([] { (void)ValIO::ReadJson<std::uint64_t>(Json(-1)); }), "negative unsigned value is rejected");
    check(Rejects([] { (void)ValIO::ReadJson<double>(Json("2")); }), "numeric strings are not coerced");
    check(Rejects([] { (void)ValIO::ReadJson<bool>(Json(1)); }), "numbers are not coerced to booleans");
    check(Rejects([] { (void)ValIO::ReadJson<std::string>(Json(true)); }), "booleans are not coerced to strings");
    const auto escaped = Json::parse(R"("A\u0000B\u20ac\n\"\\")");
    const auto escapedText = ValIO::ReadJson<std::string>(escaped);
    check(escapedText.size() == 9 && escapedText[1] == '\0' && ValIO::WriteJson(escapedText) == escaped,
        "strings retain embedded null, Unicode, newlines, quotes, and backslashes");
    check(Rejects([] { (void)ValIO::WriteJson((std::numeric_limits<double>::infinity)()); }), "non-finite floating serialization is rejected");
    check(Rejects([] { (void)ValIO::ReadJson<std::vector<std::int32_t>>(Json::array({ 1, 2.5 })); }), "collection elements cannot truncate");
    check(Rejects([] { (void)ValIO::ReadJson<std::unordered_map<std::string, std::int32_t>>(Json{{"bad", 2147483648LL}}); }), "dictionary values cannot overflow");
    check(Rejects([] { (void)ValIO::ReadJson<std::vector<std::int32_t>>(Json::object()); }), "objects cannot masquerade as collections");

    check(ValIO::ReadJson<std::string>(Json("2024-02-29"), Format::Date) == "2024-02-29", "Gregorian leap-day text is preserved");
    check(Rejects([] { (void)ValIO::ReadJson<std::string>(Json("2023-02-29"), Format::Date); }), "invalid Gregorian leap day is rejected");
    check(Rejects([] { (void)ValIO::ReadJson<std::string>(Json("2024-04-31"), Format::Date); }), "invalid month day is rejected");
    check(ValIO::ReadJson<std::string>(Json("12:30:45.123+05:30"), Format::Time) == "12:30:45.123+05:30", "fractional time with offset is preserved");
    check(ValIO::ReadJson<std::string>(Json("2024-02-29t12:30:45z"), Format::DateTime) == "2024-02-29t12:30:45z", "RFC3339 lowercase separators are accepted");
    check(Rejects([] { (void)ValIO::ReadJson<std::string>(Json("12:30:45"), Format::Time); }), "RFC3339 time requires a UTC offset");
    check(Rejects([] { (void)ValIO::ReadJson<std::string>(Json("24:00:00Z"), Format::Time); }), "out-of-range time is rejected");
    check(Rejects([] { (void)ValIO::ReadJson<std::string>(Json("2024-02-29T12:30:45+24:00"), Format::DateTime); }), "out-of-range offset is rejected");
    check(Rejects([] { (void)ValIO::ReadJson<std::vector<std::optional<std::string>>>(Json::array({ nullptr, "2023-02-29" }), Format::Date); }), "nullable array retains calendar validation");
    check(ValIO::ReadJson<std::string>(Json("00000000-0000-0000-0000-000000000000"), Format::Uuid).size() == 36, "nil UUID is supported");
    check(Rejects([] { (void)ValIO::ReadJson<std::string>(Json("00000000-0000-0000-0000-00000000000g"), Format::Uuid); }), "invalid UUID digit is rejected");

    const std::vector<std::uint8_t> bytes { 0, 1, 255 };
    check(ValIO::ReadJson<std::vector<std::uint8_t>>(Json("AAH/"), Format::Base64) == bytes, "base64 preserves all byte values");
    check(ValIO::WriteJson(bytes, Format::Base64) == Json("AAH/"), "bytes serialize as base64 JSON string");
    check(ValIO::ReadJson<std::vector<std::uint8_t>>(Json("YQ=="), Format::Base64) == std::vector<std::uint8_t>{97}, "base64 padding decodes one byte");
    check(Rejects([] { (void)ValIO::ReadJson<std::vector<std::uint8_t>>(Json("YR=="), Format::Base64); }), "non-canonical base64 padding bits are rejected");
    check(Rejects([] { (void)ValIO::ReadJson<std::vector<std::uint8_t>>(Json("Y=Q="), Format::Base64); }), "malformed base64 alphabet and padding are rejected");
    check(Rejects([] { (void)ValIO::ReadJson<std::vector<std::uint8_t>>(Json::array({0, 1}), Format::Base64); }), "base64 schema rejects numeric array wire values");

    check(!ValIO::Parse<Petstore::Level>("2.5"), "integer enum rejects fractional values");
    check(!ValIO::Parse<Petstore::Level>("4294967298"), "integer enum rejects overflow aliasing a valid member");
    check(!ValIO::Parse<Petstore::OrderStatus>("\"unknown\""), "string enum rejects undeclared wire values");
    check(Rejects([] { (void)ValIO::Dump(static_cast<Petstore::Level>(7)); }), "undeclared enum member cannot serialize as zero");

    const Json valid = {
        {"date", "2024-02-29"}, {"time", "12:30:45Z"}, {"date_time", "2024-02-29T12:30:45.123+05:30"},
        {"uuid", "00000000-0000-0000-0000-000000000000"}, {"bytes", "AAH/"},
        {"count", 2}, {"number", 1.25}, {"enabled", true}, {"dates", Json::array({ nullptr, "2024-02-29" })},
        {"dictionary", Json{{"answer", 42}}}, {"orders", Json::array({ Json{{"order_id", 42}, {"status", "placed"}} })},
        {"levels", Json::array({2})}, {"optional_date", nullptr}, {"null_value", nullptr}, {"optional_null", nullptr},
    };
    const auto parsed = ValIO::Parse<Petstore::CodecFormats>(valid.dump());
    check(parsed && parsed->Bytes == bytes && parsed->Orders.size() == 1 && parsed->Orders[0].OrderId == 42, "generated model decodes nested models and binary data");
    check(parsed && parsed->OptionalNullableDate && !*parsed->OptionalNullableDate, "generated optional nullable property preserves present null");
    check(parsed && parsed->NullValue == nullptr && parsed->OptionalNull.has_value(), "generated pure-null properties retain their null representation");
    check(parsed && Json::parse(ValIO::Dump(*parsed)) == valid, "generated model round-trips all declared wire formats");
    for (const auto* key : { "date", "time", "date_time", "uuid", "bytes", "count", "number", "enabled", "dates", "dictionary", "orders", "levels", "null_value" }) {
        auto missing = valid;
        missing.erase(key);
        check(!ValIO::Parse<Petstore::CodecFormats>(missing.dump()), "generated model rejects a missing required field");
    }
    auto invalid = valid;
    invalid["date"] = "2023-02-29";
    check(!ValIO::Parse<Petstore::CodecFormats>(invalid.dump()), "generated model enforces scalar date format");
    invalid = valid;
    invalid["dates"] = Json::array({ nullptr, "2023-02-29" });
    check(!ValIO::Parse<Petstore::CodecFormats>(invalid.dump()), "generated model enforces nested nullable date format");
    invalid = valid;
    invalid["dictionary"] = Json{{"answer", 42.5}};
    check(!ValIO::Parse<Petstore::CodecFormats>(invalid.dump()), "generated model checks dictionary numeric elements");
    invalid = valid;
    invalid["orders"][0]["order_id"] = 42.5;
    check(!ValIO::Parse<Petstore::CodecFormats>(invalid.dump()), "generated nested model checks its own scalar fields");
    invalid = valid;
    invalid["levels"] = Json::array({2.5});
    check(!ValIO::Parse<Petstore::CodecFormats>(invalid.dump()), "generated enum collection checks scalar representation");
    invalid = valid;
    invalid["null_value"] = "null";
    check(!ValIO::Parse<Petstore::CodecFormats>(invalid.dump()), "generated pure-null schema rejects a non-null string");
    invalid = valid;
    invalid.erase("optional_date");
    invalid.erase("optional_null");
    const auto absent = ValIO::Parse<Petstore::CodecFormats>(invalid.dump());
    check(absent && !absent->OptionalNullableDate, "generated optional nullable property preserves absence");
    check(absent && !absent->OptionalNull, "generated optional pure-null property preserves absence");
    return failures;
}
