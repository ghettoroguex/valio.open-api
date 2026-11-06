using System.Text;
using System.Text.Json;
using ValIO.Core.Ast.Builders;
using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Core.Interfaces;
using ValIO.Core.IR;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Core.IR.Utilities;
using ValIO.Generators.Python.Ast.Nodes;
using ValIO.Generators.Python.Options;

namespace ValIO.Generators.Python.Ast.Builders;

/// <summary>
/// Builds HTTPX client facades that borrow the application's configured transport and authentication pipeline.
/// </summary>
public sealed class HttpxClientAstBuilder : AstBuilderBase
{
    /// <summary>Converts source operation and parameter names into Python identifiers.</summary>
    private readonly INamingConverter _namingConverter;
    /// <summary>Maps shared request and response types into Python annotations.</summary>
    private readonly PythonIrTypeMapper _typeMapper;

    /// <summary>Initializes the client builder with Python naming and type services.</summary>
    /// <param name="documentContext">The finalized document context shared by the generation request.</param>
    /// <param name="namingConverter">The converter for generated identifiers.</param>
    /// <param name="typeMapper">The mapper for request and response annotations.</param>
    public HttpxClientAstBuilder(IrDocumentContext documentContext, INamingConverter namingConverter, PythonIrTypeMapper typeMapper)
        : base(documentContext)
    {
        _namingConverter = namingConverter;
        _typeMapper = typeMapper;
    }

    /// <summary>Builds a client module using runtime imports for request and response validation.</summary>
    /// <param name="client">The grouped operations to generate.</param>
    /// <param name="options">The Python client generation mode.</param>
    /// <returns>The generated module containing an injected HTTPX client facade.</returns>
    /// <exception cref="NotSupportedException">An operation declares incompatible successful response bodies.</exception>
    public PythonModuleNode BuildModule(
        IrClient client,
        PythonGenerationOptions options)
    {
        var isAsync = options.HttpxMode == PythonOptionIds.HttpxMode.Async;
        var className = ToClassName(client.Name);

        var modelImports = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var datetimeImports = new SortedSet<string>(StringComparer.Ordinal);
        var uuidNeeded = false;
        var anyNeeded = false;

        var methods = new List<DeclarationNode>();
        foreach (var op in client.Operations)
        {
            var method = BuildOperationMethod(
                op,
                isAsync,
                modelImports,
                datetimeImports,
                ref uuidNeeded,
                ref anyNeeded);
            methods.Add(method);
        }

        var ctor = BuildConstructor(isAsync);
        methods.Insert(0, ctor);

        var imports = new List<PythonImportNode>();

        var typingNames = new List<string> { "get_type_hints" };
        if (anyNeeded) typingNames.Add("Any");
        if (typingNames.Count > 0)
            imports.Add(new PythonImportNode("typing", typingNames));

        if (datetimeImports.Count > 0)
            imports.Add(new PythonImportNode("datetime", datetimeImports.ToList()));

        if (uuidNeeded)
            imports.Add(new PythonImportNode("uuid", new[] { "UUID" }));

        imports.Add(new PythonImportNode("httpx", names: Array.Empty<string>()));
        imports.Add(new PythonImportNode("pydantic", new[] { "TypeAdapter" }));
        if (client.Operations.Any(operation => operation.PathParameters.Any(
                parameter => IrTypeQueries.UnwrapNullableOptional(parameter.Type) is not IrPrimitiveType
                    { PrimitiveKind: IrPrimitiveKind.Integer or IrPrimitiveKind.Long or IrPrimitiveKind.Number })))
            imports.Add(new PythonImportNode("urllib.parse", new[] { "quote" }));

        foreach (var kvp in modelImports)
            imports.Add(new PythonImportNode(kvp.Value, new[] { kvp.Key }));

        var typeCheckingImports = new List<PythonImportNode>();

        var classNode = new PythonClassDeclarationNode(
            new IdentifierNode(className),
            baseClasses: null,
            members: methods,
            documentation: BuildClientDocstring(client));

        return new PythonModuleNode(
            declarations: new DeclarationNode[] { classNode },
            imports: imports,
            typeCheckingImports: typeCheckingImports,
            futureImports: new[] { new PythonImportNode("__future__", new[] { "annotations" }) });
    }

