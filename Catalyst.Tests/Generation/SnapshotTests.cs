using Shouldly;

namespace Catalyst.Tests.Generation;

/// <summary>
/// Generates a client for every spec fixture and compares it with the checked-in snapshot. This is the
/// one place that asserts the shape of generated code; the snapshots are also compiled with tsc and
/// exercised at runtime (see GeneratedTypeScriptTests).
/// Set CATALYST_UPDATE_SNAPSHOTS=1 to rewrite the snapshots after an intended change.
/// </summary>
public class SnapshotTests
{
    public static IEnumerable<object[]> Specs() =>
        TestPaths.SnapshotSpecs.Select(name => new object[] { name });

    /// <summary>Write to the source tree: the copy in the build output is overwritten on the next build.</summary>
    [Theory]
    [MemberData(nameof(Specs))]
    public void Generate_MatchesSnapshot(string name)
    {
        string code = SpecLoader.CreateGenerator(SpecLoader.Load(name)).Generate();

        AssertMatchesSnapshotOrUpdateIt(name, code);
    }

    private static void AssertMatchesSnapshotOrUpdateIt(string name, string code)
    {
        if (Environment.GetEnvironmentVariable("CATALYST_UPDATE_SNAPSHOTS") == "1")
        {
            string target = TestPaths.SourceSnapshotPath(name);
            Directory.CreateDirectory(Path.GetDirectoryName(target).ShouldNotBeNull());
            File.WriteAllText(target, Normalize(code) + "\n");
            return;
        }

        string snapshotPath = TestPaths.SnapshotPath(name);
        File.Exists(snapshotPath)
            .ShouldBeTrue(
                $"No snapshot for '{name}'. Create it with: set CATALYST_UPDATE_SNAPSHOTS=1 and run dotnet test (it writes Snapshots/{name}.ts)."
            );

        Normalize(code).ShouldBe(Normalize(File.ReadAllText(snapshotPath)));
    }

    private static string Normalize(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
}
