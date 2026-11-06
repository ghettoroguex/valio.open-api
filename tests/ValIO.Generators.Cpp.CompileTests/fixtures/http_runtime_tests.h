// Generated transport acceptance scenarios using a real local cpp-httplib server.
#pragma once
#include <httplib.h>
#include <atomic>
#include <charconv>
#include <chrono>
#include <cmath>
#include <condition_variable>
#include <cstdio>
#include <future>
#include <limits>
#include <locale>
#include <mutex>
#include <optional>
#include <stdexcept>
#include <string>
#include <thread>
#include <vector>
#include "Runtime/ValIOHttp.h"

namespace RequestParameterFixture {
/// @brief A typed enum whose wire values differ from its numeric representation.
enum class Flag { Disabled, Enabled };
/// @brief Provides the ADL wire codec consumed by generic request parameter templates.
/// @param value Typed enum value.
/// @return Its schema-defined string value.
inline std::string ToWireText(const Flag value) { return value == Flag::Enabled ? "on" : "off"; }
/// @brief Supplies a locale whose decimal separator must never affect HTTP numeric parameters.
class CommaDecimalPoint final : public std::numpunct<char> {
protected:
    /// @brief Returns the comma separator used to expose locale-sensitive numeric conversion.
    /// @return A comma.
    char do_decimal_point() const override { return ','; }
};
/// @brief Models an externally declared native provider requiring an explicit checked formatter.
struct ExternalValue { std::string Text; };
} // namespace RequestParameterFixture

