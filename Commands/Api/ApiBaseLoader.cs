using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Auxil.Common;
using Catalyst.Common.Enums;
using Catalyst.Common.Models;
using Catalyst.Common.Services;

namespace Catalyst.Commands.Api;

/// <summary>Reads the old spec of <c>api diff</c>: a file, a URL, or a file at a git ref.</summary>
internal static class ApiBaseLoader
{
    private const string GitPrefix = "git:";

    public static async Task<Outcome<BaseSpec>> LoadAsync(
        string baseSpec,
        ApiTarget target,
        bool insecure,
        CancellationToken cancellationToken
    )
    {
        if (baseSpec.StartsWith(GitPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return await LoadFromGitAsync(baseSpec[GitPrefix.Length..], target, cancellationToken)
                .ConfigureAwait(false);
        }

        Result<SourceResolveResult> resolved = await SourceResolver
            .ResolveAsync(baseSpec, insecure, cancellationToken)
            .ConfigureAwait(false);
        if (resolved is not { IsSuccess: true, Value: { } source })
        {
            string code = SourceResolver.ClassifyFailure(resolved.ErrorMessage);
            return Outcome<BaseSpec>.Fail(
                code == IssueCodes.SourceNotFound ? IssueCodes.BaseNotFound : code,
                "Base spec: " + resolved.ErrorMessage
            );
        }

        return Outcome<BaseSpec>.Ok(new BaseSpec(source.Content, source.DisplayPath));
    }

    /// <summary>
    /// "ref:path" runs <c>git show ref:path</c> (the path is relative to the repository root); a bare "ref" means the
    /// same file as the new local source, at that ref, addressed relative to the current directory.
    /// </summary>
    private static async Task<Outcome<BaseSpec>> LoadFromGitAsync(
        string spec,
        ApiTarget target,
        CancellationToken cancellationToken
    )
    {
        int separator = spec.IndexOf(':', StringComparison.Ordinal);
        string reference = separator < 0 ? spec : spec[..separator];
        string path = separator < 0 ? string.Empty : spec[(separator + 1)..];

        if (reference.Length == 0)
        {
            return Outcome<BaseSpec>.Fail(
                IssueCodes.Usage,
                "A git base looks like git:<ref>:<path>, or git:<ref> for the same file as the source."
            );
        }

        if (path.Length == 0)
        {
            if (SourceResolver.DetermineSourceType(target.Source) != SourceType.FilePath)
            {
                return Outcome<BaseSpec>.Fail(
                    IssueCodes.Usage,
                    $"git:{reference} needs a local file as the source; use git:{reference}:<path> instead."
                );
            }

            path = RelativeToCurrentDirectory(target.Source);
        }

        return await RunGitShowAsync(spec, reference, path, cancellationToken)
            .ConfigureAwait(false);
    }

    private static string RelativeToCurrentDirectory(string source)
    {
        string relative = Path.GetRelativePath(
            Directory.GetCurrentDirectory(),
            Path.GetFullPath(source)
        );
        return "./" + relative.Replace('\\', '/');
    }

    private static async Task<Outcome<BaseSpec>> RunGitShowAsync(
        string spec,
        string reference,
        string path,
        CancellationToken cancellationToken
    )
    {
        ProcessStartInfo startInfo = new("git")
        {
            WorkingDirectory = Directory.GetCurrentDirectory(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("show");
        startInfo.ArgumentList.Add($"{reference}:{path}");

        try
        {
            using Process? process = Process.Start(startInfo);
            if (process is null)
            {
                return Outcome<BaseSpec>.Fail(
                    IssueCodes.BaseNotFound,
                    $"git is not available, so {GitPrefix}{spec} cannot be read."
                );
            }

            Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                string message = (await error.ConfigureAwait(false)).Trim().Split('\n')[0].Trim();
                return Outcome<BaseSpec>.Fail(
                    IssueCodes.BaseNotFound,
                    $"git could not read {reference}:{path}: {message}"
                );
            }

            return Outcome<BaseSpec>.Ok(new BaseSpec(await output.ConfigureAwait(false), path));
        }
        catch (Win32Exception ex)
        {
            return Outcome<BaseSpec>.Fail(
                IssueCodes.BaseNotFound,
                $"git is not available, so {GitPrefix}{spec} cannot be read: {ex.Message}"
            );
        }
    }
}
