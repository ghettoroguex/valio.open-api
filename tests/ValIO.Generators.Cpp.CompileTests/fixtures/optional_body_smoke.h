// Real socket acceptance checks for request-body presence and explicit JSON null.
#pragma once
#include <httplib.h>
#include <cstdint>
#include <cstdio>
#include <initializer_list>
#include <optional>
#include <string>
#include <thread>
#include <vector>
#include "Clients/OptionalBodiesClient.h"
#include "Runtime/ValIOJson.h"

#if VALIO_ASYNC_ONLY
#define VALIO_BODY_CALL(method, ...) client.method##Async(__VA_ARGS__).get()
#else
#define VALIO_BODY_CALL(method, ...) client.method(__VA_ARGS__)
#endif

/// @brief Checks absent, present, and nullable payloads reach the server with their declared wire representation.
/// @return The number of failed acceptance checks.
inline int RunOptionalBodySmoke()
{
    int failures = 0;
    const auto check = [&](const bool valid, const char* description) {
        std::printf("%s: %s\n", valid ? "ok" : "FAIL", description);
        if (!valid) ++failures;
    };
    httplib::Server server;
    server.Post(R"(/optional/.*)", [](const httplib::Request& request, httplib::Response& response) {
        const auto state = request.get_header_value("X-Expected-Body");
        const auto contentType = request.get_header_value("Content-Type");
        bool valid = false;
        if (state == "missing")
            valid = request.body.empty() && !request.has_header("Content-Type");
        else if (state == "json") {
            const auto json = nlohmann::json::parse(request.body, nullptr, false);
            valid = contentType == "application/json" && json.is_object() && json.value("id", "") == "value";
        }
        else if (state == "null")
            valid = contentType == "application/json" && request.body == "null";
        else if (state == "form")
            valid = contentType == "application/x-www-form-urlencoded" && request.body == "id=value";
        else if (state == "multipart")
            valid = request.is_multipart_form_data() && request.has_file("id") && request.get_file_value("id").content == "value";
        else if (state == "binary")
            valid = contentType == "application/octet-stream" && request.body == std::string("\0\xff" "A", 3);
        else if (state == "empty-binary")
            valid = contentType == "application/octet-stream" && request.body.empty();
        response.set_content(valid ? "true" : "false", "application/json");
    });
    const int port = server.bind_to_any_port("127.0.0.1");
    if (port <= 0) {
        check(false, "optional-body HTTP server binds to a local port");
        return failures;
    }
    std::thread serverThread([&] { server.listen_after_bind(); });
    ValIO::HttpConfig config;
    config.BaseUrl = "http://127.0.0.1:" + std::to_string(port);
    std::string expectedState;
    config.Pipeline.BeforeSend = [&](ValIO::Request& request, const ValIO::CancellationToken&) {
        request.SetHeader("X-Expected-Body", expectedState);
    };
    Petstore::OptionalBodiesClient client(config);
    Petstore::OptionalPayload payload;
    payload.Id = "value";
    const std::optional<Petstore::OptionalPayload> presentPayload(payload);
    const std::optional<std::optional<Petstore::OptionalPayload>> presentNull(std::in_place);
    const std::optional<std::optional<Petstore::OptionalPayload>> presentNullablePayload(std::in_place, payload);
    const auto accepted = [&](const ValIO::Expected<bool, ValIO::Error>& result, const char* description) {
        check(result.has_value() && *result, description);
    };

    expectedState = "missing";
    accepted(VALIO_BODY_CALL(PostOptionalObject, ), "optional object default omits body and content type despite required model fields");
    accepted(VALIO_BODY_CALL(PostOptionalNullable, ), "optional nullable default omits body and content type");
    accepted(VALIO_BODY_CALL(PostOptionalForm, ), "optional form default omits body and content type");
    accepted(VALIO_BODY_CALL(PostOptionalMultipart, ), "optional multipart default omits body and content type");
    accepted(VALIO_BODY_CALL(PostOptionalBinary, ), "optional binary default omits body and content type");
    expectedState = "json";
    accepted(VALIO_BODY_CALL(PostOptionalObject, presentPayload), "present optional object serializes JSON");
    accepted(VALIO_BODY_CALL(PostOptionalNullable, presentNullablePayload), "present optional nullable object serializes its value");
    expectedState = "null";
    accepted(VALIO_BODY_CALL(PostOptionalNullable, presentNull), "present optional nullable body serializes explicit JSON null");
    accepted(VALIO_BODY_CALL(PostRequiredNullable, std::optional<Petstore::OptionalPayload>{}), "required nullable body serializes JSON null without becoming absent");
    expectedState = "form";
    accepted(VALIO_BODY_CALL(PostOptionalForm, presentPayload), "present optional form serializes fields");
    expectedState = "multipart";
    accepted(VALIO_BODY_CALL(PostOptionalMultipart, presentPayload), "present optional multipart serializes fields");
    expectedState = "binary";
    const std::optional<std::vector<std::uint8_t>> binary(std::in_place, std::initializer_list<std::uint8_t>{0, 255, 65});
    accepted(VALIO_BODY_CALL(PostOptionalBinary, binary), "present optional binary preserves all bytes");
    expectedState = "empty-binary";
    const std::optional<std::vector<std::uint8_t>> emptyBinary(std::in_place);
    accepted(VALIO_BODY_CALL(PostOptionalBinary, emptyBinary), "present empty binary retains content type independently of absence");
    server.stop();
    serverThread.join();
    return failures;
}

#undef VALIO_BODY_CALL