/// @brief Checks typed parameter values, presence, precision, collection styles, and formatter customization.
/// @return The number of failed acceptance checks.
inline int RunRequestParameterTests()
{
    int failures = 0;
    const auto check = [&](const bool valid, const char* description) {
        std::printf("%s: %s\n", valid ? "ok" : "FAIL", description);
        if (!valid) ++failures;
    };
    ValIO::Request request;
    request.AddQuery("absent", std::optional<int>{});
    request.AddQuery("false", std::optional<bool>{false});
    request.AddQuery("zero", std::optional<int>{0});
    request.AddQuery("empty", std::optional<std::string>{""});
    request.AddQuery("text", "literal");
    request.AddQuery("view", std::string_view("view"));
    check(request.Query.size() == 5 && request.Query[0].Values[0] == "false" && request.Query[1].Values[0] == "0"
          && request.Query[2].Values[0].empty() && request.Query[3].Values[0] == "literal" && request.Query[4].Values[0] == "view",
          "optional parameters omit only absence and preserve false, zero, empty strings, literals, and string views");
    request.AddHeader("X-False", false);
    request.AddHeader("X-True", true);
    request.AddFormField("zero", 0);
    check(request.Headers[0].second == "false" && request.Headers[1].second == "true" && request.FormFields[0].second == "0",
          "typed header and form values use the same invariant scalar codec");
    request.AddQuery("minimum", (std::numeric_limits<std::int64_t>::min)());
    request.AddQuery("maximum", (std::numeric_limits<std::uint64_t>::max)());
    check(request.Query[5].Values[0] == "-9223372036854775808" && request.Query[6].Values[0] == "18446744073709551615",
          "signed and unsigned 64-bit parameter boundaries retain their complete range");
    const double precise = 1.2345678901234567;
    const float preciseFloat = 0.12345679F;
    const auto originalLocale = std::locale();
    std::locale::global(std::locale(std::locale::classic(), new RequestParameterFixture::CommaDecimalPoint));
    request.AddQuery("precise", precise);
    request.AddQuery("precise-float", preciseFloat);
    std::locale::global(originalLocale);
    double parsedDouble = 0.0;
    float parsedFloat = 0.0F;
    const auto& doubleText = request.Query[7].Values[0];
    const auto& floatText = request.Query[8].Values[0];
    const auto decodedDouble = std::from_chars(doubleText.data(), doubleText.data() + doubleText.size(), parsedDouble);
    const auto decodedFloat = std::from_chars(floatText.data(), floatText.data() + floatText.size(), parsedFloat);
    check(decodedDouble.ec == std::errc{} && decodedDouble.ptr == doubleText.data() + doubleText.size() && parsedDouble == precise
          && decodedFloat.ec == std::errc{} && decodedFloat.ptr == floatText.data() + floatText.size() && parsedFloat == preciseFloat
          && doubleText.find(',') == std::string::npos && floatText.find(',') == std::string::npos,
          "float and double parameters round-trip exactly and ignore a comma decimal locale");
    bool nonfiniteRejected = false;
    try { request.AddQuery("infinite", (std::numeric_limits<double>::infinity)()); }
    catch (const std::invalid_argument&) { nonfiniteRejected = true; }
    check(nonfiniteRejected && request.Query.size() == 9, "nonfinite floating parameters fail before mutating the request");
    bool nullPointerRejected = false;
    const char* missingText = nullptr;
    try { request.AddHeader("X-Null", missingText); }
    catch (const std::invalid_argument&) { nullPointerRejected = true; }
    check(nullPointerRejected && request.Headers.size() == 2, "a runtime null C string is rejected without converting to false");
    bool nestedNullRejected = false;
    const std::optional<std::optional<int>> explicitNull{std::optional<int>{}};
    try { request.AddQuery("null", explicitNull); }
    catch (const std::invalid_argument&) { nestedNullRejected = true; }
    check(nestedNullRejected, "present nested optional null fails instead of being silently omitted");
    const std::optional<int> presentZero{0};
    request.AddQuery("required", ValIO::RequireParameterValue("required", presentZero));
    bool requiredNullRejected = false;
    try { (void)ValIO::RequireParameterValue("required", std::optional<int>{}); }
    catch (const std::invalid_argument& exception) { requiredNullRejected = std::string(exception.what()).find("required") != std::string::npos; }
    check(requiredNullRejected && request.Query.back().Values[0] == "0", "required nullable parameters preserve zero and reject named explicit null");
    ValIO::Request collections;
    collections.AddQuery("flags", std::optional<std::vector<bool>>{std::vector<bool>{false, true}});
    collections.AddHeader("X-Flags", std::vector<bool>{false, true});
    collections.AddQuery("csv", std::vector<int>{1, 2}, ValIO::CollectionEncoding::CommaSeparated);
    collections.AddQuery("space", std::vector<int>{1, 2}, ValIO::CollectionEncoding::SpaceSeparated);
    collections.AddQuery("pipe", std::vector<int>{1, 2}, ValIO::CollectionEncoding::PipeSeparated);
    check(collections.Query.size() == 4 && collections.Query[0].Name == "flags" && collections.Query[0].Values == std::vector<std::string>{"false", "true"}
          && collections.Query[0].Encoding == ValIO::CollectionEncoding::Repeated && collections.Query[1].Encoding == ValIO::CollectionEncoding::CommaSeparated
          && collections.Query[2].Encoding == ValIO::CollectionEncoding::SpaceSeparated && collections.Query[3].Encoding == ValIO::CollectionEncoding::PipeSeparated
          && collections.Query[1].Values == std::vector<std::string>{"1", "2"} && collections.Headers[0].second == "false,true",
          "flat collections honor repeated query defaults, CSV header defaults, and explicit CSV, space, and pipe encodings");
    collections.AddQuery("empty-repeated", std::vector<int>{});
    collections.AddHeader("X-Empty", std::vector<int>{});
    check(collections.Query.size() == 4 && collections.Headers.back().second.empty(),
          "empty repeated collections emit no entries while empty separated collections preserve an empty value");
    collections.AddQuery("enum", RequestParameterFixture::Flag::Disabled);
    collections.AddHeader("X-Enums", std::vector<RequestParameterFixture::Flag>{RequestParameterFixture::Flag::Disabled, RequestParameterFixture::Flag::Enabled});
    check(collections.Query.back().Values[0] == "off" && collections.Headers.back().second == "off,on",
          "generated enum-style ADL codecs retain declared scalar and collection wire values");
    int formatterCalls = 0;
    const auto formatter = [&](const RequestParameterFixture::ExternalValue& value) { ++formatterCalls; return value.Text; };
    collections.AddQuery("native-absent", std::optional<RequestParameterFixture::ExternalValue>{}, formatter);
    collections.AddQuery("native", std::vector<RequestParameterFixture::ExternalValue>{{"first"}, {"second"}}, formatter, ValIO::CollectionEncoding::PipeSeparated);
    check(formatterCalls == 2 && collections.Query.back().Values == std::vector<std::string>{"first", "second"}
          && collections.Query.back().Encoding == ValIO::CollectionEncoding::PipeSeparated,
          "native provider formatters run only for present scalar leaves and preserve requested collection encoding");
    bool nullElementRejected = false;
    const auto sizeBeforeInvalid = collections.Query.size();
    try { collections.AddQuery("items", std::vector<std::optional<int>>{1, std::nullopt}); }
    catch (const std::invalid_argument&) { nullElementRejected = true; }
    check(nullElementRejected && collections.Query.size() == sizeBeforeInvalid,
          "unrepresentable null collection elements fail transactionally before appending repeated entries");
    return failures;
}

