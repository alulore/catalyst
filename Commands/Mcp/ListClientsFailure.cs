using Catalyst.Common.Models;

namespace Catalyst.Commands.Mcp;

/// <summary>The structured content of a <c>list_clients</c> call whose config could not be read.</summary>
internal sealed record ListClientsFailure(
    int SchemaVersion,
    string Command,
    bool Success,
    Issue Error
);
