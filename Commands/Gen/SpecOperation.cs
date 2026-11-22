namespace Catalyst.Commands.Gen;

internal sealed record SpecOperation(
    string Method,
    string Path,
    string? OperationId,
    string? Summary
);
