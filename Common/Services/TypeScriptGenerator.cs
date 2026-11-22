using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Auxil.CodeGeneration.Common;
using Auxil.CodeGeneration.Common.Builders;
using Auxil.CodeGeneration.Common.Builders.Scopes;
using Auxil.CodeGeneration.Common.Languages;
using Catalyst.Common.Models;
using Microsoft.OpenApi;

namespace Catalyst.Common.Services;

internal sealed partial class TypeScriptGenerator
{
    /// <summary>
    /// Binding names a generated method parameter must not take: the fixed parameters of the
    /// method, the inner request function the body calls, and the helper used for path segments.
    /// </summary>
    private static readonly string[] ReservedBindings =
    [
        "body",
        "headers",
        "signal",
        "request",
        "encodeURIComponent",
    ];

    private readonly OpenApiDocument _document;
    private readonly string _className;
    private readonly string _clientInterfaceName;
    private readonly HashSet<string> _usedTypeNames;

    /// <summary>One name map serves both the declarations and every $ref site, so they cannot drift apart.</summary>
    private readonly Dictionary<string, string> _componentNames;
    private readonly List<string> _skipPathPrefixes;
    private readonly string _generatorVersion;

    /// <summary>Non-JSON request encodings used by the operations; the request helper only handles these.</summary>
    private List<string> _bodyEncodings = [];

    private bool _usesBlobResponse;

    /// <summary>Collects the declared type names a piece of generation refers to (see CaptureReferences).</summary>
    private readonly Stack<List<string>> _referenceScopes = new();

    private readonly List<DeclarationRecord> _declarations = [];

    /// <summary>A declared type as the generator saw it: what the model keeps besides the rendered text.</summary>
    private sealed record DeclarationRecord(string Name, string Kind, List<string> DependsOn)
    {
        public List<TypeMember> Members { get; init; } = [];
        public string? AliasType { get; init; }
        public List<string> Values { get; init; } = [];
    }

    public TypeScriptGenerator(
        OpenApiDocument document,
        string? className,
        List<string>? skipPaths = null,
        string? generatorVersion = null
    )
    {
        _document = document;
        _className = className ?? document.Info?.Title ?? "ApiClient";
        _clientInterfaceName =
            TypeScriptNaming.SanitizeIdentifier(TypeScriptNaming.ToPascalCase(_className))
            + "Client";
        _skipPathPrefixes = skipPaths ?? [];
        _generatorVersion = string.IsNullOrWhiteSpace(generatorVersion)
            ? ResolveGeneratorVersion()
            : generatorVersion;

        _componentNames = TypeScriptNaming.BuildComponentNames(
            document.Components?.Schemas?.Keys ?? Enumerable.Empty<string>(),
            [_clientInterfaceName]
        );
        _usedTypeNames = [.. _componentNames.Values, _clientInterfaceName];
    }

    /// <summary>
    /// What was generated, in lookup form: the operations with their generated names and types, and every
    /// declared type with its TypeScript text and dependencies. Filled by <see cref="Generate"/>.
    /// </summary>
    public ClientModel Model { get; private set; } = new([], []);

    /// <summary>Non-fatal problems found while generating (name collisions, path parameter mismatches).</summary>
    public List<Issue> Warnings { get; } = [];

    public string Generate()
    {
        List<OperationInfo> operations = ExtractOperations();

        string[] encodingOrder = ["form", "urlencoded", "text", "binary"];
        _bodyEncodings = encodingOrder
            .Where(encoding =>
                operations.Any(o =>
                    (o.RequestBodySchema != null || o.RequestBodyEncoding == "binary")
                    && o.RequestBodyEncoding == encoding
                )
            )
            .ToList();
        _usesBlobResponse = operations.Any(o => o.ResponseIsBinary);

        CodeBuilder<TypeScript> ts = new(new CodeBuilderOptions { BraceStyle = BraceStyle.KAndR });
        FileScope<TypeScript> file = ts.File("api.ts");

        BuildHeader(file);
        BuildClientOptionsInterface(file);
        BuildRequestOptionsInterface(file);
        BuildSchemaInterfaces(file);
        BuildOperationTypeInterfaces(file, operations);
        BuildApiClientInterface(file, operations);
        BuildFactoryFunction(file, operations);

        file.EndFile();

        string code = ts.Render();
        Model = BuildModel(code, operations);
        return code;
    }

    public int SkippedPathCount { get; private set; }

