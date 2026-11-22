using System.Globalization;
using System.Text.RegularExpressions;
using Catalyst.Common.Models;

namespace Catalyst.Commands.Api;

/// <summary>
/// The operation half of <see cref="ApiDiff"/>. Operations are matched by method and path with every path parameter
/// name collapsed, so renaming <c>{id}</c> to <c>{itemId}</c> is no change; a changed path with the same operationId is
/// the same operation that moved.
/// </summary>
internal static partial class OperationDiff
{
    [GeneratedRegex(@"\{[^{}]*\}")]
    private static partial Regex PathParameter();

    public static void Compare(ClientModel before, ClientModel after, List<ApiChange> changes)
    {
        Dictionary<string, ApiOperation> afterByKey = after
            .Operations.GroupBy(Key)
            .ToDictionary(g => g.Key, g => g.First());
        List<ApiOperation> unmatchedAfter = after
            .Operations.Where(o => !before.Operations.Any(b => Key(b) == Key(o)))
            .ToList();

        foreach (ApiOperation old in before.Operations)
        {
            ApiOperation? current = afterByKey.GetValueOrDefault(Key(old))
                ?? TakeMoved(old, unmatchedAfter, changes);

            if (current == null)
            {
                changes.Add(
                    OperationChange(
                        ChangeSeverity.Breaking,
                        ChangeKind.OperationRemoved,
                        old,
                        old.Call,
                        null,
                        "removed"
                    )
                );
                continue;
            }

            CompareOperation(old, current, changes);
        }

        foreach (ApiOperation added in unmatchedAfter)
        {
            changes.Add(
                OperationChange(
                    ChangeSeverity.NonBreaking,
                    ChangeKind.OperationAdded,
                    added,
                    null,
                    added.Call,
                    "added"
                )
            );
        }
    }

    private static string Key(ApiOperation operation) =>
        operation.Method + " " + PathParameter().Replace(operation.Path, "{}");

    private static ApiOperation? TakeMoved(
        ApiOperation old,
        List<ApiOperation> unmatchedAfter,
        List<ApiChange> changes
    )
    {
        if (old.OperationId == null)
        {
            return null;
        }

        ApiOperation? moved = unmatchedAfter.FirstOrDefault(o => o.OperationId == old.OperationId);
        if (moved == null)
        {
            return null;
        }

        unmatchedAfter.Remove(moved);
        string severity =
            old.Call == moved.Call && old.Signature == moved.Signature
                ? ChangeSeverity.NonBreaking
                : ChangeSeverity.Breaking;
        changes.Add(
            OperationChange(
                severity,
                ChangeKind.OperationMoved,
                moved,
                old.Method + " " + old.Path,
                moved.Method + " " + moved.Path,
                $"moved from {old.Method} {old.Path}"
            )
        );
        return moved;
    }

    private static ApiChange OperationChange(
        string severity,
        string kind,
        ApiOperation operation,
        string? beforeValue,
        string? afterValue,
        string message,
        string? member = null
    )
    {
        return new ApiChange(
            severity,
            kind,
            new ApiOperationRef(operation.Method, operation.Path, operation.Call),
            null,
            member,
            beforeValue,
            afterValue,
            message
        );
    }

    private static void CompareOperation(
        ApiOperation old,
        ApiOperation current,
        List<ApiChange> changes
    )
    {
        if (old.Call != current.Call)
        {
            changes.Add(
                OperationChange(
                    ChangeSeverity.Breaking,
                    ChangeKind.OperationCallChanged,
                    current,
                    old.Call,
                    current.Call,
                    $"generated call changed from {old.Call} to {current.Call}"
                )
            );
        }

        CompareParameters(old, current, changes);
        CompareBody(old, current, changes);

        if (ApiDiff.Normalize(old.Response.Type) != ApiDiff.Normalize(current.Response.Type))
        {
            changes.Add(
                OperationChange(
                    ChangeSeverity.Breaking,
                    ChangeKind.ResponseTypeChanged,
                    current,
                    old.Response.Type,
                    current.Response.Type,
                    $"response type changed from {old.Response.Type} to {current.Response.Type}"
                )
            );
        }
    }

    private static string ParameterKey(ApiParameter parameter) =>
        parameter.In + ":" + parameter.WireName;

    private static void CompareParameters(
        ApiOperation old,
        ApiOperation current,
        List<ApiChange> changes
    )
    {
        Dictionary<string, ApiParameter> oldByKey = old.Parameters.ToDictionary(ParameterKey);
        Dictionary<string, ApiParameter> currentByKey = current.Parameters.ToDictionary(
            ParameterKey
        );

        ReportRemovedParameters(old, current, currentByKey, changes);
        ReportAddedParameters(current, oldByKey, changes);

        List<string> commonOld = SignatureOrder(old).Where(currentByKey.ContainsKey).ToList();
        List<string> commonCurrent = SignatureOrder(current).Where(oldByKey.ContainsKey).ToList();

        foreach (string key in commonOld)
        {
            CompareParameter(
                current,
                oldByKey[key],
                currentByKey[key],
                commonOld.IndexOf(key),
                commonCurrent.IndexOf(key),
                changes
            );
        }
    }

    private static void ReportRemovedParameters(
        ApiOperation old,
        ApiOperation current,
        Dictionary<string, ApiParameter> currentByKey,
        List<ApiChange> changes
    )
    {
        foreach (
            ApiParameter removed in old.Parameters.Where(p =>
                !currentByKey.ContainsKey(ParameterKey(p))
            )
        )
        {
            changes.Add(
                OperationChange(
                    ChangeSeverity.Breaking,
                    ChangeKind.ParameterRemoved,
                    current,
                    removed.Name,
                    null,
                    $"parameter \"{removed.WireName}\" removed",
                    removed.WireName
                )
            );
        }
    }

