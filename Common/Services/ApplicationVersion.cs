using System.Reflection;

namespace Catalyst.Common.Services;

/// <summary>The version of this build without the source-revision suffix (for example "0.3.0+abc1234"), so callers see a clean version.</summary>
internal static class ApplicationVersion
{
    public static string Current { get; } = Read();

    private static string Read()
    {
        string? informational = typeof(ApplicationVersion)
            .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        return informational?.Split('+')[0] ?? "0.0.0";
    }
}
