#include "Clients/OrdersClient.h"
#include "Interfaces/IOrders.h"
#include "Runtime/ValIOHttp.h"
#include <httplib.h>
#include <atomic>
#include <iostream>
#include <memory>
#include <optional>
#include <stdexcept>
#include <string>
#include <thread>

namespace {

/// @brief Owns the local demonstration server and always joins its worker.
class LocalServer final {
public:
    LocalServer() {
        server_.Post("/refresh", [](const httplib::Request&, httplib::Response& response) {
            response.set_content("fresh", "text/plain");
        });
        server_.Get(R"(/orders/(\d+))", [this](const httplib::Request& request, httplib::Response& response) {
            ++attempts_;
            if (request.get_header_value("Authorization") != "Bearer fresh") {
                response.status = 401;
                response.set_content("expired", "text/plain");
                return;
            }
            response.set_content(R"({"order_id":42,"state":"new","nullable_note":null,"nullable_items":[]})",
                                 "application/json");
        });
        port_ = server_.bind_to_any_port("127.0.0.1");
        if (port_ <= 0) throw std::runtime_error("Cannot bind the local example server");
        worker_ = std::thread([this] { server_.listen_after_bind(); });
    }

    ~LocalServer() {
        server_.stop();
        if (worker_.joinable()) worker_.join();
    }

    LocalServer(const LocalServer&) = delete;
    LocalServer& operator=(const LocalServer&) = delete;

    std::string BaseUrl() const { return "http://127.0.0.1:" + std::to_string(port_); }
    int Attempts() const { return attempts_.load(); }

private:
    httplib::Server server_;
    std::thread worker_;
    std::atomic<int> attempts_{0};
    int port_ = 0;
};

/// @brief Keeps demonstration authentication state without creating an ownership cycle.
struct Authentication {
    std::string Token = "expired";
    std::weak_ptr<ValIO::HttpClient> Transport;
};

} // namespace

int main() {
    LocalServer server;
    const auto authentication = std::make_shared<Authentication>();
    ValIO::HttpConfig config;
    config.BaseUrl = server.BaseUrl();
    config.Pipeline.MaxRetries = 1;
    config.Pipeline.BeforeSend = [authentication](ValIO::Request& request, const ValIO::CancellationToken&) {
        request.SetHeader("Authorization", "Bearer " + authentication->Token);
        std::cout << "send " << request.Path << '\n';
    };
    config.Pipeline.AfterReceive = [](const ValIO::Request& request, ValIO::Response& response,
                                      const ValIO::CancellationToken&) {
        std::cout << "receive " << request.Path << ": " << response.StatusCode << '\n';
    };
    config.Pipeline.ShouldRetry = [authentication](ValIO::Request& request, const ValIO::Response& response,
                                                   const std::size_t retries, const ValIO::CancellationToken& token) {
        if (request.Path == "/refresh" || response.StatusCode != 401 || retries != 0) return false;
        const auto transport = authentication->Transport.lock();
        if (!transport) return false;
        ValIO::Request refresh;
        refresh.Verb = ValIO::HttpVerb::Post;
        refresh.Path = "/refresh";
        const auto result = transport->Send(refresh, token);
        if (!result || result->StatusCode != 200) return false;
        authentication->Token = result->Body;
        request.SetHeader("Authorization", "Bearer " + authentication->Token);
        return true;
    };

    const auto transport = std::make_shared<ValIO::HttpClient>(config);
    authentication->Transport = transport;
    const Integration::OrdersClient firstClient(transport);
    const Integration::OrdersClient secondClient(transport);
    const Integration::IOrders& orders = firstClient;
    const auto first = orders.GetOrder(42);
    const auto second = secondClient.GetOrder(42);
    if (!first || !second || first->OrderId != 42 || second->OrderId != 42 || server.Attempts() != 3)
        throw std::runtime_error("Shared transport, refresh/retry, or model parsing failed");

    ValIO::CancellationSource cancellation;
    cancellation.Cancel();
    const auto cancelled = orders.GetOrder(42, std::nullopt, cancellation.GetToken());
    if (cancelled || cancelled.error().What != ValIO::Error::Code::Cancelled || server.Attempts() != 3)
        throw std::runtime_error("Pre-cancelled request reached the server");
    std::cout << "Shared transport, bounded refresh/replay, and cancellation passed\n";
}
