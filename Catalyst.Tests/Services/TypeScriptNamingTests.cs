using Shouldly;

namespace Catalyst.Tests.Services;

public class TypeScriptNamingTests
{
    [Theory]
    [InlineData("userId", "userId")]
    [InlineData("user-id", "user_id")]
    [InlineData("123abc", "_123abc")]
    [InlineData("class", "\"class\"")]
    [InlineData("interface", "\"interface\"")]
    [InlineData("with space", "with_space")]
    [InlineData("", "_")]
    [InlineData("_", "param")]
    public void SanitizeIdentifier_HandlesCases(string input, string expected)
    {
        TypeScriptNaming.SanitizeIdentifier(input).ShouldBe(expected);
    }

    [Theory]
    [InlineData("class", "_class")]
    [InlineData("1st", "_1st")]
    [InlineData("a-b", "a_b")]
    public void SanitizeIdentifier_AsParameter_StaysAValidBinding(string input, string expected)
    {
        TypeScriptNaming.SanitizeIdentifier(input, asParameter: true).ShouldBe(expected);
    }

    [Theory]
    [InlineData("getUser", "GetUser")]
    [InlineData("user-permissions-list", "UserPermissionsList")]
    [InlineData("", "Unknown")]
    [InlineData("API", "Api")]
    public void ToPascalCase_HandlesCases(string input, string expected)
    {
        TypeScriptNaming.ToPascalCase(input).ShouldBe(expected);
    }

    [Theory]
    [InlineData("name", "name")]
    [InlineData("page-size", "\"page-size\"")]
    [InlineData("@odata.type", "\"@odata.type\"")]
    [InlineData("1st", "\"1st\"")]
    [InlineData("class", "class")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    public void QuoteKey_QuotesOnlyWhatIsNotAnIdentifier(string key, string expected)
    {
        TypeScriptNaming.QuoteKey(key).ShouldBe(expected);
    }

    [Theory]
    [InlineData("Pet", "Pet")]
    [InlineData("Item.Dto", "Item_Dto")]
    [InlineData("Links-Self", "Links_Self")]
    [InlineData("A..B", "A_B")]
    [InlineData("Foo[]", "Foo")]
    [InlineData("1Thing", "_1Thing")]
    [InlineData("class", "_class")]
    [InlineData("Record", "Record_2")]
    [InlineData("string", "string_2")]
    [InlineData("***", "Schema")]
    public void BuildComponentNames_ProducesValidTypeIdentifiers(string key, string expected)
    {
        Dictionary<string, string> names = TypeScriptNaming.BuildComponentNames([key]);

        names[key].ShouldBe(expected);
    }

    /// <summary>"Item.Dto" is listed first but must not take the name of the valid key "Item_Dto".</summary>
    [Fact]
    public void BuildComponentNames_KeepsValidKeysAndDisambiguatesTheRest()
    {
        Dictionary<string, string> names = TypeScriptNaming.BuildComponentNames([
            "Item.Dto",
            "Item_Dto",
            "Item-Dto",
        ]);

        names["Item_Dto"].ShouldBe("Item_Dto");
        names["Item.Dto"].ShouldBe("Item_Dto_2");
        names["Item-Dto"].ShouldBe("Item_Dto_3");
        names.Values.Distinct().Count().ShouldBe(3);
    }

    [Fact]
    public void BuildComponentNames_AvoidsNamesTheGeneratedFileDeclares()
    {
        Dictionary<string, string> names = TypeScriptNaming.BuildComponentNames(
            ["ApiResult", "Get", "PetClient"],
            reserved: ["PetClient"]
        );

        names["ApiResult"].ShouldBe("ApiResult_2");
        names["Get"].ShouldBe("Get_2");
        names["PetClient"].ShouldBe("PetClient_2");
    }

    [Theory]
    [InlineData("GetUser", "get", false, "user")]
    [InlineData("PostcodeLookup", "post", false, "postcodeLookup")]
    [InlineData("Getaway", "get", false, "getaway")]
    [InlineData("getMedia", "get", true, "media")]
    [InlineData("getlowercase", "get", true, "lowercase")]
    [InlineData("get1abc", "get", true, "get1abc")]
    [InlineData("Get", "get", false, "get")]
    [InlineData("ListItems", "get", false, "listItems")]
    [InlineData("FindPetsByStatus", "get", false, "findPetsByStatus")]
    [InlineData("UpdatePet", "put", false, "updatePet")]
    [InlineData("BatchGetItems", "post", false, "batchGetItems")]
    public void StripHttpVerbPrefix_ReturnsACamelCaseName_AndOnlyStripsAtAWordBoundary(
        string name,
        string verb,
        bool pathDerived,
        string expected
    )
    {
        TypeScriptNaming.StripHttpVerbPrefix(name, verb, pathDerived).ShouldBe(expected);
    }

    [Theory]
    [InlineData("ApiResult", true)]
    [InlineData("class", true)]
    [InlineData("Pet", false)]
    public void IsReservedTypeName_IsTrueForAGeneratedTypeNameOrAKeyword(string name, bool expected)
    {
        TypeScriptNaming.IsReservedTypeName(name).ShouldBe(expected);
    }
}
