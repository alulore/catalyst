using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Catalyst.Common.Models;

namespace Catalyst.Commands.Gen;

/// <summary>The outcome for one client (the single --source client or one configured client).</summary>
internal sealed class ClientResult
{
    public string? Name { get; init; }
    public required string Source { get; init; }
    public string? OutputPath { get; set; }
    public required string Status { get; set; }
    public List<Issue> Warnings { get; } = [];
    public Issue? Error { get; set; }

    /// <summary>Paths skipped by the configured prefixes; shown as an info line, not a warning, in the text output.</summary>
    [JsonIgnore]
    public int SkippedPathCount { get; set; }

    [JsonIgnore]
    [MemberNotNullWhen(true, nameof(Error))]
    [MemberNotNullWhen(false, nameof(OutputPath))]
    public bool Failed => Status == ClientStatus.Failed;

    public static ClientResult Fail(
        string? name,
        string source,
        string code,
        string message,
        string? outputPath = null
    )
    {
        return new ClientResult
        {
            Name = name,
            Source = source,
            OutputPath = outputPath,
            Status = ClientStatus.Failed,
            Error = new Issue(code, message),
        };
    }
}