    private static void ReportAddedParameters(
        ApiOperation current,
        Dictionary<string, ApiParameter> oldByKey,
        List<ApiChange> changes
    )
    {
        foreach (
            ApiParameter added in current.Parameters.Where(p =>
                !oldByKey.ContainsKey(ParameterKey(p))
            )
        )
        {
            changes.Add(
                added.Required
                    ? OperationChange(
                        ChangeSeverity.Breaking,
                        ChangeKind.ParameterAddedRequired,
                        current,
                        null,
                        added.Name,
                        $"parameter \"{added.WireName}\" added as required",
                        added.WireName
                    )
                    : OperationChange(
                        ChangeSeverity.NonBreaking,
                        ChangeKind.ParameterAddedOptional,
                        current,
                        null,
                        added.Name,
                        $"parameter \"{added.WireName}\" added as optional",
                        added.WireName
                    )
            );
        }
    }

    /// <summary>
    /// Arguments are positional, so a parameter that is no longer in the same place among the others breaks callers.
    /// A parameter whose requiredness changed moves by design and is already reported as that change.
    /// </summary>
    private static void CompareParameter(
        ApiOperation current,
        ApiParameter before,
        ApiParameter after,
        int oldPosition,
        int currentPosition,
        List<ApiChange> changes
    )
    {
        bool requirednessChanged = before.Required != after.Required;

        if (requirednessChanged)
        {
            changes.Add(RequirednessChange(current, after));
        }

        if (ApiDiff.Normalize(before.Type) != ApiDiff.Normalize(after.Type))
        {
            changes.Add(
                OperationChange(
                    ChangeSeverity.Breaking,
                    ChangeKind.ParameterTypeChanged,
                    current,
                    before.Type,
                    after.Type,
                    $"parameter \"{after.WireName}\" type changed from {before.Type} to {after.Type}",
                    after.WireName
                )
            );
        }

        if (!requirednessChanged && oldPosition != currentPosition)
        {
            changes.Add(
                OperationChange(
                    ChangeSeverity.Breaking,
                    ChangeKind.ParameterMoved,
                    current,
                    (oldPosition + 1).ToString(CultureInfo.InvariantCulture),
                    (currentPosition + 1).ToString(CultureInfo.InvariantCulture),
                    $"parameter \"{after.WireName}\" moved from position {oldPosition + 1} to {currentPosition + 1}",
                    after.WireName
                )
            );
        }
    }

    private static ApiChange RequirednessChange(ApiOperation current, ApiParameter after)
    {
        return after.Required
            ? OperationChange(
                ChangeSeverity.Breaking,
                ChangeKind.ParameterBecameRequired,
                current,
                "optional",
                "required",
                $"parameter \"{after.WireName}\" became required",
                after.WireName
            )
            : OperationChange(
                ChangeSeverity.NonBreaking,
                ChangeKind.ParameterBecameOptional,
                current,
                "required",
                "optional",
                $"parameter \"{after.WireName}\" became optional",
                after.WireName
            );
    }

    /// <summary>Parameter keys in call order: path and required parameters first, then the optional ones, as in the generated signature.</summary>
    private static List<string> SignatureOrder(ApiOperation operation)
    {
        List<string> order = operation
            .Parameters.Where(p => p.Required || p.In == "path")
            .Select(ParameterKey)
            .ToList();
        order.AddRange(
            operation.Parameters.Where(p => !p.Required && p.In != "path").Select(ParameterKey)
        );
        return order;
    }

    /// <summary>The generated body parameter is always mandatory, so any new body breaks existing calls.</summary>
    private static void CompareBody(ApiOperation old, ApiOperation current, List<ApiChange> changes)
    {
        switch (old.Body, current.Body)
        {
            case (null, { } added):
                changes.Add(
                    OperationChange(
                        ChangeSeverity.Breaking,
                        ChangeKind.BodyAdded,
                        current,
                        null,
                        added.Type,
                        $"body {added.Type} added",
                        "body"
                    )
                );
                break;
            case ({ } removed, null):
                changes.Add(
                    OperationChange(
                        ChangeSeverity.Breaking,
                        ChangeKind.BodyRemoved,
                        current,
                        removed.Type,
                        null,
                        "body removed",
                        "body"
                    )
                );
                break;
            case ({ } before, { } after):
                CompareBodyContent(current, before, after, changes);
                break;
        }
    }

    private static void CompareBodyContent(
        ApiOperation current,
        ApiBody before,
        ApiBody after,
        List<ApiChange> changes
    )
    {
        if (ApiDiff.Normalize(before.Type) != ApiDiff.Normalize(after.Type))
        {
            changes.Add(
                OperationChange(
                    ChangeSeverity.Breaking,
                    ChangeKind.BodyTypeChanged,
                    current,
                    before.Type,
                    after.Type,
                    $"body type changed from {before.Type} to {after.Type}",
                    "body"
                )
            );
        }
        else if (before.MediaType != after.MediaType)
        {
            changes.Add(
                OperationChange(
                    ChangeSeverity.NonBreaking,
                    ChangeKind.BodyMediaTypeChanged,
                    current,
                    before.MediaType,
                    after.MediaType,
                    $"body media type changed from {before.MediaType} to {after.MediaType}",
                    "body"
                )
            );
        }
    }
}
