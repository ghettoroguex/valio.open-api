// Smoke для std-профиля ValIO: локальный httplib::Server + сгенерированный клиент.
// Проверяет: wire-имена (order_id/status), optional-семантику (отсутствующий note),
// enum-кодек ("placed"), не-2xx → Error{Status}, void-ответ (204), async-обёртку.
#include <httplib.h>

#include <cstdio>
#ifndef VALIO_SMOKE_ASYNC
#define VALIO_SMOKE_ASYNC 1
#endif

#include <future>
#include <atomic>
#include <iterator>
#include <limits>
#include <memory>
#include <optional>
#include <stdexcept>
#include <string>
#include <thread>
#include <vector>

#include "Clients/StoreClient.h"
#include "Models/Order.h"
#include "Models/CodecProbe.h"
#include "Models/Empty.h"
#include "Models/Upload.h"
#include "Runtime/ValIOJson.h"
#include "Serializers/CodecProbeJson.h"
#include "Serializers/ColorJson.h"
#include "Serializers/EmptyJson.h"
#include "Serializers/OrderJson.h"
#include "Serializers/SingleBaseJson.h"
#include "Serializers/CombinedJson.h"
#include "Serializers/WideJson.h"
#include "http_runtime_tests.h"
#include "optional_body_smoke.h"
#include "nullable_reference_smoke.h"

int RunCheckedCodecSmoke();

#if VALIO_ASYNC_ONLY
#define VALIO_CALL(method, ...) client.method##Async(__VA_ARGS__).get()
#else
#define VALIO_CALL(method, ...) client.method(__VA_ARGS__)
#endif

namespace {

int g_failures = 0;

void Check(bool condition, const char* what)
{
    if (!condition) {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    } else {
        std::printf("ok:   %s\n", what);
    }
}

const char* kCanaryBody = R"({"order_id": 42, "status": "placed"})";

} // namespace

