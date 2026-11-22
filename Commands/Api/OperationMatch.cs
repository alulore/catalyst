using System.Diagnostics.CodeAnalysis;
using Catalyst.Common.Models;

namespace Catalyst.Commands.Api;

/// <summary>The outcome of resolving an operation selector: one operation, or an error code with suggestions.</summary>
internal sealed record OperationMatch(
    ApiOperation? Operation,
    string? ErrorCode,
    string? Message,
    List<string> Suggestions
)
{
    [MemberNotNullWhen(true, nameof(Operation))]
    [MemberNotNullWhen(false, nameof(ErrorCode), nameof(Message))]
    public bool IsFound => Operation is not null;
}
