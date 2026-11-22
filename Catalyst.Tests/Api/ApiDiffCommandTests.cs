using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Shouldly;

namespace Catalyst.Tests.Api;

/// <summary>A fact that is skipped, with a reason, only when git is not installed (CI has it).</summary>
public sealed class GitFactAttribute : FactAttribute
{
    public GitFactAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1
    )
        : base(sourceFilePath, sourceLineNumber)
    {
        try
        {
            using Process? git = Process.Start(
                new ProcessStartInfo("git", "--version")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                }
            );
            git?.WaitForExit();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Skip = "git is not on PATH, so the git: base cannot be tested here.";
        }
    }
}

[Collection("AnsiConsole")]
public class ApiDiffCommandTests
{
    private static CommandRun Api(params string[] arguments) =>
        CommandHarness.Run(["api", .. arguments]);

    private static string CreateTempDirectory()
    {
        string dir = Path.Combine(
            Path.GetTempPath(),
            "catalyst-diff-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Writes the edge-cases spec twice: as is, and with a parameter removed and an operation added.</summary>
    private static async Task<(string Old, string New)> WriteVersionsAsync(string dir)
    {
        string oldPath = Path.Combine(dir, "old.json");
        string newPath = Path.Combine(dir, "new.json");
        File.Copy(TestPaths.SpecPath("edge-cases"), oldPath);

        JsonObject spec = (JsonObject)
            JsonNode
                .Parse(await File.ReadAllTextAsync(oldPath, TestContext.Current.CancellationToken))
                .ShouldNotBeNull();
        ((JsonObject)spec["paths"].ShouldNotBeNull()).Remove("/ping");
        ((JsonObject)spec["paths"].ShouldNotBeNull())["/extra"] = JsonNode.Parse(
            """{ "get": { "operationId": "getExtra", "responses": { "200": { "description": "ok" } } } }"""
        );
        await File.WriteAllTextAsync(
            newPath,
            spec.ToJsonString(),
            TestContext.Current.CancellationToken
        );

        return (oldPath, newPath);
    }

    private static void AssertBaseNotFound(CommandRun run)
    {
        JsonElement root = JsonElement.Parse(run.Output);
        run.ExitCode.ShouldBe(1);
        root.GetProperty("error").GetProperty("code").GetString().ShouldBe("BASE_NOT_FOUND");
    }

    private static List<string?> ChangeKinds(CommandRun run) =>
        JsonElement
            .Parse(run.Output)
            .GetProperty("changes")
            .EnumerateArray()
            .Select(change => change.GetProperty("kind").GetString())
            .ToList();

    [Fact]
    public async Task Diff_PrintsBreakingChangesFirst_AndASummary()
    {
        string dir = CreateTempDirectory();
        try
        {
            (string oldPath, string newPath) = await WriteVersionsAsync(dir);

            CommandRun run = Api("diff", "--base", oldPath, "--source", newPath);

            run.ExitCode.ShouldBe(0);
            run.Error.ShouldBe(string.Empty);
            string[] lines = run.Output.Split('\n', StringSplitOptions.TrimEntries);
            lines[0].ShouldBe("Breaking changes (1)");
            lines[1].ShouldBe("BREAKING  GET /ping  client.get.ping: removed");
            lines[2].ShouldBe("Other changes (1)");
            lines[3].ShouldBe("change    GET /extra  client.get.extra: added");
            run.Output.ShouldContain("1 breaking, 1 non-breaking change(s).", Case.Sensitive);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Diff_Says_NoChanges_ForIdenticalSpecs()
    {
        CommandRun run = Api(
            "diff",
            "--base",
            TestPaths.SpecPath("petstore"),
            "--source",
            TestPaths.SpecPath("petstore")
        );

        run.ExitCode.ShouldBe(0);
        run.Output.Trim().ShouldBe("No changes.");
    }

    [Fact]
    public async Task Diff_Json_HasTheEnvelopeChangesAndSummary()
    {
        string dir = CreateTempDirectory();
        try
        {
            (string oldPath, string newPath) = await WriteVersionsAsync(dir);

            CommandRun run = Api("diff", "--base", oldPath, "--source", newPath, "--json");
            JsonElement root = JsonElement.Parse(run.Output);

            run.Error.ShouldBe(string.Empty);
            root.GetProperty("command").GetString().ShouldBe("api diff");
            root.GetProperty("success").GetBoolean().ShouldBeTrue();
            root.GetProperty("summary").GetProperty("breaking").GetInt32().ShouldBe(1);
            root.GetProperty("summary").GetProperty("nonBreaking").GetInt32().ShouldBe(1);

            JsonElement removed = root.GetProperty("changes")
                .EnumerateArray()
                .Single(c => c.GetProperty("kind").GetString() == "OPERATION_REMOVED");
            removed.GetProperty("severity").GetString().ShouldBe("breaking");
            removed.GetProperty("operation").GetProperty("method").GetString().ShouldBe("GET");
            removed.GetProperty("operation").GetProperty("path").GetString().ShouldBe("/ping");
            removed
                .GetProperty("operation")
                .GetProperty("call")
                .GetString()
                .ShouldBe("client.get.ping");
            removed.GetProperty("type").ValueKind.ShouldBe(JsonValueKind.Null);
            removed.GetProperty("message").GetString().ShouldBe("removed");
            root.GetProperty("error").ValueKind.ShouldBe(JsonValueKind.Null);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Diff_FailOnBreaking_ChangesTheExitCodeOnly_WhenSomethingBreaks()
    {
        string dir = CreateTempDirectory();
        try
        {
            (string oldPath, string newPath) = await WriteVersionsAsync(dir);

            CommandRun breaking = Api(
                "diff",
                "--base",
                oldPath,
                "--source",
                newPath,
                "--fail-on-breaking"
            );
            CommandRun clean = Api(
                "diff",
                "--base",
                oldPath,
                "--source",
                oldPath,
                "--fail-on-breaking"
            );
            CommandRun additive = Api("diff", "--base", newPath, "--source", oldPath);
            JsonElement json = JsonElement.Parse(
                Api(
                    "diff",
                    "--base",
                    oldPath,
                    "--source",
                    newPath,
                    "--fail-on-breaking",
                    "--json"
                ).Output
            );

            breaking.ExitCode.ShouldBe(1);
            clean.ExitCode.ShouldBe(0);
            additive.ExitCode.ShouldBe(0);
            json.GetProperty("exitCode").GetInt32().ShouldBe(1);
            json.GetProperty("success").GetBoolean().ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Diff_RequiresABase_AsAUsageError()
    {
        CommandRun run = Api("diff", "--source", TestPaths.SpecPath("minimal"));

        run.ExitCode.ShouldBe(2);
        run.Error.ShouldContain("--base", Case.Sensitive);
    }

    [Fact]
    public async Task Diff_ReportsAMissingBase_WithTheBaseNotFoundCode()
    {
        string dir = CreateTempDirectory();
        try
        {
            CommandRun missingFile = Api(
                "diff",
                "--base",
                Path.Combine(dir, "nope.json"),
                "--source",
                TestPaths.SpecPath("minimal"),
                "--json"
            );
            string previous = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(dir);
            CommandRun notARepo;
            try
            {
                notARepo = Api(
                    "diff",
                    "--base",
                    "git:HEAD:spec.json",
                    "--source",
                    TestPaths.SpecPath("minimal"),
                    "--json"
                );
            }
            finally
            {
                Directory.SetCurrentDirectory(previous);
            }

            AssertBaseNotFound(missingFile);
            AssertBaseNotFound(notARepo);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [GitFact]
    public async Task Diff_ReadsTheBaseFromGit_WithAPathOrFromTheSourceFile()
    {
        string repo = CreateTempDirectory();
        string previous = Directory.GetCurrentDirectory();
        try
        {
            string spec = Path.Combine(repo, "spec.json");
            File.Copy(TestPaths.SpecPath("edge-cases"), spec);
            Git(repo, "init", "-q");
            Git(repo, "add", "spec.json");
            Git(
                repo,
                "-c",
                "user.email=test@example.com",
                "-c",
                "user.name=Test",
                "commit",
                "-q",
                "-m",
                "spec"
            );

            JsonObject edited = (JsonObject)
                JsonNode
                    .Parse(await File.ReadAllTextAsync(spec, TestContext.Current.CancellationToken))
                    .ShouldNotBeNull();
            ((JsonObject)edited["paths"].ShouldNotBeNull()).Remove("/ping");
            await File.WriteAllTextAsync(
                spec,
                edited.ToJsonString(),
                TestContext.Current.CancellationToken
            );
            Directory.SetCurrentDirectory(repo);

            CommandRun withPath = Api(
                "diff",
                "--base",
                "git:HEAD:spec.json",
                "--source",
                spec,
                "--json"
            );
            CommandRun sameFile = Api("diff", "--base", "git:HEAD", "--source", spec, "--json");

            withPath.ExitCode.ShouldBe(0);
            sameFile.ExitCode.ShouldBe(0);
            ChangeKinds(withPath).ShouldBe(["OPERATION_REMOVED"]);
            ChangeKinds(sameFile).ShouldBe(["OPERATION_REMOVED"]);
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
            DeleteRepository(repo);
        }
    }

    private static void Git(string directory, params string[] arguments)
    {
        ProcessStartInfo startInfo = new("git")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo).ShouldNotBeNull();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        process.ExitCode.ShouldBe(0, $"git {string.Join(' ', arguments)} failed: {error}");
    }

    /// <summary>Git marks object files read-only, which Directory.Delete refuses on Windows.</summary>
    private static void DeleteRepository(string directory)
    {
        foreach (
            string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
        )
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(directory, recursive: true);
    }
}
