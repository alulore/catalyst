using System.Text.Json;
using Catalyst.Common.Serialization;

namespace Catalyst.Common.Configuration;

internal static class ConfigLoader
{
    private static readonly string[] ConfigFileNames =
    [
        "catalyst.config.json",
        ".catalyst.config.json",
    ];

    public static CatalystConfig? Load()
    {
        return Load(Directory.GetCurrentDirectory());
    }

    public static CatalystConfig? Load(string startDirectory)
    {
        return LoadDetailed(startDirectory).Config;
    }

    /// <summary>
    /// Like <see cref="Load(string)"/> but reports where the file was found and why an existing
    /// file could not be used, so callers can fail with a useful message.
    /// </summary>
    public static ConfigLoadResult LoadDetailed(string startDirectory)
    {
        DirectoryInfo? dir = new(startDirectory);

        while (dir != null)
        {
            foreach (string name in ConfigFileNames)
            {
                string path = Path.Combine(dir.FullName, name);

                if (!File.Exists(path))
                {
                    continue;
                }

                try
                {
                    string content = File.ReadAllText(path);
                    CatalystConfig? config = JsonSerializer.Deserialize(
                        content,
                        CatalystJson.Config.CatalystConfig
                    );

                    return config is null
                        ? new ConfigLoadResult(
                            null,
                            path,
                            $"The config is empty or not a JSON object: {path}"
                        )
                        : new ConfigLoadResult(config, path, null);
                }
                catch (Exception ex)
                    when (ex is JsonException or IOException or UnauthorizedAccessException)
                {
                    return new ConfigLoadResult(null, path, $"Failed to read {path}: {ex.Message}");
                }
            }

            dir = dir.Parent;
        }

        return new ConfigLoadResult(null, null, null);
    }
}
