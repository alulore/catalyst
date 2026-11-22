using System.Text.Encodings.Web;
using System.Text.Json;

namespace Catalyst.Common.Serialization;

/// <summary>The serializer settings of each document, each bound to the shared <see cref="CatalystJsonContext"/> metadata.</summary>
internal static class CatalystJson
{
    /// <summary>The gen openapi JSON document and the MCP payloads: camelCase, indented.</summary>
    public static readonly CatalystJsonContext Report = new(
        new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        }
    );

    /// <summary>Like <see cref="Report"/>, but quotes in the TypeScript text stay readable instead of becoming ".</summary>
    public static readonly CatalystJsonContext Api = new(
        new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }
    );

    /// <summary>The catalyst.config.json reader.</summary>
    public static readonly CatalystJsonContext Config = new(
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
    );

    /// <summary>Double-quoted TypeScript string literals.</summary>
    public static readonly CatalystJsonContext Literal = new(
        new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }
    );
}
