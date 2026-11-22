namespace Catalyst.Common.Models;

/// <summary>
/// One operation as the generated client exposes it: the HTTP side (method, path, ids, docs) and the
/// TypeScript side (verb group, method name, signature, parameter and body types).
/// </summary>
internal sealed record ApiOperation(
    string Method,
    string Path,
    string? OperationId,
    string? Summary,
    string? Description,
    List<string> Tags,
    bool Deprecated,
    string Verb,
    string MethodName,
    string Signature,
    List<ApiParameter> Parameters,
    ApiBody? Body,
    ApiResponse Response,
    List<string> ReferencedTypes
)
{
    /// <summary>How the operation is called on the generated client, for example client.get.itemsId.</summary>
    public string Call => $"client.{Verb}.{MethodName}";
}
