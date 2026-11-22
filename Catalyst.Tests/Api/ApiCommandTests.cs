using System.Text.Json;
using Shouldly;

namespace Catalyst.Tests.Api;

/// <summary>The tests replace the static consoles and some change the working directory, so they run one at a time.</summary>
[Collection("AnsiConsole")]
public class ApiCommandTests
{
    private static readonly string EdgeCases = TestPaths.SpecPath("edge-cases");

    private static CommandRun Api(params string[] arguments) =>
        CommandHarness.Run(["api", .. arguments]);

    private static JsonElement ParseJson(CommandRun run)
    {
        run.Error.ShouldBe(string.Empty);
        return JsonElement.Parse(run.Output);
    }

    [Fact]
    public void List_PrintsOneLinePerOperation_Filtered_AndSorted()
    {
        CommandRun run = Api("list", "--source", EdgeCases, "--filter", "items id");

        run.ExitCode.ShouldBe(0);
        string[] lines = run.Output.Split(
            '\n',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        lines.Length.ShouldBe(2);
        lines[0].ShouldMatch(@"^GET\s+/items/\{id\}\s+client\.get\.itemsId\s+- Get an item");
        lines[1].ShouldMatch(@"^PUT\s+/items/\{id\}\s+client\.put\.itemsId$");
    }

    [Fact]
    public void List_Json_HasTheEnvelopeAndOneEntryPerMatch()
    {
        JsonElement root = ParseJson(
            Api("list", "--source", EdgeCases, "--method", "put", "--filter", "items", "--json")
        );

        root.GetProperty("schemaVersion").GetInt32().ShouldBe(1);
        root.GetProperty("command").GetString().ShouldBe("api list");
        root.GetProperty("success").GetBoolean().ShouldBeTrue();
        root.GetProperty("exitCode").GetInt32().ShouldBe(0);
        JsonElement operation = root.GetProperty("operations")
            .EnumerateArray()
            .ShouldHaveSingleItem();
        operation.GetProperty("method").GetString().ShouldBe("PUT");
        operation.GetProperty("path").GetString().ShouldBe("/items/{id}");
        operation.GetProperty("call").GetString().ShouldBe("client.put.itemsId");
        root.GetProperty("error").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public void Show_PrintsTheCallParametersBodyResponseAndTypes()
    {
        CommandRun run = Api("show", "PUT /items/{id}", "--source", EdgeCases);

        run.ExitCode.ShouldBe(0);
        run.Output.ShouldContain("PUT /items/{id}", Case.Sensitive);
        run.Output.ShouldContain(
            "client.put.itemsId(id: string, body: Derived, dryRun?: boolean, headers?: Record<string, string>, signal?: AbortSignal): Promise<ApiResult<void>>",
            Case.Sensitive
        );
        run.Output.ShouldMatch(@"dryRun\s+dryRun\s+query\s+optional\s+boolean");
        run.Output.ShouldContain("Body: Derived (application/json, required)", Case.Sensitive);
        run.Output.ShouldContain("Response: void", Case.Sensitive);
        run.Output.ShouldContain("export type Derived = Base & { name?: string };", Case.Sensitive);
        (
            run.Output.IndexOf("export interface Base", StringComparison.Ordinal)
            < run.Output.IndexOf("export type Derived", StringComparison.Ordinal)
        ).ShouldBeTrue();
    }

    [Fact]
    public void Show_NoTypes_OmitsTheDeclarations_AndAcceptsAnUnquotedSelector()
    {
        CommandRun run = Api("show", "PUT", "/items/{id}", "--source", EdgeCases, "--no-types");

        run.ExitCode.ShouldBe(0);
        run.Output.ShouldContain("Call:", Case.Sensitive);
        run.Output.ShouldNotContain("Types:", Case.Sensitive);
        run.Output.ShouldNotContain("export ", Case.Sensitive);
    }

    [Fact]
    public void Show_Json_CarriesTheOperationAndItsTypes()
    {
        JsonElement root = ParseJson(
            Api("show", "client.put.itemsId", "--source", EdgeCases, "--json")
        );

        JsonElement operation = root.GetProperty("operation");
        root.GetProperty("command").GetString().ShouldBe("api show");
        operation.GetProperty("call").GetString().ShouldBe("client.put.itemsId");
        operation.GetProperty("parameters")[0].GetProperty("name").GetString().ShouldBe("id");
        operation.GetProperty("parameters")[0].GetProperty("in").GetString().ShouldBe("path");
        operation.GetProperty("body").GetProperty("type").GetString().ShouldBe("Derived");
        root.GetProperty("types")
            .EnumerateArray()
            .Select(t => t.GetProperty("name").GetString())
            .ShouldBe(["Base", "Derived"]);
    }

    [Fact]
    public void Show_ReportsNotFound_WithSuggestions_AndExitCodeOne()
    {
        CommandRun text = Api("show", "get.serch", "--source", EdgeCases);
        JsonElement root = ParseJson(Api("show", "get.serch", "--source", EdgeCases, "--json"));

        text.ExitCode.ShouldBe(1);
        text.Error.ShouldContain("No operation matches", Case.Sensitive);
        text.Error.ShouldContain("client.get.search", Case.Sensitive);
        text.Output.ShouldBe(string.Empty);
        root.GetProperty("success").GetBoolean().ShouldBeFalse();
        root.GetProperty("exitCode").GetInt32().ShouldBe(1);
        root.GetProperty("error").GetProperty("code").GetString().ShouldBe("OPERATION_NOT_FOUND");
        root.GetProperty("suggestions")[0]
            .GetString()
            .ShouldNotBeNull()
            .ShouldContain("client.get.search", Case.Sensitive);
    }

    [Fact]
    public void Schema_PrintsTheTypeAndItsDependencies_ByComponentName()
    {
        CommandRun all = Api("schema", "Item.Dto", "--source", EdgeCases);
        JsonElement alone = ParseJson(
            Api("schema", "Item_Dto", "--source", EdgeCases, "--depth", "0", "--json")
        );
        CommandRun missing = Api("schema", "Itm_Dto", "--source", EdgeCases);

        all.ExitCode.ShouldBe(0);
        all.Output.ShouldContain("export interface Item_Dto", Case.Sensitive);
        all.Output.ShouldContain("export interface Base", Case.Sensitive);
        all.Output.ShouldContain("export type IntegerStatus = 0 | 1 | 2;", Case.Sensitive);
        alone
            .GetProperty("types")
            .EnumerateArray()
            .Select(t => t.GetProperty("name").GetString())
            .ShouldBe(["Item_Dto"]);
        missing.ExitCode.ShouldBe(1);
        missing.Error.ShouldContain("Item_Dto", Case.Sensitive);
    }

    [Fact]
    public async Task Source_ComesFromTheOnlyConfiguredClient_OrTheNamedOne_OrIsAUsageError()
    {
        string dir = CreateTempDirectory();
        string previous = Directory.GetCurrentDirectory();
        try
        {
            File.Copy(EdgeCases, Path.Combine(dir, "edge.json"));
            File.Copy(TestPaths.SpecPath("minimal"), Path.Combine(dir, "minimal.json"));
            Directory.SetCurrentDirectory(dir);

            await File.WriteAllTextAsync(
                Path.Combine(dir, "catalyst.config.json"),
                """
                { "gen": { "openapi": { "skipPaths": ["/internal/"], "clients": [
                  { "name": "Edge", "source": "edge.json", "output": "out" } ] } } }
                """,
                TestContext.Current.CancellationToken
            );
            CommandRun single = Api("list");

            await File.WriteAllTextAsync(
                Path.Combine(dir, "catalyst.config.json"),
                """
                { "gen": { "openapi": { "skipPaths": ["/internal/"], "clients": [
                  { "name": "Edge", "source": "edge.json", "output": "out" },
                  { "name": "Small", "source": "minimal.json", "output": "out" } ] } } }
                """,
                TestContext.Current.CancellationToken
            );
            CommandRun ambiguous = Api("list");
            CommandRun named = Api("list", "--client", "small");
            CommandRun unknown = Api("list", "--client", "Nope", "--json");

            single.ExitCode.ShouldBe(0);
            single.Output.ShouldContain("client.get.itemsId", Case.Sensitive);
            single.Output.ShouldNotContain("internalHealth", Case.Sensitive);

            ambiguous.ExitCode.ShouldBe(2);
            ambiguous.Error.ShouldContain("Edge, Small", Case.Sensitive);

            named.ExitCode.ShouldBe(0);
            named.Output.ShouldContain("client.get.widgets", Case.Sensitive);
            named.Output.ShouldNotContain("itemsId", Case.Sensitive);

            JsonElement root = ParseJson(unknown);
            unknown.ExitCode.ShouldBe(2);
            root.GetProperty("error").GetProperty("code").GetString().ShouldBe("USAGE");
            root.GetProperty("error")
                .GetProperty("message")
                .GetString()
                .ShouldNotBeNull()
                .ShouldContain("Edge, Small", Case.Sensitive);
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void UsageErrors_InJson_UseTheApiEnvelope()
    {
        CommandRun run = Api("show", "--source", EdgeCases, "--nope", "--json");
        JsonElement root = ParseJson(run);

        run.ExitCode.ShouldBe(2);
        root.GetProperty("command").GetString().ShouldBe("api show");
        root.GetProperty("error").GetProperty("code").GetString().ShouldBe("USAGE");
    }

    private static string CreateTempDirectory()
    {
        string dir = Path.Combine(
            Path.GetTempPath(),
            "catalyst-api-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(dir);
        return dir;
    }
}
