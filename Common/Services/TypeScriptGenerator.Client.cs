using Auxil.CodeGeneration.Common.Builders;
using Auxil.CodeGeneration.Common.Builders.Scopes;
using Auxil.CodeGeneration.Common.Languages;
using Auxil.CodeGeneration.Common.Model;
using Catalyst.Common.Models;

namespace Catalyst.Common.Services;

internal sealed partial class TypeScriptGenerator
{
    private void BuildApiClientInterface(FileScope<TypeScript> file, List<OperationInfo> operations)
    {
        InterfaceScope<TypeScript, FileScope<TypeScript>> clientInterface = file.Interface(
                _clientInterfaceName
            )
            .Export();

        IEnumerable<IGrouping<string, OperationInfo>> groups = operations.GroupBy(o =>
            o.HttpMethod
        );

        foreach (IGrouping<string, OperationInfo> group in groups)
        {
            string verbInterfaceName = TypeScriptNaming.SanitizeIdentifier(
                TypeScriptNaming.ToPascalCase(group.Key)
            );
            clientInterface.Property(group.Key, verbInterfaceName).EndProperty();

            InterfaceScope<TypeScript, FileScope<TypeScript>> verbInterface = file.Interface(
                    verbInterfaceName
                )
                .Export();

            foreach (OperationInfo operation in group)
            {
                MethodScope<TypeScript, InterfaceScope<TypeScript, FileScope<TypeScript>>> method =
                    verbInterface.Method(operation.MethodName);

                foreach (SignatureParameter param in BuildSignature(operation))
                {
                    method.Parameter(param.Name, param.Type);
                    if (param.Optional)
                    {
                        method.Nullable();
                    }
                }

                method.Returns("Promise<ApiResult<" + operation.ReturnType + ">>").EndMethod();
            }

            verbInterface.EndInterface();
        }

        clientInterface.EndInterface();
    }

    /// <summary>
    /// Parameter order of a generated method: path parameters, required query parameters, the body,
    /// optional query parameters, then the optional headers and signal. Required parameters never
    /// follow an optional one, which TypeScript would reject.
    /// </summary>
    private static List<SignatureParameter> BuildSignature(OperationInfo operation)
    {
        List<SignatureParameter> result = [];

        foreach (ParameterInfo param in operation.PathParams)
        {
            result.Add(new SignatureParameter(param.Name, param.Type, Optional: false));
        }

        foreach (ParameterInfo param in operation.QueryParams.Where(p => p.Required))
        {
            result.Add(new SignatureParameter(param.Name, param.Type, Optional: false));
        }

        if (operation.RequestBodyType != null)
        {
            result.Add(new SignatureParameter("body", operation.RequestBodyType, Optional: false));
        }

        foreach (ParameterInfo param in operation.QueryParams.Where(p => !p.Required))
        {
            result.Add(new SignatureParameter(param.Name, param.Type, Optional: true));
        }

        result.Add(new SignatureParameter("headers", "Record<string, string>", Optional: true));
        result.Add(new SignatureParameter("signal", "AbortSignal", Optional: true));

        return result;
    }

    private void BuildFactoryFunction(FileScope<TypeScript> file, List<OperationInfo> operations)
    {
        file.Function("createClient")
            .Export()
            .Parameter("options", "ClientOptions", "{}")
            .Returns(_clientInterfaceName)
            .Body(b =>
            {
                EmitClientSettings(b);
                EmitBodyEncoders(b);

                b.Function("request", f =>
                    f.AsyncModifier()
                        .Generic("T")
                        .Parameter("path", "string")
                        .Parameter("method", "string")
                        .Parameter("init", "RequestOptions")
                        .Nullable()
                        .Returns("Promise<ApiResult<T>>")
                        .Body(EmitRequestFunction)
                );
                b.BlankLine();

                EmitOperationMembers(b, operations);
            })
            .EndFunction();
    }

    private void EmitClientSettings(BodyBuilder<TypeScript> b)
    {
        string defaultBaseUrl = GetDefaultBaseUrl();

        if (!string.IsNullOrWhiteSpace(defaultBaseUrl))
        {
            b.Const(
                "baseUrl",
                (CodeType?)null,
                $"options.baseUrl || {TypeScriptNaming.SingleQuotedLiteral(defaultBaseUrl)}"
            );
        }
        else
        {
            b.If("!options.baseUrl", i =>
                i.Throw("new Error('baseUrl is required in ClientOptions')")
            );
            b.Const("baseUrl", (CodeType?)null, "options.baseUrl");
        }

        b.Const("credentials", (CodeType?)null, "options.credentials");
        b.Const("headers", (CodeType?)null, "options.headers || {}");
        b.Const("timeoutMs", (CodeType?)null, "options.timeoutMs");
        b.BlankLine();
    }

