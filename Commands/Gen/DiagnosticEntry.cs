namespace Catalyst.Commands.Gen;

internal sealed record DiagnosticEntry(string Severity, string? Pointer, string Message);
