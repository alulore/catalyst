namespace Catalyst.Common.Models;

/// <summary>What the generator knows about the client it renders: the operations and the declared types.</summary>
internal sealed record ClientModel(List<ApiOperation> Operations, List<TypeDeclaration> Types);
