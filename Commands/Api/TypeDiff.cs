using Catalyst.Common.Models;

namespace Catalyst.Commands.Api;

/// <summary>The type half of <see cref="ApiDiff"/>: interfaces, enums and aliases compared by name.</summary>
internal static class TypeDiff
{
    public static void Compare(ClientModel before, ClientModel after, List<ApiChange> changes)
    {
        Dictionary<string, TypeDeclaration> afterByName = after.Types.ToDictionary(t => t.Name);
        Dictionary<string, TypeDeclaration> beforeByName = before.Types.ToDictionary(t => t.Name);

        foreach (TypeDeclaration old in before.Types)
        {
            if (!afterByName.TryGetValue(old.Name, out TypeDeclaration? current))
            {
                changes.Add(
                    TypeChange(
                        ChangeSeverity.Breaking,
                        ChangeKind.TypeRemoved,
                        old.Name,
                        null,
                        null,
                        null,
                        "removed"
                    )
                );
                continue;
            }

            CompareType(old, current, changes);
        }

        foreach (TypeDeclaration added in after.Types.Where(t => !beforeByName.ContainsKey(t.Name)))
        {
            changes.Add(
                TypeChange(
                    ChangeSeverity.NonBreaking,
                    ChangeKind.TypeAdded,
                    added.Name,
                    null,
                    null,
                    null,
                    "added"
                )
            );
        }
    }

    private static ApiChange TypeChange(
        string severity,
        string kind,
        string type,
        string? member,
        string? beforeValue,
        string? afterValue,
        string message
    )
    {
        return new ApiChange(severity, kind, null, type, member, beforeValue, afterValue, message);
    }

    /// <summary>An enum and a literal union are different kinds, as are an interface and an alias.</summary>
    private static void CompareType(
        TypeDeclaration old,
        TypeDeclaration current,
        List<ApiChange> changes
    )
    {
        string oldKind = old.Kind;
        string currentKind = current.Kind;
        if (oldKind != currentKind)
        {
            changes.Add(
                TypeChange(
                    ChangeSeverity.Breaking,
                    ChangeKind.TypeKindChanged,
                    current.Name,
                    null,
                    oldKind,
                    currentKind,
                    $"changed from {oldKind} to {currentKind}"
                )
            );
            return;
        }

        switch (current.Kind)
        {
            case "interface":
                CompareInterface(old, current, changes);
                break;
            case "enum":
                CompareMembers(current.Name, old.Values, current.Values, changes);
                break;
            default:
                CompareAlias(old, current, changes);
                break;
        }
    }

    private static void CompareInterface(
        TypeDeclaration old,
        TypeDeclaration current,
        List<ApiChange> changes
    )
    {
        Dictionary<string, TypeMember> oldByName = old.Members.ToDictionary(m => m.Name);
        Dictionary<string, TypeMember> currentByName = current.Members.ToDictionary(m => m.Name);

        foreach (TypeMember removed in old.Members.Where(m => !currentByName.ContainsKey(m.Name)))
        {
            changes.Add(
                TypeChange(
                    ChangeSeverity.Breaking,
                    ChangeKind.TypePropertyRemoved,
                    current.Name,
                    removed.Name,
                    removed.Type,
                    null,
                    $"property \"{removed.Name}\" removed"
                )
            );
        }

        foreach (TypeMember added in current.Members.Where(m => !oldByName.ContainsKey(m.Name)))
        {
            changes.Add(AddedProperty(current.Name, added));
        }

        foreach (TypeMember before in old.Members.Where(m => currentByName.ContainsKey(m.Name)))
        {
            CompareProperty(current.Name, before, currentByName[before.Name], changes);
        }
    }

    private static ApiChange AddedProperty(string type, TypeMember added)
    {
        return added.Required
            ? TypeChange(
                ChangeSeverity.Breaking,
                ChangeKind.TypePropertyAddedRequired,
                type,
                added.Name,
                null,
                added.Type,
                $"property \"{added.Name}\" added as required"
            )
            : TypeChange(
                ChangeSeverity.NonBreaking,
                ChangeKind.TypePropertyAddedOptional,
                type,
                added.Name,
                null,
                added.Type,
                $"property \"{added.Name}\" added as optional"
            );
    }

