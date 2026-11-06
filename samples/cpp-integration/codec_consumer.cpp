#include "Models/Order.h"
#include "Serializers/OrderJson.h"
#include <nlohmann/json.hpp>
#include <iostream>
#include <stdexcept>

int main()
{
    const auto source = nlohmann::json::parse(R"({
        "order_id": 42,
        "state": "new",
        "nullable_note": null,
        "optional_nullable_note": null,
        "nullable_items": ["first", null]
    })");
    auto order = source.get<Integration::Order>();
    if (order.OrderId != 42 || order.NullableNote.has_value()
        || order.OptionalNote.has_value()
        || !order.OptionalNullableNote.has_value()
        || order.OptionalNullableNote->has_value())
        throw std::runtime_error("Missing/null/value states were not preserved");

    const nlohmann::json encoded = order;
    if (encoded != source)
        throw std::runtime_error("JSON round trip changed the wire contract");

    std::cout << encoded.dump(2) << '\n';
    return 0;
}
