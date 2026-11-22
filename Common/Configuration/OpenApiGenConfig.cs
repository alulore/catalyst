namespace Catalyst.Common.Configuration;

internal sealed class OpenApiGenConfig
{
    public List<string> SkipPaths { get; set; } = [];

    /// <summary>
    /// Named clients this repository consumes. The Syncfully Workbench lists these projects and
    /// prefills its generator form from them; `catalyst gen openapi` without --source generates
    /// every configured client in one run.
    /// </summary>
    public List<OpenApiClientConfig> Clients { get; set; } = [];
}
