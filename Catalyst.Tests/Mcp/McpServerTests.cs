using System.Diagnostics;
using System.IO.Pipelines;
using System.Text.Json;
using System.Text.Json.Nodes;
using Catalyst.Commands.Mcp;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Shouldly;

namespace Catalyst.Tests.Mcp;

/// <summary>
/// Drives the real tool set through the SDK client over in-memory pipes. The payload of each tool is compared with what
/// the CLI prints with --json for the same inputs; the lookup and diff semantics themselves are tested with the api commands.
/// </summary>
/// <summary>Some tests change the working directory (config discovery) and the CLI helper replaces the static consoles.</summary>
[Collection("AnsiConsole")]
public class McpServerTests
{
    private static readonly string EdgeCases = TestPaths.SpecPath("edge-cases");

    private sealed class Session : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly CancellationTokenSource _stop = new();

        public McpClient Client { get; }

        private Session(ServiceProvider provider, McpClient client)
        {
            _provider = provider;
            Client = client;
        }

        public static async Task<Session> StartAsync()
        {
            Pipe clientToServer = new();
            Pipe serverToClient = new();

            ServiceCollection services = new();
            services.AddLogging();
            services
                .AddCatalystServer()
                .WithStreamServerTransport(
                    clientToServer.Reader.AsStream(),
                    serverToClient.Writer.AsStream()
                );
            ServiceProvider provider = services.BuildServiceProvider();

            _ = provider
                .GetRequiredService<McpServer>()
                .RunAsync(TestContext.Current.CancellationToken);
            McpClient client = await McpClient.CreateAsync(
                new StreamClientTransport(
                    clientToServer.Writer.AsStream(),
                    serverToClient.Reader.AsStream()
                ),
                cancellationToken: TestContext.Current.CancellationToken
            );
            return new Session(provider, client);
        }

        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync();
            await _provider.DisposeAsync();
            _stop.Dispose();
        }
    }

    private static async Task<CallToolResult> Call(
        Session session,
        string tool,
        Dictionary<string, object?> arguments
    )
    {
        return await session.Client.CallToolAsync(
            tool,
            arguments,
            cancellationToken: TestContext.Current.CancellationToken
        );
    }

    private static JsonNode Structured(CallToolResult result) =>
        JsonNode.Parse(result.StructuredContent.ShouldNotBeNull().GetRawText()).ShouldNotBeNull();

    private static JsonNode Cli(params string[] arguments)
    {
        CommandRun run = CommandHarness.Run(arguments);
        run.Error.ShouldBe(string.Empty);
        return JsonNode.Parse(run.Output).ShouldNotBeNull();
    }

    private static string CreateTempDirectory()
    {
        string dir = Path.Combine(
            Path.GetTempPath(),
            "catalyst-mcp-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public async Task ToolsList_HasExactlyTheSixTools_EachWithAnInputSchema()
    {
        await using Session session = await Session.StartAsync();

        IList<McpClientTool> tools = await session.Client.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken
        );

        tools
            .Select(t => t.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ShouldBe([
                "diff_spec",
                "generate_clients",
                "list_clients",
                "list_operations",
                "show_operation",
                "show_type",
            ]);
        tools.ShouldAllBe(tool => tool.JsonSchema.ValueKind == JsonValueKind.Object);
        tools
            .Select(tool => tool.JsonSchema.GetProperty("type").GetString())
            .ShouldAllBe(type => type == "object");
        tools.ShouldAllBe(tool => !string.IsNullOrWhiteSpace(tool.Description));
        McpClientTool show = tools.Single(t => t.Name == "show_operation");
        show.JsonSchema.GetProperty("required")
            .EnumerateArray()
            .ShouldHaveSingleItem()
            .GetString()
            .ShouldBe("operation");
    }

    [Fact]
    public async Task LookupTools_ReturnThePayloadOfTheMatchingCliCommand()
    {
        await using Session session = await Session.StartAsync();

        CallToolResult list = await Call(
            session,
            "list_operations",
            new()
            {
                ["source"] = EdgeCases,
                ["filter"] = "items id",
                ["method"] = "get",
            }
        );
        CallToolResult show = await Call(
            session,
            "show_operation",
            new() { ["source"] = EdgeCases, ["operation"] = "PUT /items/{id}" }
        );
        CallToolResult shortShow = await Call(
            session,
            "show_operation",
            new()
            {
                ["source"] = EdgeCases,
                ["operation"] = "PUT /items/{id}",
                ["includeTypes"] = false,
            }
        );
        CallToolResult type = await Call(
            session,
            "show_type",
            new()
            {
                ["source"] = EdgeCases,
                ["name"] = "Item.Dto",
                ["depth"] = 0,
            }
        );

        JsonNode
            .DeepEquals(
                Cli(
                    "api",
                    "list",
                    "--source",
                    EdgeCases,
                    "--filter",
                    "items id",
                    "--method",
                    "get",
                    "--json"
                ),
                Structured(list)
            )
            .ShouldBeTrue();
        JsonNode
            .DeepEquals(
                Cli("api", "show", "PUT /items/{id}", "--source", EdgeCases, "--json"),
                Structured(show)
            )
            .ShouldBeTrue();
        JsonNode
            .DeepEquals(
                Cli(
                    "api",
                    "show",
                    "PUT /items/{id}",
                    "--source",
                    EdgeCases,
                    "--no-types",
                    "--json"
                ),
                Structured(shortShow)
            )
            .ShouldBeTrue();
        JsonNode
            .DeepEquals(
                Cli("api", "schema", "Item.Dto", "--source", EdgeCases, "--depth", "0", "--json"),
                Structured(type)
            )
            .ShouldBeTrue();
        ((TextContentBlock)show.Content[0]).Text.ShouldContain(
            "client.put.itemsId(",
            Case.Sensitive
        );
        list.IsError.ShouldBe(false);
    }

    [Fact]
    public async Task DiffSpec_ReportsChanges_WithoutFailing()
    {
        string dir = CreateTempDirectory();
        try
        {
            string newPath = Path.Combine(dir, "new.json");
            JsonObject spec = (JsonObject)
                JsonNode
                    .Parse(
                        await File.ReadAllTextAsync(
                            EdgeCases,
                            TestContext.Current.CancellationToken
                        )
                    )
                    .ShouldNotBeNull();
            ((JsonObject)spec["paths"].ShouldNotBeNull()).Remove("/ping");
            await File.WriteAllTextAsync(
                newPath,
                spec.ToJsonString(),
                TestContext.Current.CancellationToken
            );
            await using Session session = await Session.StartAsync();

            CallToolResult diff = await Call(
                session,
                "diff_spec",
                new() { ["base"] = EdgeCases, ["source"] = newPath }
            );

            diff.IsError.ShouldBe(false);
            JsonNode
                .DeepEquals(
                    Cli("api", "diff", "--base", EdgeCases, "--source", newPath, "--json"),
                    Structured(diff)
                )
                .ShouldBeTrue();
            Structured(diff)["summary"]
                .ShouldNotBeNull()["breaking"]
                .ShouldNotBeNull()
                .GetValue<int>()
                .ShouldBe(1);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task UnknownOperation_IsAToolError_WithTheCliCodeAndSuggestions()
    {
        await using Session session = await Session.StartAsync();

        CallToolResult result = await Call(
            session,
            "show_operation",
            new() { ["source"] = EdgeCases, ["operation"] = "get.serch" }
        );

        result.IsError.ShouldBe(true);
        JsonNode payload = Structured(result);
        payload["error"]
            .ShouldNotBeNull()["code"]
            .ShouldNotBeNull()
            .GetValue<string>()
            .ShouldBe("OPERATION_NOT_FOUND");
        payload["suggestions"]
            .ShouldNotBeNull()[0]
            .ShouldNotBeNull()
            .GetValue<string>()
            .ShouldContain("client.get.search", Case.Sensitive);
        ((TextContentBlock)result.Content[0]).Text.ShouldContain("Did you mean", Case.Sensitive);
    }

    [Fact]
    public async Task ConfiguredClientTools_ListGenerateAndCheck_OnlyTheDeclaredClients()
    {
        string dir = CreateTempDirectory();
        string previous = Directory.GetCurrentDirectory();
        try
        {
            File.Copy(TestPaths.SpecPath("minimal"), Path.Combine(dir, "minimal.json"));
            await File.WriteAllTextAsync(
                Path.Combine(dir, "catalyst.config.json"),
                """
                { "gen": { "openapi": { "clients": [
                  { "name": "Small", "source": "minimal.json", "output": "out" },
                  { "name": "Other", "source": "minimal.json", "output": "other" } ] } } }
                """,
                TestContext.Current.CancellationToken
            );
            Directory.SetCurrentDirectory(dir);
            await using Session session = await Session.StartAsync();

            JsonNode clients = Structured(await Call(session, "list_clients", []));
            CallToolResult missing = await Call(
                session,
                "generate_clients",
                new() { ["check"] = true }
            );
            JsonNode cliCheck = Cli("gen", "openapi", "--check", "--json");
            bool checkWroteNothing =
                !Directory.Exists(Path.Combine(dir, "out"))
                && !Directory.Exists(Path.Combine(dir, "other"));
            CallToolResult unknown = await Call(
                session,
                "generate_clients",
                new() { ["client"] = "Nope" }
            );
            CallToolResult generated = await Call(
                session,
                "generate_clients",
                new() { ["client"] = "Small" }
            );

            clients["clients"]
                .ShouldNotBeNull()
                .AsArray()
                .Select(c => c.ShouldNotBeNull()["name"].ShouldNotBeNull().GetValue<string>())
                .ShouldBe(["Small", "Other"]);
            JsonNode.DeepEquals(cliCheck, Structured(missing)).ShouldBeTrue();
            Structured(missing)["results"]
                .ShouldNotBeNull()[0]
                .ShouldNotBeNull()["status"]
                .ShouldNotBeNull()
                .GetValue<string>()
                .ShouldBe("missing");
            checkWroteNothing.ShouldBeTrue();
            unknown.IsError.ShouldBe(true);
            Structured(unknown)["error"]
                .ShouldNotBeNull()["code"]
                .ShouldNotBeNull()
                .GetValue<string>()
                .ShouldBe("USAGE");
            generated.IsError.ShouldBe(false);
            Structured(generated)["results"]
                .ShouldNotBeNull()
                .AsArray()
                .ShouldHaveSingleItem()
                .ShouldNotBeNull()["status"]
                .ShouldNotBeNull()
                .GetValue<string>()
                .ShouldBe("written");
            File.Exists(Path.Combine(dir, "out", "Small.ts")).ShouldBeTrue();
            Directory.Exists(Path.Combine(dir, "other")).ShouldBeFalse();
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task ListClients_IsEmpty_WithoutAConfig()
    {
        string dir = CreateTempDirectory();
        string previous = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(dir);
            await using Session session = await Session.StartAsync();

            CallToolResult result = await Call(session, "list_clients", []);

            result.IsError.ShouldBe(false);
            Structured(result)["clients"].ShouldNotBeNull().AsArray().ShouldBeEmpty();
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
            Directory.Delete(dir, recursive: true);
        }
    }

    private static async Task<List<JsonNode>> ReadMessagesAsync(
        Process process,
        int count,
        CancellationToken cancellationToken
    )
    {
        List<JsonNode> messages = [];
        while (messages.Count < count)
        {
            string? line = await process.StandardOutput.ReadLineAsync(cancellationToken);
            messages.Add(JsonNode.Parse(line.ShouldNotBeNull()).ShouldNotBeNull());
        }

        return messages;
    }

    /// <summary>
    /// The published shape: a child process on real stdio. stdout must carry nothing but protocol messages, one JSON-RPC
    /// message per line; logs belong on stderr.
    /// </summary>
    [Fact]
    public async Task McpCommand_AnswersOverRealStdio_AndWritesOnlyProtocolMessagesToStdout()
    {
        string catalyst = Path.Combine(AppContext.BaseDirectory, "Catalyst.dll");
        ProcessStartInfo startInfo = new("dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(catalyst);
        startInfo.ArgumentList.Add("mcp");

        using Process process = Process.Start(startInfo).ShouldNotBeNull();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(60));

        await process.StandardInput.WriteLineAsync(
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}""".AsMemory(),
            timeout.Token
        );
        await process.StandardInput.WriteLineAsync(
            """{"jsonrpc":"2.0","method":"notifications/initialized"}""".AsMemory(),
            timeout.Token
        );
        await process.StandardInput.WriteLineAsync(
            """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""".AsMemory(),
            timeout.Token
        );
        await process.StandardInput.FlushAsync(timeout.Token);

        List<JsonNode> messages = await ReadMessagesAsync(process, 2, timeout.Token);

        process.StandardInput.Close();
        await process.WaitForExitAsync(timeout.Token);
        string rest = await process.StandardOutput.ReadToEndAsync(timeout.Token);

        rest.Trim().ShouldBe(string.Empty);
        messages
            .Select(message => message["jsonrpc"].ShouldNotBeNull().GetValue<string>())
            .ShouldAllBe(version => version == "2.0");
        messages[0]
            ["result"]
            .ShouldNotBeNull()["serverInfo"]
            .ShouldNotBeNull()["name"]
            .ShouldNotBeNull()
            .GetValue<string>()
            .ShouldBe("catalyst");
        messages[1]
            ["result"]
            .ShouldNotBeNull()["tools"]
            .ShouldNotBeNull()
            .AsArray()
            .Count.ShouldBe(6);
    }
}
