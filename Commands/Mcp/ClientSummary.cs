namespace Catalyst.Commands.Mcp;

/// <summary>One configured client as <c>list_clients</c> reports it.</summary>
internal sealed record ClientSummary(string Name, string Source, string Output);