    private void EmitRequestFunction(BodyBuilder<TypeScript> req)
    {
        EmitUrl(req);
        req.BlankLine();
        req.Const(
            "requestHeaders",
            "Record<string, string>",
            "{ ...headers, ...(init?.headers ?? {}) }"
        );
        req.Var("body", "BodyInit | undefined");
        req.If("init?.body !== undefined", EmitBodyEncoding);
        req.BlankLine();
        EmitFetch(req);
        req.BlankLine();
        req.If("!response.ok", EmitErrorResponse);
        req.BlankLine();
        EmitSuccessResponse(req);
    }

    /// <summary>The URL is built as a string: new URL(path, base) would drop the path of the base URL.</summary>
    private static void EmitUrl(BodyBuilder<TypeScript> req)
    {
        req.Var("url", (CodeType?)null, "baseUrl.replace(/\\/+$/, \"\") + path");
        req.If("init?.params", p =>
        {
            p.Const("search", (CodeType?)null, "new URLSearchParams()");
            p.Const(
                "entries",
                (CodeType?)null,
                "Object.entries(init.params).filter(([, v]) => v !== undefined)"
            );
            p.ForEach("[key, value]", "entries", e =>
            {
                e.If("Array.isArray(value)", arr =>
                        arr.ForEach("v", "value", v => v.Line("search.append(key, String(v));"))
                    )
                    .Else(other => other.Line("search.append(key, String(value));"));
            });
            p.Const("query", (CodeType?)null, "search.toString()");
            p.If("query", q => q.Line("url += (url.includes(\"?\") ? \"&\" : \"?\") + query;"));
        });
    }

    /// <summary>One branch per encoding the spec uses; JSON stays the default.</summary>
    private void EmitBodyEncoding(BodyBuilder<TypeScript> body)
    {
        const string contentTypeCheck =
            "Object.keys(requestHeaders).some((name) => name.toLowerCase() === \"content-type\")";
        string missingContentType = "!" + contentTypeCheck;

        if (_bodyEncodings.Count > 0)
        {
            body.Const("hasContentType", (CodeType?)null, contentTypeCheck);
            missingContentType = "!hasContentType";
        }

        EncodingBranches branches = new(body, missingContentType);

        if (_bodyEncodings.Contains("form"))
        {
            branches.Add("init.bodyType === \"form\"", form =>
                form.Line(
                    "body = init.body instanceof FormData ? init.body : toFormData(init.body);"
                )
            );
        }

        if (_bodyEncodings.Contains("urlencoded"))
        {
            branches.Add("init.bodyType === \"urlencoded\"", urlencoded =>
            {
                urlencoded.Line(
                    "body = typeof init.body === \"string\" || init.body instanceof URLSearchParams ? init.body : toSearchParams(init.body);"
                );
                branches.SetDefaultContentType(urlencoded, "application/x-www-form-urlencoded");
            });
        }

        if (_bodyEncodings.Contains("text"))
        {
            branches.Add("init.bodyType === \"text\"", text =>
            {
                text.Line("body = init.body as BodyInit;");
                branches.SetDefaultContentType(text, "text/plain");
            });
        }

        if (_bodyEncodings.Contains("binary"))
        {
            branches.Add("init.bodyType === \"binary\"", binary =>
            {
                binary.Line("body = init.body as BodyInit;");
                branches.SetDefaultContentType(binary, "application/octet-stream");
            });
        }

        branches.Add(
            "typeof init.body === \"object\" && init.body !== null && !(init.body instanceof FormData) && !(init.body instanceof Blob) && !(init.body instanceof ArrayBuffer)",
            obj =>
            {
                branches.SetDefaultContentType(obj, "application/json");
                obj.Line("body = JSON.stringify(init.body);");
            }
        );
        body.Else(raw => raw.Line("body = init.body as BodyInit;"));
    }

    private static void EmitFetch(BodyBuilder<TypeScript> req)
    {
        req.Const(
            "timeoutSignal",
            (CodeType?)null,
            "timeoutMs !== undefined ? AbortSignal.timeout(timeoutMs) : undefined"
        );
        req.Var("signal", "AbortSignal | undefined", "init?.signal");
        req.If("timeoutSignal", t =>
            t.Line("signal = signal ? AbortSignal.any([signal, timeoutSignal]) : timeoutSignal;")
        );
        req.BlankLine();
        req.Line("const response = await fetch(url, {");
        req.Line("    method,");
        req.Line("    credentials,");
        req.Line("    headers: requestHeaders,");
        req.Line("    body,");
        req.Line("    signal,");
        req.Line("});");
    }

