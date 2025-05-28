/*
 * Copyright (c) 2025 Brill Power.
 *
 * SPDX-License-Identifier: Apache-2.0
 */
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Mono.Unix.Native;
using ThingSet.Client;
using ThingSet.Common.Transports.Ip;

namespace ThingSet.Test.Integration;

public class AsioServerTest
{
    private Process? _serverProcess;

    [SetUp]
    public void Setup()
    {
        // find server binary
        FileInfo assembyLocation = new FileInfo(Assembly.GetExecutingAssembly().Location);
        DirectoryInfo? folder = assembyLocation.Directory;
        DirectoryInfo? rootFolder = null;
        while (folder is not null)
        {
            if (folder.Name == "thingset-integration-testing")
            {
                rootFolder = folder;
                break;
            }
            folder = folder.Parent;
        }
        if (rootFolder is null)
        {
            throw new InvalidOperationException($"Could not find root folder from {assembyLocation}.");
        }
        string serverBinary = Path.Combine(rootFolder.ToString(), "thingsetplusplus", "examples", "asioserver", "build", "asioserver");
        if (!File.Exists(serverBinary))
        {
            throw new FileNotFoundException($"Could not find server binary in expected location ({serverBinary}).");
        }
        ProcessStartInfo startInfo = new ProcessStartInfo(serverBinary)
        {
            RedirectStandardInput = true,
        };
        _serverProcess = Process.Start(startInfo);
        Thread.Sleep(100);
    }

    [Test]
    public async Task TestVarious()
    {
        IpClientTransport transport = new IpClientTransport("127.0.0.1", 9001);
        ThingSetClient client = new ThingSetClient(transport);
        await client.ConnectAsync();

        ManualResetEvent receivedPublication = new ManualResetEvent(false);
        await client.SubscribeAsync((id, path, value) =>
        {
            receivedPublication.Set();
        });

        // get a value
        object? value = client.Get(0x300);
        Assert.That(value, Is.Not.Null);
        Assert.That((float)value, Is.EqualTo(24.0f));

        // run a function
        value = client.Exec(0x1000, 2, 3);
        Assert.That(value, Is.Not.Null);
        Assert.That((uint)value, Is.EqualTo(5));

        // update a value
        client.Update("totalVoltage", 25.0f);

        // check update succeeded
        value = client.Get(0x300);
        Assert.That(value, Is.Not.Null);
        Assert.That((float)value, Is.EqualTo(25.0f));

        // count of modules
        value = client.Get(0x620);
        Assert.That(value, Is.EqualTo(2));

        // should not need to wait too long for this
        if (!receivedPublication.WaitOne(TimeSpan.FromSeconds(2)))
        {
            Assert.Fail("Timed out waiting for a publication.");
        }
    }

    [TearDown]
    public void Shutdown()
    {
        if (_serverProcess is not null)
        {
            Syscall.kill(_serverProcess.Id, Signum.SIGINT);
            _serverProcess.Dispose();
        }
    }
}
