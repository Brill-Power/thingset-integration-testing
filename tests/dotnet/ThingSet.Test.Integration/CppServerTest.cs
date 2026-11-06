/*
 * Copyright (c) 2026 Gareth Potter.
 *
 * SPDX-License-Identifier: Apache-2.0
 */
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ThingSet.Client;
using ThingSet.Client.Schema;
using ThingSet.Common.Transports.Ip;

namespace ThingSet.Test.Integration;

/// <summary>
/// ThingSet.Net client against the thingset++ fixture server.
/// </summary>
public class CppServerTest
{
    private ThingSetClient _client = null!;

    public static IEnumerable<TestCaseData> AllProperties =>
        Fixture.Properties.Select(p => new TestCaseData(p).SetArgDisplayNames((string)p["path"]!));

    public static IEnumerable<TestCaseData> WritableProperties =>
        Fixture.Properties.Where(p => (bool)p["writable"]!)
            .Select(p => new TestCaseData(p).SetArgDisplayNames((string)p["path"]!));

    public static IEnumerable<TestCaseData> ReadOnlyProperties =>
        Fixture.Properties.Where(p => !(bool)p["writable"]!)
            .Select(p => new TestCaseData(p).SetArgDisplayNames((string)p["path"]!));

    public static IEnumerable<TestCaseData> FunctionCases =>
        Fixture.Functions.SelectMany(f => f["cases"]!.AsArray().Select(c => new TestCaseData(f, c!.AsObject())
            .SetArgDisplayNames($"{f["path"]}{c!["args"]!.ToJsonString()}")));

    [SetUp]
    public async Task Connect()
    {
        _client = new ThingSetClient(new IpClientTransport("127.0.0.1", Fixture.TcpPort));
        await _client.ConnectAsync();
        _client.Exec((uint)Fixture.Function("xReset")["id"]!);
    }

    [TearDown]
    public void Disconnect() => _client.Dispose();

    [TestCaseSource(nameof(AllProperties))]
    public void GetById(JsonObject property)
    {
        object? value = _client.Get((uint)property["id"]!);
        Assert.That(Fixture.Normalise(value), Is.EqualTo(Fixture.Normalise(property["initial"])));
    }

    [TestCaseSource(nameof(AllProperties))]
    public void GetByPath(JsonObject property)
    {
        object? value = _client.Get((string)property["path"]!);
        Assert.That(Fixture.Normalise(value), Is.EqualTo(Fixture.Normalise(property["initial"])));
    }

    [TestCaseSource(nameof(WritableProperties))]
    public void UpdateByPath(JsonObject property)
    {
        _client.Update((string)property["path"]!, Fixture.ToClr(property["update"], (string)property["type"]!));
        object? value = _client.Get((uint)property["id"]!);
        Assert.That(Fixture.Normalise(value), Is.EqualTo(Fixture.Normalise(property["update"])));
    }

    [TestCaseSource(nameof(ReadOnlyProperties))]
    public void UpdateReadOnlyIsRejected(JsonObject property)
    {
        string type = (string)property["type"]!;
        object attempt = type == "string" ? "changed" : Fixture.ToClr(JsonNode.Parse("1"), type);
        Assert.Throws<ThingSetException>(() => _client.Update((string)property["path"]!, attempt));
        object? value = _client.Get((uint)property["id"]!);
        Assert.That(Fixture.Normalise(value), Is.EqualTo(Fixture.Normalise(property["initial"])));
    }

    [TestCaseSource(nameof(FunctionCases))]
    public void Exec(JsonObject function, JsonObject testCase)
    {
        string argType = ((string)function["type"]!).TrimStart('(').Split(')')[0].Split(',')[0];
        object[] args = testCase["args"]!.AsArray().Select(a => Fixture.ToClr(a, argType)).ToArray();
        object? result = _client.Exec((uint)function["id"]!, args);
        Assert.That(Fixture.Normalise(result), Is.EqualTo(Fixture.Normalise(testCase["result"])));
    }

    [Test]
    public void GetUnknownIdIsNotFound()
    {
        ThingSetException ex = Assert.Throws<ThingSetException>(() => _client.Get(Fixture.UnknownId))!;
        Assert.That((byte)ex.ErrorCode, Is.EqualTo(0xA4));
    }

    [Test]
    public void DiscoveredSchemaMatchesFixture()
    {
        Dictionary<uint, ThingSetNode> nodes = _client
            .GetNodes(ThingSetNodeEnumerationOptions.All)
            .SelectMany(Flatten)
            .GroupBy(n => n.Id)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (JsonObject item in Fixture.Groups)
        {
            Assert.That(nodes.ContainsKey((uint)item["id"]!), $"group {item["name"]} missing");
            Assert.That(nodes[(uint)item["id"]!].Name, Is.EqualTo((string)item["name"]!));
        }
        foreach (JsonObject item in Fixture.Properties.Concat(Fixture.Functions))
        {
            string path = (string)item["path"]!;
            Assert.That(nodes.ContainsKey((uint)item["id"]!), $"{path} missing");
            Assert.That(nodes[(uint)item["id"]!].Name, Is.EqualTo(path.Split('/').Last()));
        }
    }

    [Test]
    public async Task ReceivesReports()
    {
        HashSet<uint> expected = Fixture.ReportIds.ToHashSet();
        ConcurrentDictionary<uint, object?> received = new ConcurrentDictionary<uint, object?>();
        using ManualResetEventSlim gotAll = new ManualResetEventSlim(false);
        await _client.SubscribeAsync((id, _, value) =>
        {
            received[id] = value;
            if (expected.IsSubsetOf(received.Keys))
            {
                gotAll.Set();
            }
        });

        Assert.That(gotAll.Wait(TimeSpan.FromSeconds(2)), Is.True,
            $"Timed out waiting for reports; got ids [{string.Join(", ", received.Keys.Select(k => $"0x{k:X}"))}].");
    }

    private static IEnumerable<ThingSetNode> Flatten(ThingSetNode node) =>
        new[] { node }.Concat(node.Children.SelectMany(Flatten));
}
