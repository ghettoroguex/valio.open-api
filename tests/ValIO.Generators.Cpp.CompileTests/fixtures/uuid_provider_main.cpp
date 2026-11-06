#include <algorithm>
#include <cstdio>
#include <fstream>
#include <optional>
#include <string>
#include <unordered_map>
#include <vector>
#include "Models/UuidProbe.h"
#include "Serializers/UuidProbeJson.h"
#include "Runtime/ValIOJson.h"
#if VALIO_UUID_PROVIDER
#include "Runtime/ValIOUuidJson.h"
#endif

/// @brief Verifies a real provider against strict UUID text, generated model codecs, and nested collections.
/// @param fixturePath Explicit JSON fixture containing accepted and rejected values.
/// @return The number of failed acceptance checks.
int RunUuidProviderChecks(const std::string& fixturePath)
{
    int failures = 0;
    const auto check = [&](const bool valid, const char* description) {
        std::printf("%s: %s\n", valid ? "ok" : "FAIL", description);
        if (!valid) ++failures;
    };
    std::ifstream input(fixturePath);
    check(input.good(), "explicit UUID fixture is readable");
    if (!input.good()) return failures;
    const auto fixture = nlohmann::json::parse(input);
#if VALIO_UUID_PROVIDER
    using UuidValue = ValIO::Uuid;
#else
    using UuidValue = std::string;
#endif
    for (const auto& item : fixture.at("accepted")) {
        const auto parsed = ValIO::Parse<UuidValue>(item.dump(), ValIO::JsonStringFormat::Uuid);
        check(parsed.has_value(), "canonical UUID, including nil, max, uppercase, and RFC 9562 values parses");
        if (!parsed) continue;
        auto expectedText = item.get<std::string>();
#if VALIO_UUID_PROVIDER
        std::transform(expectedText.begin(), expectedText.end(), expectedText.begin(), [](const char character) {
            return character >= 'A' && character <= 'F' ? static_cast<char>(character + ('a' - 'A')) : character;
        });
        check(ValIO::ToWireText(*parsed) == expectedText, "wire parameters use lowercase canonical provider text");
#endif
        const auto encoded = ValIO::WriteJson(*parsed, ValIO::JsonStringFormat::Uuid);
        check(encoded == expectedText, "UUID serialization preserves the complete 128-bit value");
        const auto reparsed = ValIO::ReadJson<UuidValue>(encoded, ValIO::JsonStringFormat::Uuid);
        check(reparsed == *parsed, "UUID codec round-trips without generating a replacement value");

        ScalarProviders::UuidProbe model;
        model.Id = *parsed;
        model.Ids = {*parsed, reparsed};
        model.ByName = {{"first", *parsed}};
        model.NullableId = *parsed;
        model.OptionalId = *parsed;
        model.OptionalNullableId = std::optional<UuidValue>(*parsed);
        const auto modelJson = ValIO::WriteJson(model);
        const auto decoded = ValIO::ReadJson<ScalarProviders::UuidProbe>(modelJson);
        check(decoded.Id == *parsed && decoded.Ids.size() == 2 && decoded.Ids[1] == reparsed
              && decoded.ByName.at("first") == *parsed && decoded.NullableId && *decoded.NullableId == *parsed
              && decoded.OptionalId && *decoded.OptionalId == *parsed && decoded.OptionalNullableId
              && *decoded.OptionalNullableId && **decoded.OptionalNullableId == *parsed,
              "generated UUID fields, nullable/optional wrappers, arrays, and dictionaries share the checked provider codec");
    }
    for (const auto& item : fixture.at("rejected")) {
        const auto rejected = ValIO::Parse<UuidValue>(item.dump(), ValIO::JsonStringFormat::Uuid);
        check(!rejected && rejected.error().What == ValIO::Error::Code::Parse,
              "malformed, compact, braced, trailing, null, and non-string UUID input yields a typed parse error");
        auto invalidModel = nlohmann::json::object();
        invalidModel["id"] = item;
        invalidModel["ids"] = nlohmann::json::array();
        invalidModel["by_name"] = nlohmann::json::object();
        invalidModel["nullable_id"] = nullptr;
        const auto rejectedModel = ValIO::Parse<ScalarProviders::UuidProbe>(invalidModel.dump());
        check(!rejectedModel && rejectedModel.error().What == ValIO::Error::Code::Parse,
              "nested generated UUID properties reject the same invalid values as scalar codecs");
    }
    const auto nil = ValIO::ReadJson<UuidValue>(fixture.at("accepted").at(0), ValIO::JsonStringFormat::Uuid);
    ScalarProviders::UuidProbe nullableModel;
    nullableModel.Id = nil;
    nullableModel.NullableId = std::nullopt;
    nullableModel.OptionalId = std::nullopt;
    nullableModel.OptionalNullableId = std::optional<UuidValue>{};
    const auto nullableJson = ValIO::WriteJson(nullableModel);
    check(nullableJson.at("nullable_id").is_null() && !nullableJson.contains("optional_id")
          && nullableJson.at("optional_nullable_id").is_null(), "UUID presence and explicit JSON null remain separate");
    const auto nullableDecoded = ValIO::ReadJson<ScalarProviders::UuidProbe>(nullableJson);
    check(!nullableDecoded.NullableId && !nullableDecoded.OptionalId && nullableDecoded.OptionalNullableId
          && !*nullableDecoded.OptionalNullableId, "UUID null and absence survive generated model round-trips");
    const std::vector<std::optional<UuidValue>> optionalItems{nil, std::nullopt};
    using NestedUuids = std::unordered_map<std::string, std::vector<std::optional<UuidValue>>>;
    const NestedUuids nested{{"uuids", optionalItems}};
    const auto nestedJson = ValIO::WriteJson(nested, ValIO::JsonStringFormat::Uuid);
    const auto nestedDecoded = ValIO::ReadJson<NestedUuids>(nestedJson, ValIO::JsonStringFormat::Uuid);
    check(nestedDecoded.at("uuids").size() == 2 && nestedDecoded.at("uuids")[0] == nil
          && !nestedDecoded.at("uuids")[1], "nested dictionary/array/nullable UUID values use the same recursive codec");
    return failures;
}
