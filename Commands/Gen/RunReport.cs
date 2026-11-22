using System.Text.Json.Serialization;
using Catalyst.Common.Models;

namespace Catalyst.Commands.Gen;

/// <summary>
/// Everything a run of <c>gen openapi</c> found out. The command builds this; <see cref="ReportRenderer"/>
/// prints it as text or as the JSON document described in the README. Property order is the JSON field order.
/// </summary>
internal sealed class RunReport
{
    public int SchemaVersion { get; } = 1;
    public string Command { get; } = "gen openapi";
    public required string Mode { get; init; }
    public bool Success { get; set; }
    public int ExitCode { get; set; }
    public List<ClientResult> Results { get; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SpecSummary? Spec { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<DiagnosticEntry>? Diagnostics { get; set; }

    public Issue? Error { get; set; }

    /// <summary>The config file a configured run read; shown in the text output only.</summary>
    [JsonIgnore]
    public string? ConfigPath { get; set; }

    /// <summary>The parsed document of a validate run, for the text rendering of the spec overview.</summary>
    [JsonIgnore]
    public (string Source, OpenApiParseResult Parsed)? Parsed { get; set; }

    [JsonIgnore]
    public bool SingleClient { get; set; }

    /// <summary>The exit code of every usage error (the conventional code for misuse of a command).</summary>
    public const int UsageExitCode = 2;

    /// <summary>The conventional exit code for a process stopped with Ctrl+C (128 + SIGINT).</summary>
    public const int CancelledExitCode = 130;

    /// <summary>Fills success and exitCode from the results and the top-level error.</summary>
    public RunReport Complete()
    {
        bool ok =
            Error == null
            && Results.All(result =>
                Mode == RunMode.Check ? result.Status == ClientStatus.UpToDate : !result.Failed
            );

        Success = ok;

        if (Error?.Code == IssueCodes.Cancelled)
        {
            ExitCode = CancelledExitCode;
        }
        else if (Error?.Code == IssueCodes.Usage)
        {
            ExitCode = UsageExitCode;
        }
        else
        {
            ExitCode = ok ? 0 : 1;
        }

        return this;
    }

    public static RunReport Failure(string mode, string code, string message)
    {
        return new RunReport { Mode = mode, Error = new Issue(code, message) }.Complete();
    }
}
