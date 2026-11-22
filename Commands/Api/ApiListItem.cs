namespace Catalyst.Commands.Api;

internal sealed record ApiListItem(
    string Method,
    string Path,
    string? OperationId,
    string? Summary,
    bool Deprecated,
    List<string> Tags,
    string Call
);
