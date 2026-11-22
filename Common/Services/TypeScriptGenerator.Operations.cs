using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Auxil.Extensions;
using Catalyst.Common.Models;
using Microsoft.OpenApi;

namespace Catalyst.Common.Services;

internal sealed partial class TypeScriptGenerator
{
    private List<OperationInfo> ExtractOperations()
    {
        List<OperationInfo> operations = [];
        Dictionary<string, Dictionary<string, (string Path, string Method)>> usedNamesByMethod = [];
        OpenApiPaths? paths = _document.Paths;

        if (paths == null || paths.Count == 0)
        {
            return operations;
        }

        foreach (KeyValuePair<string, IOpenApiPathItem> pathEntry in paths)
        {
            string path = pathEntry.Key;

            if (IsSkippedPath(path))
            {
                SkippedPathCount++;
                continue;
            }

            Dictionary<HttpMethod, OpenApiOperation>? opMap = pathEntry.Value.Operations;
            if (opMap == null)
            {
                continue;
            }

            foreach (KeyValuePair<HttpMethod, OpenApiOperation> opEntry in opMap)
            {
                if (opEntry.Value == null)
                {
                    continue;
                }

                string httpMethod = opEntry.Key.Method.ToLowerInvariant();
                if (
                    !usedNamesByMethod.TryGetValue(
                        httpMethod,
                        out Dictionary<string, (string Path, string Method)>? value
                    )
                )
                {
                    value = [];
                    usedNamesByMethod[httpMethod] = value;
                }

                OperationInfo info = ExtractOperationInfo(
                    path,
                    pathEntry.Value,
                    opEntry.Key,
                    opEntry.Value,
                    value
                );
                operations.Add(info);
            }
        }

        AssignMethodNames(operations);

        return operations;
    }

    /// <summary>
    /// Gives every operation the method name used on its verb interface. Two operations can end up with the
    /// same short name once the verb is stripped; the later one keeps its full name instead.
    /// </summary>
    private void AssignMethodNames(List<OperationInfo> operations)
    {
        foreach (IGrouping<string, OperationInfo> group in operations.GroupBy(o => o.HttpMethod))
        {
            HashSet<string> usedMethodNames = [];

            foreach (OperationInfo operation in group)
            {
                string methodName = TypeScriptNaming.StripHttpVerbPrefix(
                    operation.Name,
                    operation.HttpMethod,
                    operation.NameFromPath
                );

                if (!usedMethodNames.Add(methodName))
                {
                    string fullName = TypeScriptNaming.LowerFirst(operation.Name);
                    int counter = 2;
                    while (!usedMethodNames.Add(fullName))
                    {
                        fullName = TypeScriptNaming.LowerFirst(operation.Name) + counter++;
                    }

                    Warnings.Add(
                        new Issue(
                            IssueCodes.DuplicateMethodName,
                            $"Method name \"{methodName}\" is used twice for {operation.HttpMethod} operations. {operation.HttpMethod} {operation.Path} is named \"{fullName}\" instead."
                        )
                    );
                    methodName = fullName;
                }

                operation.MethodName = methodName;
            }
        }
    }

