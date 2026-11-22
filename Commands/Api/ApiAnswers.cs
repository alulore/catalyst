using Catalyst.Common.Models;

namespace Catalyst.Commands.Api;

/// <summary>The answers of the api commands, from a client model. The CLI commands and the MCP tools both call these.</summary>
internal static class ApiAnswers
{
    public static ApiReport List(ClientModel model, string? filter, string? tag, string? method)
    {
        List<ApiListItem> items = ApiLookup
            .FilterOperations(model, filter, tag, method)
            .Select(o => new ApiListItem(
                o.Method,
                o.Path,
                o.OperationId,
                o.Summary,
                o.Deprecated,
                o.Tags,
                o.Call
            ))
            .ToList();

        return new ApiReport { Command = "api list", Operations = items }.Complete();
    }

    public static ApiReport Show(ClientModel model, string selector, bool includeTypes)
    {
        OperationMatch match = ApiLookup.FindOperation(model, selector);

        if (!match.IsFound)
        {
            return ApiReport.Failure("api show", match.ErrorCode, match.Message, match.Suggestions);
        }

        return new ApiReport
        {
            Command = "api show",
            Operation = match.Operation,
            Types = includeTypes
                ? ApiLookup.TypeClosure(model, match.Operation.ReferencedTypes)
                : null,
        }.Complete();
    }

    public static ApiReport Schema(ClientModel model, string name, int? depth)
    {
        TypeDeclaration? type = ApiLookup.FindType(model, name);
        if (type == null)
        {
            return ApiReport.Failure(
                "api schema",
                IssueCodes.SchemaNotFound,
                $"No type matches \"{name}\".",
                ApiLookup.SuggestTypes(model, name)
            );
        }

        return new ApiReport
        {
            Command = "api schema",
            Types = ApiLookup.TypeClosure(model, [type.Name], depth),
        }.Complete();
    }

    /// <summary>Compares the base spec with the current model. With failOnBreaking a breaking change fails the report (exit code 1).</summary>
    public static async Task<ApiReport> DiffAsync(
        ClientModel model,
        ApiTarget target,
        string baseSpec,
        bool insecure,
        bool failOnBreaking,
        CancellationToken cancellationToken
    )
    {
        if (
            !(
                await ApiBaseLoader
                    .LoadAsync(baseSpec, target, insecure, cancellationToken)
                    .ConfigureAwait(false)
            ).TryGet(out BaseSpec? loaded, out Issue? problem)
        )
        {
            return ApiReport.Failure("api diff", problem.Code, problem.Message);
        }

        if (
            !ApiCommandRunner
                .BuildModel(loaded.Content, loaded.DisplayPath, target)
                .TryGet(out ClientModel? baseModel, out Issue? baseProblem)
        )
        {
            return ApiReport.Failure(
                "api diff",
                baseProblem.Code,
                "Base spec: " + baseProblem.Message
            );
        }

        List<ApiChange> changes = ApiDiff.Compare(baseModel, model);
        int breaking = changes.Count(c => c.Severity == ChangeSeverity.Breaking);
        ApiReport report = new()
        {
            Command = "api diff",
            Changes = changes,
            Summary = new DiffSummary(breaking, changes.Count - breaking),
        };
        report.Complete();

        if (failOnBreaking && breaking > 0)
        {
            report.Success = false;
            report.ExitCode = 1;
        }

        return report;
    }
}
