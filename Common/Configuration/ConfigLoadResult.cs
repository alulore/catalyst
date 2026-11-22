namespace Catalyst.Common.Configuration;

/// <summary>Outcome of locating and reading a catalyst config file.</summary>
/// <param name="Config">The parsed config, or null when none was found or the file is unusable.</param>
/// <param name="Path">Absolute path of the config file, when one was found.</param>
/// <param name="Error">A human readable reason when a file was found but could not be used.</param>
internal sealed record ConfigLoadResult(CatalystConfig? Config, string? Path, string? Error);
