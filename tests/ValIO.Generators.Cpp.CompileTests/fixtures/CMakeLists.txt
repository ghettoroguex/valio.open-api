# Compile-fixture для std-профиля ValIO.
# Пины совпадают с conan-графом gta-sa-rw: cpp-httplib v0.18.3 + nlohmann_json v3.12.0 (EXACT).
# GEN_DIR — корень сгенерированного вывода (передаётся -D из теста).
cmake_minimum_required(VERSION 3.16)
project(ValIO_Cpp_CompileFixture LANGUAGES CXX)

if(NOT DEFINED GEN_DIR)
    message(FATAL_ERROR "GEN_DIR must point at the generated output root")
endif()

set(CMAKE_CXX_STANDARD_REQUIRED ON)

# VALIO_SMOKE_ASYNC передаётся из теста (-DVALIO_SMOKE_ASYNC=0|1):
# smoke-файл использует async-методы только когда генерация их создала.
if(NOT DEFINED VALIO_SMOKE_ASYNC)
    set(VALIO_SMOKE_ASYNC 1)
endif()
add_compile_definitions(VALIO_SMOKE_ASYNC=${VALIO_SMOKE_ASYNC})
if(NOT DEFINED VALIO_ASYNC_ONLY)
    set(VALIO_ASYNC_ONLY 0)
endif()
add_compile_definitions(VALIO_ASYNC_ONLY=${VALIO_ASYNC_ONLY})

# Configure the generated model target before making either third-party package available.
set(VALIO_BUILD_SERIALIZERS OFF CACHE BOOL "Fixture consumes codec sources directly" FORCE)
set(VALIO_BUILD_CLIENTS OFF CACHE BOOL "Fixture consumes client sources directly" FORCE)
add_subdirectory("${GEN_DIR}" generated_definitions)

option(VALIO_USE_INSTALLED_DEPENDENCIES "Use exact-version installed CMake packages without fetching sources" OFF)
if(VALIO_USE_INSTALLED_DEPENDENCIES)
    find_package(httplib 0.18.3 EXACT REQUIRED CONFIG)
    find_package(nlohmann_json 3.12.0 EXACT REQUIRED CONFIG)
else()
    include(FetchContent)
    FetchContent_Declare(httplib
        GIT_REPOSITORY https://github.com/yhirose/cpp-httplib.git
        GIT_TAG        v0.18.3
        GIT_SHALLOW    TRUE)
    FetchContent_MakeAvailable(httplib)

    FetchContent_Declare(json
        GIT_REPOSITORY https://github.com/nlohmann/json.git
        GIT_TAG        v3.12.0
        GIT_SHALLOW    TRUE)
    FetchContent_MakeAvailable(json)
endif()

file(GLOB_RECURSE VALIO_SOURCES CONFIGURE_DEPENDS "${GEN_DIR}/*.cpp")
if(NOT VALIO_SOURCES)
    message(FATAL_ERROR "no generated sources found under ${GEN_DIR}")
endif()

# Pure DTO consumers do not receive nlohmann, httplib, or serializer include directories.
add_executable(model_only model_only.cpp)
target_link_libraries(model_only PRIVATE valio_models)

add_library(valio_fixture_implementations STATIC ${VALIO_SOURCES})
target_link_libraries(valio_fixture_implementations PUBLIC valio_models httplib::httplib nlohmann_json::nlohmann_json)

add_executable(smoke_main smoke_main.cpp checked_codec_smoke.cpp)
target_link_libraries(smoke_main PRIVATE valio_fixture_implementations)

if(MSVC)
    target_compile_options(valio_fixture_implementations PRIVATE /W4 /WX)
    target_compile_options(smoke_main PRIVATE /W4 /WX)
    target_compile_options(model_only PRIVATE /W4 /WX)
else()
    target_compile_options(valio_fixture_implementations PRIVATE -Wall -Wextra -Werror)
    target_compile_options(smoke_main PRIVATE -Wall -Wextra -Werror)
    target_compile_options(model_only PRIVATE -Wall -Wextra -Werror)
endif()
