#include "Models/Order.h"
#include <type_traits>

static_assert(std::is_default_constructible_v<Integration::Order>);

int main()
{
    Integration::Order order{};
    order.OrderId = 42;
    order.State = Integration::OrderState::New;
    return order.OrderId == 42 ? 0 : 1;
}
