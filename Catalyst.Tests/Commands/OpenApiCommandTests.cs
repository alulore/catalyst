using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Catalyst.Commands.Gen;
using Catalyst.Common.Configuration;
using Shouldly;
using Spectre.Console;

namespace Catalyst.Tests.Commands;

/// <summary>The tests that run the command replace the static AnsiConsole, so they must not overlap.</summary>
[Collection("AnsiConsole")]
public class OpenApiCommandTests
{
    private static CatalystConfig ConfigWith(params string[] skipPaths) =>
        new()
        {
            Gen = new GenConfig
            {
                OpenApi = new OpenApiGenConfig { SkipPaths = skipPaths.ToList() },
            },
        };

    [Fact]
    public void Insecure_DefaultsToFalse_SoCertificateValidationStaysOn()
    {
        OpenApiCommand.Settings settings = new();

        settings.Insecure.ShouldBeFalse();
    }

    [Fact]
    public void ResolveSkipPaths_UsesCli_WhenProvided()
    {
        List<string> result = OpenApiCommand.ResolveSkipPaths(["/cli/"], ConfigWith("/cfg/"));

        result.ShouldBe(["/cli/"]);
    }

    [Fact]
    public void ResolveSkipPaths_UsesConfig_WhenCliAbsent()
    {
        List<string> result = OpenApiCommand.ResolveSkipPaths(null, ConfigWith("/cfg/"));

        result.ShouldBe(["/cfg/"]);
    }

    [Fact]
    public void ResolveSkipPaths_ReturnsEmpty_WhenNeitherProvided()
    {
        List<string> result = OpenApiCommand.ResolveSkipPaths(null, null);

        result.ShouldBeEmpty();
    }

    [Fact]
    public void ResolveSkipPaths_SplitsCommaSeparatedCliValues()
    {
        List<string> result = OpenApiCommand.ResolveSkipPaths(["/a/, /b/"], null);

        result.ShouldBe(["/a/", "/b/"]);
    }

    [Fact]
    public void ResolveSkipPaths_SupportsRepeatableCliValues()
    {
        List<string> result = OpenApiCommand.ResolveSkipPaths(["/a/", "/b/"], null);

        result.ShouldBe(["/a/", "/b/"]);
    }

    [Fact]
    public void ResolveOutputPath_UsesClassName_WhenOutputEndsWithSeparator()
    {
        char separator = Path.DirectorySeparatorChar;
        string result = OpenApiCommand.ResolveOutputPath(
            $"src{separator}external-apis{separator}",
            "CommunicationApi"
        );

        result.ShouldBe(Path.Combine($"src{separator}external-apis", "CommunicationApi.ts"));
    }

    [Fact]
    public void ResolveOutputPath_DefaultsToApi_WhenOutputIsDirectoryAndNoClassName()
    {
        char separator = Path.DirectorySeparatorChar;
        string result = OpenApiCommand.ResolveOutputPath(
            $"src{separator}external-apis{separator}",
            null
        );

        result.ShouldBe(Path.Combine($"src{separator}external-apis", "api.ts"));
    }

    [Fact]
    public void ResolveOutputPath_TreatsExistingDirectoryAsDirectory()
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string result = OpenApiCommand.ResolveOutputPath(dir, "CommunicationApi");

