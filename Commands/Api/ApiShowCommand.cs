using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Spectre.Console.Cli;

namespace Catalyst.Commands.Api;

internal sealed class ApiShowCommand : AsyncCommand<ApiShowCommand.Settings>
{
    internal sealed class Settings : ApiSettings
    {
        [CommandArgument(0, "<operation>")]
        [Description(
            "\"GET /items/{id}\", an operationId, or a generated call such as get.itemsId or client.get.itemsId."
        )]
        public string[] Operation { get; init; } = [];

        [CommandOption("--no-types")]
        [Description("Do not print the TypeScript declarations of the types the operation uses.")]
        public bool NoTypes { get; init; }
    }

    protected override Task<int> ExecuteAsync(
        [NotNull] CommandContext context,
        [NotNull] Settings settings,
        CancellationToken cancellationToken
    )
    {
        return ApiCommandRunner.RunAsync(
            settings,
            "api show",
            model =>
                ApiAnswers.Show(
                    model,
                    string.Join(' ', settings.Operation),
                    includeTypes: !settings.NoTypes
                ),
            cancellationToken
        );
    }
}
