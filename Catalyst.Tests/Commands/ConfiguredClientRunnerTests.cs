using Catalyst.Commands.Gen;
using Catalyst.Common.Configuration;
using Shouldly;

namespace Catalyst.Tests.Commands;

public class ConfiguredClientRunnerTests
{
    private const string Spec = """
        {
          "openapi": "3.0.3",
          "info": { "title": "Runner API", "version": "1.0.0" },
          "paths": {
            "/keep": { "get": { "responses": { "200": { "description": "ok" } } } },
            "/skip": { "get": { "responses": { "200": { "description": "ok" } } } }
          }
        }
        """;

    private static string CreateTempDirectory()
    {
        string dir = Path.Combine(
            Path.GetTempPath(),
            "catalyst-runner-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static CatalystConfig ConfigWith(params OpenApiClientConfig[] clients) =>
        new()
        {
            Gen = new GenConfig { OpenApi = new OpenApiGenConfig { Clients = clients.ToList() } },
        };

    [Fact]
    public async Task RunAsync_GeneratesEveryConfiguredClient_RelativeToTheConfigDirectory()
    {
        string dir = CreateTempDirectory();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(dir, "openapi.json"),
                Spec,
                TestContext.Current.CancellationToken
            );

            CatalystConfig config = ConfigWith(
                new OpenApiClientConfig
                {
                    Name = "FirstApi",
                    Source = "openapi.json",
                    Output = "clients",
                },
                new OpenApiClientConfig
                {
                    Name = "SecondApi",
                    Source = "openapi.json",
                    Output = Path.Combine("clients", "second.ts"),
                }
            );

            List<ClientResult> outcomes = await ConfiguredClientRunner.RunAsync(
                config,
                dir,
                CancellationToken.None
            );

            outcomes.Where(o => o.Failed).Select(o => o.Error?.Message).ShouldBeEmpty();

            string first = Path.Combine(dir, "clients", "FirstApi.ts");
            string second = Path.Combine(dir, "clients", "second.ts");
            File.Exists(first).ShouldBeTrue();
            File.Exists(second).ShouldBeTrue();
            outcomes[0].OutputPath.ShouldBe(first);
            outcomes[1].OutputPath.ShouldBe(second);
            (
                await File.ReadAllTextAsync(first, TestContext.Current.CancellationToken)
            ).ShouldContain("FirstApi", Case.Sensitive);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_FallsBackToProjectSkipPaths_WhenTheClientHasNone()
    {
        string dir = CreateTempDirectory();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(dir, "openapi.json"),
                Spec,
                TestContext.Current.CancellationToken
            );

            CatalystConfig config = new()
            {
                Gen = new GenConfig
                {
                    OpenApi = new OpenApiGenConfig
                    {
                        SkipPaths = ["/skip"],
                        Clients =
                        [
                            new OpenApiClientConfig
                            {
                                Name = "Api",
                                Source = "openapi.json",
                                Output = "out",
                            },
                        ],
                    },
                },
            };

            List<ClientResult> outcomes = await ConfiguredClientRunner.RunAsync(
                config,
                dir,
                CancellationToken.None
            );

            outcomes.ShouldHaveSingleItem().Failed.ShouldBeFalse();
            string code = await File.ReadAllTextAsync(
                Path.Combine(dir, "out", "Api.ts"),
                TestContext.Current.CancellationToken
            );
            code.ShouldContain("/keep", Case.Sensitive);
            code.ShouldNotContain("/skip", Case.Sensitive);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_ClientSkipPaths_TakePrecedenceOverProjectSkipPaths()
    {
        string dir = CreateTempDirectory();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(dir, "openapi.json"),
                Spec,
                TestContext.Current.CancellationToken
            );

            CatalystConfig config = new()
            {
                Gen = new GenConfig
                {
                    OpenApi = new OpenApiGenConfig
                    {
                        SkipPaths = ["/keep"],
                        Clients =
                        [
                            new OpenApiClientConfig
                            {
                                Name = "Api",
                                Source = "openapi.json",
                                Output = "out",
                                SkipPaths = ["/skip"],
                            },
                        ],
                    },
                },
            };

            List<ClientResult> outcomes = await ConfiguredClientRunner.RunAsync(
                config,
                dir,
                CancellationToken.None
            );

            outcomes.ShouldHaveSingleItem().Failed.ShouldBeFalse();
            string code = await File.ReadAllTextAsync(
                Path.Combine(dir, "out", "Api.ts"),
                TestContext.Current.CancellationToken
            );
            code.ShouldContain("/keep", Case.Sensitive);
            code.ShouldNotContain("/skip", Case.Sensitive);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_ReportsAClientWithoutSource_AndStillGeneratesTheOthers()
    {
        string dir = CreateTempDirectory();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(dir, "openapi.json"),
                Spec,
                TestContext.Current.CancellationToken
            );

            CatalystConfig config = ConfigWith(
                new OpenApiClientConfig { Name = "ComesLater", Output = "out" },
                new OpenApiClientConfig
                {
                    Name = "Works",
                    Source = "openapi.json",
                    Output = "out",
                }
            );

            List<ClientResult> outcomes = await ConfiguredClientRunner.RunAsync(
                config,
                dir,
                CancellationToken.None
            );

            outcomes[0].Failed.ShouldBeTrue();
            outcomes[0].Error.ShouldNotBeNull().Message.ShouldContain("No source", Case.Sensitive);
            outcomes[1].Failed.ShouldBeFalse();
            File.Exists(Path.Combine(dir, "out", "Works.ts")).ShouldBeTrue();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_ReportsEntriesWithoutNameOrOutput()
    {
        string dir = CreateTempDirectory();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(dir, "openapi.json"),
                Spec,
                TestContext.Current.CancellationToken
            );

            CatalystConfig config = ConfigWith(
                new OpenApiClientConfig
                {
                    Name = "",
                    Source = "openapi.json",
                    Output = "out",
                },
                new OpenApiClientConfig
                {
                    Name = "NoOutput",
                    Source = "openapi.json",
                    Output = "  ",
                }
            );

            List<ClientResult> outcomes = await ConfiguredClientRunner.RunAsync(
                config,
                dir,
                CancellationToken.None
            );

            outcomes.ShouldAllBe(o => o.Failed);
            outcomes[0].Name.ShouldBe("(unnamed)");
            outcomes[1]
                .Error.ShouldNotBeNull()
                .Message.ShouldContain("name and an output", Case.Sensitive);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_ReportsAMissingSourceFile()
    {
        string dir = CreateTempDirectory();
        try
        {
            CatalystConfig config = ConfigWith(
                new OpenApiClientConfig
                {
                    Name = "Api",
                    Source = "missing.json",
                    Output = "out",
                }
            );

            List<ClientResult> outcomes = await ConfiguredClientRunner.RunAsync(
                config,
                dir,
                CancellationToken.None
            );

            ClientResult outcome = outcomes.ShouldHaveSingleItem();
            outcome.Failed.ShouldBeTrue();
            outcome.Error.ShouldNotBeNull().Message.ShouldContain("File not found", Case.Sensitive);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>A NUL in the output path makes the file system throw an ArgumentException, not an IOException.</summary>
    [Fact]
    public async Task RunAsync_ContinuesWithTheNextClient_WhenOneThrowsUnexpectedly()
    {
        string dir = CreateTempDirectory();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(dir, "openapi.json"),
                Spec,
                TestContext.Current.CancellationToken
            );

            CatalystConfig config = ConfigWith(
                new OpenApiClientConfig
                {
                    Name = "Broken",
                    Source = "openapi.json",
                    Output = "bad\0dir",
                },
                new OpenApiClientConfig
                {
                    Name = "Works",
                    Source = "openapi.json",
                    Output = "out",
                }
            );

            List<ClientResult> outcomes = await ConfiguredClientRunner.RunAsync(
                config,
                dir,
                CancellationToken.None
            );

            outcomes[0].Failed.ShouldBeTrue();
            outcomes[0].Name.ShouldBe("Broken");
            string.IsNullOrWhiteSpace(outcomes[0].Error.ShouldNotBeNull().Message).ShouldBeFalse();
            outcomes[1].Failed.ShouldBeFalse();
            File.Exists(Path.Combine(dir, "out", "Works.ts")).ShouldBeTrue();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_ReportsUnchanged_WhenTheFileAlreadyHasTheGeneratedContent()
    {
        string dir = CreateTempDirectory();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(dir, "openapi.json"),
                Spec,
                TestContext.Current.CancellationToken
            );
            CatalystConfig config = ConfigWith(
                new OpenApiClientConfig
                {
                    Name = "Api",
                    Source = "openapi.json",
                    Output = "out",
                }
            );

            ClientResult first = (
                await ConfiguredClientRunner.RunAsync(config, dir, CancellationToken.None)
            ).ShouldHaveSingleItem();
            ClientResult second = (
                await ConfiguredClientRunner.RunAsync(config, dir, CancellationToken.None)
            ).ShouldHaveSingleItem();

            first.Status.ShouldBe(ClientStatus.Written);
            second.Status.ShouldBe(ClientStatus.Unchanged);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_ReportsWhatIsWrongWithAMalformedSpec()
    {
        string dir = CreateTempDirectory();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(dir, "broken.json"),
                "{ \"openapi\": ",
                TestContext.Current.CancellationToken
            );
            CatalystConfig config = ConfigWith(
                new OpenApiClientConfig
                {
                    Name = "Api",
                    Source = "broken.json",
                    Output = "out",
                }
            );

            ClientResult outcome = (
                await ConfiguredClientRunner.RunAsync(config, dir, CancellationToken.None)
            ).ShouldHaveSingleItem();

            outcome.Failed.ShouldBeTrue();
            outcome
                .Error.ShouldNotBeNull()
                .Message.ShouldNotContain("empty result", Case.Sensitive);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_Check_ReportsEachStatus_WithoutWritingAnything()
    {
        string dir = CreateTempDirectory();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(dir, "openapi.json"),
                Spec,
                TestContext.Current.CancellationToken
            );
            CatalystConfig generate = ConfigWith(
                new OpenApiClientConfig
                {
                    Name = "Current",
                    Source = "openapi.json",
                    Output = "out",
                }
            );
            await ConfiguredClientRunner.RunAsync(generate, dir, CancellationToken.None);

            string staleFile = Path.Combine(dir, "out", "Stale.ts");
            await File.WriteAllTextAsync(
                staleFile,
                "// hand edited",
                TestContext.Current.CancellationToken
            );

            CatalystConfig config = ConfigWith(
                new OpenApiClientConfig
                {
                    Name = "Current",
                    Source = "openapi.json",
                    Output = "out",
                },
                new OpenApiClientConfig
                {
                    Name = "Stale",
                    Source = "openapi.json",
                    Output = "out",
                },
                new OpenApiClientConfig
                {
                    Name = "Absent",
                    Source = "openapi.json",
                    Output = "out",
                },
                new OpenApiClientConfig
                {
                    Name = "Broken",
                    Source = "gone.json",
                    Output = "out",
                }
            );

            List<ClientResult> results = await ConfiguredClientRunner.RunAsync(
                config,
                dir,
                CancellationToken.None,
                check: true
            );

            results
                .Select(r => r.Status)
                .ShouldBe([
                    ClientStatus.UpToDate,
                    ClientStatus.Stale,
                    ClientStatus.Missing,
                    ClientStatus.Failed,
                ]);
            (
                await File.ReadAllTextAsync(staleFile, TestContext.Current.CancellationToken)
            ).ShouldBe("// hand edited");
            File.Exists(Path.Combine(dir, "out", "Absent.ts")).ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
