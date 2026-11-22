using Shouldly;

namespace Catalyst.Tests.Services;

public class OpenApiParserTests
{
    [Fact]
    public void Parse_ReturnsSuccess_ForValidSpec()
    {
        string json = File.ReadAllText(TestPaths.SpecPath("enterprise"));

        Result<OpenApiParseResult> result = OpenApiParser.Parse(json);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldNotBeNull().Document.ShouldNotBeNull();
        result.Value.ShouldNotBeNull().Diagnostic.ShouldNotBeNull();
    }

    [Fact]
    public void Parse_ReturnsSuccess_WithDiagnostics_ForInvalidSpec()
    {
        string json = File.ReadAllText(TestPaths.FixturePath("openapi-invalid.json"));

        Result<OpenApiParseResult> result = OpenApiParser.Parse(json);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldNotBeNull().Diagnostic.Errors.ShouldNotBeEmpty();
    }

    [Fact]
    public void Parse_ReturnsError_ForEmptyContent()
    {
        Result<OpenApiParseResult> result = OpenApiParser.Parse(string.Empty);

        result.IsSuccess.ShouldBeFalse();
    }

    [Theory]
    [InlineData("openapi: 3.0.1\ninfo: {}", null, true)]
    [InlineData("{\"openapi\":\"3.0.1\"}", null, false)]
    [InlineData("  \n{\"openapi\":\"3.0.1\"}", "spec", false)]
    [InlineData("{\"openapi\":\"3.0.1\"}", "spec.yaml", true)]
    [InlineData("{\"openapi\":\"3.0.1\"}", "https://example.com/spec.YML?token=1", true)]
    [InlineData("openapi: 3.0.1", "spec.json", false)]
    public void IsYaml_DetectsTheFormatFromTheExtensionThenTheContent(
        string content,
        string? sourceName,
        bool expected
    )
    {
        OpenApiParser.IsYaml(content, sourceName).ShouldBe(expected);
    }

    [Fact]
    public void Parse_KeepsNullableOnCompositions_ForYamlToo()
    {
        const string yaml = """
            openapi: 3.0.1
            info: { title: Yaml, version: "1" }
            paths: {}
            components:
              schemas:
                Pet: { type: object, properties: { name: { type: string } } }
                Owner:
                  type: object
                  properties:
                    pet:
                      nullable: true
                      allOf:
                        - $ref: '#/components/schemas/Pet'
            """;

        OpenApiDocument document = OpenApiParser.Parse(yaml).Value.ShouldNotBeNull().Document;
        string code = SpecLoader.CreateGenerator(document).Generate();

        code.ShouldContain("pet?: Pet | null;", Case.Sensitive);
    }

    [Fact]
    public void Parse_QuotesTheReadersErrors_WhenThereIsNoDocument()
    {
        Result<OpenApiParseResult> result = OpenApiParser.Parse("{ \"openapi\": ");

        result.IsSuccess.ShouldBeFalse();
        result.ErrorMessage.ShouldStartWith("Failed to parse OpenAPI document: ", Case.Sensitive);
        result.ErrorMessage.ShouldNotContain("empty result", Case.Sensitive);
    }
}