    /// <summary>Requiredness changing in either direction can break strict TypeScript: reading an optional value, or constructing without a required one.</summary>
    private static void CompareProperty(
        string type,
        TypeMember before,
        TypeMember after,
        List<ApiChange> changes
    )
    {
        if (
            ApiDiff.Normalize(before.Type) != ApiDiff.Normalize(after.Type)
            && !CompareLiteralUnion(type, before, after, changes)
        )
        {
            changes.Add(
                TypeChange(
                    ChangeSeverity.Breaking,
                    ChangeKind.TypePropertyTypeChanged,
                    type,
                    before.Name,
                    before.Type,
                    after.Type,
                    $"property \"{before.Name}\" type changed from {before.Type} to {after.Type}"
                )
            );
        }

        if (before.Required != after.Required)
        {
            changes.Add(
                after.Required
                    ? TypeChange(
                        ChangeSeverity.Breaking,
                        ChangeKind.TypePropertyBecameRequired,
                        type,
                        before.Name,
                        "optional",
                        "required",
                        $"property \"{before.Name}\" became required"
                    )
                    : TypeChange(
                        ChangeSeverity.Breaking,
                        ChangeKind.TypePropertyBecameOptional,
                        type,
                        before.Name,
                        "required",
                        "optional",
                        $"property \"{before.Name}\" became optional"
                    )
            );
        }
    }

    /// <summary>
    /// A property whose type only gained or lost literal members of a union ("a" | "b") is a member change, not a
    /// type change. Returns false when the difference is anything else.
    /// </summary>
    private static bool CompareLiteralUnion(
        string type,
        TypeMember before,
        TypeMember after,
        List<ApiChange> changes
    )
    {
        List<string> oldMembers = ApiDiff.SplitUnion(ApiDiff.Normalize(before.Type));
        List<string> currentMembers = ApiDiff.SplitUnion(ApiDiff.Normalize(after.Type));
        List<string> removed = oldMembers.Except(currentMembers).ToList();
        List<string> added = currentMembers.Except(oldMembers).ToList();

        bool onlyLiterals = removed.Concat(added).All(IsLiteral);
        bool oneDirection = (removed.Count == 0) != (added.Count == 0);
        if (!onlyLiterals || !oneDirection)
        {
            return false;
        }

        foreach (string member in removed)
        {
            changes.Add(
                TypeChange(
                    ChangeSeverity.Breaking,
                    ChangeKind.TypeMemberRemoved,
                    type,
                    before.Name,
                    member,
                    null,
                    $"property \"{before.Name}\": member {member} removed"
                )
            );
        }

        foreach (string member in added)
        {
            changes.Add(
                TypeChange(
                    ChangeSeverity.NonBreaking,
                    ChangeKind.TypeMemberAdded,
                    type,
                    before.Name,
                    null,
                    member,
                    $"property \"{before.Name}\": member {member} added"
                )
            );
        }

        return true;
    }

    private static bool IsLiteral(string member) =>
        member.StartsWith('"', StringComparison.Ordinal)
        || member is "true" or "false"
        || (member.Length > 0 && (char.IsDigit(member[0]) || member[0] == '-'));

    /// <summary>Unions (including literal unions) compare member by member; anything else is one changed type.</summary>
    private static void CompareAlias(
        TypeDeclaration old,
        TypeDeclaration current,
        List<ApiChange> changes
    )
    {
        string oldType = ApiDiff.Normalize(old.AliasType ?? string.Empty);
        string currentType = ApiDiff.Normalize(current.AliasType ?? string.Empty);
        if (oldType == currentType)
        {
            return;
        }

        List<string> oldMembers = ApiDiff.SplitUnion(oldType);
        List<string> currentMembers = ApiDiff.SplitUnion(currentType);

        if (oldMembers.Count > 1 || currentMembers.Count > 1)
        {
            CompareMembers(current.Name, oldMembers, currentMembers, changes);
            return;
        }

        changes.Add(
            TypeChange(
                ChangeSeverity.Breaking,
                ChangeKind.TypeAliasChanged,
                current.Name,
                null,
                old.AliasType,
                current.AliasType,
                $"changed from {old.AliasType} to {current.AliasType}"
            )
        );
    }

    private static void CompareMembers(
        string type,
        List<string> oldMembers,
        List<string> currentMembers,
        List<ApiChange> changes
    )
    {
        foreach (string removed in oldMembers.Except(currentMembers))
        {
            changes.Add(
                TypeChange(
                    ChangeSeverity.Breaking,
                    ChangeKind.TypeMemberRemoved,
                    type,
                    removed,
                    removed,
                    null,
                    $"member {removed} removed"
                )
            );
        }

        foreach (string added in currentMembers.Except(oldMembers))
        {
            changes.Add(
                TypeChange(
                    ChangeSeverity.NonBreaking,
                    ChangeKind.TypeMemberAdded,
                    type,
                    added,
                    null,
                    added,
                    $"member {added} added"
                )
            );
        }
    }
}
