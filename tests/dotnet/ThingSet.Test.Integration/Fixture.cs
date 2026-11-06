/*
 * Copyright (c) 2026 Gareth Potter.
 *
 * SPDX-License-Identifier: Apache-2.0
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ThingSet.Test.Integration;

/// <summary>
/// The language-neutral fixture contract in fixtures/fixture.json.
/// </summary>
public static class Fixture
{
    public static DirectoryInfo RepoRoot { get; } = FindRepoRoot();

    public static JsonObject Contract { get; } =
        JsonNode.Parse(File.ReadAllText(Path.Combine(RepoRoot.FullName, "fixtures", "fixture.json")))!.AsObject();

    public static int TcpPort => (int)Contract["tcpPort"]!;

    public static uint UnknownId => (uint)Contract["unknownId"]!;

    public static IEnumerable<JsonObject> Groups => Contract["groups"]!.AsArray().Select(n => n!.AsObject());

    public static IEnumerable<JsonObject> Properties => Contract["properties"]!.AsArray().Select(n => n!.AsObject());

    public static IEnumerable<JsonObject> Functions => Contract["functions"]!.AsArray().Select(n => n!.AsObject());

    public static IEnumerable<uint> ReportIds => Contract["reports"]!["ids"]!.AsArray().Select(n => (uint)n!);

    public static JsonObject Function(string path) => Functions.Single(f => (string)f["path"]! == path);

    /// <summary>
    /// Converts a JSON value from the fixture into the CLR value to send to the server
    /// for a property of the given ThingSet type.
    /// </summary>
    public static object ToClr(JsonNode? value, string type) => type switch
    {
        "f32" => (float)value!,
        "f64" => (double)value!,
        "i32" => (int)value!,
        "u32" => (uint)value!,
        "i64" => (long)value!,
        "u64" => (ulong)value!,
        "bool" => (bool)value!,
        "string" => (string)value!,
        "f32[]" => value!.AsArray().Select(v => (float)v!).ToArray(),
        _ => throw new NotSupportedException($"Fixture type {type} not handled"),
    };

    /// <summary>
    /// Normalises a value (from the fixture or from a client) so numbers of
    /// different CLR types and different collection types compare equal.
    /// </summary>
    public static object? Normalise(object? value) => value switch
    {
        null => null,
        JsonValue json when json.GetValueKind() == JsonValueKind.Number => (decimal)json.GetValue<double>(),
        JsonValue json when json.GetValueKind() is JsonValueKind.True or JsonValueKind.False => json.GetValue<bool>(),
        JsonValue json when json.GetValueKind() == JsonValueKind.String => json.GetValue<string>(),
        JsonArray array => array.Select(Normalise).ToList(),
        string s => s,
        bool b => b,
        float f => (decimal)f,
        double d => (decimal)d,
        IConvertible c when value.GetType().IsPrimitive => Convert.ToDecimal(c),
        System.Collections.IEnumerable e => e.Cast<object?>().Select(Normalise).ToList(),
        _ => value,
    };

    private static DirectoryInfo FindRepoRoot()
    {
        for (DirectoryInfo? folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "fixtures", "fixture.json")))
            {
                return folder;
            }
        }
        throw new InvalidOperationException($"Could not find fixtures/fixture.json above {AppContext.BaseDirectory}.");
    }
}
