#include <cstdio>
#include <string>

/// @brief Runs strict UUID parsing and generated model round-trip acceptance checks.
/// @param fixturePath Explicit repository JSON fixture path.
/// @return The number of failed checks.
int RunUuidProviderChecks(const std::string& fixturePath);
/// @brief Runs temporal provider parsing and representation acceptance checks.
/// @param fixturePath Explicit repository JSON fixture path.
/// @return The number of failed checks.
int RunTemporalProviderChecks(const std::string& fixturePath);
/// @brief Runs generated native scalar client operations against a real local HTTP server.
/// @return The number of failed checks.
int RunProviderClientChecks();

/// @brief Executes all native provider acceptance checks without relying on the process working directory.
/// @param argc Number of command-line arguments.
/// @param argv Arguments containing the explicit UUID and temporal fixture paths.
/// @return Zero on complete acceptance; nonzero when any check fails.
int main(const int argc, const char* const argv[])
{
    if (argc != 3) {
        std::printf("Expected explicit UUID and temporal fixture paths\n");
        return 1;
    }
    const int failures = RunUuidProviderChecks(argv[1]) + RunTemporalProviderChecks(argv[2]) + RunProviderClientChecks();
    std::printf("%d SCALAR PROVIDER CHECK(S) FAILED\n", failures);
    return failures == 0 ? 0 : 1;
}
