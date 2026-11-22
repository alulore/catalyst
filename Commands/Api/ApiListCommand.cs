using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Spectre.Console.Cli;

namespace Catalyst.Commands.Api;

internal sealed class ApiListCommand : AsyncCommand<ApiListCommand.Settings>
{
    internal sealed class Settings : ApiSettings
    {
        [CommandOption("--filter <TEXT>")]
        [Description(
            "Only operations matching every word: path, operationId, method name, summary or tags (case-insensitive)."
        )]
        public string? Filter { get; init; }

        [CommandOption("--tag <TAG>")]
        [Description("Only operations with this tag.")]
        public string? Tag { get; init; }

        [CommandOption("--method <VERB>")]
        [Description("Only operations with this HTTP method (get, post, ...).")]
        public string? Method { get; init; }
    }

    protected override Task<int> ExecuteAsync(
        [NotNull] CommandContext context,
        [NotNull] Settings settings,
        CancellationToken cancellationToken
    )
    {
        return ApiCommandRunner.RunAsync(
            settings,
            "api list",
            model => ApiAnswers.List(model, settings.Filter, settings.Tag, settings.Method),
            cancellationToken
        );
    }
}
