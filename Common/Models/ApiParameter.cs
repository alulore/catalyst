namespace Catalyst.Common.Models;

/// <summary>One parameter of a generated method (path or query), in the order of the generated signature.</summary>
internal sealed record ApiParameter(
    string Name,
    string WireName,
    string In,
    bool Required,
    string Type,
    string? Description
);
