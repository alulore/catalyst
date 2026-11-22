using System.ComponentModel;
using Spectre.Console.Cli;

namespace Catalyst.Commands.Api;

/// <summary>Options shared by every <c>api</c> command.</summary>
internal class ApiSettings : CommandSettings
{
    [CommandOption("--source <SOURCE>")]
    [Description(
        "Path to a local JSON or YAML file, or a URL, of an OpenAPI specification. Without --source or --client the only client in catalyst.config.json is used."
    )]
    public string? Source { get; init; }

    [CommandOption("--client <NAME>")]
    [Description(
        "Name of a client in catalyst.config.json: its source, skip paths and name are used, so the output matches the generated client."
    )]
    public string? Client { get; init; }

    [CommandOption("--insecure")]
    [Description(
        "Skip TLS certificate validation when fetching a remote spec over HTTPS (local servers with self-signed certificates only)."
    )]
    public bool Insecure { get; init; }

    [CommandOption("--json")]
    [Description("Print one JSON document on stdout instead of text (see the README).")]
    public bool Json { get; init; }
}
