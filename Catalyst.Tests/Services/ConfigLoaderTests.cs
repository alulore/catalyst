using System.Text.Json;
using Catalyst.Common.Configuration;
using Shouldly;

namespace Catalyst.Tests.Services;

public class ConfigLoaderTests
{
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    private static string WriteConfig(string directory, string fileName, CatalystConfig config)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, fileName);
        File.WriteAllText(path, JsonSerializer.Serialize(config, IndentedJson));
        return path;
    }

    [Fact]
    public void Load_ReturnsNull_WhenNoConfigFilePresent()
    {
        string dir = Path.Combine(
            Path.GetTempPath(),
            "catalyst-config-tests",
            Guid.NewGuid().ToString()
        );
        Directory.CreateDirectory(dir);

        try
        {
            ConfigLoader.Load(dir).ShouldBeNull();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Load_ReadsSkipPaths_FromCatalystConfigJson()
    {
        string dir = Path.Combine(
            Path.GetTempPath(),
            "catalyst-config-tests",
            Guid.NewGuid().ToString()
        );
        WriteConfig(
            dir,
            "catalyst.config.json",
            new CatalystConfig
            {
                Gen = new GenConfig
                {
                    OpenApi = new OpenApiGenConfig { SkipPaths = ["/api/auth/", "/internal/"] },
                },
            }
        );

        try
        {
            CatalystConfig? loaded = ConfigLoader.Load(dir);

            loaded.ShouldNotBeNull();
            loaded.Gen.OpenApi.SkipPaths.ShouldBe(["/api/auth/", "/internal/"]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Load_ReadsSkipPaths_FromHiddenConfigFile()
    {
        string dir = Path.Combine(
            Path.GetTempPath(),
            "catalyst-config-tests",
            Guid.NewGuid().ToString()
        );
        WriteConfig(
            dir,
            ".catalyst.config.json",
            new CatalystConfig
            {
                Gen = new GenConfig { OpenApi = new OpenApiGenConfig { SkipPaths = ["/x/"] } },
            }
        );

        try
        {
            CatalystConfig? loaded = ConfigLoader.Load(dir);

            loaded.ShouldNotBeNull();
            loaded.ShouldNotBeNull().Gen.OpenApi.SkipPaths.ShouldContain("/x/");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Load_WalksUpToParentDirectory()
    {
        string parent = Path.Combine(
            Path.GetTempPath(),
            "catalyst-config-tests",
            Guid.NewGuid().ToString()
        );
        string child = Path.Combine(parent, "sub", "deep");
        WriteConfig(
            parent,
            "catalyst.config.json",
            new CatalystConfig
            {
                Gen = new GenConfig
                {
                    OpenApi = new OpenApiGenConfig { SkipPaths = ["/parent-only/"] },
                },
            }
        );

        try
        {
            CatalystConfig? loaded = ConfigLoader.Load(child);

            loaded.ShouldNotBeNull();
            loaded.ShouldNotBeNull().Gen.OpenApi.SkipPaths.ShouldContain("/parent-only/");
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    [Fact]
    public void Load_ReadsFixtureConfigFile()
    {
        CatalystConfig? loaded = ConfigLoader.Load(
            Path.Combine(AppContext.BaseDirectory, "Fixtures")
        );

        loaded.ShouldNotBeNull().Gen.OpenApi.SkipPaths.ShouldBe(["/api/auth/", "/internal/"]);
    }

    [Fact]
    public void Load_ReadsClientDefinitions()
    {
        string dir = Path.Combine(
            Path.GetTempPath(),
            "catalyst-config-tests",
            Guid.NewGuid().ToString()
        );
        WriteConfig(
            dir,
            "catalyst.config.json",
            new CatalystConfig
            {
                Gen = new GenConfig
                {
                    OpenApi = new OpenApiGenConfig
                    {
                        SkipPaths = ["/project/"],
                        Clients =
                        [
                            new OpenApiClientConfig
                            {
                                Name = "CommunicationApi",
                                Source = "https://localhost:7036/openapi/v1.json",
                                Output = "src/external-apis",
                                Insecure = true,
                                SkipPaths = ["/client/"],
                            },
                        ],
                    },
                },
            }
        );

        try
        {
            CatalystConfig? loaded = ConfigLoader.Load(dir);

            loaded.ShouldNotBeNull();
            OpenApiClientConfig client = loaded
                .ShouldNotBeNull()
                .Gen.OpenApi.Clients.ShouldHaveSingleItem();
            client.Name.ShouldBe("CommunicationApi");
            client.Source.ShouldBe("https://localhost:7036/openapi/v1.json");
            client.Output.ShouldBe("src/external-apis");
            client.Insecure.ShouldBeTrue();
            client.SkipPaths.ShouldBe(["/client/"]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LoadDetailed_ReportsConfigPathAndConfig()
    {
        string dir = Path.Combine(
            Path.GetTempPath(),
            "catalyst-config-tests",
            Guid.NewGuid().ToString()
        );
        string path = WriteConfig(dir, "catalyst.config.json", new CatalystConfig());

        try
        {
            ConfigLoadResult result = ConfigLoader.LoadDetailed(dir);

            result.Config.ShouldNotBeNull();
            result.Path.ShouldBe(path);
            result.Error.ShouldBeNull();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LoadDetailed_ReportsNothing_WhenNoConfigExists()
    {
        string dir = Path.Combine(
            Path.GetTempPath(),
            "catalyst-config-tests",
            Guid.NewGuid().ToString()
        );
        Directory.CreateDirectory(dir);

        try
        {
            ConfigLoadResult result = ConfigLoader.LoadDetailed(dir);

            result.Config.ShouldBeNull();
            result.Path.ShouldBeNull();
            result.Error.ShouldBeNull();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LoadDetailed_ReportsThePathAndReason_ForInvalidJson()
    {
        string dir = Path.Combine(
            Path.GetTempPath(),
            "catalyst-config-tests",
            Guid.NewGuid().ToString()
        );
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "catalyst.config.json");
        File.WriteAllText(path, "{ not json");

        try
        {
            ConfigLoadResult result = ConfigLoader.LoadDetailed(dir);

            result.Config.ShouldBeNull();
            result.Path.ShouldBe(path);
            result.Error.ShouldNotBeNull();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
