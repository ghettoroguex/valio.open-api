#include "Models/UuidProbe.h"
#include "Models/TemporalProbe.h"

#if defined(INCLUDE_NLOHMANN_JSON_HPP_) || defined(CPPHTTPLIB_HTTPLIB_H)
#error "Pure scalar DTO headers must not import JSON or HTTP implementations"
#endif

/// @brief Compiles application-owned DTO use without importing serializers or HTTP targets.
/// @return Zero after constructing both selected provider representations.
int main()
{
    ScalarProviders::UuidProbe uuid{};
    ScalarProviders::TemporalProbe temporal{};
    (void)uuid;
    (void)temporal;
    return 0;
}