/// @brief Exercises interception, exact request replay, and request-scoped cancellation on a real socket.
/// @return The number of failed acceptance checks.
inline int RunHttpRuntimeTests()
{
    int failures = RunRequestParameterTests();
    const auto check = [&](const bool valid, const char* description) {
        std::printf("%s: %s\n", valid ? "ok" : "FAIL", description);
        if (!valid) ++failures;
    };
    httplib::Server server;
    std::atomic<int> replayAttempts{0};
    std::atomic<int> refreshAttempts{0};
    std::atomic<int> deniedAttempts{0};
    std::atomic<int> quickAttempts{0};
    std::mutex bodiesMutex;
    std::vector<std::string> replayBodies;
    std::vector<std::string> replayContentTypes;
    std::mutex gateMutex;
    std::condition_variable gate;
    bool releaseSlow = false;
    int slowEntered = 0;
    server.Post("/replay", [&](const httplib::Request& request, httplib::Response& response) {
        ++replayAttempts;
        {
            const std::lock_guard<std::mutex> guard(bodiesMutex);
            replayBodies.push_back(request.body);
            replayContentTypes.push_back(request.get_header_value("Content-Type"));
        }
        const bool refreshed = request.get_header_value("Authorization") == "Bearer fresh"
            && request.get_header_value_count("Authorization") == 1
            && request.get_header_value("X-Default") == "application-default"
            && request.get_header_value("X-Override") == "request-specific";
        response.status = refreshed ? 200 : 401;
        response.set_header("X-Response", "observed");
        response.set_content(refreshed ? "accepted" : "expired", "text/plain");
    });
    server.Post("/refresh", [&](const httplib::Request&, httplib::Response& response) {
        ++refreshAttempts;
        response.set_content("fresh", "text/plain");
    });
    server.Get("/denied", [&](const httplib::Request&, httplib::Response& response) {
        ++deniedAttempts;
        response.status = 401;
        response.set_content("still expired", "text/plain");
    });
    server.Get("/quick", [&](const httplib::Request&, httplib::Response& response) {
        ++quickAttempts;
        response.set_content("ok", "text/plain");
    });
    server.Get("/slow", [&](const httplib::Request&, httplib::Response& response) {
        std::unique_lock<std::mutex> guard(gateMutex);
        ++slowEntered;
        gate.notify_all();
        gate.wait_for(guard, std::chrono::seconds(3), [&] { return releaseSlow; });
        response.set_content("completed", "text/plain");
    });
    server.Delete("/delete-body", [](const httplib::Request& request, httplib::Response& response) {
        response.set_content(request.body, "text/plain");
    });
    server.Get("/parameters", [](const httplib::Request& request, httplib::Response& response) {
        response.set_content(request.target, "text/plain");
    });
    server.Get("/paths/.*", [](const httplib::Request& request, httplib::Response& response) {
        response.set_content(request.target, "text/plain");
    });

    const int port = server.bind_to_any_port("127.0.0.1");
    if (port <= 0) {
        check(false, "runtime HTTP server binds to a local port");
        return failures;
    }
    std::thread serverThread([&] { server.listen_after_bind(); });
    ValIO::HttpConfig config;
    config.BaseUrl = "http://127.0.0.1:" + std::to_string(port);
    config.ReadTimeoutSeconds = 5;
    config.DefaultHeaders = {{"X-Default", "application-default"}, {"X-Override", "default"}};
    std::string auth = "expired";
    std::vector<std::string> events;
    int responseHeadersObserved = 0;
    ValIO::HttpClient* sharedTransport = nullptr;
    config.Pipeline.MaxRetries = 1;
    config.Pipeline.BeforeSend = [&](ValIO::Request& request, const ValIO::CancellationToken&) {
        events.push_back("before:" + request.Path);
        if (request.Path == "/replay") request.SetHeader("Authorization", "Bearer " + auth);
    };
    config.Pipeline.AfterReceive = [&](const ValIO::Request& request, ValIO::Response& response, const ValIO::CancellationToken&) {
        events.push_back("after:" + request.Path + ":" + std::to_string(response.StatusCode));
        for (const auto& header : response.Headers) {
            if (header.first == "X-Response" && header.second == "observed") ++responseHeadersObserved;
        }
    };
    config.Pipeline.ShouldRetry = [&](ValIO::Request& request, const ValIO::Response& response, const std::size_t retries,
                                      const ValIO::CancellationToken& cancellationToken) {
        if (request.Path != "/replay" || response.StatusCode != 401 || retries != 0) return false;
        events.push_back("retry:/replay");
        ValIO::Request refresh;
        refresh.Verb = ValIO::HttpVerb::Post;
        refresh.Path = "/refresh";
        const auto result = sharedTransport->Send(refresh, cancellationToken);
        if (!result || result->StatusCode != 200) return false;
        auth = result->Body;
        request.SetHeader("authorization", "Bearer " + auth);
        return true;
    };
    ValIO::HttpClient transport(config);
    sharedTransport = &transport;
    ValIO::Request multipart;
    multipart.Verb = ValIO::HttpVerb::Post;
    multipart.Path = "/replay";
    multipart.ContentType = "multipart/form-data";
    multipart.AddHeader("x-override", "request-specific");
    multipart.AddHeader("Content-Type", "application/json");
    multipart.AddHeader("Content-Length", "1");
    multipart.AddFormField("description", "----ValIOBoundary");
    multipart.AddFilePart("documents", "one.bin", {0, 1, 0, 255}, "application/octet-stream");
    const auto replay = transport.Send(multipart);
    check(replay && replay->StatusCode == 200 && replay->Body == "accepted", "401 refresh retries through the same reusable transport without deadlock");
    check(replayAttempts == 2 && refreshAttempts == 1, "authentication refresh performs exactly one bounded replay");
    {
        const std::lock_guard<std::mutex> guard(bodiesMutex);
        check(replayBodies.size() == 2 && replayBodies[0] == replayBodies[1]
              && replayContentTypes[0] == replayContentTypes[1], "multipart body and boundary replay byte-for-byte");
    }
    check(events == std::vector<std::string>{"before:/replay", "after:/replay:401", "retry:/replay", "before:/refresh",
              "after:/refresh:200", "before:/replay", "after:/replay:200"}, "interceptors run in deterministic order around each attempt");
    check(responseHeadersObserved == 2, "response interceptors receive headers for unsuccessful and successful responses");
    check(multipart.Headers.size() == 3, "pipeline modifications do not mutate the caller's request");
    check(transport.GetConfig().BaseUrl == config.BaseUrl, "injected transports expose their immutable configuration");

    auto boundedConfig = config;
    boundedConfig.Pipeline.BeforeSend = {};
    boundedConfig.Pipeline.AfterReceive = {};
    boundedConfig.Pipeline.ShouldRetry = [](ValIO::Request&, const ValIO::Response&, std::size_t, const ValIO::CancellationToken&) { return true; };
    ValIO::HttpClient bounded(boundedConfig);
    ValIO::Request denied;
    denied.Path = "/denied";
    const auto deniedResult = bounded.Send(denied);
    check(deniedResult && deniedResult->StatusCode == 401 && deniedAttempts == 2, "an always-retry policy stops at the configured maximum");
    boundedConfig.Pipeline.MaxRetries = 0;
    ValIO::HttpClient noRetries(boundedConfig);
    const auto noRetryResult = noRetries.Send(denied);
    check(noRetryResult && noRetryResult->StatusCode == 401 && deniedAttempts == 3, "retry hooks alone do not enable replay");

    auto rawConfig = config;
    rawConfig.Pipeline = {};
    ValIO::HttpClient raw(rawConfig);
    ValIO::Request parameterRequest;
    parameterRequest.Path = "/parameters";
    parameterRequest.AddQuery("csv", std::vector<std::string>{"a,b", "c&d", "x+y", "a b"}, ValIO::CollectionEncoding::CommaSeparated);
    parameterRequest.AddQuery("repeat", std::vector<std::string>{"first,item", "second&item"});
    parameterRequest.AddQuery("q&name", "space plus+");
    parameterRequest.AddQuery("spaces", std::vector<std::string>{"one", "two"}, ValIO::CollectionEncoding::SpaceSeparated);
    parameterRequest.AddQuery("pipes", std::vector<std::string>{"one", "two"}, ValIO::CollectionEncoding::PipeSeparated);
    const auto parameterResponse = raw.Send(parameterRequest);
    const std::string expectedParameterTarget = "/parameters?csv=a%2Cb,c%26d,x%2By,a%20b&repeat=first%2Citem&repeat=second%26item&q%26name=space%20plus%2B&spaces=one%20two&pipes=one%7Ctwo";
    if (!parameterResponse) std::fprintf(stderr, "Raw query transport failure: %s\n", parameterResponse.error().Message.c_str());
    else if (parameterResponse->Body != expectedParameterTarget)
        std::fprintf(stderr, "Raw query target mismatch:\n  expected: %s\n  actual:   %s\n  status:   %d\n", expectedParameterTarget.c_str(), parameterResponse->Body.c_str(), parameterResponse->StatusCode);
    check(parameterResponse && parameterResponse->Body == expectedParameterTarget,
          "raw HTTP query target encodes each element before inserting collection separators and preserves commas, ampersands, plus, and spaces");
    ValIO::Request pathRequest;
    pathRequest.Path = "/paths/segment with+comma,and%2Fencoded";
    const auto pathResponse = raw.Send(pathRequest);
    check(pathResponse && pathResponse->Body == "/paths/segment%20with%2Bcomma%2Cand%2Fencoded",
          "request paths retain their existing encoding while preencoded route values are not encoded twice");
    ValIO::Request quick;
    quick.Path = "/quick";
    ValIO::CancellationSource preCancelled;
    check(preCancelled.Cancel() && !preCancelled.Cancel(), "cancellation is idempotent");
    const auto early = raw.Send(quick, preCancelled.GetToken());
    check(!early && early.error().What == ValIO::Error::Code::Cancelled && quickAttempts == 0, "pre-cancelled requests never reach the server");
    {
        const std::lock_guard<std::mutex> guard(gateMutex);
        slowEntered = 0;
        releaseSlow = false;
    }
    ValIO::Request slow;
    slow.Path = "/slow";
    auto held = std::async(std::launch::async, [&] { return raw.Send(slow); });
    bool started;
    {
        std::unique_lock<std::mutex> guard(gateMutex);
        started = gate.wait_for(guard, std::chrono::seconds(2), [&] { return slowEntered == 1; });
    }
    check(started, "a held request acquires the shared connection");
    ValIO::CancellationSource queuedSource;
    auto queued = std::async(std::launch::async, [&] { return raw.Send(quick, queuedSource.GetToken()); });
    queuedSource.Cancel();
    const bool queuedReady = queued.wait_for(std::chrono::milliseconds(500)) == std::future_status::ready;
    check(queuedReady, "a cancelled queued request finishes before the active request");
    {
        const std::lock_guard<std::mutex> guard(gateMutex);
        releaseSlow = true;
        gate.notify_all();
    }
    const auto queuedResult = queued.get();
    const auto heldResult = held.get();
    check(!queuedResult && queuedResult.error().What == ValIO::Error::Code::Cancelled && heldResult && quickAttempts == 0,
          "cancelling a queued call does not stop the active call or dispatch the queued call");

    {
        const std::lock_guard<std::mutex> guard(gateMutex);
        slowEntered = 0;
        releaseSlow = false;
    }
    ValIO::CancellationSource activeSource;
    auto active = std::async(std::launch::async, [&] { return raw.Send(slow, activeSource.GetToken()); });
    {
        std::unique_lock<std::mutex> guard(gateMutex);
        started = gate.wait_for(guard, std::chrono::seconds(2), [&] { return slowEntered == 1; });
    }
    check(started, "the cancellable request is active on the server");
    activeSource.Cancel();
    const bool activeReady = active.wait_for(std::chrono::seconds(1)) == std::future_status::ready;
    check(activeReady, "active cancellation interrupts the waiting response socket");
    {
        const std::lock_guard<std::mutex> guard(gateMutex);
        releaseSlow = true;
        gate.notify_all();
    }
    const auto activeResult = active.get();
    check(!activeResult && activeResult.error().What == ValIO::Error::Code::Cancelled, "active cancellation returns its distinct error category");
    const auto afterCancel = raw.Send(quick);
    activeSource.Cancel();
    const auto afterLateCancel = raw.Send(quick);
    check(afterCancel && afterLateCancel && quickAttempts == 2, "the connection remains usable and an old token cannot cancel a later request");

    ValIO::Request deleteBody;
    deleteBody.Verb = ValIO::HttpVerb::Delete;
    deleteBody.Path = "/delete-body";
    deleteBody.Body = "delete payload";
    deleteBody.ContentType = "text/plain";
    const auto deleted = raw.Send(deleteBody);
    check(deleted && deleted->Body == deleteBody.Body, "DELETE requests preserve their explicitly supplied request body");
    auto invalidHeader = quick;
    invalidHeader.AddHeader("Authorization", "Bearer token\r\nInjected: value");
    const auto injected = raw.Send(invalidHeader);
    check(!injected && injected.error().What == ValIO::Error::Code::Transport && quickAttempts == 2,
          "invalid interceptor headers are rejected before network dispatch");

    auto failedHookConfig = rawConfig;
    failedHookConfig.Pipeline.BeforeSend = [](ValIO::Request&, const ValIO::CancellationToken&) { throw std::runtime_error("hook failure"); };
    ValIO::HttpClient failedHook(failedHookConfig);
    const auto hookFailure = failedHook.Send(quick);
    check(!hookFailure && hookFailure.error().What == ValIO::Error::Code::Interceptor && hookFailure.error().Message == "hook failure",
          "hook exceptions become typed interception failures before dispatch");
    auto noResponseConfig = boundedConfig;
    noResponseConfig.BaseUrl = "unsupported://127.0.0.1";
    int retryCalls = 0;
    noResponseConfig.Pipeline.MaxRetries = 3;
    noResponseConfig.Pipeline.ShouldRetry = [&](ValIO::Request&, const ValIO::Response&, std::size_t, const ValIO::CancellationToken&) { ++retryCalls; return true; };
    ValIO::HttpClient noResponse(noResponseConfig);
    const auto transportFailure = noResponse.Send(quick);
    check(!transportFailure && retryCalls == 0, "transport failures never trigger response retry callbacks");

    server.stop();
    serverThread.join();
    return failures;
}