            result.ShouldBe(Path.Combine(dir, "CommunicationApi.ts"));
        }
        finally
        {
            Directory.Delete(dir);
        }
    }

    [Fact]
    public void ResolveOutputPath_PassesThroughPlainFilePath()
    {
        string result = OpenApiCommand.ResolveOutputPath(
            "src\\external-apis\\api.ts",
            "CommunicationApi"
        );

        result.ShouldBe("src\\external-apis\\api.ts");
    }

    /// <summary>'/' is invalid in file names on both Windows and Unix.</summary>
    [Fact]
    public void ResolveOutputPath_SanitizesInvalidFileNameCharacters()
    {
        char separator = Path.DirectorySeparatorChar;

        string result = OpenApiCommand.ResolveOutputPath($"out{separator}", "My/Api");

        result.ShouldBe(Path.Combine("out", "My_Api.ts"));
    }

    [Fact]
    public void ResolveOutputPath_ReturnsEmpty_WhenOutputIsBlank()
    {
        OpenApiCommand.ResolveOutputPath("", null).ShouldBe(string.Empty);
        OpenApiCommand.ResolveOutputPath("   ", null).ShouldBe(string.Empty);
    }

    [Fact]
    public void Validate_AllowsMissingSource_ForConfigDrivenGeneration()
    {
        new OpenApiCommand.Settings().Validate().Successful.ShouldBeTrue();
    }

    [Fact]
    public void Validate_RejectsSingleClientOptions_WithoutSource()
    {
        new OpenApiCommand.Settings { Language = "typescript", Output = "api.ts" }
            .Validate()
            .Successful.ShouldBeFalse();
        new OpenApiCommand.Settings { ClassName = "Api" }
            .Validate()
            .Successful.ShouldBeFalse();
        new OpenApiCommand.Settings { SkipPaths = ["/x/"] }
            .Validate()
            .Successful.ShouldBeFalse();
        new OpenApiCommand.Settings { Insecure = true }
            .Validate()
            .Successful.ShouldBeFalse();
    }

    [Fact]
    public void Validate_RequiresOutput_WhenLanguageIsGivenWithSource()
    {
        new OpenApiCommand.Settings { Source = "spec.json", Language = "typescript" }
            .Validate()
            .Successful.ShouldBeFalse();
        new OpenApiCommand.Settings
        {
            Source = "spec.json",
            Language = "typescript",
            Output = "api.ts",
        }
            .Validate()
            .Successful.ShouldBeTrue();
    }

    [Fact]
    public void ResolveOutputPath_TreatsAPathWithoutExtensionAsAFolder()
    {
        string result = OpenApiCommand.ResolveOutputPath(Path.Combine("src", "clients"), "Api");

        result.ShouldBe(Path.Combine("src", "clients", "Api.ts"));
    }

    [Fact]
    public void EffectiveLanguage_DefaultsToTypeScript_WhenOutputIsGiven()
    {
        new OpenApiCommand.Settings
        {
            Source = "spec.json",
            Output = "api.ts",
        }.EffectiveLanguage.ShouldBe("typescript");
        new OpenApiCommand.Settings
        {
            Source = "spec.json",
            Language = "typescript",
            Output = "api.ts",
        }.EffectiveLanguage.ShouldBe("typescript");
        new OpenApiCommand.Settings { Source = "spec.json" }.EffectiveLanguage.ShouldBeNull();
    }

    /// <summary>"[1]" is a Spectre style tag in markup; the error must be printed as text, not parsed.</summary>
    [Fact]
    public void Run_ReportsMissingSourceFile_ToStderr_EvenWhenItsNameLooksLikeMarkup()
    {
        string source = Path.Combine(Path.GetTempPath(), "catalyst-missing", "api[1].json");

        CommandRun run = RunCommand("--source", source);

        run.ExitCode.ShouldBe(1);
        run.Error.ShouldContain("File not found", Case.Sensitive);
        run.Error.ShouldContain("api[1].json", Case.Sensitive);
        run.Output.ShouldNotContain("File not found", Case.Sensitive);
    }

    [Fact]
    public void Run_ReportsWhatIsWrongWithAMalformedSpec()
    {
        string dir = CreateTempDirectory();
        try
        {
            string spec = Path.Combine(dir, "broken.json");
            File.WriteAllText(spec, "{ \"openapi\": \"3.0.1\", \"info\": ");

            CommandRun run = RunCommand("--source", spec);

            run.ExitCode.ShouldBe(1);
            run.Error.ShouldContain("Failed to parse OpenAPI document", Case.Sensitive);
            run.Error.ShouldNotContain("empty result", Case.Sensitive);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>An identical regeneration must not rewrite the file (no timestamp churn).</summary>
    [Fact]
    public void Run_GeneratesTypeScript_WhenOnlyOutputIsGiven_AndLeavesUnchangedFilesAlone()
    {
        string dir = CreateTempDirectory();
        try
        {
            string spec = Path.Combine(dir, "spec.json");
            string output = Path.Combine(dir, "out", "api.ts");
            File.Copy(TestPaths.SpecPath("minimal"), spec);

            CommandRun first = RunCommand("--source", spec, "--output", output);

            first.ExitCode.ShouldBe(0);
            first.Output.ShouldContain("TypeScript generated", Case.Sensitive);
            File.Exists(output).ShouldBeTrue();

            DateTime old = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(output, old);

            CommandRun second = RunCommand("--source", spec, "--output", output);

            second.ExitCode.ShouldBe(0);
            second.Output.ShouldContain("Unchanged since the last generation", Case.Sensitive);
            File.GetLastWriteTimeUtc(output).ShouldBe(old);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// The Syncfully Workbench finds the generated file with this exact pattern (electron/main/catalyst.ts).
    /// The line must match, and carry nothing but the path, whether or not the file was rewritten.
    /// JavaScript ends a line at CR for $, .NET does not: compare with LF line ends.
    /// Longer than 200 characters (but under Windows MAX_PATH) and written with mixed separators: the line must not wrap, and the path must come back normalised. Segments are added until the full path passes 200 characters, whatever the length of the temp directory.
    /// </summary>
    [Fact]
    public void Run_SuccessLine_MatchesTheWorkbenchContract_WhenWrittenAndWhenUnchanged()
    {
        Regex workbench = new(
            @"^[ \t]*TypeScript generated:[ \t]*(?<path>.+?)[ \t]*$",
            RegexOptions.Multiline
        );
        string dir = CreateTempDirectory();
        try
        {
            string spec = Path.Combine(dir, "spec.json");
            string nested = NestedDirectoryPast(dir, 200);
            string output = Path.GetFullPath($"{dir}/{nested}{Path.DirectorySeparatorChar}api.ts");
            output.Length.ShouldBeInRange(201, 259);
            File.Copy(TestPaths.SpecPath("minimal"), spec);

            string written = RunCommand(
                80,
                "--source",
                spec,
                "--output",
                $"{dir}/{nested}{Path.DirectorySeparatorChar}api.ts"
            ).Output;
            string unchanged = RunCommand(
                80,
                "--source",
                spec,
                "--output",
                $"{dir}/{nested}{Path.DirectorySeparatorChar}api.ts"
            ).Output;

            written.ShouldNotContain("Unchanged since", Case.Sensitive);
            unchanged.ShouldContain("Unchanged since", Case.Sensitive);
            AssertWorkbenchLine(workbench, written, output);
            AssertWorkbenchLine(workbench, unchanged, output);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// On GitHub Actions Spectre reports ANSI support even though the output is redirected. The lines that tools parse
    /// must stay free of escape codes then, or "TypeScript generated:" and the other labels would no longer match.
    /// </summary>
    [Fact]
    public void Run_KeepsEscapeCodesOutOfTheParsedLines_EvenWhenTheConsoleReportsAnsi()
    {
        string dir = CreateTempDirectory();
        try
        {
            string spec = Path.Combine(dir, "spec.json");
            string output = Path.Combine(dir, "api.ts");
            File.Copy(TestPaths.SpecPath("edge-cases"), spec);

            CommandRun generated = CommandHarness.Run(
                1000,
                ansi: true,
                ["gen", "openapi", "--source", spec, "--output", output]
            );
            File.WriteAllText(output, "// old");
            CommandRun stale = CommandHarness.Run(
                1000,
                ansi: true,
                ["gen", "openapi", "--source", spec, "--output", output, "--check"]
            );
            CommandRun failed = CommandHarness.Run(
                1000,
                ansi: true,
                ["gen", "openapi", "--source", Path.Combine(dir, "missing.json")]
            );

            generated.Output.ShouldContain("TypeScript generated: " + output, Case.Sensitive);
            generated.Error.ShouldContain("Warning: ", Case.Sensitive);
            stale.Output.ShouldContain("Out of date: " + output, Case.Sensitive);
            failed.Error.ShouldContain("Error: File not found", Case.Sensitive);
            new[] { generated.Output, generated.Error, stale.Output, failed.Error }
                .SelectMany(text => text.Split('\n'))
                .Where(line =>
                    line.Contains("TypeScript generated:", StringComparison.Ordinal)
                    || line.Contains("Out of date:", StringComparison.Ordinal)
                    || line.Contains("Warning:", StringComparison.Ordinal)
                    || line.Contains("Error:", StringComparison.Ordinal)
                )
                .ShouldAllBe(line => !line.Contains('\u001b', StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Run_SendsWarningsToStderr_AndResultsToStdout()
    {
        string dir = CreateTempDirectory();
        try
        {
            string spec = Path.Combine(dir, "spec.json");
            File.Copy(TestPaths.SpecPath("edge-cases"), spec);

            CommandRun run = RunCommand("--source", spec, "--output", Path.Combine(dir, "api.ts"));

            run.ExitCode.ShouldBe(0);
            run.Output.ShouldContain("TypeScript generated", Case.Sensitive);
            run.Output.ShouldNotContain("Warning:", Case.Sensitive);
            run.Error.ShouldContain("Warning:", Case.Sensitive);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_ReportsCancelled_WithExitCode130_NotATimeout()
    {
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        (int exitCode, string error) = await CapturingErrorAsync(() =>
            OpenApiCommand.RunAsync(
                new OpenApiCommand.Settings { Source = "http://127.0.0.1:1/openapi.json" },
                cancellation.Token
            )
        );

        exitCode.ShouldBe(130);
        error.ShouldContain("Cancelled.", Case.Sensitive);
        error.ShouldNotContain("timed out", Case.Sensitive);
    }

    [Fact]
    public void Check_RejectsValidationOnlyMode()
    {
        new OpenApiCommand.Settings { Source = "spec.json", Check = true }
            .Validate()
            .Successful.ShouldBeFalse();
        new OpenApiCommand.Settings
        {
            Source = "spec.json",
            Check = true,
            Output = "api.ts",
        }
            .Validate()
            .Successful.ShouldBeTrue();
        new OpenApiCommand.Settings { Check = true }
            .Validate()
            .Successful.ShouldBeTrue();
    }

    [Fact]
    public void Check_ExitsZero_WhenTheFileIsUpToDate()
    {
        string dir = CreateTempDirectory();
        try
        {
            string spec = Path.Combine(dir, "spec.json");
            string output = Path.Combine(dir, "api.ts");
            File.Copy(TestPaths.SpecPath("minimal"), spec);
            RunCommand("--source", spec, "--output", output);

            CommandRun run = RunCommand("--source", spec, "--output", output, "--check");

            run.ExitCode.ShouldBe(0);
            run.Output.ShouldContain("Up to date: " + output, Case.Sensitive);
            run.Output.ShouldContain("1 client(s) up to date.", Case.Sensitive);
            run.Error.ShouldBe(string.Empty);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Check_ExitsOne_AndLeavesAStaleFileUntouched()
    {
        string dir = CreateTempDirectory();
        try
        {
            string spec = Path.Combine(dir, "spec.json");
            string output = Path.Combine(dir, "api.ts");
            File.Copy(TestPaths.SpecPath("minimal"), spec);
            File.WriteAllText(output, "// hand edited");
            DateTime old = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(output, old);

            CommandRun run = RunCommand("--source", spec, "--output", output, "--check");

            run.ExitCode.ShouldBe(1);
            run.Output.ShouldContain("Out of date: " + output, Case.Sensitive);
            File.ReadAllText(output).ShouldBe("// hand edited");
            File.GetLastWriteTimeUtc(output).ShouldBe(old);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Check_ExitsOne_AndCreatesNothing_WhenTheFileIsMissing()
    {
        string dir = CreateTempDirectory();
        try
        {
            string spec = Path.Combine(dir, "spec.json");
            string output = Path.Combine(dir, "not-yet", "api.ts");
            File.Copy(TestPaths.SpecPath("minimal"), spec);

            CommandRun run = RunCommand("--source", spec, "--output", output, "--check");

            run.ExitCode.ShouldBe(1);
            run.Output.ShouldContain("Missing: " + output, Case.Sensitive);
            Directory.Exists(Path.Combine(dir, "not-yet")).ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>Parsing with unmapped members disallowed means the README shape and the output cannot drift apart silently.</summary>
    [Fact]
    public void Json_Validate_DescribesTheSpec_InTheDocumentedShape()
    {
        CommandRun run = RunCommand("--source", TestPaths.SpecPath("minimal"), "--json");

        JsonReport report = ParseReport(run);

        run.ExitCode.ShouldBe(0);
        report.SchemaVersion.ShouldBe(1);
        report.Command.ShouldBe("gen openapi");
        report.Mode.ShouldBe("validate");
        report.Success.ShouldBeTrue();
        report.ExitCode.ShouldBe(0);
        report.Results.ShouldBeEmpty();
        report.Error.ShouldBeNull();
        report.Spec.ShouldNotBeNull().Title.ShouldBe("Minimal API");
        report.Spec.Version.ShouldBe("1.0.0");
        report.Spec.Openapi.ShouldBe("3.0");
        report.Spec.PathCount.ShouldBe(10);
        report.Spec.SchemaCount.ShouldBe(1);
        report.Spec.Operations.ShouldContain(o =>
            o.Method == "GET"
            && o.Path == "/widgets"
            && o.Summary == "List widgets"
            && o.OperationId == null
        );
        (report.Diagnostics.ShouldNotBeNull()).ShouldBeEmpty();
    }

    [Fact]
    public void Json_Generate_ReportsStatusWarningsAndTheNormalisedPath()
    {
        string dir = CreateTempDirectory();
        try
        {
            string spec = Path.Combine(dir, "spec.json");
            string output = Path.Combine(dir, "out", "api.ts");
            File.Copy(TestPaths.SpecPath("edge-cases"), spec);
            string messy = dir + "/out/../out/api.ts";

            JsonReport first = ParseReport(
                RunCommand("--source", spec, "--output", messy, "--json")
            );
            JsonReport second = ParseReport(
                RunCommand("--source", spec, "--output", messy, "--json")
            );

            first.Mode.ShouldBe("generate");
            first.Success.ShouldBeTrue();
            JsonResult result = first.Results.ShouldHaveSingleItem();
            result.Name.ShouldBeNull();
            result.Source.ShouldBe(spec);
            result.OutputPath.ShouldBe(output);
            result.Status.ShouldBe("written");
            result.Warnings.ShouldContain(w => w.Code == "PATH_PARAMETER_MISMATCH");
            result.Error.ShouldBeNull();
            second.Results.ShouldHaveSingleItem().Status.ShouldBe("unchanged");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Json_Check_ReportsStaleAndExitCodeOne()
    {
        string dir = CreateTempDirectory();
        try
        {
            string spec = Path.Combine(dir, "spec.json");
            string output = Path.Combine(dir, "api.ts");
            File.Copy(TestPaths.SpecPath("minimal"), spec);
            File.WriteAllText(output, "// old");

            CommandRun run = RunCommand("--source", spec, "--output", output, "--check", "--json");
            JsonReport report = ParseReport(run);

            run.ExitCode.ShouldBe(1);
            report.Mode.ShouldBe("check");
            report.Success.ShouldBeFalse();
            report.ExitCode.ShouldBe(1);
            report.Results.ShouldHaveSingleItem().Status.ShouldBe("stale");
            report.Error.ShouldBeNull();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Json_Configured_ReportsEveryClient_AndCodesForTheFailedOne()
    {
        string dir = CreateTempDirectory();
        string previous = Directory.GetCurrentDirectory();
        try
        {
            File.Copy(TestPaths.SpecPath("minimal"), Path.Combine(dir, "spec.json"));
            File.WriteAllText(
                Path.Combine(dir, "catalyst.config.json"),
                """
                { "gen": { "openapi": { "clients": [
                  { "name": "Works", "source": "spec.json", "output": "out" },
                  { "name": "NoSource", "output": "out" },
                  { "name": "Missing", "source": "gone.json", "output": "out" }
                ] } } }
                """
            );
            Directory.SetCurrentDirectory(dir);

            CommandRun run = RunCommand("--json");
            JsonReport report = ParseReport(run);

            run.ExitCode.ShouldBe(1);
            report.Mode.ShouldBe("generate");
            report.Success.ShouldBeFalse();
            report.Results.Select(r => r.Status).ShouldBe(["written", "failed", "failed"]);
            report.Results[0].OutputPath.ShouldBe(Path.Combine(dir, "out", "Works.ts"));
            report.Results[1].Error.ShouldNotBeNull().Code.ShouldBe("CLIENT_INVALID");
            report.Results[2].Error.ShouldNotBeNull().Code.ShouldBe("SOURCE_NOT_FOUND");
            report.Error.ShouldBeNull();
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Json_ReportsErrorsAsCodes_WithNothingOnStderr()
    {
        string source = Path.Combine(Path.GetTempPath(), "catalyst-missing", "api[1].json");

        CommandRun run = RunCommand("--source", source, "--json");
        JsonReport report = ParseReport(run);

        run.ExitCode.ShouldBe(1);
        report.Success.ShouldBeFalse();
        report.Error.ShouldNotBeNull().Code.ShouldBe("SOURCE_NOT_FOUND");
        report.Error.Message.ShouldContain("api[1].json", Case.Sensitive);
    }

    [Fact]
    public void Json_ReportsUsageErrors_WithExitCodeTwo()
    {
        CommandRun run = RunCommand("--output", "api.ts", "--json");
        JsonReport report = ParseReport(run);

        run.ExitCode.ShouldBe(2);
        report.Error.ShouldNotBeNull().Code.ShouldBe("USAGE");
        report.ExitCode.ShouldBe(2);
        report.Success.ShouldBeFalse();
    }

    /// <summary>Spectre's parser rejects this before the command runs; --json still gets its document.</summary>
    [Fact]
    public void Json_ReportsAMistypedOption_AsAUsageError()
    {
        CommandRun run = RunCommand(
            "--source",
            TestPaths.SpecPath("minimal"),
            "--outptu",
            "x.ts",
            "--json"
        );
        JsonReport report = ParseReport(run);

        run.ExitCode.ShouldBe(2);
        report.Error.ShouldNotBeNull().Code.ShouldBe("USAGE");
        report.Error.Message.ShouldContain("outptu", Case.Sensitive);
    }

    [Fact]
    public void UsageErrors_ExitWithTwo_AndWriteNothing()
    {
        string dir = CreateTempDirectory();
        try
        {
            string spec = Path.Combine(dir, "spec.json");
            string output = Path.Combine(dir, "x.ts");
            File.Copy(TestPaths.SpecPath("minimal"), spec);

            CommandRun typo = RunCommand("--source", spec, "--outptu", output);
            CommandRun stray = RunCommand("--source", spec, "--output", output, "stray-argument");
            CommandRun incompatible = RunCommand("--output", output);
            CommandRun missingValue = RunCommand("--source");

            AssertUsageError(typo);
            AssertUsageError(stray);
            AssertUsageError(incompatible);
            AssertUsageError(missingValue);

            typo.Error.ShouldContain("outptu", Case.Sensitive);
            stray.Error.ShouldContain("stray-argument", Case.Sensitive);
            File.Exists(output).ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static void AssertUsageError(CommandRun run)
    {
        run.ExitCode.ShouldBe(2);
        run.Error.ShouldNotBe(string.Empty);
        run.Output.ShouldBe(string.Empty);
    }

    private static readonly JsonSerializerOptions ReportOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private static JsonReport ParseReport(CommandRun run)
    {
        run.Error.ShouldBe(string.Empty);

        return JsonSerializer.Deserialize<JsonReport>(run.Output, ReportOptions).ShouldNotBeNull();
    }

    /// <summary>The documented JSON shape (see the README), spelled out independently of the production types.</summary>
    private sealed record JsonIssue(string Code, string Message);

    private sealed record JsonResult(
        string? Name,
        string Source,
        string? OutputPath,
        string Status,
        List<JsonIssue> Warnings,
        JsonIssue? Error
    );

    private sealed record JsonOperation(
        string Method,
        string Path,
        string? OperationId,
        string? Summary
    );

    private sealed record JsonSpec(
        string Title,
        string Version,
        string Openapi,
        int PathCount,
        int SchemaCount,
        List<JsonOperation> Operations
    );

    private sealed record JsonDiagnostic(string Severity, string? Pointer, string Message);

    private sealed record JsonReport(
        int SchemaVersion,
        string Command,
        string Mode,
        bool Success,
        int ExitCode,
        List<JsonResult> Results,
        JsonSpec? Spec,
        List<JsonDiagnostic>? Diagnostics,
        JsonIssue? Error
    );

    private static void AssertWorkbenchLine(Regex workbench, string stdout, string expectedPath)
    {
        Match match = workbench.Match(stdout.Replace("\r\n", "\n", StringComparison.Ordinal));
        match.Success.ShouldBeTrue(stdout);
        match.Groups["path"].Value.ShouldBe(expectedPath);
    }

    private static string NestedDirectoryPast(string dir, int length)
    {
        const string segment = "nested-directory-segment";
        int segments = 1;
        while (
            Path.GetFullPath(
                $"{dir}/{string.Join('/', Enumerable.Repeat(segment, segments))}{Path.DirectorySeparatorChar}api.ts"
            ).Length <= length
        )
        {
            segments++;
        }

        return string.Join('/', Enumerable.Repeat(segment, segments));
    }

    private static string CreateTempDirectory()
    {
        string dir = Path.Combine(
            Path.GetTempPath(),
            "catalyst-command-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static async Task<(int ExitCode, string Error)> CapturingErrorAsync(
        Func<Task<int>> action
    )
    {
        StringWriter error = new();
        IAnsiConsole previous = Terminal.Error;
        Terminal.Error = CommandHarness.CreateConsole(error);
        try
        {
            int exitCode = await action();
            return (exitCode, error.ToString());
        }
        finally
        {
            Terminal.Error = previous;
        }
    }

    private static CommandRun RunCommand(params string[] arguments) => RunCommand(1000, arguments);

    private static CommandRun RunCommand(int width, params string[] arguments)
    {
        return CommandHarness.Run(width, ["gen", "openapi", .. arguments]);
    }
}