    private static void EmitErrorResponse(BodyBuilder<TypeScript> err)
    {
        err.Const("errorText", (CodeType?)null, "await response.text().catch(() => \"\")");
        err.Var(
            "problemDetail",
            "ProblemDetail",
            "{ status: response.status, title: response.statusText }"
        );
        err.Try(t =>
            {
                t.Const("parsed", (CodeType?)null, "JSON.parse(errorText)");
                t.If("parsed && typeof parsed === \"object\"", parsed =>
                {
                    parsed.Line(
                        "const { type, title, status, detail, instance, ...rest } = parsed;"
                    );
                    parsed.Line("problemDetail = {");
                    parsed.Line("    type,");
                    parsed.Line("    title: title || response.statusText,");
                    parsed.Line("    status: status || response.status,");
                    parsed.Line("    detail: detail || errorText,");
                    parsed.Line("    instance,");
                    parsed.Line("    extensions: Object.keys(rest).length > 0 ? rest : undefined,");
                    parsed.Line("};");
                });
            })
            .Catch(null, c =>
            {
                c.Line(
                    "problemDetail = { status: response.status, title: response.statusText, detail: errorText || `HTTP ${response.status}` };"
                );
            });
        err.Return("{ data: null, error: problemDetail }");
    }

    /// <summary>Only JSON is parsed. A response without a content-type is parsed when it happens to be JSON.</summary>
    private void EmitSuccessResponse(BodyBuilder<TypeScript> req)
    {
        if (_usesBlobResponse)
        {
            req.If("init?.responseType === \"blob\"", blob =>
                blob.Return("{ data: (await response.blob()) as T, error: null }")
            );
        }

        req.Const("text", (CodeType?)null, "await response.text()");
        req.If("!text", empty => empty.Return("{ data: undefined as T, error: null }"));
        req.Const(
            "contentType",
            (CodeType?)null,
            "(response.headers.get(\"content-type\") ?? \"\").toLowerCase()"
        );
        req.If("contentType.includes(\"json\")", json =>
            json.Return("{ data: JSON.parse(text) as T, error: null }")
        );
        req.If("!contentType", none =>
            none.Try(t => t.Return("{ data: JSON.parse(text) as T, error: null }"))
                .Catch(null, c => c.Line("// Not JSON: fall through and return the text."))
        );
        req.Return("{ data: text as T, error: null }");
    }

    private static void EmitOperationMembers(
        BodyBuilder<TypeScript> b,
        List<OperationInfo> operations
    )
    {
        b.ObjectLiteral("return", obj =>
        {
            foreach (
                IGrouping<string, OperationInfo> group in operations.GroupBy(o => o.HttpMethod)
            )
            {
                obj.Member(group.Key, (ObjectLiteralBuilder<TypeScript> g) =>
                {
                    foreach (OperationInfo operation in group.ToArray())
                    {
                        g.Member(operation.MethodName, (FunctionStatementBuilder<TypeScript> m) =>
                            EmitOperationMethod(m, operation)
                        );
                    }
                });
            }
        });
    }

    private static void EmitOperationMethod(
        FunctionStatementBuilder<TypeScript> m,
        OperationInfo operation
    )
    {
        if (!string.IsNullOrWhiteSpace(operation.Summary))
        {
            m.Doc(DocText(operation.Summary));
        }

        m.AsyncModifier();

        foreach (SignatureParameter param in BuildSignature(operation))
        {
            m.Parameter(param.Name, param.Type);
            if (param.Optional)
            {
                m.Nullable();
            }
        }

        m.Returns($"Promise<ApiResult<{operation.ReturnType}>>");
        m.Body(fn =>
            fn.Return(
                $"request<{operation.ReturnType}>({BuildUrl(operation)}, '{operation.HttpMethod.ToUpperInvariant()}'{BuildInitArgument(operation)})"
            )
        );
    }

    /// <summary>
    /// Chains the encoding branches of the request body into one if / else if sequence. A form body gets no
    /// Content-Type: fetch adds the multipart boundary itself.
    /// </summary>
    private sealed class EncodingBranches(BodyBuilder<TypeScript> body, string missingContentType)
    {
        private bool _isFirst = true;

        public void Add(string condition, Action<BodyBuilder<TypeScript>> branch)
        {
            if (_isFirst)
            {
                body.If(condition, branch);
            }
            else
            {
                body.ElseIf(condition, branch);
            }

            _isFirst = false;
        }

        public void SetDefaultContentType(BodyBuilder<TypeScript> target, string contentType)
        {
            target.If(missingContentType, ct =>
                ct.Line($"requestHeaders[\"Content-Type\"] = \"{contentType}\";")
            );
        }
    }

