using System.Text.Json.Serialization;

namespace Catalyst.Common.Models;

/// <summary>A named type of the generated file with its rendered TypeScript and the declared types it uses.</summary>
internal sealed record TypeDeclaration(
    string Name,
    string Kind,
    string? ComponentName,
    string Declaration,
    List<string> DependsOn
)
{
    /// <summary>Interface properties (name as in the spec, TypeScript type, required); for comparing versions of a type.</summary>
    [JsonIgnore]
    public List<TypeMember> Members { get; init; } = [];

    /// <summary>The right-hand side of a type alias.</summary>
    [JsonIgnore]
    public string? AliasType { get; init; }

    /// <summary>The values of a string enum.</summary>
    [JsonIgnore]
    public List<string> Values { get; init; } = [];
}
