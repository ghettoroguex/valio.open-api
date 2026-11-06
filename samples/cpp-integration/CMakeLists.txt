cmake_minimum_required(VERSION 3.24)
project(ValIOIntegrationExample LANGUAGES CXX)
include(CTest)

option(VALIO_EXAMPLE_FULL "Compile JSON codecs and HTTP client as well as pure DTOs" ON)
set(VALIO_EXAMPLE_DEPENDENCY_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/third_party" CACHE PATH "Optional bundled header-only dependencies")
if(VALIO_EXAMPLE_FULL AND EXISTS "${VALIO_EXAMPLE_DEPENDENCY_ROOT}/httplib/include/httplib.h")
    add_library(httplib INTERFACE)
    add_library(httplib::httplib ALIAS httplib)
    target_include_directories(httplib INTERFACE "${VALIO_EXAMPLE_DEPENDENCY_ROOT}/httplib/include")
    if(WIN32)
        target_link_libraries(httplib INTERFACE crypt32 cryptui ws2_32)
    else()
        find_package(Threads REQUIRED)
        target_link_libraries(httplib INTERFACE Threads::Threads)
    endif()
    add_library(nlohmann_json INTERFACE)
    add_library(nlohmann_json::nlohmann_json ALIAS nlohmann_json)
    target_include_directories(nlohmann_json INTERFACE "${VALIO_EXAMPLE_DEPENDENCY_ROOT}/nlohmann_json/include")
elseif(VALIO_EXAMPLE_FULL)
    include(FetchContent)
    FetchContent_Declare(httplib
        GIT_REPOSITORY https://github.com/yhirose/cpp-httplib.git
        GIT_TAG v0.18.3 GIT_SHALLOW TRUE OVERRIDE_FIND_PACKAGE)
    FetchContent_Declare(nlohmann_json
        GIT_REPOSITORY https://github.com/nlohmann/json.git
        GIT_TAG v3.12.0 GIT_SHALLOW TRUE OVERRIDE_FIND_PACKAGE)
    FetchContent_MakeAvailable(httplib nlohmann_json)
else()
    set(VALIO_BUILD_SERIALIZERS OFF CACHE BOOL "" FORCE)
    set(VALIO_BUILD_CLIENTS OFF CACHE BOOL "" FORCE)
endif()

add_subdirectory(generated)

# This executable consumes only the generated DTO target.
add_executable(model_consumer model_consumer.cpp)
target_link_libraries(model_consumer PRIVATE valio_models)
add_test(NAME model_consumer COMMAND model_consumer)

if(VALIO_EXAMPLE_FULL)
    add_executable(codec_consumer codec_consumer.cpp)
    target_link_libraries(codec_consumer PRIVATE valio_generated)
    add_test(NAME codec_consumer COMMAND codec_consumer)

    add_executable(http_consumer http_consumer.cpp)
    target_link_libraries(http_consumer PRIVATE valio_clients httplib::httplib)
    add_test(NAME http_consumer COMMAND http_consumer)
endif()
