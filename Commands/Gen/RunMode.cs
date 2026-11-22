namespace Catalyst.Commands.Gen;

/// <summary>The modes of <c>gen openapi</c>, as written to the "mode" field of the JSON output.</summary>
internal static class RunMode
{
    public const string Validate = "validate";
    public const string Generate = "generate";
    public const string Check = "check";
}
