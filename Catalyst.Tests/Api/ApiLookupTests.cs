using Catalyst.Commands.Api;
using Catalyst.Tests.TypeScript;
using Shouldly;

namespace Catalyst.Tests.Api;

/// <summary>Selector resolution, suggestions and the type closure, against the real fixtures.</summary>
[Collection("TypeScript")]
public class ApiLookupTests
{
    private readonly TypeScriptToolchain _toolchain;

    public ApiLookupTests(TypeScriptToolchain toolchain)
    {
        _toolchain = toolchain;
    }

    private static ClientModel ModelOf(string spec)
    {
        TypeScriptGenerator generator = SpecLoader.CreateGenerator(SpecLoader.Load(spec));
        generator.Generate();
        return generator.Model;
    }

    [Theory]
    [InlineData("GET /items/{id}", "client.get.itemsId")]
    [InlineData("get /items/{whatever}", "client.get.itemsId")]
    [InlineData("PUT /items/{id}", "client.put.itemsId")]
    [InlineData("getSearch", "client.get.search")]
    [InlineData("GETSEARCH", "client.get.search")]
    [InlineData("get.search", "client.get.search")]
    [InlineData("client.get.search", "client.get.search")]
    [InlineData("PostcodeLookup", "client.post.postcodeLookup")]
    [InlineData("post.PostcodeLookup", "client.post.postcodeLookup")]
    public void FindOperation_ResolvesEverySelectorForm(string selector, string expectedCall)
    {
        OperationMatch match = ApiLookup.FindOperation(ModelOf("edge-cases"), selector);

        (match.Operation?.Call).ShouldBe(expectedCall);
    }

    [Theory]
    [InlineData("GET /albums/{id}", "client.get.anAlbum")]
    [InlineData("get-an-album", "client.get.anAlbum")]
    [InlineData("get.anAlbum", "client.get.anAlbum")]
    public void FindOperation_ResolvesSpotifyOperations(string selector, string expectedCall)
    {
        OperationMatch match = ApiLookup.FindOperation(ModelOf("spotify"), selector);

        (match.Operation?.Call).ShouldBe(expectedCall);
    }

    [Fact]
    public void FindOperation_ReportsAmbiguity_WithTheCandidates()
    {
        ApiOperation First(string path) =>
            new(
                "GET",
                path,
                "same",
                null,
                null,
                [],
                false,
                "get",
                "same",
                "same()",
                [],
                null,
                new ApiResponse("void", null),
                []
            );
        ClientModel model = new([First("/a/{x}"), First("/b/{y}")], []);

        OperationMatch byId = ApiLookup.FindOperation(model, "same");
        OperationMatch byShape = ApiLookup.FindOperation(
            new ClientModel([First("/a/{x}"), First("/a/{y}")], []),
            "GET /a/{z}"
        );

        byId.ErrorCode.ShouldBe("OPERATION_AMBIGUOUS");
        byId.Suggestions.Count.ShouldBe(2);
        byShape.ErrorCode.ShouldBe("OPERATION_AMBIGUOUS");
    }

    [Fact]
    public void FindOperation_SuggestsTheClosestOperations_WhenNothingMatches()
    {
        OperationMatch typo = ApiLookup.FindOperation(ModelOf("edge-cases"), "get.serch");
        OperationMatch spotify = ApiLookup.FindOperation(ModelOf("spotify"), "album");

        typo.ErrorCode.ShouldBe("OPERATION_NOT_FOUND");
        typo.Suggestions[0].ShouldContain("client.get.search", Case.Sensitive);
        spotify.ErrorCode.ShouldBe("OPERATION_NOT_FOUND");
        spotify.Suggestions.Count.ShouldBeInRange(1, 5);
        spotify.Suggestions.ShouldAllBe(s =>
            s.Contains("lbum", StringComparison.OrdinalIgnoreCase)
        );
    }

    [Fact]
    public void FindType_AcceptsTheGeneratedNameOrTheComponentName()
    {
        ClientModel model = ModelOf("edge-cases");

        (ApiLookup.FindType(model, "Item_Dto")?.Name).ShouldBe("Item_Dto");
        (ApiLookup.FindType(model, "Item.Dto")?.Name).ShouldBe("Item_Dto");
        (ApiLookup.FindType(model, "item.dto")?.Name).ShouldBe("Item_Dto");
        ApiLookup.FindType(model, "Nope").ShouldBeNull();
    }

    [Fact]
    public void TypeClosure_ListsDependenciesFirst_AndDepthStopsTheWalk()
    {
        ClientModel model = ModelOf("spotify");

        ApiLookup
            .TypeClosure(model, ["AlbumObject"], depth: 0)
            .Select(t => t.Name)
            .ShouldBe(["AlbumObject"]);
        ApiLookup
            .TypeClosure(model, ["AlbumObject"], depth: 1)
            .Select(t => t.Name)
            .ShouldBe(["AlbumBase", "ArtistObject", "PagingSimplifiedTrackObject", "AlbumObject"]);
    }

    /// <summary>AlbumObject is allOf-based; its closure pulls in the base type, artists and the track page.</summary>
    [Fact]
    public async Task TypeClosure_ForASpotifyOperation_HasExactlyTheExpectedTypes_AndTheirDeclarationsCompile()
    {
        ClientModel model = ModelOf("spotify");
        ApiOperation operation = ApiLookup
            .FindOperation(model, "GET /albums/{id}")
            .Operation.ShouldNotBeNull();

        List<TypeDeclaration> closure = ApiLookup.TypeClosure(model, operation.ReferencedTypes);

        closure
            .Select(t => t.Name)
            .ShouldBe([
                "CopyrightObject",
                "ExternalIdObject",
                "ExternalUrlObject",
                "ImageObject",
                "AlbumRestrictionObject",
                "AlbumBase",
                "FollowersObject",
                "ArtistObject",
                "PagingObject",
                "SimplifiedArtistObject",
                "LinkedTrackObject",
                "TrackRestrictionObject",
                "SimplifiedTrackObject",
                "PagingSimplifiedTrackObject",
                "AlbumObject",
            ]);

        closure
            .SelectMany(
                (type, index) =>
                    type
                        .DependsOn.Where(dependency =>
                            dependency != type.Name
                            && closure.FindIndex(t => t.Name == dependency) >= index
                        )
                        .Select(dependency =>
                            $"{type.Name} is printed before its dependency {dependency}"
                        )
            )
            .ShouldBeEmpty();

        ToolResult result = await _toolchain.TypecheckAsync(
            string.Join("\n\n", closure.Select(t => t.Declaration)) + "\n"
        );
        result.ExitCode.ShouldBe(
            0,
            "The printed declarations do not compile:" + Environment.NewLine + result.Output
        );
    }
}
