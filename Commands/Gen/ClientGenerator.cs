using Auxil.Common;
using Catalyst.Common.Models;
using Catalyst.Common.Services;

namespace Catalyst.Commands.Gen;

/// <summary>
/// Resolves, parses and generates one client, then writes the file or (with check) only compares it. This is
/// the one pipeline behind both <c>--source</c> runs and configured clients.
/// </summary>
internal static class ClientGenerator
{
    public static async Task<ClientResult> GenerateAsync(
        ClientRequest request,
        bool check,
        CancellationToken cancellationToken
    )
    {
        Result<SourceResolveResult> resolveResult = await SourceResolver
            .ResolveAsync(request.Source, request.Insecure, cancellationToken)
            .ConfigureAwait(false);
        if (resolveResult is not { IsSuccess: true, Value: { } source })
        {
            return ClientResult.Fail(
                request.Name,
                request.Source,
                SourceResolver.ClassifyFailure(resolveResult.ErrorMessage),
                resolveResult.ErrorMessage ?? "Unknown error"
            );
        }

        Result<OpenApiParseResult> parseResult = OpenApiParser.Parse(
            source.Content,
            source.DisplayPath
        );
        if (parseResult is not { IsSuccess: true, Value: { } parsed })
        {
            return ClientResult.Fail(
                request.Name,
                request.Source,
                IssueCodes.ParseFailed,
                parseResult.ErrorMessage ?? "Unknown error"
            );
        }

        if (!request.Language.Equals("typescript", StringComparison.OrdinalIgnoreCase))
        {
            return ClientResult.Fail(
                request.Name,
                request.Source,
                IssueCodes.UnsupportedLanguage,
                $"Unsupported language: {request.Language}"
            );
        }

        return Render(request, check, parsed);
    }

    private static ClientResult Render(ClientRequest request, bool check, OpenApiParseResult parsed)
    {
        TypeScriptGenerator generator = new(parsed.Document, request.ClassName, request.SkipPaths);
        string code;
        try
        {
            code = generator.Generate();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ClientResult.Fail(
                request.Name,
                request.Source,
                IssueCodes.GenerationFailed,
                ex.Message
            );
        }

        ClientResult result = Persist(request, check, code);
        if (result.Failed)
        {
            return result;
        }

        result.SkippedPathCount = generator.SkippedPathCount;
        result.Warnings.AddRange(Warnings(parsed, generator));
        return result;
    }

    private static List<Issue> Warnings(OpenApiParseResult parsed, TypeScriptGenerator generator)
    {
        List<Issue> warnings = [];
        int validationErrors = parsed.Diagnostic.Errors.Count;
        if (validationErrors > 0)
        {
            warnings.Add(
                new Issue(
                    IssueCodes.SpecValidationErrors,
                    $"OpenAPI spec has {validationErrors} validation error(s). Generated code may be incomplete."
                )
            );
        }

        warnings.AddRange(generator.Warnings);
        if (generator.SkippedPathCount > 0)
        {
            warnings.Add(
                new Issue(
                    IssueCodes.SkippedPaths,
                    $"Skipped {generator.SkippedPathCount} path(s) matching configured skip prefixes."
                )
            );
        }

        return warnings;
    }

    private static ClientResult Persist(ClientRequest request, bool check, string code)
    {
        string? outputPath = null;
        try
        {
            outputPath = Path.GetFullPath(
                OpenApiCommand.ResolveOutputPath(request.Output, request.ClassName)
            );
            return new ClientResult
            {
                Name = request.Name,
                Source = request.Source,
                OutputPath = outputPath,
                Status = Output(outputPath, code, check),
            };
        }
        catch (Exception ex)
            when (ex
                    is IOException
                        or UnauthorizedAccessException
                        or ArgumentException
                        or NotSupportedException
            )
        {
            return ClientResult.Fail(
                request.Name,
                request.Source,
                IssueCodes.WriteFailed,
                ex.Message,
                outputPath
            );
        }
    }

    /// <summary>Check never writes: not the file and not its folder.</summary>
    private static string Output(string outputPath, string code, bool check)
    {
        if (!check)
        {
            return GeneratedFileWriter.WriteIfChanged(outputPath, code)
                ? ClientStatus.Written
                : ClientStatus.Unchanged;
        }

        return GeneratedFileWriter.Compare(outputPath, code) switch
        {
            GeneratedFileWriter.FileState.Same => ClientStatus.UpToDate,
            GeneratedFileWriter.FileState.Different => ClientStatus.Stale,
            _ => ClientStatus.Missing,
        };
    }
}
