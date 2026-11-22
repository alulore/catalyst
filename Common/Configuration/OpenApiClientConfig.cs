namespace Catalyst.Common.Configuration;

internal sealed class OpenApiClientConfig
{
    /// <summary>Generated client name; becomes the --class-name and the default file name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>OpenAPI source: a local JSON file path or an http(s) URL.</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Output folder or file, relative to the config file's folder.</summary>
    public string Output { get; set; } = string.Empty;

    /// <summary>Skip TLS certificate validation for a local HTTPS server with a self-signed certificate.</summary>
    public bool Insecure { get; set; }

    /// <summary>Per-client skip paths; falls back to the project wide gen.openapi.skipPaths.</summary>
    public List<string> SkipPaths { get; set; } = [];
}
