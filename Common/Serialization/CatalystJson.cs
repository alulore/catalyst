using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Catalyst.Commands.Api;
using Catalyst.Commands.Gen;
using Catalyst.Commands.Mcp;
using Catalyst.Common.Configuration;

namespace Catalyst.Common.Serialization;

/// <summary>
/// The source-generated serializer metadata of every JSON document the CLI reads or writes, so nothing is
/// serialized by reflection and the code stays safe to trim and to compile ahead of time.
/// </summary>
[JsonSerializable(typeof(RunReport))]
[JsonSerializable(typeof(ApiReport))]
[JsonSerializable(typeof(ListClientsReport))]
[JsonSerializable(typeof(ListClientsFailure))]
[JsonSerializable(typeof(CatalystConfig))]
[JsonSerializable(typeof(string))]
internal sealed partial class CatalystJsonContext : JsonSerializerContext
{
}

/// <summary>The serializer settings of each document, each bound to the shared <see cref="CatalystJsonContext"/> metadata.</summary>
internal static class CatalystJson
{
    /// <summary>The gen openapi JSON document and the MCP payloads: camelCase, indented.</summary>
    public static readonly CatalystJsonContext Report = new(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    });

    /// <summary>Like <see cref="Report"/>, but quotes in the TypeScript text stay readable instead of becoming ".</summary>
    public static readonly CatalystJsonContext Api = new(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });

    /// <summary>The catalyst.config.json reader.</summary>
    public static readonly CatalystJsonContext Config = new(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

    /// <summary>Double-quoted TypeScript string literals.</summary>
    public static readonly CatalystJsonContext Literal = new(new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
}