    /// <summary>
    /// Maps a schema to the TypeScript type used inline (parameters, tests), without declaring any
    /// named types. Object schemas become literal types.
    /// </summary>
    internal string ResolveInlineType(IOpenApiSchema schema)
    {
        return ResolveType(null, "Inline", schema);
    }

    private void BuildHeader(FileScope<TypeScript> file)
    {
        file.Comment(
            " Generated from "
                + _className
                + "\n Generated by Catalyst "
                + _generatorVersion
                + "\n Requires TypeScript target ES2022 or higher, or include ES2022 in lib"
        );

        file.Interface("ProblemDetail")
            .Generic("T", null, "Record<string, unknown>")
            .Export()
            .Property("type", "string")
            .Nullable()
            .EndProperty()
            .Property("title", "string")
            .Nullable()
            .EndProperty()
            .Property("status", "number")
            .Nullable()
            .EndProperty()
            .Property("detail", "string")
            .Nullable()
            .EndProperty()
            .Property("instance", "string")
            .Nullable()
            .EndProperty()
            .Property("extensions", "T")
            .Nullable()
            .EndProperty()
            .EndInterface();

        file.TypeAlias(
                "ApiResult<T>",
                "{ data: T; error: null } | { data: null; error: ProblemDetail }"
            )
            .Export()
            .EndTypeAlias();
    }

    /// <summary>The assembly version without a source revision suffix (e.g. "0.2.0+1a2b3c4"), as the header of the generated file states it.</summary>
    private static string ResolveGeneratorVersion()
    {
        Assembly assembly = typeof(TypeScriptGenerator).Assembly;

        string? informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            int metadataIndex = informational.IndexOf('+', StringComparison.Ordinal);
            return metadataIndex > 0 ? informational[..metadataIndex] : informational;
        }