int main()
{
    g_failures += RunHttpRuntimeTests();
    g_failures += RunCheckedCodecSmoke();
    g_failures += RunOptionalBodySmoke();
    g_failures += RunNullableReferenceSmoke();
    Petstore::Order plainDefaultOrder;
    Check(plainDefaultOrder.Status == Petstore::OrderStatus::Placed,
        "ordinary default construction value-initializes a required string enum");
    Check(nlohmann::json::parse(ValIO::Dump(plainDefaultOrder)).at("status") == "placed",
        "plain default-constructed model serializes its valid zero enum safely");
    const ValIO::Expected<int, ValIO::Error> defaultResult;
    Check(defaultResult.has_value() && *defaultResult == 0, "default Expected value agrees with std::expected semantics");
    const auto wide = ValIO::Parse<Petstore::Wide>("-9223372036854775808");
    Check(wide.has_value() && *wide == Petstore::Wide::Minimum, "signed 64-bit minimum enum wire value parses");
    Check(ValIO::Dump(Petstore::Wide::Maximum) == "9223372036854775807", "signed 64-bit maximum enum wire value round-trips");
    httplib::Server server;
    std::atomic<int> placeRequests{0};
    std::atomic<int> browseRequests{0};
    server.Get("/browse", [&](const httplib::Request& request, httplib::Response& res) {
        ++browseRequests;
        const auto mode = request.get_param_value("mode");
        bool valid = request.has_param("required") && request.get_param_value("required") == "0";
        if (mode == "0") {
            valid = valid && request.params.size() == 2
                && !request.has_header("X-Enabled") && !request.has_header("X-Count")
                && !request.has_header("X-Text") && !request.has_header("X-Values") && !request.has_header("X-States");
        } else if (mode == "1") {
            const auto filter = nlohmann::json::parse(request.get_param_value("filter"), nullptr, false);
            valid = valid && request.get_param_value("enabled") == "false"
                && request.get_param_value("count") == "0" && request.get_param_value("ratio") == "0"
                && request.has_param("text") && request.get_param_value("text").empty()
                && request.get_param_value_count("values") == 2
                && request.get_param_value("values", 0) == "0" && request.get_param_value("values", 1) == "2"
                && request.get_param_value_count("switches") == 2
                && request.get_param_value("switches", 0) == "false" && request.get_param_value("switches", 1) == "true"
                && request.get_param_value_count("states") == 2
                && request.get_param_value("states", 0) == "placed" && request.get_param_value("states", 1) == "approved"
                && request.get_param_value("csv") == "0,2" && request.get_param_value("spaces") == "0 2"
                && request.get_param_value("pipes") == "0|2" && filter.is_object()
                && filter.contains("order_id") && filter.at("order_id") == 7
                && filter.contains("status") && filter.at("status") == "approved"
                && request.has_param("maybeText") && request.get_param_value("maybeText").empty()
                && request.get_header_value("X-Enabled") == "false" && request.get_header_value("X-Count") == "0"
                && request.has_header("X-Text") && request.get_header_value("X-Text").empty()
                && request.get_header_value("X-Values") == "0,2" && request.get_header_value("X-States") == "placed,approved";
        } else if (mode == "2") {
            valid = valid && !request.has_param("values") && !request.has_param("switches") && !request.has_param("states")
                && request.has_param("csv") && request.get_param_value("csv").empty()
                && request.has_param("spaces") && request.get_param_value("spaces").empty()
                && request.has_param("pipes") && request.get_param_value("pipes").empty()
                && !request.has_param("enabled") && !request.has_param("count") && !request.has_param("ratio")
                && !request.has_param("text") && !request.has_param("filter") && !request.has_param("maybeText")
                && request.has_header("X-Values") && request.get_header_value("X-Values").empty()
                && request.has_header("X-States") && request.get_header_value("X-States").empty();
        } else {
            valid = false;
        }
        res.set_content(valid ? "true" : "false", "application/json");
    });
    server.Get("/store/order/42", [](const httplib::Request&, httplib::Response& res) {
        res.status = 200;
        res.set_content(kCanaryBody, "application/json");
    });
    server.Get("/store/order/999", [](const httplib::Request&, httplib::Response& res) {
        res.status = 404;
        res.set_content(R"({"title": "not found"})", "application/json");
    });
    server.Get(R"(/store/maybe/(\d+))", [](const httplib::Request& request, httplib::Response& res) {
        const int id = std::stoi(request.matches[1]);
        res.status = id == 0 ? 204 : id == 1 ? 205 : 200;
        if (res.status == 200)
            res.set_content(kCanaryBody, "application/json");
    });
    server.Get(R"(/store/status/(\d+))", [](const httplib::Request& request, httplib::Response& res) {
        res.status = std::stoi(request.matches[1]);
        res.set_content(res.status < 300 ? "true" : "not-json error body", "application/json");
    });
    server.Post("/store/order", [&](const httplib::Request& request, httplib::Response& res) {
        ++placeRequests;
        // Сервер валидирует wire-имена запроса и эхом возвращает тело клиенту.
        if (request.body.find("\"order_id\"") == std::string::npos
            || request.body.find("\"status\"") == std::string::npos) {
            res.status = 400;
            res.set_content("wire name check failed", "text/plain");
            return;
        }
        res.status = 200;
        res.set_content(request.body, "application/json");
    });
    server.Delete("/store/order/42", [](const httplib::Request&, httplib::Response& res) {
        res.status = 204;
    });
    server.Get("/store/active", [](const httplib::Request& request, httplib::Response& res) {
        res.set_content(request.get_param_value("filter[level]") == "2" ? "true" : "false", "application/json");
    });
    const auto uploadHandler = [](const httplib::Request& request, httplib::Response& res) {
        const auto documents = request.files.equal_range("documents");
        const auto count = std::distance(documents.first, documents.second);
        const bool valid = request.is_multipart_form_data()
            && request.has_file("document.title")
            && request.get_file_value("document.title").content == "----ValIOBoundary"
            && count == 2;
        res.set_content(valid ? "true" : "false", "application/json");
    };
    server.Post("/store/upload", uploadHandler);
    server.Put("/store/upload", uploadHandler);
    server.Patch("/store/upload", uploadHandler);

    const int port = server.bind_to_any_port("127.0.0.1");
    if (port <= 0) {
        std::printf("FAIL: cannot bind smoke server\n");
        return 1;
    }
    std::thread serverThread([&] { server.listen_after_bind(); });

    ValIO::HttpConfig config;
    config.BaseUrl = "http://127.0.0.1:" + std::to_string(port);
    auto transport = std::make_shared<ValIO::HttpClient>(config);
    Petstore::StoreClient client(transport);
    Petstore::StoreClient secondClient(transport);
    Check(transport.use_count() >= 3, "multiple generated clients retain the same injected transport");
    bool nullTransportRejected = false;
    try {
        Petstore::StoreClient invalid(std::shared_ptr<ValIO::HttpClient>{});
    } catch (const std::invalid_argument&) {
        nullTransportRejected = true;
    }
    Check(nullTransportRejected, "null shared transport is rejected explicitly");

    // 1. Happy-path: wire-имена + optional-отсутствие + enum-кодек.
    const auto fetched = VALIO_CALL(GetOrderById, 42, "extra");
    Check(fetched.has_value(), "GetOrderById(42) succeeds");
    if (fetched) {
        Check(fetched->OrderId == 42, "wire name order_id decoded");
        Check(!fetched->Note.has_value(), "absent optional note -> nullopt");
        Check(fetched->Status == Petstore::OrderStatus::Placed, "enum wire value placed decoded");
    }

    // 2. Не-2xx → Error{Status, 404}.
    const auto missing = VALIO_CALL(GetOrderById, 999);
    Check(!missing.has_value(), "404 yields error");
    if (!missing) {
        Check(missing.error().What == ValIO::Error::Code::Status, "error code is Status");
        Check(missing.error().StatusCode == 404, "status code preserved");
    }

    // 3. Тело запроса сериализуется с wire-именами (сервер проверяет и эхает).
    Petstore::Order order;
    order.OrderId = 7;
    order.Status = Petstore::OrderStatus::Approved;
    const auto placed = VALIO_CALL(PlaceOrder, order);
    Check(placed.has_value(), "PlaceOrder round-trip succeeds");
    if (placed) {
        Check(placed->OrderId == 7, "request body encoded with wire names and decoded back");
        Check(placed->Status == Petstore::OrderStatus::Approved, "enum round-trips through request");
    }
    const int beforeInvalidRequest = placeRequests.load();
    auto invalidOrder = order;
    invalidOrder.Status = static_cast<Petstore::OrderStatus>(999);
    const auto rejectedEnumRequest = VALIO_CALL(PlaceOrder, invalidOrder);
    Check(!rejectedEnumRequest && rejectedEnumRequest.error().What == ValIO::Error::Code::Serialization,
        "invalid request enum returns typed serialization error without escaping Expected");
    Check(placeRequests.load() == beforeInvalidRequest, "invalid request DTO is rejected before HTTP dispatch");
    const auto rejectedFormatRequest = VALIO_CALL(PlaceFormats, Petstore::CodecFormats{});
    Check(!rejectedFormatRequest && rejectedFormatRequest.error().What == ValIO::Error::Code::Serialization,
        "invalid request string format returns typed serialization error without throwing");

    // 4. void-ответ (204).
    const auto deleted = VALIO_CALL(DeleteOrder, 42);
    Check(deleted.has_value(), "void response (204) is a success");
    const auto present = VALIO_CALL(GetMaybeOrder, 42);
    const auto noContent = VALIO_CALL(GetMaybeOrder, 0);
    const auto resetContent = VALIO_CALL(GetMaybeOrder, 1);
    Check(present.has_value() && present->has_value() && (*present)->OrderId == 42, "mixed200/204 response decodes its typed payload");
    Check(noContent.has_value() && !noContent->has_value(), "204 mixed response is an empty successful optional");
    Check(resetContent.has_value() && !resetContent->has_value(), "205 mixed response skips JSON decoding");
    const auto head = VALIO_CALL(HeadOrder, 42);
    Check(head.has_value(), "HEAD skips decoding even when its response schema declares a model");
    const auto success200 = VALIO_CALL(GetStatus, 200);
    const auto success299 = VALIO_CALL(GetStatus, 299);
    const auto redirect300 = VALIO_CALL(GetStatus, 300);
    const auto error500 = VALIO_CALL(GetStatus, 500);
    Check(success200.has_value() && success200->has_value() && **success200, "200 is in the successful HTTP range");
    Check(success299.has_value() && success299->has_value() && **success299, "299 is in the successful HTTP range");
    Check(!redirect300 && redirect300.error().StatusCode == 300, "300 is outside the successful HTTP range");
    Check(!error500 && error500.error().What == ValIO::Error::Code::Status
        && error500.error().Body == "not-json error body", "non2xx preserves raw error payload before decoding");
    const auto exactOverride = VALIO_CALL(GetRangeOverride, 200);
    const auto emptyRange = VALIO_CALL(GetRangeOverride, 299);
    Check(exactOverride.has_value() && exactOverride->has_value() && **exactOverride,
        "exact200 payload schema takes precedence over bodyless2XX range");
    Check(emptyRange.has_value() && !emptyRange->has_value(), "bodyless2XX range applies when no exact status overrides it");

#if VALIO_SMOKE_ASYNC
    // 5. Async-обёртка (std::future).
    std::future<ValIO::Expected<Petstore::Order, ValIO::Error>> asyncResult = client.GetOrderByIdAsync(42);
    const auto asyncFetched = asyncResult.get();
    Check(asyncFetched.has_value() && asyncFetched->OrderId == 42, "async wrapper resolves");
#endif

    const auto active = VALIO_CALL(GetActive, Petstore::Level::High);
    Check(active.has_value() && *active, "primitive response and integer enum query wire value");

    const auto omittedParameters = VALIO_CALL(Browse, 0, 0);
    Check(omittedParameters.has_value() && *omittedParameters,
        "generated optional query and header arguments omit absent values while required nullable zero is present");
    const std::vector<std::int32_t> numbers{0, 2};
    const std::vector<bool> switches{false, true};
    const std::vector<Petstore::OrderStatus> states{Petstore::OrderStatus::Placed, Petstore::OrderStatus::Approved};
    const std::optional<std::optional<std::string>> presentEmptyText(std::in_place, std::string{});
    const auto presentParameters = VALIO_CALL(Browse, 1, 0, false, 0, 0.0, std::string{},
        numbers, switches, states, numbers, numbers, numbers, order, presentEmptyText,
        false, 0, std::string{}, numbers, states);
    Check(presentParameters.has_value() && *presentParameters,
        "generated typed parameters preserve false zero empty text enum and model values with declared array encodings");
    const std::vector<std::int32_t> emptyNumbers;
    const std::vector<bool> emptySwitches;
    const std::vector<Petstore::OrderStatus> emptyStates;
    const auto emptyParameters = VALIO_CALL(Browse, 2, 0, std::nullopt, std::nullopt, std::nullopt, std::nullopt,
        emptyNumbers, emptySwitches, emptyStates, emptyNumbers, emptyNumbers, emptyNumbers, std::nullopt, std::nullopt,
        std::nullopt, std::nullopt, std::nullopt, emptyNumbers, emptyStates);
    Check(emptyParameters.has_value() && *emptyParameters,
        "generated empty repeated arrays omit keys and empty separated arrays retain empty values");
    const int beforeInvalidParameters = browseRequests.load();
    const auto nullRequiredParameter = VALIO_CALL(Browse, 3, std::nullopt);
    Check(!nullRequiredParameter && nullRequiredParameter.error().What == ValIO::Error::Code::Serialization,
        "explicit null required query value produces a typed serialization error");
    const std::optional<std::optional<std::string>> presentNullText(std::in_place, std::nullopt);
    const auto nullOptionalParameter = VALIO_CALL(Browse, 3, 0, std::nullopt, std::nullopt, std::nullopt, std::nullopt,
        std::nullopt, std::nullopt, std::nullopt, std::nullopt, std::nullopt, std::nullopt, std::nullopt, presentNullText);
    Check(!nullOptionalParameter && nullOptionalParameter.error().What == ValIO::Error::Code::Serialization,
        "explicit null optional query value is rejected instead of being confused with absence");
    const auto nonFiniteParameter = VALIO_CALL(Browse, 3, 0, std::nullopt, std::nullopt, std::numeric_limits<double>::infinity());
    Check(!nonFiniteParameter && nonFiniteParameter.error().What == ValIO::Error::Code::Serialization,
        "non-finite query numbers produce a typed serialization error");
    Check(browseRequests.load() == beforeInvalidParameters, "invalid generated HTTP parameters fail before HTTP dispatch");

    Petstore::Upload upload;
    upload.Title = "----ValIOBoundary";
    upload.Files = {{0, 1, 2}, {3, 4, 5}};
    const auto uploadPost = VALIO_CALL(UploadPost, upload);
    const auto uploadPut = VALIO_CALL(UploadPut, upload);
    const auto uploadPatch = VALIO_CALL(UploadPatch, upload);
    Check(uploadPost.has_value() && *uploadPost, "POST multipart retains optional text and repeated binary parts");
    Check(uploadPut.has_value() && *uploadPut, "PUT multipart retains text and repeated binary parts");
    Check(uploadPatch.has_value() && *uploadPatch, "PATCH multipart retains text and repeated binary parts");

    const auto nullable = ValIO::Parse<Petstore::CodecProbe>(
        R"({"required_null":null,"optional_null":null,"nullable_items":[null,"text"],"color":"red"})");
    Check(nullable.has_value(), "nullable model with nested nullable items parses");
    if (nullable) {
        Check(!nullable->RequiredNullable, "required nullable key retains explicit null");
        Check(nullable->OptionalNullable.has_value() && !*nullable->OptionalNullable,
            "optional nullable key distinguishes present null from missing");
        Check(nullable->NullableItems.size() == 2 && !nullable->NullableItems[0]
            && nullable->NullableItems[1] == "text", "nullable array elements retain values");
        Check(nullable->Color == Petstore::Color::Red, "second string enum has a distinct parser");
        const auto json = nlohmann::json::parse(ValIO::Dump(*nullable));
        Check(json.at("required_null").is_null() && json.at("optional_null").is_null(), "explicit null keys round-trip");

        auto reused = *nullable;
        nlohmann::json::parse(R"({"required_null":"value","nullable_items":[]})").get_to(reused);
        Check(!reused.OptionalNullable && !reused.Color, "deserialization resets missing optional fields on reused objects");
    }
    const auto absentRequired = ValIO::Parse<Petstore::CodecProbe>(R"({"nullable_items":[]})");
    Check(!absentRequired && absentRequired.error().What == ValIO::Error::Code::Parse, "missing required nullable key is rejected");
    const auto invalidOptional = ValIO::Parse<Petstore::Order>(R"({"order_id":1,"status":"placed","note":null})");
    Check(!invalidOptional, "optional non-nullable field rejects explicit null");
    Check(ValIO::Dump(Petstore::Empty{}) == "{}", "empty model serializes as an object");
    const auto invalidEmpty = ValIO::Parse<Petstore::Empty>("[]");
    Check(!invalidEmpty, "empty model rejects a non-object JSON value");
    const auto inherited = ValIO::Parse<Petstore::SingleBase>(R"({"name":"base"})");
    Check(inherited.has_value() && inherited->Name == "base", "single-base allOf codec delegates to base");
    const auto combined = ValIO::Parse<Petstore::Combined>(R"({"name":"base","id":12})");
    Check(combined.has_value() && combined->Name == "base" && combined->Id == 12, "multiple-base allOf fields flatten without losing base properties");
    if (combined) {
        const auto flattened = nlohmann::json::parse(ValIO::Dump(*combined));
        Check(flattened.at("name") == "base" && flattened.at("id") == 12, "multiple-base allOf wire properties round-trip");
    }

#if VALIO_SMOKE_ASYNC
    auto firstConcurrent = client.GetOrderByIdAsync(42);
    auto secondConcurrent = secondClient.GetOrderByIdAsync(42);
    Check(firstConcurrent.get().has_value() && secondConcurrent.get().has_value(), "concurrent generated clients safely reuse a shared connection");
    std::future<ValIO::Expected<Petstore::Order, ValIO::Error>> detached;
    {
        auto temporaryTransport = std::make_shared<ValIO::HttpClient>(config);
        Petstore::StoreClient temporary(temporaryTransport);
        detached = temporary.GetOrderByIdAsync(42);
    }
    const auto detachedResult = detached.get();
    Check(detachedResult.has_value(), "async operation owns transport beyond client destruction");
#endif

    server.stop();
    serverThread.join();

    if (g_failures == 0) {
        std::printf("ALL SMOKE CHECKS PASSED\n");
    } else {
        std::printf("%d SMOKE CHECK(S) FAILED\n", g_failures);
    }
    return g_failures;
}
