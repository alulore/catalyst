using Auxil.Common;
using Catalyst.Common.Enums;
using Catalyst.Common.Models;

namespace Catalyst.Common.Services;

internal static class SourceResolver
{
    private static readonly TimeSpan DefaultHttpTimeout = TimeSpan.FromSeconds(30);

    private const string FileNotFoundPrefix = "File not found: ";
    private const string TimeoutPrefix = "Request timed out";

    /// <summary>Maps a failed Resolve to its stable issue code; the messages above are produced by this class.</summary>
    public static string ClassifyFailure(string? errorMessage)
    {
        if (
            errorMessage != null
            && errorMessage.StartsWith(FileNotFoundPrefix, StringComparison.Ordinal)
        )
        {
            return IssueCodes.SourceNotFound;
        }

        if (
            errorMessage != null
            && errorMessage.StartsWith(TimeoutPrefix, StringComparison.Ordinal)
        )
        {
            return IssueCodes.SourceTimeout;
        }

        return IssueCodes.SourceFetchFailed;
    }

    public static SourceType DetermineSourceType(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return SourceType.Unknown;
        }

        if (Uri.TryCreate(source, UriKind.Absolute, out Uri? uriResult))
        {
            if (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps)
            {
                return SourceType.Url;
            }
        }

        return SourceType.FilePath;
    }

    public static Task<Result<SourceResolveResult>> ResolveAsync(
        string source,
        CancellationToken cancellationToken
    ) => ResolveAsync(source, insecure: false, cancellationToken);

    /// <summary>
    /// Reads the spec from a file or URL. Cancelling <paramref name="cancellationToken"/> throws an
    /// <see cref="OperationCanceledException"/>; only a real timeout is reported as an error result.
    /// </summary>
    public static async Task<Result<SourceResolveResult>> ResolveAsync(
        string source,
        bool insecure,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null
    )
    {
        SourceType type = DetermineSourceType(source);

        if (type == SourceType.Url)
        {
            return await ResolveUrlAsync(
                    source,
                    insecure,
                    timeout ?? DefaultHttpTimeout,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        if (type == SourceType.FilePath)
        {
            return ResolveFile(source);
        }

        return Result<SourceResolveResult>.Error($"Unrecognized source type: {source}");
    }

    /// <summary>Opt-in only: with <paramref name="insecure"/> the handler skips TLS certificate validation when fetching a remote spec. Never enabled unless the caller explicitly requests it.</summary>
    internal static HttpClientHandler CreateHttpHandler(bool insecure)
    {
        HttpClientHandler handler = new();

        if (insecure)
        {
#pragma warning disable MA0039 // --insecure exists to fetch specs from servers with untrusted certificates; it runs only when the user passes it
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
#pragma warning restore MA0039
        }

        return handler;
    }

    /// <summary>A cancellation the caller asked for (Ctrl+C) propagates; any other cancelled request is a timeout.</summary>
    private static async Task<Result<SourceResolveResult>> ResolveUrlAsync(
        string url,
        bool insecure,
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
    {
        try
        {
            using HttpClientHandler handler = CreateHttpHandler(insecure);
            using HttpClient client = new(handler);
            client.Timeout = timeout;
            client.DefaultRequestHeaders.Add("User-Agent", "Catalyst-CLI/1.0");
            string content = await client
                .GetStringAsync(url, cancellationToken)
                .ConfigureAwait(false);

            return Result<SourceResolveResult>.Ok(
                new SourceResolveResult
                {
                    Content = content,
                    DisplayPath = url,
                    Type = SourceType.Url,
                }
            );
        }
        catch (HttpRequestException ex)
        {
            return Result<SourceResolveResult>.Error($"Failed to fetch URL: {ex.Message}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            return Result<SourceResolveResult>.Error(
                $"{TimeoutPrefix} after {timeout.TotalSeconds:0.##} seconds."
            );
        }
        catch (Exception ex)
        {
            return Result<SourceResolveResult>.Error(
                $"Unexpected error while fetching URL: {ex.Message}"
            );
        }
    }

    private static Result<SourceResolveResult> ResolveFile(string path)
    {
        string fullPath = Path.GetFullPath(path);

        if (!File.Exists(fullPath))
        {
            return Result<SourceResolveResult>.Error(FileNotFoundPrefix + fullPath);
        }

        try
        {
            string content = File.ReadAllText(fullPath);

            return Result<SourceResolveResult>.Ok(
                new SourceResolveResult
                {
                    Content = content,
                    DisplayPath = fullPath,
                    Type = SourceType.FilePath,
                }
            );
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result<SourceResolveResult>.Error($"Access denied reading file: {ex.Message}");
        }
        catch (IOException ex)
        {
            return Result<SourceResolveResult>.Error($"IO error reading file: {ex.Message}");
        }
        catch (Exception ex)
        {
            return Result<SourceResolveResult>.Error(
                $"Unexpected error reading file: {ex.Message}"
            );
        }
    }
}
