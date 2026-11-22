namespace Catalyst.Common.Models;

/// <summary>A machine-readable problem or warning: a stable <see cref="IssueCodes"/> value plus the human text.</summary>
internal sealed record Issue(string Code, string Message);
