/*
 * Copyright (c) 2026 Gareth Potter.
 *
 * SPDX-License-Identifier: Apache-2.0
 */

// ThingSet fixture server for cross-language integration tests.
//
// The object tree here must match fixtures/fixture.json. Client test suites read
// that file for IDs, paths and expected values.
//
// Usage: thingset-fixture-server [--exit-with-parent] [bind-interface]
// Prints "READY" on stdout once the TCP listener is accepting connections.
//
// --exit-with-parent: exit when stdin reaches EOF. Test harnesses start the
// server with stdin as a pipe, so if the harness dies without tearing down,
// the pipe closes and the server goes too, rather than holding the ports.

#include <asio.hpp>
#include <chrono>
#include <cstdint>
#include <cstring>
#include <functional>
#include <iostream>
#include <limits>
#include <thread>
#include <thingset++/ThingSet.hpp>
#include <thingset++/ThingSetServer.hpp>
#include <thingset++/ip/asio/ThingSetAsyncSocketServerTransport.hpp>

using namespace ThingSet;
using namespace ThingSet::Ip;
using namespace ThingSet::Ip::Async;

static constexpr auto reportInterval = std::chrono::milliseconds(250);

// note that these are also defined in ../fixtures/fixture.json for
// consumption by C# and Python client tests
// (TODO: could we just generate these C++ definitions from the JSON as well?)

ThingSetGroup<0x200, 0, "Measurements"> measurements;
ThingSetReadWriteProperty<float> voltage { 0x201, 0x200, "voltage" };
ThingSetReadWriteProperty<float> current { 0x202, 0x200, "current" };
ThingSetReadWriteProperty<int32_t> temperature { 0x203, 0x200, "temperature" };
ThingSetReadOnlyProperty<uint32_t> uptime { 0x204, 0x200, "uptime" };
ThingSetReadWriteProperty<uint64_t> errorFlags { 0x205, 0x200, "errorFlags" };

ThingSetGroup<0x300, 0, "Config"> config;
ThingSetReadOnlyProperty name { 0x301, 0x300, "name", "fixture" };
ThingSetReadWriteProperty<bool> enabled { 0x302, 0x300, "enabled" };
ThingSetReadWriteProperty<std::array<float, 4>> cellVoltages { 0x303, 0x300, "cellVoltages" };

static void reset()
{
    voltage = 24.0f;
    current = -1.5f;
    temperature = -20;
    uptime = 12345u;
    errorFlags = 0u;
    enabled = true;
    cellVoltages = std::array<float, 4> { 3.25f, 3.125f, 3.5f, 3.0f };
}

ThingSetUserFunction<0x1000, 0, "xAdd", int32_t, int32_t, int32_t> xAdd([](int32_t x, int32_t y) { return x + y; });
ThingSetUserFunction<0x1010, 0, "xMultiply", float, float, float> xMultiply([](float x, float y) { return x * y; });
ThingSetUserFunction<0x1020, 0, "xReset", void> xReset(reset);

using Server = ThingSetServer<asio::ip::tcp::endpoint, THINGSET_STREAMING_MSG_SIZE,
                              StreamingUdpThingSetBinaryEncoder<asio::ip::tcp::endpoint>>;

static void publishReport(const asio::error_code &error, asio::steady_timer *timer, Server *server)
{
    if (error) {
        return;
    }
    server->publish(voltage, current);
    timer->expires_at(timer->expiry() + reportInterval);
    timer->async_wait(std::bind(publishReport, std::placeholders::_1, timer, server));
}

static ThingSetAsyncSocketServerTransport makeTransport(asio::io_context &ioContext, const char *bindInterface)
{
    if (bindInterface != nullptr) {
        return ThingSetAsyncSocketServerTransport(ioContext, std::string(bindInterface));
    }
    return ThingSetAsyncSocketServerTransport(ioContext);
}

int main(int argc, char *argv[])
{
    bool exitWithParent = false;
    const char *bindInterface = nullptr;
    for (int i = 1; i < argc; i++) {
        if (std::strcmp(argv[i], "--exit-with-parent") == 0) {
            exitWithParent = true;
        } else {
            bindInterface = argv[i];
        }
    }

    reset();

    asio::io_context ioContext(1);
    ThingSetAsyncSocketServerTransport transport = makeTransport(ioContext, bindInterface);
    auto server = ThingSetServerBuilder::build(transport);

    asio::steady_timer timer(ioContext, reportInterval);
    timer.async_wait(std::bind(publishReport, std::placeholders::_1, &timer, &server));

    if (exitWithParent) {
        std::thread([&ioContext] {
            std::cin.ignore(std::numeric_limits<std::streamsize>::max());
            ioContext.stop(); // thread-safe
        }).detach();
    }

    server.listen();
    // listen() co_spawns the acceptor coroutine; it binds port 9001 the first
    // time the io_context runs. This handler is queued after it, so by the time
    // it runs the listener is ready.
    asio::post(ioContext, [] { std::cout << "READY" << std::endl; });

    ioContext.run();
    return 0;
}