    private OperationInfo ExtractOperationInfo(
        string path,
        IOpenApiPathItem pathItem,
        HttpMethod method,
        OpenApiOperation operation,
        Dictionary<string, (string Path, string Method)> usedNames
    )
    {
        string operationName = GetOperationName(path, method, operation, usedNames);
        string httpMethod = method.Method.ToLowerInvariant();

        (List<ParameterInfo> pathParams, List<ParameterInfo> queryParams) = ExtractParameters(
            path,
            httpMethod,
            pathItem,
            operation
        );

        BodySelection? requestBody = SelectRequestBody(operation);
        ResponseSelection response = SelectResponse(operation);

        return new OperationInfo
        {
            Name = operationName,
            NameFromPath = string.IsNullOrWhiteSpace(operation.OperationId),
            Path = path,
            HttpMethod = httpMethod,
            Summary = operation.Summary ?? string.Empty,
            PathParams = pathParams,
            QueryParams = queryParams,
            RequestBodySchema = requestBody?.Schema,
            RequestBodyEncoding = requestBody?.Encoding,
            RequestMediaType = requestBody?.MediaType,
            RequestBodyRequired = operation.RequestBody?.Required ?? false,
            ResponseSchema = response.Schema,
            ResponseIsBinary = response.Binary,
            ResponseMediaType = response.MediaType,
            OperationId = string.IsNullOrWhiteSpace(operation.OperationId)
                ? null
                : operation.OperationId,
            Description = string.IsNullOrWhiteSpace(operation.Description)
                ? null
                : operation.Description,
            Tags = operation
                .Tags?.Select(tag => tag.Name)
                .OfType<string>()
                .Where(name => name.Length > 0)
                .ToList()
                ?? [],
            Deprecated = operation.Deprecated,
            ReturnType = "void",
        };
    }

    /// <summary>
    /// Collects the path and query parameters of an operation. Path-level parameters are merged in (the
    /// operation wins on the same name and location). Every {variable} of the path template becomes a
    /// path parameter, in template order, even when the spec does not declare it.
    /// </summary>
    private (List<ParameterInfo> PathParams, List<ParameterInfo> QueryParams) ExtractParameters(
        string path,
        string httpMethod,
        IOpenApiPathItem pathItem,
        OpenApiOperation operation
    )
    {
        List<IOpenApiParameter> merged = MergeParameters(pathItem, operation);
        HashSet<string> usedBindings = ReservedBindings.ToHashSet();

        List<ParameterInfo> pathParams = ExtractPathParameters(
            path,
            httpMethod,
            merged.Where(p => p.In == ParameterLocation.Path).ToList(),
            usedBindings
        );

        List<ParameterInfo> queryParams = [];

        foreach (IOpenApiParameter param in merged.Where(p => p.In == ParameterLocation.Query))
        {
            queryParams.Add(
                CreateParameterInfo(
                    param.Name ?? "param",
                    param.Schema,
                    param.Required,
                    "query",
                    usedBindings,
                    param.Description
                )
            );
        }

        return (pathParams, queryParams);
    }

    private static List<IOpenApiParameter> MergeParameters(
        IOpenApiPathItem pathItem,
        OpenApiOperation operation
    )
    {
        List<IOpenApiParameter> merged = [];

        foreach (
            IOpenApiParameter param in (pathItem.Parameters ?? []).Concat(
                operation.Parameters ?? []
            )
        )
        {
            int existing = merged.FindIndex(m => m.In == param.In && m.Name == param.Name);
            if (existing >= 0)
            {
                merged[existing] = param;
            }
            else
            {
                merged.Add(param);
            }
        }

        return merged;
    }

    private List<ParameterInfo> ExtractPathParameters(
        string path,
        string httpMethod,
        List<IOpenApiParameter> declaredPath,
        HashSet<string> usedBindings
    )
    {
        List<string> templateVariables = Regex
            .Matches(path, @"\{([^{}]+)\}")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();

        List<ParameterInfo> pathParams = [];

        foreach (string variable in templateVariables)
        {
            IOpenApiParameter? declared = declaredPath.FirstOrDefault(p => p.Name == variable);
            if (declared == null)
            {
                Warnings.Add(
                    new Issue(
                        IssueCodes.PathParameterMismatch,
                        $"Path parameter \"{variable}\" of {httpMethod} {path} is used in the URL but not declared. Assuming a required string."
                    )
                );
            }

            pathParams.Add(
                CreateParameterInfo(
                    variable,
                    declared?.Schema,
                    required: true,
                    "path",
                    usedBindings,
                    declared?.Description
                )
            );
        }

        foreach (IOpenApiParameter declared in declaredPath)
        {
            if (!templateVariables.Contains(declared.Name ?? string.Empty))
            {
                Warnings.Add(
                    new Issue(
                        IssueCodes.PathParameterMismatch,
                        $"Path parameter \"{declared.Name}\" of {httpMethod} {path} does not appear in the URL and was ignored."
                    )
                );
            }
        }

        return pathParams;
    }

