namespace Catalyst.Commands.Gen;

internal sealed record SpecSummary(
    string? Title,
    string? Version,
    string? Openapi,
    int PathCount,
    int SchemaCount,
    List<SpecOperation> Operations
);
