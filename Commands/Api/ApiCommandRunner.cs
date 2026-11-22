using Auxil.Common;
using Catalyst.Commands.Gen;
using Catalyst.Common.Configuration;
using Catalyst.Common.Models;
using Catalyst.Common.Services;

namespace Catalyst.Commands.Api;

/// <summary>The flow every <c>api</c> command shares: pick the spec, build the client model, answer, print.</summary>
internal static class ApiCommandRunner
{
    public static Task<int> RunAsync(
        ApiSettings settings,
        string command,
        Func<ClientModel, ApiReport> answer,
        CancellationToken cancellationToken
    )
    {
        return RunAsync(
            settings,
            command,
            (model, _, _) => Task.FromResult(answer(model)),
            cancellationToken
        );
    }

    /// <summary>Like the synchronous overload, for answers that need the target (to find a base spec) or do I/O.</summary>
    public static async Task<int> RunAsync(
        ApiSettings settings,
        string command,
        Func<ClientModel, ApiTarget, CancellationToken, Task<ApiReport>> answer,
        CancellationToken cancellationToken
    )
    {
        ApiReport report;

        try
        {
            report = await BuildReportAsync(settings, command, answer, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            report = ApiReport.Failure(command, IssueCodes.Cancelled, "Cancelled.");
        }

        report.Render(settings.Json);
        return report.ExitCode;
    }

    /// <summary>
    /// Picks the spec, builds the client model and answers. Used by the CLI commands and the MCP tools alike, so both
    /// give the same report. Cancellation is not caught here.
    /// </summary>
    internal static async Task<ApiReport> BuildReportAsync(
        ApiSettings settings,
        string command,
        Func<ClientModel, ApiTarget, CancellationToken, Task<ApiReport>> answer,
        CancellationToken cancellationToken
    )
    {
        if (!ResolveTarget(settings).TryGet(out ApiTarget? target, out Issue? targetProblem))
        {
            return ApiReport.Failure(command, targetProblem.Code, targetProblem.Message);
        }

        Result<SourceResolveResult> resolved = await SourceResolver
            .ResolveAsync(target.Source, target.Insecure, cancellationToken)
            .ConfigureAwait(false);
        if (resolved is not { IsSuccess: true, Value: { } source })
        {
            return ApiReport.Failure(
                command,
                SourceResolver.ClassifyFailure(resolved.ErrorMessage),
                resolved.ErrorMessage ?? "Unknown error"
            );
        }

        if (
            !BuildModel(source.Content, source.DisplayPath, target)
                .TryGet(out ClientModel? model, out Issue? modelProblem)
        )
        {
            return ApiReport.Failure(command, modelProblem.Code, modelProblem.Message);
        }

        return await answer(model, target, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Parses a spec and generates the client model for it; the same pipeline for the new and the base spec.</summary>
    internal static Outcome<ClientModel> BuildModel(
        string content,
        string displayPath,
        ApiTarget target
    )
    {
        Result<OpenApiParseResult> parsed = OpenApiParser.Parse(content, displayPath);
        if (parsed is not { IsSuccess: true, Value: { } parseResult })
        {
            return Outcome<ClientModel>.Fail(
                IssueCodes.ParseFailed,
                parsed.ErrorMessage ?? "Unknown error"
            );
        }

        TypeScriptGenerator generator = new(
            parseResult.Document,
            target.ClassName,
            target.SkipPaths
        );
        try
        {
            generator.Generate();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Outcome<ClientModel>.Fail(IssueCodes.GenerationFailed, ex.Message);
        }

        return Outcome<ClientModel>.Ok(generator.Model);
    }

    /// <summary>
    /// --source wins as given; --client picks a configured client; with neither, the config must hold exactly one
    /// client. Anything else is a usage error that lists the configured client names.
    /// </summary>
    internal static Outcome<ApiTarget> ResolveTarget(ApiSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.Source))
        {
            return string.IsNullOrWhiteSpace(settings.Client)
                ? Outcome<ApiTarget>.Ok(
                    new ApiTarget(
                        settings.Source,
                        null,
                        OpenApiCommand.ResolveSkipPaths(null),
                        settings.Insecure
                    )
                )
                : Outcome<ApiTarget>.Fail(
                    IssueCodes.Usage,
                    "Use either --source or --client, not both."
                );
        }

        ConfigLoadResult loaded = ConfigLoader.LoadDetailed(Directory.GetCurrentDirectory());
        if (loaded.Error is not null)
        {
            return Outcome<ApiTarget>.Fail(IssueCodes.ConfigInvalid, loaded.Error);
        }

        List<OpenApiClientConfig> clients = loaded.Config?.Gen.OpenApi.Clients ?? [];
        if (loaded.Config is null || loaded.Path is null || clients.Count == 0)
        {
            return Outcome<ApiTarget>.Fail(
                IssueCodes.Usage,
                "Pass --source <path|url>, or define a client in catalyst.config.json and pass --client <name>."
            );
        }

        if (
            !PickClient(settings, clients, loaded.Path)
                .TryGet(out OpenApiClientConfig? client, out Issue? problem)
        )
        {
            return Outcome<ApiTarget>.Fail(problem);
        }

        return TargetOf(client, settings, loaded.Config, loaded.Path);
    }

    private static Outcome<ApiTarget> TargetOf(
        OpenApiClientConfig client,
        ApiSettings settings,
        CatalystConfig config,
        string configPath
    )
    {
        string source = client.Source.Trim();
        if (source.Length == 0)
        {
            return Outcome<ApiTarget>.Fail(
                IssueCodes.ClientInvalid,
                $"Client \"{client.Name}\" has no source configured."
            );
        }

        string configDirectory = Path.GetDirectoryName(configPath)
            ?? Directory.GetCurrentDirectory();
        List<string> skipPaths =
            client.SkipPaths.Count > 0 ? client.SkipPaths : config.Gen.OpenApi.SkipPaths;

        return Outcome<ApiTarget>.Ok(
            new ApiTarget(
                ConfiguredClientRunner.ResolveSource(source, configDirectory),
                client.Name.Trim(),
                skipPaths,
                client.Insecure || settings.Insecure
            )
        );
    }

    private static Outcome<OpenApiClientConfig> PickClient(
        ApiSettings settings,
        List<OpenApiClientConfig> clients,
        string configPath
    )
    {
        string names = string.Join(", ", clients.Select(c => c.Name));

        if (string.IsNullOrWhiteSpace(settings.Client))
        {
            return clients.Count == 1
                ? Outcome<OpenApiClientConfig>.Ok(clients[0])
                : Outcome<OpenApiClientConfig>.Fail(
                    IssueCodes.Usage,
                    $"{configPath} defines several clients: {names}. Pass --client <name> or --source."
                );
        }

        OpenApiClientConfig? client = clients.FirstOrDefault(c =>
            c.Name.Equals(settings.Client.Trim(), StringComparison.OrdinalIgnoreCase)
        );
        return client is null
            ? Outcome<OpenApiClientConfig>.Fail(
                IssueCodes.Usage,
                $"No client named \"{settings.Client}\" in {configPath}. Configured clients: {names}."
            )
            : Outcome<OpenApiClientConfig>.Ok(client);
    }
}