    /// <summary>
    /// Only separator-delimited names are normalized ("user-id" becomes "userId"); names that are already camelCase stay
    /// untouched, since the library's ToCamelCase would lowercase the inner capital. A name that is taken (by another
    /// parameter or by the method's own parameters) gets its location as a prefix, so "id" in the query becomes
    /// "queryId". The wire name is untouched.
    /// </summary>
    private ParameterInfo CreateParameterInfo(
        string rawName,
        IOpenApiSchema? schema,
        bool required,
        string location,
        HashSet<string> usedBindings,
        string? description
    )
    {
        string camelName = rawName.Any(ch => ch is '-' or '_' or ' ')
            ? rawName.ToCamelCase()
            : rawName;
        string candidate = TypeScriptNaming.SanitizeIdentifier(camelName, asParameter: true);

        string binding = candidate;
        if (!usedBindings.Add(binding))
        {
            string prefixed = location + TypeScriptNaming.ToPascalCase(candidate);
            binding = prefixed;
            int counter = 2;
            while (!usedBindings.Add(binding))
            {
                binding = prefixed + counter++;
            }
        }

        string type = "string";
        List<string> references =
            schema == null ? [] : CaptureReferences(() => type = ResolveInlineType(schema));

        return new ParameterInfo
        {
            OriginalName = rawName,
            Name = binding,
            Type = type,
            Required = required,
            Location = location,
            Description = description,
            References = references,
        };
    }

    /// <summary>Server variables are replaced by their default value. A variable without one leaves the URL incomplete, so there is no usable default.</summary>
    private string GetDefaultBaseUrl()
    {
        OpenApiServer? server = _document.Servers?.FirstOrDefault();
        string? serverUrl = server?.Url;
        if (string.IsNullOrWhiteSpace(serverUrl))
        {
            return string.Empty;
        }

        string resolved = Regex.Replace(serverUrl, @"\{([^{}]+)\}", match =>
        {
            string variableName = match.Groups[1].Value;
            if (
                server?.Variables is { } variables
                && variables.TryGetValue(variableName, out OpenApiServerVariable? variable)
                && !string.IsNullOrEmpty(variable.Default)
            )
            {
                return variable.Default;
            }

            return match.Value;
        });

        return Regex.IsMatch(resolved, @"\{[^{}]*\}") ? string.Empty : resolved;
    }

    private string GetOperationName(
        string path,
        HttpMethod method,
        OpenApiOperation operation,
        Dictionary<string, (string Path, string Method)> usedNames
    )
    {
        string candidate;
        if (!string.IsNullOrWhiteSpace(operation.OperationId))
        {
            candidate = TypeScriptNaming.SanitizeIdentifier(
                TypeScriptNaming.ToPascalCase(operation.OperationId)
            );
        }
        else
        {
            string methodPrefix = method.Method.ToLowerInvariant();
            string pathSuffix = PathToPascalCase(path);

            if (string.IsNullOrEmpty(pathSuffix))
            {
                pathSuffix = "Root";
            }

            candidate = TypeScriptNaming.SanitizeIdentifier(methodPrefix + pathSuffix);
        }

        string baseName = candidate;
        if (!usedNames.ContainsKey(candidate))
        {
            usedNames[candidate] = (path, method.Method);
            return candidate;
        }

        string suffix = BuildDuplicateSuffix(path, usedNames[baseName].Path);
        candidate = TypeScriptNaming.SanitizeIdentifier(baseName + suffix);

        int counter = 2;
        while (usedNames.ContainsKey(candidate))
        {
            candidate = TypeScriptNaming.SanitizeIdentifier(baseName + suffix + counter);
            counter++;
        }

        usedNames[candidate] = (path, method.Method);

        string existingPath = usedNames[baseName].Path;
        string existingMethod = usedNames[baseName].Method;
        Warnings.Add(
            new Issue(
                IssueCodes.DuplicateMethodName,
                $"Duplicate method name \"{baseName}\" for {existingMethod} {existingPath} and {method.Method} {path}. Suffix added: \"{candidate}\"."
            )
        );

        return candidate;
    }

