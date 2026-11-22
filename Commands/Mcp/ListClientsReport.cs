using Catalyst.Common.Models;

namespace Catalyst.Commands.Mcp;

/// <summary>The structured content of a successful <c>list_clients</c> call. Property order is the JSON field order.</summary>
internal sealed record ListClientsReport(
    int SchemaVersion,
    string Command,
    bool Success,
    string? ConfigPath,
    List<ClientSummary> Clients,
    Issue? Error
);