    /// <summary>Documents the caller-owned HTTPX pipeline and cancellation behavior.</summary>
    /// <param name="client">The source client group.</param>
    /// <returns>The generated class documentation.</returns>
    private static CommentNode BuildClientDocstring(IrClient client) =>
        new CommentNode($"HTTP client for {client.Name}.\n\nBorrows the supplied HTTPX client; the caller owns its lifetime.\nConfigure headers, Auth, event_hooks, and timeouts on that client.\nAsync calls propagate asyncio cancellation through the HTTPX transport.", CommentKind.MultiLine);

    /// <summary>Builds a constructor borrowing an existing HTTPX client without closing it.</summary>
    /// <param name="isAsync">Whether the facade uses an asynchronous HTTPX client.</param>
    /// <returns>The generated injected-client constructor.</returns>
    private static PythonFunctionDeclarationNode BuildConstructor(bool isAsync)
    {
        var clientAnnotation = new SimpleTypeNode(
            new IdentifierNode(isAsync ? "httpx.AsyncClient" : "httpx.Client"),
            isPrimitive: true);

        var parameters = new[]
        {
            new PythonParameterNode(new IdentifierNode("self")),
            new PythonParameterNode(new IdentifierNode("client"), clientAnnotation),
        };

        return new PythonFunctionDeclarationNode(
            new IdentifierNode("__init__"),
            parameters,
            returnType: new SimpleTypeNode(new IdentifierNode("None"), isPrimitive: true),
            body: new[] { "self._client = client" });
    }

    /// <summary>Builds a typed operation whose transport calls use the borrowed HTTPX pipeline.</summary>
    /// <param name="op">The operation to generate.</param>
    /// <param name="isAsync">Whether transport calls are awaited.</param>
    /// <param name="modelImports">The runtime declaration imports collected for the module.</param>
    /// <param name="datetimeImports">The required standard date and time imports.</param>
    /// <param name="uuidNeeded">Whether UUID annotations require an import.</param>
    /// <param name="anyNeeded">Whether unconstrained annotations require an import.</param>
    /// <returns>The generated Python operation declaration.</returns>
    /// <exception cref="NotSupportedException">Successful response bodies have incompatible types.</exception>
    private PythonFunctionDeclarationNode BuildOperationMethod(
        IrOperation op,
        bool isAsync,
        SortedDictionary<string, string> modelImports,
        SortedSet<string> datetimeImports,
        ref bool uuidNeeded,
        ref bool anyNeeded)
    {
        var methodName = _namingConverter.ToSnakeCase(op.MethodName);
        methodName = PythonKeywords.Avoid(methodName);

        var parameters = new List<PythonParameterNode>
        {
            new PythonParameterNode(new IdentifierNode("self"))
        };

        foreach (var p in op.PathParameters)
        {
            var annotation = _typeMapper.MapType(p.Type);
            ScanForImports(p.Type, modelImports,
                datetimeImports, ref uuidNeeded, ref anyNeeded);
            parameters.Add(new PythonParameterNode(
                new IdentifierNode(PythonKeywords.Avoid(_namingConverter.ToSnakeCase(p.Name))),
                annotation));
        }

        if (op.RequestBody is { IsRequired: true })
        {
            var annotation = _typeMapper.MapType(op.RequestBody.Type);
            ScanForImports(op.RequestBody.Type, modelImports,
                datetimeImports, ref uuidNeeded, ref anyNeeded);
            parameters.Add(new PythonParameterNode(
                new IdentifierNode("body"),
                annotation));
        }

        foreach (var p in op.QueryParameters.Concat(op.HeaderParameters).OrderBy(parameter => !parameter.IsRequired))
        {
            var annotation = _typeMapper.MapType(p.Type);
            ScanForImports(p.Type, modelImports,
                datetimeImports, ref uuidNeeded, ref anyNeeded);
            string? defaultValue = null;
            if (!p.IsRequired)
            {
                annotation = AddNone(annotation);
                defaultValue = "None";
            }
            parameters.Add(new PythonParameterNode(
                new IdentifierNode(PythonKeywords.Avoid(_namingConverter.ToSnakeCase(p.Name))),
                annotation,
                defaultValue));
        }

        if (op.RequestBody is { IsRequired: false } optionalBody)
        {
            var annotation = AddNone(_typeMapper.MapType(optionalBody.Type));
            ScanForImports(optionalBody.Type, modelImports,
                datetimeImports, ref uuidNeeded, ref anyNeeded);
            parameters.Add(new PythonParameterNode(new IdentifierNode("body"), annotation, "None"));
        }

        var successfulResponses = op.Responses.Where(response => response.StatusCode.StartsWith("2", StringComparison.Ordinal)).ToArray();
        var successResponse = successfulResponses.FirstOrDefault(response => response.Type is not null);
        if (successResponse?.Type is { } selectedType && successfulResponses.Any(response =>
                response.Type is not null && !IrTypeComparer.AreSemanticallyEqual(selectedType, response.Type, ignorePresence: false)))
            throw new NotSupportedException($"Python operation '{op.MethodName}' declares incompatible successful response bodies.");
        TypeNode? returnType = null;
        string? responseModelName = null;
        if (successResponse?.Type != null)
        {
            returnType = _typeMapper.MapType(successResponse.Type);
            ScanForImports(successResponse.Type, modelImports,
                datetimeImports, ref uuidNeeded, ref anyNeeded);
            var referencedName = TryGetSimpleModelName(successResponse.Type);
            responseModelName = referencedName is not null && ModelsByName.ContainsKey(referencedName) ? referencedName : null;
            if (successfulResponses.Any(response => response.Type is null)) returnType = AddNone(returnType);
        }
        else
        {
            returnType = new SimpleTypeNode(new IdentifierNode("None"), isPrimitive: true);
        }

        var body = BuildMethodBody(op, isAsync, responseModelName, successResponse?.Type, methodName);

        return new PythonFunctionDeclarationNode(
            new IdentifierNode(methodName),
            parameters,
            returnType: returnType,
            body: body,
            isAsync: isAsync,
            documentation: BuildOperationDocstring(op));
    }

