/*
 * Copyright (c) 2026 Gareth Potter.
 *
 * SPDX-License-Identifier: Apache-2.0
 */
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;

namespace ThingSet.Test.Integration;

/// <summary>
/// Starts the C++ fixture server once for the whole test run.
/// </summary>
[SetUpFixture]
public class FixtureServer
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(10);

    private Process? _process;

    [OneTimeSetUp]
    public void Start()
    {
        string binary = Environment.GetEnvironmentVariable("THINGSET_FIXTURE_SERVER")
            ?? Path.Combine(Fixture.RepoRoot.FullName, "build", "cpp-server", "thingset-fixture-server");
        if (!File.Exists(binary))
        {
            throw new FileNotFoundException(
                $"Fixture server not found at {binary}. Build it with " +
                "`cmake -S fixtures/cpp-server -B build/cpp-server && cmake --build build/cpp-server`, " +
                "or set THINGSET_FIXTURE_SERVER.");
        }

        using ManualResetEventSlim ready = new ManualResetEventSlim(false);
        _process = new Process
        {
            // stdin is a pipe we never write to: if the test host dies, it closes and the server exits.
            StartInfo = new ProcessStartInfo(binary, "--exit-with-parent")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            },
        };
        // Keep draining stdout so the server never blocks on a full pipe.
        _process.OutputDataReceived += (_, e) =>
        {
            if (e.Data?.Trim() == "READY")
            {
                ready.Set();
            }
        };
        _process.Start();
        _process.BeginOutputReadLine();

        if (!ready.Wait(StartupTimeout))
        {
            Stop();
            throw new InvalidOperationException($"Fixture server did not print READY within {StartupTimeout}.");
        }

        // Belt and braces: make sure the port really accepts connections.
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                using TcpClient probe = new TcpClient("127.0.0.1", Fixture.TcpPort);
                return;
            }
            catch (SocketException) when (stopwatch.Elapsed < StartupTimeout)
            {
                Thread.Sleep(50);
            }
        }
    }

    [OneTimeTearDown]
    public void Stop()
    {
        if (_process is not null)
        {
            if (!_process.HasExited)
            {
                _process.Kill();
                _process.WaitForExit(5000);
            }
            _process.Dispose();
            _process = null;
        }
    }
}
