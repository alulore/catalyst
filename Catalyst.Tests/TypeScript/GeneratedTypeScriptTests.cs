using System.ComponentModel;
using System.Diagnostics;
using Shouldly;

namespace Catalyst.Tests.TypeScript;

/// <summary>
/// Proves the snapshots are real TypeScript: every snapshot must compile with tsc --strict, and the
/// compiled clients are exercised against a stubbed fetch (runtime/*.test.mjs, run by node:test).
/// Needs node and npm on PATH; the first run installs the pinned TypeScript with npm ci.
/// </summary>
[Collection("TypeScript")]
public class GeneratedTypeScriptTests
{
    private readonly TypeScriptToolchain _toolchain;

    public GeneratedTypeScriptTests(TypeScriptToolchain toolchain)
    {
        _toolchain = toolchain;
    }

    [Fact]
    public async Task GeneratedClients_PassTheNodeRuntimeTests()
    {
        ToolResult compile = _toolchain.Compile;
        compile.ExitCode.ShouldBe(
            0,
            "tsc must compile the snapshots with strict settings before the runtime tests can run:"
                + Environment.NewLine
                + compile.Output
        );

        ToolResult result = await TypeScriptToolchain.RunAsync(
            "node",
            ["--test", "runtime/*.test.mjs"],
            TestPaths.TypeScriptDirectory
        );

        result.ExitCode.ShouldBe(0, "node --test failed:" + Environment.NewLine + result.Output);
    }
}

/// <summary>Every test class that needs the TypeScript toolchain shares one instance, so npm ci never runs twice at once.</summary>
[CollectionDefinition("TypeScript")]
public sealed class TypeScriptCollection : ICollectionFixture<TypeScriptToolchain> { }

/// <summary>Installs the TypeScript compiler once per test run and compiles the snapshots on demand.</summary>
public sealed class TypeScriptToolchain : IAsyncLifetime
{
    private static readonly TimeSpan ToolTimeout = TimeSpan.FromMinutes(5);

    public ToolResult? CompileResult { get; private set; }

    public ToolResult Compile =>
        CompileResult ?? throw new InvalidOperationException("The toolchain is not initialized.");

    public async ValueTask InitializeAsync()
    {
        string typeScript = TestPaths.TypeScriptDirectory;

        if (!File.Exists(CompilerPath))
        {
            ToolResult install = await RunAsync(
                "npm",
                ["ci", "--no-audit", "--no-fund"],
                typeScript
            );
            if (install.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    "npm ci failed in " + typeScript + ":" + Environment.NewLine + install.Output
                );
            }
        }

        CompileResult = await RunAsync(
            "node",
            [CompilerPath, "-p", "tsconfig.json"],
            TestPaths.TypeScriptDirectory
        );
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>Type-checks standalone TypeScript with the same strictness as the snapshots.</summary>
    public async Task<ToolResult> TypecheckAsync(string code)
    {
        string directory = Path.Combine(TestPaths.TypeScriptDirectory, "build", "lookup");
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".ts");
        await File.WriteAllTextAsync(file, code, TestContext.Current.CancellationToken);

        try
        {
            return await RunAsync(
                "node",
                [
                    CompilerPath,
                    file,
                    "--noEmit",
                    "--strict",
                    "--target",
                    "ES2022",
                    "--lib",
                    "ES2022,DOM",
                ],
                TestPaths.TypeScriptDirectory
            );
        }
        finally
        {
            File.Delete(file);
        }
    }

    private static string CompilerPath =>
        Path.Combine(TestPaths.TypeScriptDirectory, "node_modules", "typescript", "bin", "tsc");

    /// <summary>
    /// Runs a command and captures its output. npm is a .cmd shim on Windows, which CreateProcess
    /// cannot start directly, so it goes through cmd.exe there.
    /// Reading to the end after the process exits drains whatever is left in the pipes.
    /// </summary>
    public static async Task<ToolResult> RunAsync(
        string tool,
        IReadOnlyList<string> arguments,
        string workingDirectory
    )
    {
        ProcessStartInfo startInfo = new()
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        if (OperatingSystem.IsWindows() && tool == "npm")
        {
            startInfo.FileName = "cmd.exe";
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add("npm");
        }
        else
        {
            startInfo.FileName = tool;
        }

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process process;
        try
        {
            process = Process.Start(startInfo).ShouldNotBeNull();
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException(
                $"Could not start '{tool}'. These tests need node and npm on PATH (Node 22 or newer). {ex.Message}",
                ex
            );
        }

        using (process)
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken
            );
            timeout.CancelAfter(ToolTimeout);
            Task<string> output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            Task<string> error = process.StandardError.ReadToEndAsync(timeout.Token);

            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
                when (!TestContext.Current.CancellationToken.IsCancellationRequested)
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException(
                    $"'{tool} {string.Join(' ', arguments)}' did not finish within {ToolTimeout.TotalMinutes} minutes.",
                    new OperationCanceledException(timeout.Token)
                );
            }

            return new ToolResult(process.ExitCode, await output + await error);
        }
    }
}

public sealed record ToolResult(int ExitCode, string Output);