    /// <summary>
    /// A readable suffix for a duplicate method name: the last path segment where the two paths differ, else the last
    /// parameter names when the paths are structurally identical, else "_V2".
    /// </summary>
    private static string BuildDuplicateSuffix(string path1, string path2)
    {
        string[] segments1 = path1.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string[] segments2 = path2.Split('/', StringSplitOptions.RemoveEmptyEntries);

        int minLen = Math.Min(segments1.Length, segments2.Length);
        for (int i = minLen - 1; i >= 0; i--)
        {
            string s1 = segments1[i];
            string s2 = segments2[i];
            if (!string.Equals(s1, s2, StringComparison.OrdinalIgnoreCase))
            {
                return "_" + TypeScriptNaming.SanitizeIdentifier(TypeScriptNaming.ToPascalCase(s1));
            }
        }

        string lastParam1 = ExtractLastParamName(path1);
        string lastParam2 = ExtractLastParamName(path2);
        if (!string.IsNullOrEmpty(lastParam1) || !string.IsNullOrEmpty(lastParam2))
        {
            string p1 = string.IsNullOrEmpty(lastParam1)
                ? "Default"
                : TypeScriptNaming.ToPascalCase(lastParam1);
            string p2 = string.IsNullOrEmpty(lastParam2)
                ? "Default"
                : TypeScriptNaming.ToPascalCase(lastParam2);
            if (p1 != p2)
            {
                return "_" + TypeScriptNaming.SanitizeIdentifier(p1);
            }
        }

        return "_V2";
    }

    private static string ExtractLastParamName(string path)
    {
        int lastOpen = path.LastIndexOf('{', StringComparison.Ordinal);
        int lastClose = path.LastIndexOf('}', StringComparison.Ordinal);
        if (lastOpen >= 0 && lastClose > lastOpen)
        {
            return path.Substring(lastOpen + 1, lastClose - lastOpen - 1).TrimStart('*');
        }

        return string.Empty;
    }

