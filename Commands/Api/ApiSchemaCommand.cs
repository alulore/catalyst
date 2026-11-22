using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Spectre.Console.Cli;

namespace Catalyst.Commands.Api;

internal sealed class ApiSchemaCommand : AsyncCommand<ApiSchemaCommand.Settings>
{
    internal sealed class Settings : ApiSettings
    {
        [CommandArgument(0, "<name>")]
        [Description("A generated type name or the original component name.")]
        public string Name { get; init; } = string.Empty;

        [CommandOption("--depth <N>")]
        [Description(
            "How many levels of referenced types to include (0 is the type alone). Default: all."
        )]
        public int? Depth { get; init; }

        public override Spectre.Console.ValidationResult Validate()
        {
            return Depth < 0
                ? Spectre.Console.ValidationResult.Error("--depth cannot be negative.")
                : base.Validate();
        }
    }

    protected override Task<int> ExecuteAsync(
        [NotNull] CommandContext context,
        [NotNull] Settings settings,
        CancellationToken cancellationToken
    )
    {
        return ApiCommandRunner.RunAsync(
            settings,
            "api schema",
            model => ApiAnswers.Schema(model, settings.Name, settings.Depth),
            cancellationToken
        );
    }
}
