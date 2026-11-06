cmake_minimum_required(VERSION 3.21)
project(ValIO_ScalarProvider_Acceptance LANGUAGES CXX)

set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)
if(NOT DEFINED GEN_DIR OR NOT DEFINED VALIO_UUID_BACKEND OR NOT DEFINED VALIO_TEMPORAL_BACKEND)
    message(FATAL_ERROR "GEN_DIR, VALIO_UUID_BACKEND, and VALIO_TEMPORAL_BACKEND are required")
endif()

if(VALIO_UUID_BACKEND STREQUAL "StdUuid" AND DEFINED VALIO_STDUUID_SOURCE)
    set(UUID_BUILD_TESTS OFF CACHE BOOL "The fixture checks the selected UUID APIs itself" FORCE)
    set(UUID_ENABLE_INSTALL OFF CACHE BOOL "Dependency installation is unnecessary for acceptance" FORCE)
    add_subdirectory("${VALIO_STDUUID_SOURCE}" stduuid)
endif()
if(VALIO_UUID_BACKEND STREQUAL "ModernUuid" AND DEFINED VALIO_MODERN_UUID_SOURCE)
    set(MUUID_NO_TESTS ON CACHE BOOL "The fixture checks the selected UUID APIs itself" FORCE)
    set(MUUID_STATIC ON CACHE BOOL "Use the real static upstream provider target" FORCE)
    set(MUUID_SHARED OFF CACHE BOOL "No shared dependency installation is required" FORCE)
    add_subdirectory("${VALIO_MODERN_UUID_SOURCE}" modern-uuid)
endif()

find_package(httplib REQUIRED)
find_package(nlohmann_json REQUIRED)
add_subdirectory("${GEN_DIR}" generated)
add_executable(scalar_model_only "${CMAKE_CURRENT_SOURCE_DIR}/../scalar_model_only.cpp")
target_link_libraries(scalar_model_only PRIVATE valio_models)

add_executable(scalar_provider_main
    "${CMAKE_CURRENT_SOURCE_DIR}/../scalar_provider_main.cpp"
    "${CMAKE_CURRENT_SOURCE_DIR}/../uuid_provider_main.cpp"
    "${CMAKE_CURRENT_SOURCE_DIR}/../temporal_provider_main.cpp"
    "${CMAKE_CURRENT_SOURCE_DIR}/../provider_client_smoke.cpp")
target_link_libraries(scalar_provider_main PRIVATE valio_generated httplib::httplib)
target_compile_definitions(scalar_provider_main PRIVATE
    VALIO_UUID_PROVIDER=$<NOT:$<STREQUAL:${VALIO_UUID_BACKEND},String>>
    VALIO_TEMPORAL_PROVIDER=$<NOT:$<STREQUAL:${VALIO_TEMPORAL_BACKEND},String>>
    VALIO_TEMPORAL_BACKEND=${VALIO_TEMPORAL_BACKEND_INDEX})

foreach(target scalar_model_only scalar_provider_main valio_serializers valio_clients)
    if(NOT TARGET ${target})
        continue()
    endif()
    get_target_property(target_type ${target} TYPE)
    if(target_type STREQUAL "INTERFACE_LIBRARY")
        continue()
    endif()
    if(MSVC)
        target_compile_options(${target} PRIVATE /W4 /WX)
    else()
        target_compile_options(${target} PRIVATE -Wall -Wextra -Werror)
    endif()
endforeach()