    /// <summary>A redundant leading "api" segment is stripped.</summary>
    private static string PathToPascalCase(string path)
    {
        string[] parts = path.Split(
            ['/', '-', '_', '{', '}', '*'],
            StringSplitOptions.RemoveEmptyEntries
        );

        if (parts.Length == 0)
        {
            return string.Empty;
        }

        int startIndex = 0;
        if (parts.Length > 1 && string.Equals(parts[0], "api", StringComparison.OrdinalIgnoreCase))
        {
            startIndex = 1;
        }

        StringBuilder sb = new();

        for (int i = startIndex; i < parts.Length; i++)
        {
            string part = parts[i];
            if (part.Length > 0)
            {
                sb.Append(char.ToUpperInvariant(part[0]));
                if (part.Length > 1)
                {
                    sb.Append(part.AsSpan(1));
                }
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Picks the request media type: any JSON type, then multipart/form-data, then
    /// application/x-www-form-urlencoded, then text/plain, then whatever is left (sent as raw bytes).
    /// </summary>
    private static BodySelection? SelectRequestBody(OpenApiOperation operation)
    {
        IDictionary<string, IOpenApiMediaType>? content = operation.RequestBody?.Content;
        if (content == null || content.Count == 0)
        {
            return null;
        }

        KeyValuePair<string, IOpenApiMediaType> best = content
            .OrderBy(entry => MediaTypeRank(entry.Key))
            .First();
        string encoding = MediaTypeRank(best.Key) switch
        {
            0 => "json",
            1 => "form",
            2 => "urlencoded",
            3 => "text",
            _ => "binary",
        };

        return new BodySelection(best.Value.Schema, encoding, best.Key);
    }

    /// <summary>*/* counts as JSON: it is what many frameworks write for a JSON payload they do not negotiate.</summary>
    private static int MediaTypeRank(string mediaType)
    {
        string type = mediaType.Split(';')[0].Trim().ToLowerInvariant();

        if (
            type is "application/json" or "text/json" or "*/*"
            || type.EndsWith("+json", StringComparison.Ordinal)
        )
        {
            return 0;
        }

        return type switch
        {
            "multipart/form-data" => 1,
            "application/x-www-form-urlencoded" => 2,
            "text/plain" => 3,
            _ => 4,
        };
    }

    private static bool IsBinaryMediaType(string mediaType)
    {
        string type = mediaType.Split(';')[0].Trim().ToLowerInvariant();

        return type.StartsWith("image/", StringComparison.Ordinal)
            || type.StartsWith("audio/", StringComparison.Ordinal)
            || type.StartsWith("video/", StringComparison.Ordinal)
            || type is "application/octet-stream" or "application/pdf";
    }

    /// <summary>
    /// True for a string that carries raw bytes: format "binary", or (3.1) a contentMediaType that is
    /// neither JSON nor text without a contentEncoding such as base64. The 3.1 content keywords are only on the
    /// concrete schema, not on the interface.
    /// </summary>
    internal static bool IsBinarySchema(IOpenApiSchema schema)
    {
        if (schema.Format == "binary")
        {
            return true;
        }

        OpenApiSchema? concrete =
            schema as OpenApiSchema ?? (schema as OpenApiSchemaReference)?.Target as OpenApiSchema;
        if (concrete?.ContentMediaType is not { Length: > 0 } mediaType)
        {
            return false;
        }

        return string.IsNullOrEmpty(concrete.ContentEncoding)
            && MediaTypeRank(mediaType) > 0
            && !mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase);
    }

    private static ResponseSelection SelectResponse(OpenApiOperation operation)
    {
        IOpenApiResponse? response = FindSuccessResponse(operation);
        IDictionary<string, IOpenApiMediaType>? content = response?.Content;
        if (content == null || content.Count == 0)
        {
            return new ResponseSelection(null, false, null);
        }

        KeyValuePair<string, IOpenApiMediaType> best = content
            .OrderBy(entry => MediaTypeRank(entry.Key))
            .First();
        IOpenApiSchema? schema = best.Value.Schema;
        bool binary =
            IsBinaryMediaType(best.Key)
            || (
                schema != null
                && schema.Type is { } type
                && type.HasFlag(JsonSchemaType.String)
                && IsBinarySchema(schema)
            );

        return new ResponseSelection(schema, binary, best.Key);
    }

    /// <summary>
    /// Some APIs (e.g. ASP.NET Core's ProducesDefaultResponseType) describe the success payload as the `default` response
    /// instead of a 2xx status, so that is the fallback when no success response is declared.
    /// </summary>
    private static IOpenApiResponse? FindSuccessResponse(OpenApiOperation operation)
    {
        if (operation.Responses == null)
        {
            return null;
        }

        if (operation.Responses.TryGetValue("200", out IOpenApiResponse? response))
        {
            return response;
        }

        if (operation.Responses.TryGetValue("201", out IOpenApiResponse? response201))
        {
            return response201;
        }

        foreach (KeyValuePair<string, IOpenApiResponse> entry in operation.Responses)
        {
            if (entry.Key.StartsWith('2', StringComparison.Ordinal))
            {
                return entry.Value;
            }
        }

        foreach (KeyValuePair<string, IOpenApiResponse> entry in operation.Responses)
        {
            if (entry.Key.Equals("default", StringComparison.OrdinalIgnoreCase))
            {
                return entry.Value;
            }
        }

        return null;
    }

    private bool IsSkippedPath(string path)
    {
        if (_skipPathPrefixes.Count == 0)
        {
            return false;
        }

        return _skipPathPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The path argument of the request call: a plain string when the path has no variables,
    /// otherwise a template string with every variable passed through encodeURIComponent.
    /// </summary>
    private static string BuildUrl(OperationInfo operation)
    {
        if (operation.PathParams.Count == 0)
        {
            return TypeScriptNaming.SingleQuotedLiteral(operation.Path);
        }

        StringBuilder url = new();
        int position = 0;

        foreach (Match match in Regex.Matches(operation.Path, @"\{([^{}]+)\}"))
        {
            url.Append(TypeScriptNaming.TemplateLiteralText(operation.Path[position..match.Index]));

            ParameterInfo param = operation.PathParams.First(p =>
                p.OriginalName == match.Groups[1].Value
            );
            url.Append(CultureInfo.InvariantCulture, $"${{encodeURIComponent({param.Name})}}");

            position = match.Index + match.Length;
        }

        url.Append(TypeScriptNaming.TemplateLiteralText(operation.Path[position..]));

        return $"`{url}`";
    }

    /// <summary>JSON is the helper's default, so only the other body encodings are spelled out.</summary>
    private static string BuildInitArgument(OperationInfo operation)
    {
        List<string> parts = [];

        if (operation.QueryParams.Count > 0)
        {
            IEnumerable<string> names = operation.QueryParams.Select(p =>
                p.OriginalName == p.Name
                    ? p.Name
                    : $"{TypeScriptNaming.QuoteKey(p.OriginalName)}: {p.Name}"
            );
            parts.Add("params: { " + string.Join(", ", names) + " }");
        }

        if (operation.RequestBodyType != null)
        {
            parts.Add("body");

            if (operation.RequestBodyEncoding is { } encoding and not "json")
            {
                parts.Add($"bodyType: \"{encoding}\"");
            }
        }

        if (operation.ResponseIsBinary)
        {
            parts.Add("responseType: \"blob\"");
        }

        parts.Add("headers");
        parts.Add("signal");

        return ", { " + string.Join(", ", parts) + " }";
    }

    private sealed class OperationInfo
    {
        public required string Name { get; init; }
        public required bool NameFromPath { get; init; }
        public required string Path { get; init; }
        public required string HttpMethod { get; init; }
        public required string Summary { get; init; }
        public List<ParameterInfo> PathParams { get; init; } = [];
        public List<ParameterInfo> QueryParams { get; init; } = [];
        public IOpenApiSchema? RequestBodySchema { get; init; }
        public string? RequestBodyEncoding { get; init; }
        public IOpenApiSchema? ResponseSchema { get; init; }
        public bool ResponseIsBinary { get; init; }
        public string? RequestBodyType { get; set; }
        public string ReturnType { get; set; } = "void";
        public string MethodName { get; set; } = string.Empty;
        public string? OperationId { get; init; }
        public string? Description { get; init; }
        public List<string> Tags { get; init; } = [];
        public bool Deprecated { get; init; }
        public string? RequestMediaType { get; init; }
        public bool RequestBodyRequired { get; init; }
        public string? ResponseMediaType { get; init; }
        public List<string> RequestRefs { get; set; } = [];
        public List<string> ResponseRefs { get; set; } = [];
    }

    private sealed class ParameterInfo
    {
        public required string OriginalName { get; init; }
        public required string Name { get; init; }
        public required string Type { get; init; }
        public required bool Required { get; init; }
        public string Location { get; init; } = string.Empty;
        public string? Description { get; init; }
        public List<string> References { get; init; } = [];
    }

    private sealed record SignatureParameter(string Name, string Type, bool Optional);

    /// <summary>The request body media type chosen for an operation and how the client encodes it.</summary>
    private sealed record BodySelection(IOpenApiSchema? Schema, string Encoding, string MediaType);

    private sealed record ResponseSelection(IOpenApiSchema? Schema, bool Binary, string? MediaType);
}
