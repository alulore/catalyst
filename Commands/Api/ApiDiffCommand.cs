using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Spectre.Console.Cli;

namespace Catalyst.Commands.Api;

internal sealed class ApiDiffCommand : AsyncCommand<ApiDiffCommand.Settings>
{
    internal sealed class Settings : ApiSettings
    {
        [CommandOption("--base <BASE>")]
        [Description(
            "The old spec to compare with: a path, a URL, or git:<ref>:<path> (read with git show). With a file source, git:<ref> alone means the same file at that ref, e.g. git:HEAD."
        )]
        public string Base { get; init; } = string.Empty;

        [CommandOption("--fail-on-breaking")]
        [Description(
            "Exit with code 1 when there is any breaking change (the default exit code is 0)."
        )]
        public bool FailOnBreaking { get; init; }

        public override Spectre.Console.ValidationResult Validate()
        {
            return string.IsNullOrWhiteSpace(Base)
                ? Spectre.Console.ValidationResult.Error(
                    "The --base option is required: the old spec to compare with."
                )
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
            "api diff",
            (model, target, token) =>
                ApiAnswers.DiffAsync(
                    model,
                    target,
                    settings.Base,
                    settings.Insecure,
                    settings.FailOnBreaking,
                    token
                ),
            cancellationToken
        );
    }
}
