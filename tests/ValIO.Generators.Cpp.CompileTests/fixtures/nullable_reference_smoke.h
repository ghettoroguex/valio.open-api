#pragma once

#include <type_traits>
#include "Models/NullableReferences.h"
#include "Serializers/NullableReferencesJson.h"
#include "Runtime/ValIOJson.h"

// The contract is parsed from OpenAPI; none of these types is manufactured in the fixture IR.
inline int RunNullableReferenceSmoke()
{
    using Petstore::NullableCountry;
    using Petstore::NullableReferences;
    using Petstore::NullableState;
    static_assert(std::is_same_v<decltype(NullableReferences::Country), std::optional<NullableCountry>>);
    static_assert(std::is_same_v<decltype(NullableReferences::State), std::optional<NullableState>>);
    static_assert(std::is_same_v<decltype(NullableReferences::OptionalCountry), std::optional<std::optional<NullableCountry>>>);
    static_assert(std::is_same_v<decltype(NullableReferences::OptionalState), std::optional<std::optional<NullableState>>>);
    const auto allNull = ValIO::Parse<NullableReferences>(R"({"country":null,"state":null,"optionalCountry":null,"optionalState":null,"countries":[null],"states":{"one":null}})");
    if (!allNull || allNull->Country || allNull->State || !allNull->OptionalCountry ||
        *allNull->OptionalCountry || !allNull->OptionalState || *allNull->OptionalState)
        return 1;
    const auto nullWire = nlohmann::json::parse(ValIO::Dump(*allNull));
    if (!nullWire.at("country").is_null() || !nullWire.at("optionalCountry").is_null() ||
        !nullWire.at("countries").at(0).is_null() || !nullWire.at("states").at("one").is_null())
        return 2;
    const auto values = ValIO::Parse<NullableReferences>(R"({"country":{"id":7,"name":"DE"},"state":"granted","optionalCountry":{"id":8,"name":"RU"},"optionalState":"pending","countries":[null,{"id":9,"name":"US"}],"states":{"one":"granted","two":null}})");
    if (!values || !values->Country || values->Country->Id != 7 || values->State != NullableState::Granted ||
        !values->OptionalCountry || !*values->OptionalCountry || !values->OptionalState ||
        **values->OptionalState != NullableState::Pending || values->Countries.size() != 2 ||
        values->Countries[0] || !values->Countries[1] || values->States.at("two"))
        return 3;
    const auto roundTrip = ValIO::Parse<NullableReferences>(ValIO::Dump(*values));
    if (!roundTrip || roundTrip->Country->Id != 7 || roundTrip->Countries[1]->Id != 9)
        return 4;
    const auto absent = ValIO::Parse<NullableReferences>(R"({"country":null,"state":null,"countries":[],"states":{}})");
    if (!absent || absent->OptionalCountry || absent->OptionalState)
        return 5;
    if (ValIO::Parse<NullableReferences>(R"({"state":null,"countries":[],"states":{}})") ||
        ValIO::Parse<NullableReferences>(R"({"country":"DE","state":null,"countries":[],"states":{}})") ||
        ValIO::Parse<NullableReferences>(R"({"country":null,"state":"unknown","countries":[],"states":{}})"))
        return 6;
    return 0;
}
