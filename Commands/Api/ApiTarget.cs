namespace Catalyst.Commands.Api;

/// <summary>Where the spec of an <c>api</c> command comes from, and how its client would be generated.</summary>
internal sealed record ApiTarget(
    string Source,
    string? ClassName,
    List<string> SkipPaths,
    bool Insecure
);