    /// <summary>Combines source documentation into a generated operation docstring.</summary>
    /// <param name="op">The operation containing the source summary and description.</param>
    /// <returns>The operation documentation, falling back to its HTTP method and path.</returns>
    private static CommentNode? BuildOperationDocstring(IrOperation op)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(op.Summary)) sb.Append(op.Summary!.Trim());
        if (!string.IsNullOrWhiteSpace(op.Description))
        {
            if (sb.Length > 0) sb.AppendLine().AppendLine();
            sb.Append(op.Description!.Trim());
        }
        if (sb.Length == 0) sb.Append($"{op.HttpMethod} {op.Path}");
        return new CommentNode(sb.ToString(), CommentKind.MultiLine);
    }

    /// <summary>Builds HTTPX wire calls and validates successful typed response bodies.</summary>
    /// <param name="op">The operation with source wire names and transport metadata.</param>
    /// <param name="isAsync">Whether to await the HTTPX operation.</param>
    /// <param name="responseModelName">The direct Pydantic response model name, if applicable.</param>
    /// <param name="responseType">The typed successful response body.</param>
    /// <param name="methodName">The generated method whose annotations provide container adapter types.</param>
    /// <returns>The generated function body lines.</returns>
    private IReadOnlyList<string> BuildMethodBody(
        IrOperation op,
        bool isAsync,
        string? responseModelName,
        IrType? responseType,
        string methodName)
    {
        var lines = new List<string>();

        var pathExpr = ToFStringPath(op.Path, op.PathParameters);
        var awaitKeyword = isAsync ? "await " : "";
        var httpMethod = op.HttpMethod.ToLowerInvariant();
        var requestArgs = new List<string> { pathExpr };

        if (op.QueryParameters.Count > 0)
        {
            lines.Add("params = {}");
            foreach (var p in op.QueryParameters)
            {
                var pyName = PythonKeywords.Avoid(_namingConverter.ToSnakeCase(p.Name));
                if (p.IsRequired)
                    lines.Add($"params[{JsonSerializer.Serialize(p.OriginalName)}] = {pyName}");
                else
                    lines.Add($"if {pyName} is not None: params[{JsonSerializer.Serialize(p.OriginalName)}] = {pyName}");
            }
            requestArgs.Add("params=params");
        }

        if (op.HeaderParameters.Count > 0)
        {
            lines.Add("headers = {}");
            foreach (var parameter in op.HeaderParameters)
            {
                var parameterName = PythonKeywords.Avoid(_namingConverter.ToSnakeCase(parameter.Name));
                var assignment = $"headers[{JsonSerializer.Serialize(parameter.OriginalName)}] = str({parameterName})";
                lines.Add(parameter.IsRequired ? assignment : $"if {parameterName} is not None: {assignment}");
            }
            requestArgs.Add("headers=headers");
        }

        if (op.RequestBody != null)
        {
            if (op.RequestBody.IsJson || (op.RequestBody.ContentType == null))
            {
                var bodyExpression = $"TypeAdapter(get_type_hints(self.{methodName})[\"body\"]).dump_python(body, mode=\"json\", by_alias=True, exclude_unset=True)";
                if (op.RequestBody.IsRequired) requestArgs.Add($"json={bodyExpression}");
                else
                {
                    lines.Add($"body_args = {{\"json\": {bodyExpression}}} if body is not None else {{}}");
                    requestArgs.Add("**body_args");
                }
            }
            else if (op.RequestBody.IsFormUrlEncoded)
            {
                requestArgs.Add("data=body");
            }
            else if (op.RequestBody.IsMultipartFormData)
            {
                requestArgs.Add("files=body");
            }
            else
            {
                requestArgs.Add("content=body");
            }
        }

        var argList = string.Join(", ", requestArgs);
        lines.Add($"response = {awaitKeyword}self._client.{httpMethod}({argList})");
        lines.Add("response.raise_for_status()");
        lines.Add("if response.status_code in (204, 205) or response.request.method == \"HEAD\": return None");

        if (responseType != null)
        {
            if (responseModelName != null)
            {
                if (responseType is IrNullableType || responseType is IrOptionalType)
                    lines.Add($"return None if response.json() is None else {responseModelName}.model_validate(response.json())");
                else lines.Add($"return {responseModelName}.model_validate(response.json())");
            }
            else if (responseType is IrPrimitiveType { PrimitiveKind: IrPrimitiveKind.Void or IrPrimitiveKind.Null })
            {
                lines.Add("return None");
            }
            else if (responseType is IrPrimitiveType { PrimitiveKind: IrPrimitiveKind.Binary or IrPrimitiveKind.ByteArray or IrPrimitiveKind.File })
            {
                lines.Add("return response.content");
            }
            else
            {
                lines.Add($"return TypeAdapter(get_type_hints(self.{methodName})[\"return\"]).validate_python(response.json())");
            }
        }
        else
        {
            lines.Add("return None");
        }
        return lines;
    }

    /// <summary>Finds a direct declaration reference beneath presence wrappers.</summary>
    /// <param name="type">The response type to inspect.</param>
    /// <returns>The referenced generated declaration name, or null for container and primitive types.</returns>
    private static string? TryGetSimpleModelName(IrType type) => type switch
    {
        IrModelReference r => r.ModelName,
        IrNullableType n => TryGetSimpleModelName(n.InnerType),
        IrOptionalType o => TryGetSimpleModelName(o.InnerType),
        _ => null
    };

    /// <summary>Adds nullability without repeating an existing None union arm.</summary>
    /// <param name="inner">The original type annotation.</param>
    /// <returns>The annotation accepting both its original values and None.</returns>
    private static TypeNode AddNone(TypeNode inner)
    {
        if (inner is UnionTypeNode u && u.Types.Any(t => t is SimpleTypeNode s && s.Name.Name == "None"))
            return inner;
        if (inner is UnionTypeNode existing)
            return new UnionTypeNode(existing.Types.Concat(new[] { (TypeNode)new SimpleTypeNode(new IdentifierNode("None"), isPrimitive: true) }).ToList());
        return new UnionTypeNode(new[] { inner, (TypeNode)new SimpleTypeNode(new IdentifierNode("None"), isPrimitive: true) });
    }

    /// <summary>Substitutes source path placeholders using safe generated parameter identifiers.</summary>
    /// <param name="path">The source wire path template.</param>
    /// <param name="pathParams">The operation's declared path parameters.</param>
    /// <returns>A Python f-string expression with escaped textual parameter values.</returns>
    private string ToFStringPath(string path, IReadOnlyList<IrParameter> pathParams)
    {
        var result = path;
        foreach (var p in pathParams)
        {
            var snake = PythonKeywords.Avoid(_namingConverter.ToSnakeCase(p.Name));
            var value = IrTypeQueries.UnwrapNullableOptional(p.Type) is IrPrimitiveType
                { PrimitiveKind: IrPrimitiveKind.Integer or IrPrimitiveKind.Long or IrPrimitiveKind.Number }
                ? snake : $"quote(str({snake}), safe='')";
            result = result.Replace("{" + p.OriginalName + "}", "{" + value + "}", StringComparison.Ordinal);
        }
        return $"f\"{result}\"";
    }

    /// <summary>Collects runtime and standard-library imports needed by request and response annotations.</summary>
    /// <param name="type">The schema type graph to traverse.</param>
    /// <param name="modelImports">The generated declaration imports receiving discovered references.</param>
    /// <param name="datetimeImports">The standard date and time imports.</param>
    /// <param name="uuidNeeded">Whether UUID annotations require an import.</param>
    /// <param name="anyNeeded">Whether unconstrained annotations require typing.Any.</param>
    private void ScanForImports(
        IrType type,
        SortedDictionary<string, string> modelImports,
        SortedSet<string> datetimeImports,
        ref bool uuidNeeded,
        ref bool anyNeeded)
    {
        switch (type)
        {
            case IrPrimitiveType p:
                switch (p.PrimitiveKind)
                {
                    case IrPrimitiveKind.Date: datetimeImports.Add("date"); break;
                    case IrPrimitiveKind.DateTime: datetimeImports.Add("datetime"); break;
                    case IrPrimitiveKind.Time: datetimeImports.Add("time"); break;
                    case IrPrimitiveKind.Uuid: uuidNeeded = true; break;
                    case IrPrimitiveKind.Any: anyNeeded = true; break;
                }
                break;
            case IrModelReference r:
                if (PythonDeclarationImports.TryGetModulePath(DocumentContext, _namingConverter, r.ModelName, "..models.", out var module))
                    modelImports[r.ModelName] = module;
                break;
            case IrArrayType a:
                ScanForImports(a.ElementType, modelImports, datetimeImports, ref uuidNeeded, ref anyNeeded);
                break;
            case IrDictionaryType d:
                ScanForImports(d.KeyType, modelImports, datetimeImports, ref uuidNeeded, ref anyNeeded);
                ScanForImports(d.ValueType, modelImports, datetimeImports, ref uuidNeeded, ref anyNeeded);
                break;
            case IrNullableType n:
                ScanForImports(n.InnerType, modelImports, datetimeImports, ref uuidNeeded, ref anyNeeded);
                break;
            case IrOptionalType o:
                ScanForImports(o.InnerType, modelImports, datetimeImports, ref uuidNeeded, ref anyNeeded);
                break;
            case IrUnionType u:
                foreach (var t in u.Types) ScanForImports(t, modelImports, datetimeImports, ref uuidNeeded, ref anyNeeded);
                break;
            case IrIntersectionType i:
                foreach (var t in i.Types) ScanForImports(t, modelImports, datetimeImports, ref uuidNeeded, ref anyNeeded);
                break;
        }
    }

    /// <summary>Converts a grouped client name into a Python API class identifier.</summary>
    /// <param name="clientName">The source client group name.</param>
    /// <returns>A PascalCase identifier ending in Api.</returns>
    private string ToClassName(string clientName)
    {
        var pascal = _namingConverter.ToPascalCase(clientName);
        return pascal.EndsWith("Api", StringComparison.Ordinal) ? pascal : pascal + "Api";
    }
}
