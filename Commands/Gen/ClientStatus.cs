namespace Catalyst.Commands.Gen;

/// <summary>What happened to one client, as written to the "status" field of the JSON output.</summary>
internal static class ClientStatus
{
    public const string Written = "written";
    public const string Unchanged = "unchanged";
    public const string UpToDate = "upToDate";
    public const string Stale = "stale";
    public const string Missing = "missing";
    public const string Failed = "failed";
}
