using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;

namespace Catalyst.Commands.Mcp;

[Description(
    "Runs a Model Context Protocol server over stdio, so AI agents can call Catalyst's lookup, diff and generate tools."
)]
internal sealed class McpCommand : AsyncCommand<McpCommand.Settings>
{
    internal sealed class Settings : CommandSettings { }

    /// <summary>stdout is the protocol channel, so logging goes to stderr only.</summary>
    protected override async Task<int> ExecuteAsync(
        [NotNull] CommandContext context,
        [NotNull] Settings settings,
        CancellationToken cancellationToken
    )
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();

        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

        builder.Services.AddCatalystServer().WithStdioServerTransport();

        await builder.Build().RunAsync(cancellationToken).ConfigureAwait(false);
        return 0;
    }
}