    /// <summary>Collects the operations and declared types into the lookup model, from the generation state and the rendered file. Types keep the file order, which is also the order readers know from the generated code.</summary>
    private ClientModel BuildModel(string code, List<OperationInfo> operations)
    {
        Dictionary<string, string> rendered = RenderedDeclarations.Split(code);
        Dictionary<string, string> componentOf = _componentNames.ToDictionary(
            pair => pair.Value,
            pair => pair.Key
        );

        List<TypeDeclaration> types = _declarations
            .Where(declaration => rendered.ContainsKey(declaration.Name))
            .OrderBy(declaration =>
                code.IndexOf(rendered[declaration.Name], StringComparison.Ordinal)
            )
            .Select(declaration => new TypeDeclaration(
                declaration.Name,
                declaration.Kind,
                componentOf.GetValueOrDefault(declaration.Name),
                rendered[declaration.Name],
                declaration.DependsOn
            )
            {
                Members = declaration.Members,
                AliasType = declaration.AliasType,
                Values = declaration.Values,
            })
            .ToList();

        return new ClientModel(operations.Select(ToApiOperation).ToList(), types);
    }

    private static ApiOperation ToApiOperation(OperationInfo operation)
    {
        List<SignatureParameter> signature = BuildSignature(operation);
        string signatureText =
            $"{operation.MethodName}({string.Join(", ", signature.Select(p => $"{p.Name}{(p.Optional ? "?" : string.Empty)}: {p.Type}"))}): Promise<ApiResult<{operation.ReturnType}>>";

        List<ApiParameter> parameters = operation
            .PathParams.Concat(operation.QueryParams.Where(p => p.Required))
            .Concat(operation.QueryParams.Where(p => !p.Required))
            .Select(p => new ApiParameter(
                p.Name,
                p.OriginalName,
                p.Location,
                p.Required,
                p.Type,
                p.Description
            ))
            .ToList();

        ApiBody? body =
            operation.RequestBodyType == null
                ? null
                : new ApiBody(
                    operation.RequestBodyType,
                    operation.RequestMediaType ?? string.Empty,
                    operation.RequestBodyRequired
                );

        List<string> referenced = operation
            .PathParams.SelectMany(p => p.References)
            .Concat(operation.QueryParams.SelectMany(p => p.References))
            .Concat(operation.RequestRefs)
            .Concat(operation.ResponseRefs)
            .Distinct()
            .ToList();

        return new ApiOperation(
            operation.HttpMethod.ToUpperInvariant(),
            operation.Path,
            operation.OperationId,
            string.IsNullOrWhiteSpace(operation.Summary) ? null : operation.Summary,
            operation.Description,
            operation.Tags,
            operation.Deprecated,
            operation.HttpMethod,
            operation.MethodName,
            signatureText,
            parameters,
            body,
            new ApiResponse(operation.ReturnType, operation.ResponseMediaType),
            referenced
        );
    }

    /// <summary>Helpers that turn an object into form data or a query-style string, for the encodings the spec uses. Blobs become files, arrays repeat the field, objects are sent as JSON text.</summary>
    private void EmitBodyEncoders(BodyBuilder<TypeScript> b)
    {
        if (_bodyEncodings.Contains("form"))
        {
            b.Line("function toFormData(value: unknown): FormData {");
            b.Line("    const form = new FormData();");
            b.Line("    if (typeof value === \"object\" && value !== null) {");
            b.Line("        for (const [key, entry] of Object.entries(value)) {");
            b.Line("            for (const item of Array.isArray(entry) ? entry : [entry]) {");
            b.Line("                if (item === undefined || item === null) {");
            b.Line("                    continue;");
            b.Line("                }");
            b.Line("                if (item instanceof Blob) {");
            b.Line("                    form.append(key, item);");
            b.Line("                } else if (typeof item === \"object\") {");
            b.Line("                    form.append(key, JSON.stringify(item));");
            b.Line("                } else {");
            b.Line("                    form.append(key, String(item));");
            b.Line("                }");
            b.Line("            }");
            b.Line("        }");
            b.Line("    }");
            b.Line("    return form;");
            b.Line("}");
            b.BlankLine();
        }

        if (_bodyEncodings.Contains("urlencoded"))
        {
            b.Line("function toSearchParams(value: unknown): URLSearchParams {");
            b.Line("    const search = new URLSearchParams();");
            b.Line("    if (typeof value === \"object\" && value !== null) {");
            b.Line("        for (const [key, entry] of Object.entries(value)) {");
            b.Line("            for (const item of Array.isArray(entry) ? entry : [entry]) {");
            b.Line("                if (item === undefined || item === null) {");
            b.Line("                    continue;");
            b.Line("                }");
            b.Line(
                "                search.append(key, typeof item === \"object\" ? JSON.stringify(item) : String(item));"
            );
            b.Line("            }");
            b.Line("        }");
            b.Line("    }");
            b.Line("    return search;");
            b.Line("}");
            b.BlankLine();
        }
    }
}
