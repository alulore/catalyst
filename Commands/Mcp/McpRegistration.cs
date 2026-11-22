using Catalyst.Common.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Catalyst.Commands.Mcp;

/// <summary>Registers the Catalyst tools; shared by the real server and the in-process tests.</summary>
internal static class McpRegistration
{
    public static IMcpServerBuilder AddCatalystServer(this IServiceCollection services)
    {
        return services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new ModelContextProtocol.Protocol.Implementation
                {
                    Name = "catalyst",
                    Version = ApplicationVersion.Current,
                };
                options.ServerInstructions =
                    "Catalyst looks up and generates TypeScript API clients from OpenAPI specs. "
                    + "Start with list_clients, then list_operations and show_operation to learn how to call an endpoint; "
                    + "use diff_spec to see what a spec change breaks and generate_clients to regenerate.";
            })
            .WithTools<CatalystTools>();
    }
}
