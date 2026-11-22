namespace Catalyst.Commands.Api;

/// <summary>One difference between two versions of the generated client.</summary>
internal sealed record ApiChange(
    string Severity,
    string Kind,
    ApiOperationRef? Operation,
    string? Type,
    string? Member,
    string? Before,
    string? After,
    string Message
);