        Version? version = assembly.GetName().Version;
        return version is null
            ? "unknown"
            : $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";
    }

    private static void BuildClientOptionsInterface(FileScope<TypeScript> file)
    {
        file.Interface("ClientOptions")
            .Export()
            .Property("baseUrl", "string")
            .Nullable()
            .EndProperty()
            .Property("credentials", "RequestCredentials")
            .Nullable()
            .EndProperty()
            .Property("headers", "Record<string, string>")
            .Nullable()
            .EndProperty()
            .Property("timeoutMs", "number")
            .Nullable()
            .EndProperty()
            .EndInterface();
    }

    /// <summary>Besides params, body and headers, the options carry hints from the generated methods: how to encode the body and how to read the response.</summary>
    private void BuildRequestOptionsInterface(FileScope<TypeScript> file)
    {
        InterfaceScope<TypeScript, FileScope<TypeScript>> options = file.Interface("RequestOptions")
            .Export();

        options
            .Property(
                "params",
                "Record<string, string | number | boolean | (string | number | boolean)[] | undefined>"
            )
            .Nullable()
            .EndProperty();
        options.Property("body", "unknown").Nullable().EndProperty();

        if (_bodyEncodings.Count > 0)
        {
            options
                .Property(
                    "bodyType",
                    string.Join(" | ", _bodyEncodings.Select(TypeScriptNaming.StringLiteral))
                )
                .Nullable()
                .EndProperty();
        }

        if (_usesBlobResponse)
        {
            options.Property("responseType", "\"blob\"").Nullable().EndProperty();
        }

        options.Property("headers", "Record<string, string>").Nullable().EndProperty();
        options.Property("signal", "AbortSignal").Nullable().EndProperty();
        options.EndInterface();
    }

    private void BuildSchemaInterfaces(FileScope<TypeScript> file)
    {
        IDictionary<string, IOpenApiSchema>? schemas = _document.Components?.Schemas;
        if (schemas == null || schemas.Count == 0)
        {
            return;
        }

        foreach (KeyValuePair<string, IOpenApiSchema> schemaEntry in schemas)
        {
            BuildComponent(file, _componentNames[schemaEntry.Key], schemaEntry.Value);
        }
    }

    /// <summary>
    /// Declares one component schema: an interface for plain objects, an enum for string enums and
    /// a type alias for everything else (primitives, arrays, compositions, numeric enums).
    /// </summary>
    private void BuildComponent(FileScope<TypeScript> file, string typeName, IOpenApiSchema schema)
    {
        if (schema.Enum?.Count > 0)
        {
            if (IsStringEnum(schema))
            {
                BuildEnumFromSchema(file, typeName, schema);
                _declarations.Add(
                    new DeclarationRecord(typeName, "enum", [])
                    {
                        Values = EnumStringValues(schema),
                    }
                );
            }
            else
            {
                string literalUnion = MapEnum(schema);
                BuildTypeAlias(file, typeName, schema, literalUnion);
                _declarations.Add(
                    new DeclarationRecord(typeName, "type", []) { AliasType = literalUnion }
                );
            }

            return;
        }

        bool isComposed =
            schema.AllOf?.Count > 0 || schema.OneOf?.Count > 0 || schema.AnyOf?.Count > 0;
        if (!isComposed && schema.Properties?.Count > 0)
        {
            BuildInterfaceFromSchema(file, typeName, schema);
            return;
        }

        string resolved = string.Empty;
        List<string> dependsOn = CaptureReferences(() =>
            resolved = ResolveType(file, typeName, schema)
        );
        BuildTypeAlias(file, typeName, schema, resolved);
        _declarations.Add(
            new DeclarationRecord(typeName, "type", dependsOn) { AliasType = resolved }
        );
    }

    private static void BuildTypeAlias(
        FileScope<TypeScript> file,
        string typeName,
        IOpenApiSchema schema,
        string type
    )
    {
        TypeAliasScope<TypeScript, FileScope<TypeScript>> alias = file.TypeAlias(typeName, type)
            .Export();

        if (!string.IsNullOrWhiteSpace(schema.Description))
        {
            alias.Doc(DocText(schema.Description));
        }

        alias.EndTypeAlias();
    }

    private void BuildInterfaceFromSchema(
        FileScope<TypeScript> file,
        string typeName,
        IOpenApiSchema schema
    )
    {
        List<TypeMember> members = [];
        List<string> dependsOn = CaptureReferences(() =>
        {
            InterfaceScope<TypeScript, FileScope<TypeScript>> interfaceBuilder = file.Interface(
                    typeName
                )
                .Export();

            if (!string.IsNullOrWhiteSpace(schema.Description))
            {
                interfaceBuilder.Doc(DocText(schema.Description));
            }

            if (schema.Properties != null)
            {
                foreach (KeyValuePair<string, IOpenApiSchema> property in schema.Properties)
                {
                    bool isRequired = schema.Required?.Contains(property.Key) ?? false;
                    string propertyType = BuildInterfaceProperty(
                        file,
                        interfaceBuilder,
                        typeName,
                        property.Key,
                        property.Value,
                        isRequired
                    );
                    members.Add(new TypeMember(property.Key, propertyType, isRequired));
                }
            }

            interfaceBuilder.EndInterface();
        });

        _declarations.Add(
            new DeclarationRecord(typeName, "interface", dependsOn) { Members = members }
        );
    }

    private static void BuildEnumFromSchema(
        FileScope<TypeScript> file,
        string typeName,
        IOpenApiSchema schema
    )
    {
        if (schema.Enum is not { Count: > 0 })
        {
            return;
        }

        EnumScope<TypeScript, FileScope<TypeScript>> enumBuilder = file.Enum(typeName).Export();

        HashSet<string> usedMembers = [];

        foreach (JsonNode? member in schema.Enum)
        {
            if (member is not JsonValue jsonValue)
            {
                continue;
            }

            if (!jsonValue.TryGetValue(out string? value) || value is null)
            {
                continue;
            }

            string memberName = TypeScriptNaming.SanitizeIdentifier(value, asParameter: true);
            string uniqueName = memberName;
            int suffix = 1;
            while (!usedMembers.Add(uniqueName))
            {
                uniqueName = $"{memberName}_{suffix++}";
            }

            enumBuilder.Member(uniqueName, TypeScriptNaming.StringLiteral(value));
        }

        enumBuilder.EndEnum();
    }

    private static List<string> EnumStringValues(IOpenApiSchema schema)
    {
        return (schema.Enum ?? [])
            .OfType<JsonNode>()
            .Where(member =>
                !JsonNullSentinel.IsJsonNullSentinel(member)
                && member.GetValueKind() == JsonValueKind.String
            )
            .Select(member => member.GetValue<string>())
            .ToList();
    }

    /// <summary>A string enum has at least one member and every non-null member is a string.</summary>
    private static bool IsStringEnum(IOpenApiSchema schema)
    {
        bool hasString = false;

        foreach (JsonNode? member in schema.Enum ?? [])
        {
            if (
                member is null
                || JsonNullSentinel.IsJsonNullSentinel(member)
                || member.GetValueKind() == JsonValueKind.Null
            )
            {
                continue;
            }

            if (member.GetValueKind() != JsonValueKind.String)
            {
                return false;
            }

            hasString = true;
        }

        return hasString;
    }

    private string BuildInterfaceProperty(
        FileScope<TypeScript> file,
        InterfaceScope<TypeScript, FileScope<TypeScript>> interfaceBuilder,
        string parentName,
        string name,
        IOpenApiSchema schema,
        bool isRequired
    )
    {
        string propertyType = ResolveType(
            file,
            parentName + TypeScriptNaming.ToPascalCase(name),
            schema
        );

        PropertyScope<
            TypeScript,
            InterfaceScope<TypeScript, FileScope<TypeScript>>
        > propertyBuilder = interfaceBuilder.Property(
            TypeScriptNaming.QuoteKey(name),
            propertyType
        );

        if (!isRequired)
        {
            propertyBuilder.Nullable();
        }

        propertyBuilder.EndProperty();
        return propertyType;
    }

    /// <summary>A raw binary body without a schema is still a body, typed as Blob.</summary>
    private void BuildOperationTypeInterfaces(
        FileScope<TypeScript> file,
        List<OperationInfo> operations
    )
    {
        foreach (OperationInfo operation in operations)
        {
            if (operation.RequestBodySchema != null)
            {
                operation.RequestRefs = CaptureReferences(() =>
                    operation.RequestBodyType = ResolveType(
                        file,
                        operation.Name + "Request",
                        operation.RequestBodySchema
                    )
                );
            }
            else if (operation.RequestBodyEncoding == "binary")
            {
                operation.RequestBodyType = "Blob";
            }

            if (operation.ResponseIsBinary)
            {
                operation.ReturnType = "Blob";
            }
            else if (operation.ResponseSchema != null)
            {
                operation.ResponseRefs = CaptureReferences(() =>
                    operation.ReturnType = ResolveType(
                        file,
                        operation.Name + "Response",
                        operation.ResponseSchema
                    )
                );
            }
        }
    }

    /// <summary>
    /// The single schema-to-TypeScript resolver. With a file scope, inline object schemas are declared
    /// as named interfaces; without one (parameters, allOf members, additionalProperties values) they
    /// become literal types and nothing is declared.
    /// </summary>
    private string ResolveType(
        FileScope<TypeScript>? file,
        string suggestedName,
        IOpenApiSchema schema
    )
    {
        if (schema is OpenApiSchemaReference schemaRef)
        {
            return ResolveReference(schemaRef);
        }

        JsonSchemaType? type = schema.Type;
        JsonSchemaType kinds = type.GetValueOrDefault() & ~JsonSchemaType.Null;
        bool isNullable =
            type?.HasFlag(JsonSchemaType.Null) == true
            || schema.Extensions?.ContainsKey(OpenApiParser.NullableExtension) == true;

        List<string> parts = [];

        if (schema.Enum?.Count > 0)
        {
            parts.Add(MapEnum(schema));
        }
        else if (schema.AllOf?.Count > 0)
        {
            parts.Add(ResolveAllOf(suggestedName, schema, ref isNullable));
        }
        else if (schema.OneOf?.Count > 0 || schema.AnyOf?.Count > 0)
        {
            parts.Add(ResolveUnion(file, suggestedName, schema, ref isNullable));
        }
        else
        {
            if (schema.Items != null || kinds.HasFlag(JsonSchemaType.Array))
            {
                string itemType =
                    schema.Items != null
                        ? ResolveType(file, suggestedName + "Item", schema.Items)
                        : "any";
                parts.Add(ParenthesizeForSuffix(itemType) + "[]");
            }

            if (
                schema.Properties?.Count > 0
                || schema.AdditionalProperties != null
                || kinds.HasFlag(JsonSchemaType.Object)
            )
            {
                parts.Add(ResolveObject(file, suggestedName, schema));
            }

            parts.AddRange(MapPrimitiveTypes(kinds, schema));
        }

        return Compose(parts, isNullable);
    }

    private string ResolveReference(OpenApiSchemaReference schemaRef)
    {
        string? id = schemaRef.Reference.Id;
        if (id != null && _componentNames.TryGetValue(id, out string? name))
        {
            NoteReference(name);
            return name;
        }

        return "any";
    }

    /// <summary>Runs generation work and returns the declared type names it referred to, in order of first use.</summary>
    private List<string> CaptureReferences(Action work)
    {
        List<string> references = [];
        _referenceScopes.Push(references);

        try
        {
            work();
        }
        finally
        {
            _referenceScopes.Pop();
        }

        return references;
    }

    private void NoteReference(string typeName)
    {
        if (_referenceScopes.Count > 0 && !_referenceScopes.Peek().Contains(typeName))
        {
            _referenceScopes.Peek().Add(typeName);
        }
    }

    private string ResolveObject(
        FileScope<TypeScript>? file,
        string suggestedName,
        IOpenApiSchema schema
    )
    {
        if (schema.Properties?.Count > 0)
        {
            if (file == null)
            {
                return BuildObjectLiteral(schema);
            }

            string typeName = AllocateTypeName(suggestedName);
            BuildInterfaceFromSchema(file, typeName, schema);
            NoteReference(typeName);
            return typeName;
        }

        if (schema.AdditionalProperties != null)
        {
            return $"Record<string, {ResolveType(null, suggestedName, schema.AdditionalProperties)}>";
        }

        return "Record<string, unknown>";
    }

    private string BuildObjectLiteral(IOpenApiSchema schema)
    {
        List<string> properties = [];

        foreach (
            KeyValuePair<string, IOpenApiSchema> property in schema.Properties
                ?? new Dictionary<string, IOpenApiSchema>()
        )
        {
            bool isRequired = schema.Required?.Contains(property.Key) ?? false;
            string optional = isRequired ? "" : "?";
            string propertyType = ResolveType(null, "Inline", property.Value);
            properties.Add($"{TypeScriptNaming.QuoteKey(property.Key)}{optional}: {propertyType}");
        }

        return "{ " + string.Join("; ", properties) + " }";
    }

    /// <summary>allOf becomes an intersection; own properties next to allOf join it as a literal type.</summary>
    private string ResolveAllOf(string suggestedName, IOpenApiSchema schema, ref bool isNullable)
    {
        List<string> members = [];

        foreach (IOpenApiSchema member in schema.AllOf ?? [])
        {
            if (IsNullOnlySchema(member))
            {
                isNullable = true;
                continue;
            }

            string memberType = SplitNull(
                ResolveType(null, suggestedName, member),
                out bool memberNullable
            );
            isNullable |= memberNullable;
            members.Add(ParenthesizeForIntersection(memberType));
        }

        if (schema.Properties?.Count > 0)
        {
            members.Add(BuildObjectLiteral(schema));
        }

        List<string> distinct = members.Distinct().ToList();
        return distinct.Count == 0 ? "any" : string.Join(" & ", distinct);
    }

    /// <summary>oneOf and anyOf both become a union; null-only branches turn into a trailing "| null".</summary>
    private string ResolveUnion(
        FileScope<TypeScript>? file,
        string suggestedName,
        IOpenApiSchema schema,
        ref bool isNullable
    )
    {
        IList<IOpenApiSchema> branches = schema.OneOf is { Count: > 0 } oneOf
            ? oneOf
            : schema.AnyOf ?? [];
        List<string> members = [];

        foreach (IOpenApiSchema branch in branches)
        {
            if (IsNullOnlySchema(branch))
            {
                isNullable = true;
                continue;
            }

            string branchType = SplitNull(
                ResolveType(file, suggestedName + "Union", branch),
                out bool branchNullable
            );
            isNullable |= branchNullable;
            members.Add(branchType);
        }

        List<string> distinct = members.Distinct().ToList();
        return distinct.Count == 0 ? "any" : string.Join(" | ", distinct);
    }

    private static bool IsNullOnlySchema(IOpenApiSchema schema)
    {
        JsonSchemaType? type = schema.Type;
        return schema is not OpenApiSchemaReference
            && type?.HasFlag(JsonSchemaType.Null) == true
            && type.Value == JsonSchemaType.Null
            && schema.AllOf is not { Count: > 0 }
            && schema.OneOf is not { Count: > 0 }
            && schema.AnyOf is not { Count: > 0 };
    }

    private static List<string> MapPrimitiveTypes(JsonSchemaType kinds, IOpenApiSchema schema)
    {
        List<string> result = [];

        if (kinds.HasFlag(JsonSchemaType.String))
        {
            result.Add(IsBinarySchema(schema) ? "Blob" : "string");
        }

        if (kinds.HasFlag(JsonSchemaType.Integer) || kinds.HasFlag(JsonSchemaType.Number))
        {
            result.Add("number");
        }

        if (kinds.HasFlag(JsonSchemaType.Boolean))
        {
            result.Add("boolean");
        }

        return result;
    }

    /// <summary>Literal union of an enum's members; strings are escaped, numbers and booleans are written as-is.</summary>
    private static string MapEnum(IOpenApiSchema schema)
    {
        List<string> values = [];

        foreach (JsonNode? item in schema.Enum ?? [])
        {
            if (item is null || JsonNullSentinel.IsJsonNullSentinel(item))
            {
                values.Add("null");
                continue;
            }

            switch (item.GetValueKind())
            {
                case JsonValueKind.String:
                    values.Add(TypeScriptNaming.StringLiteral(item.GetValue<string>()));
                    break;
                case JsonValueKind.Number:
                    values.Add(item.ToJsonString());
                    break;
                case JsonValueKind.True:
                    values.Add("true");
                    break;
                case JsonValueKind.False:
                    values.Add("false");
                    break;
                case JsonValueKind.Null:
                    values.Add("null");
                    break;
                default:
                    values.Add("any");
                    break;
            }
        }

        return values.Count == 0 ? "any" : string.Join(" | ", values.Distinct());
    }

    /// <summary>Joins union parts and appends "| null"; "any" absorbs everything else.</summary>
    private static string Compose(List<string> parts, bool isNullable)
    {
        List<string> members = [];

        foreach (string part in parts)
        {
            string stripped = SplitNull(part, out bool partNullable);
            isNullable |= partNullable;
            if (stripped.Length > 0)
            {
                members.Add(stripped);
            }
        }

        members = members.Distinct().ToList();

        if (members.Contains("any"))
        {
            return "any";
        }

        if (members.Count == 0)
        {
            return isNullable ? "null" : "any";
        }

        string union = string.Join(" | ", members);
        return isNullable ? union + " | null" : union;
    }

    /// <summary>Removes a trailing "null" member from a type and reports whether there was one.</summary>
    private static string SplitNull(string type, out bool hadNull)
    {
        if (type == "null")
        {
            hadNull = true;
            return string.Empty;
        }

        if (type.EndsWith(" | null", StringComparison.Ordinal))
        {
            hadNull = true;
            return type[..^" | null".Length];
        }

        hadNull = false;
        return type;
    }

    /// <summary>Wraps unions and intersections in parentheses so a suffix like "[]" applies to the whole type.</summary>
    private static string ParenthesizeForSuffix(string type)
    {
        return HasTopLevel(type, '|') || HasTopLevel(type, '&') ? "(" + type + ")" : type;
    }

    private static string ParenthesizeForIntersection(string type)
    {
        return HasTopLevel(type, '|') ? "(" + type + ")" : type;
    }

    /// <summary>Looks for an operator outside of brackets, generics and string literals.</summary>
    private static bool HasTopLevel(string type, char op)
    {
        int depth = 0;
        char quote = '\0';

        for (int i = 0; i < type.Length; i++)
        {
            char c = type[i];

            if (quote != '\0')
            {
                if (c == '\\')
                {
                    i++;
                }
                else if (c == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            switch (c)
            {
                case '"':
                case '\'':
                    quote = c;
                    break;
                case '{':
                case '(':
                case '<':
                case '[':
                    depth++;
                    break;
                case '}':
                case ')':
                case '>':
                case ']':
                    depth--;
                    break;
                default:
                    if (c == op && depth == 0)
                    {
                        return true;
                    }

                    break;
            }
        }

        return false;
    }

    /// <summary>Picks a unique type name for an inline schema; names taken by components or earlier inline types get a numeric suffix.</summary>
    private string AllocateTypeName(string suggestedName)
    {
        string candidate = TypeScriptNaming.ToTypeIdentifier(
            TypeScriptNaming.ToPascalCase(suggestedName)
        );
        string unique = candidate;
        int suffix = 2;

        while (TypeScriptNaming.IsReservedTypeName(unique) || !_usedTypeNames.Add(unique))
        {
            unique = $"{candidate}_{suffix++}";
        }

        return unique;
    }

    /// <summary>Descriptions go into /** */ comments, so a closing marker inside the text must not end the comment early.</summary>
    private static string DocText(string text)
    {
        return text.Trim().Replace("*/", "*\\/", StringComparison.Ordinal);
    }
}
