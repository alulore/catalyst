using Shouldly;

namespace Catalyst.Tests.Generation;

/// <summary>The one YAML behaviour test: the original YAML spec generates exactly what its JSON conversion does.</summary>
public class YamlSpecTests
{
    [Fact]
    public void Generate_FromYaml_EqualsTheSnapshotOfTheJsonVersion()
    {
        string yamlPath = TestPaths.SpecPath("museum", ".yaml");
        Result<OpenApiParseResult> parsed = OpenApiParser.Parse(
            File.ReadAllText(yamlPath),
            yamlPath
        );

        parsed.IsSuccess.ShouldBeTrue(parsed.ErrorMessage);

        string code = SpecLoader
            .CreateGenerator(parsed.Value.ShouldNotBeNull().Document)
            .Generate();
        string snapshot = File.ReadAllText(TestPaths.SnapshotPath("museum"));

        Normalize(code).ShouldBe(Normalize(snapshot));
    }

    private static string Normalize(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
}
