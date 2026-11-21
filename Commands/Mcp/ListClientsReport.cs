using Catalyst.Common.Models;

namespace Catalyst.Commands.Mcp;

/// <summary>One configured client as <c>list_clients</c> reports it.</summary>
internal sealed record ClientSummary(string Name, string Source, string Output);

/// <summary>The structured content of a successful <c>list_clients</c> call. Property order is the JSON field order.</summary>
internal sealed record ListClientsReport(int SchemaVersion, string Command, bool Success, string? ConfigPath, List<ClientSummary> Clients, Issue? Error);

/// <summary>The structured content of a <c>list_clients</c> call whose config could not be read.</summary>
internal sealed record ListClientsFailure(int SchemaVersion, string Command, bool Success, Issue Error);
