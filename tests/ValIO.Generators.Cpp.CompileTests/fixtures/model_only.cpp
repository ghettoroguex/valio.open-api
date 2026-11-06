// This translation unit receives only generated model include paths and the C++ standard library.
#include "Enums/Color.h"
#include "Enums/Level.h"
#include "Enums/OrderStatus.h"
#include "Enums/Wide.h"
#include "Models/CodecProbe.h"
#include "Models/Empty.h"
#include "Models/Order.h"
#include "Models/Upload.h"
#include "Models/SingleBase.h"
#include "Models/Combined.h"

#include <type_traits>

static_assert(std::is_default_constructible_v<Petstore::Order>);
static_assert(std::is_default_constructible_v<Petstore::CodecProbe>);
static_assert(std::is_default_constructible_v<Petstore::Upload>);
static_assert(std::is_enum_v<Petstore::OrderStatus>);
static_assert(std::is_base_of_v<Petstore::NameBase, Petstore::SingleBase>);
static_assert(!std::is_base_of_v<Petstore::NameBase, Petstore::Combined>);

int main()
{
    Petstore::Order order{};
    return order.OrderId == 0 ? 0 : 1;
}
