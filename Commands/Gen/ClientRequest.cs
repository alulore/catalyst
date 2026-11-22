namespace Catalyst.Commands.Gen;

/// <summary>What to generate for one client; the same for the single --source client and a configured one.</summary>
internal sealed record ClientRequest(
    string? Name,
    string Source,
    string Output,
    string? ClassName,
    string Language,
    List<string> SkipPaths,
    bool Insecure
);
