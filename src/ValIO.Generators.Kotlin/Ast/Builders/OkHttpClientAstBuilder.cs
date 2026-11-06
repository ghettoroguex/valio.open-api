using System.Text;
using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Core.Interfaces;
using ValIO.Core.IR;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Generators.Kotlin.Ast.Nodes;

namespace ValIO.Generators.Kotlin.Ast.Builders;

/// <summary>
/// Builds an OkHttp + coroutines service class for one <see cref="IrClient"/>.
/// </summary>
public sealed class OkHttpClientAstBuilder
{
    /// <summary>Converts operation and argument names into Kotlin identifiers.</summary>
    private readonly INamingConverter _namingConverter;
    /// <summary>Maps operation payload types into Kotlin values.</summary>
    private readonly KotlinIrTypeMapper _typeMapper;

    /// <summary>Creates the client builder using the shared naming and type services.</summary>
    /// <param name="namingConverter">The source identifier converter.</param>
    /// <param name="typeMapper">The Kotlin type mapper.</param>
    public OkHttpClientAstBuilder(INamingConverter namingConverter, KotlinIrTypeMapper typeMapper)
    {
        _namingConverter = namingConverter;
        _typeMapper = typeMapper;
    }

    /// <summary>Builds a caller-owned OkHttp service with coroutine-aware native request cancellation.</summary>
    /// <param name="client">The operation group.</param>
    /// <param name="apiPackage">The client declaration package.</param>
    /// <param name="modelsPackage">The generated model package.</param>
    /// <param name="knownModelNames">The finalized model and enum names.</param>
    /// <returns>The service and its cancellation adapter.</returns>
    public KotlinFileNode BuildFile(
        IrClient client,
        string apiPackage,
        string modelsPackage,
        IReadOnlySet<string> knownModelNames)
    {
        var className = ToClassName(client.Name);
        var importScan = new KotlinTypeImportScanner.Result();

        // Primary-constructor properties.
        var ctorProps = new List<KotlinPropertyNode>
        {
            new KotlinPropertyNode(
                new IdentifierNode("client"),
                new SimpleTypeNode(new IdentifierNode("OkHttpClient"), isPrimitive: true),
                visibility: "private",
                isReadOnly: true,
                isConstructorParameter: true),
            new KotlinPropertyNode(
                new IdentifierNode("baseUrl"),
                new SimpleTypeNode(new IdentifierNode("HttpUrl"), isPrimitive: true),
                visibility: "private",
                isReadOnly: true,
                isConstructorParameter: true),
            new KotlinPropertyNode(
                new IdentifierNode("json"),
                new SimpleTypeNode(new IdentifierNode("Json"), isPrimitive: true),
                visibility: "private",
                isReadOnly: true,
                isConstructorParameter: true),
        };

        var methods = new List<DeclarationNode>();
        foreach (var op in client.Operations)
        {
            methods.Add(BuildOperationMethod(op, importScan));
        }
        methods.Add(KotlinCancellableCallAstBuilder.Build());

        var classNode = new KotlinClassDeclarationNode(
            new IdentifierNode(className),
            ctorProps,
            methods,
            annotations: null,
            visibility: null,
            documentation: new CommentNode($"HTTP client for {client.Name}.", CommentKind.MultiLine));

        // Standard imports: always OkHttp + coroutines + kotlinx.serialization.json.
        var imports = new List<KotlinImportNode>
        {
            new KotlinImportNode("kotlinx.coroutines.Dispatchers"),
            new KotlinImportNode("kotlinx.coroutines.withContext"),
            new KotlinImportNode("kotlinx.coroutines.CancellableContinuation"),
            new KotlinImportNode("kotlinx.coroutines.suspendCancellableCoroutine"),
            new KotlinImportNode("kotlin.coroutines.CoroutineContext"),
            new KotlinImportNode("kotlinx.serialization.json.Json"),
            new KotlinImportNode("java.io.IOException"),
            new KotlinImportNode("okhttp3.Call"),
            new KotlinImportNode("okhttp3.Callback"),
            new KotlinImportNode("okhttp3.Response"),
            new KotlinImportNode("okhttp3.ResponseBody.Companion.toResponseBody"),
            new KotlinImportNode("okhttp3.HttpUrl"),
            new KotlinImportNode("okhttp3.MediaType.Companion.toMediaType"),
            new KotlinImportNode("okhttp3.OkHttpClient"),
            new KotlinImportNode("okhttp3.Request"),
            new KotlinImportNode("okhttp3.RequestBody.Companion.toRequestBody"),
        };
        foreach (var stdImport in importScan.StandardImports)
            imports.Add(new KotlinImportNode(stdImport));
        foreach (var modelName in importScan.ModelReferences)
        {
            // modelName carries the affix and is matched against the affixed known names.
            if (!knownModelNames.Contains(modelName)) continue;
            // Kotlin imports resolve by fully-qualified CLASS name (package + affixed type name),
            // independent of the .kt file name — so the import uses the affixed identifier.
            imports.Add(new KotlinImportNode($"{modelsPackage}.{modelName}"));
        }

        return new KotlinFileNode(apiPackage, declarations: [classNode], imports: imports);
    }

    /// <summary>Builds a public suspend operation preserving its source payload types.</summary>
    /// <param name="op">The source operation.</param>
    /// <param name="importScan">The recursive type imports for this client.</param>
    /// <returns>The operation declaration.</returns>
    private KotlinFunctionDeclarationNode BuildOperationMethod(
        IrOperation op,
        KotlinTypeImportScanner.Result importScan)
    {
        var methodName = KotlinReservedNames.Avoid(_namingConverter.ToCamelCase(op.MethodName));
        var parameters = new List<KotlinParameterNode>();

        foreach (var p in op.PathParameters)
        {
            KotlinTypeImportScanner.Walk(p.Type, importScan);
            parameters.Add(new KotlinParameterNode(
                new IdentifierNode(KotlinReservedNames.Avoid(_namingConverter.ToCamelCase(p.Name))),
                _typeMapper.MapType(p.Type)));
        }

        if (op.RequestBody != null)
        {
            KotlinTypeImportScanner.Walk(op.RequestBody.Type, importScan);
            parameters.Add(new KotlinParameterNode(
                new IdentifierNode("body"),
                _typeMapper.MapType(op.RequestBody.Type)));
        }

        foreach (var p in op.QueryParameters)
        {
            KotlinTypeImportScanner.Walk(p.Type, importScan);
            var annotation = _typeMapper.MapType(p.Type);
            string? defaultValue = null;
            if (!p.IsRequired)
            {
                annotation = annotation is NullableTypeNode ? annotation : new NullableTypeNode(annotation);
                defaultValue = "null";
            }
            parameters.Add(new KotlinParameterNode(
                new IdentifierNode(KotlinReservedNames.Avoid(_namingConverter.ToCamelCase(p.Name))),
                annotation,
                defaultValue));
        }

        // Pick the first 2xx response as success.
        var successResponse = op.Responses.FirstOrDefault(r => r.StatusCode.StartsWith("2", StringComparison.Ordinal));
        TypeNode returnType;
        string? responseModelName = null;
        string? responseModelNameForList = null;
        bool isVoidReturn = false;
        bool isBinaryReturn = false;
        if (successResponse?.Type != null)
        {
            KotlinTypeImportScanner.Walk(successResponse.Type, importScan);
            returnType = _typeMapper.MapType(successResponse.Type);
            responseModelName = TryGetSimpleModelName(successResponse.Type);
            responseModelNameForList = TryGetArrayOfSimpleModelName(successResponse.Type);
            isBinaryReturn = successResponse.Type is IrPrimitiveType { PrimitiveKind: IrPrimitiveKind.Binary or IrPrimitiveKind.ByteArray or IrPrimitiveKind.File };
        }
        else
        {
            returnType = new SimpleTypeNode(new IdentifierNode("Unit"), isPrimitive: true);
            isVoidReturn = true;
        }

        var body = BuildMethodBody(op, responseModelName, responseModelNameForList, isVoidReturn, isBinaryReturn);

        return new KotlinFunctionDeclarationNode(
            new IdentifierNode(methodName),
            parameters,
            returnType,
            body,
            isSuspend: true,
            annotations: null,
            visibility: null,
            documentation: BuildOperationDocstring(op),
            isExpressionBody: true);
    }

    /// <summary>Preserves operation descriptions in generated Kotlin documentation.</summary>
    /// <param name="op">The source operation.</param>
    /// <returns>The operation comment.</returns>
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

    /// <summary>Builds the legacy operation body using the structured cancellation adapter for transport execution.</summary>
    /// <param name="op">The source operation.</param>
    /// <param name="responseModelName">The direct response model name.</param>
    /// <param name="responseModelNameForList">The response collection element model name.</param>
    /// <param name="isVoidReturn">Whether the operation has no response payload.</param>
    /// <param name="isBinaryReturn">Whether the response is represented by raw bytes.</param>
    /// <returns>The operation body lines consumed by the legacy AST contract.</returns>
    private IReadOnlyList<string> BuildMethodBody(
        IrOperation op,
        string? responseModelName,
        string? responseModelNameForList,
        bool isVoidReturn,
        bool isBinaryReturn)
    {
        // Lines are emitted WITHOUT outer indentation — the renderer adds the
        // function-body indent. Inside the withContext lambda we still need a
        // one-level offset relative to the outer function body, so we use a
        // single 4-space indent within those lines.
        const string Inner = "    ";

        var lines = new List<string>
        {
            "withContext(Dispatchers.IO) {",
            $"{Inner}val urlBuilder = baseUrl.newBuilder()",
        };

        // Path template — replace {name} with ${name} (Kotlin string templates).
        var pathTemplate = op.Path;
        foreach (var p in op.PathParameters)
        {
            var camel = KotlinReservedNames.Avoid(_namingConverter.ToCamelCase(p.Name));
            pathTemplate = pathTemplate.Replace("{" + p.Name + "}", "${" + camel + "}", StringComparison.Ordinal);
        }
        var trimmedPath = pathTemplate.TrimStart('/');
        lines.Add($"{Inner}urlBuilder.addPathSegments(\"{EscapeStringLiteral(trimmedPath)}\")");

        // Query parameters.
        foreach (var p in op.QueryParameters)
        {
            var camel = KotlinReservedNames.Avoid(_namingConverter.ToCamelCase(p.Name));
            if (p.IsRequired)
                lines.Add($"{Inner}urlBuilder.addQueryParameter(\"{EscapeStringLiteral(p.Name)}\", {camel}.toString())");
            else
                lines.Add($"{Inner}if ({camel} != null) urlBuilder.addQueryParameter(\"{EscapeStringLiteral(p.Name)}\", {camel}.toString())");
        }

        lines.Add($"{Inner}val url = urlBuilder.build()");
        lines.Add($"{Inner}val requestBuilder = Request.Builder().url(url)");

        var httpMethod = op.HttpMethod.ToUpperInvariant();
        if (op.RequestBody != null)
        {
            if (op.RequestBody.IsJson)
            {
                var bodyModelName = TryGetSimpleModelName(op.RequestBody.Type);
                if (bodyModelName != null)
                {
                    lines.Add($"{Inner}val jsonBody = json.encodeToString({bodyModelName}.serializer(), body)");
                    lines.Add($"{Inner}val requestBody = jsonBody.toRequestBody(\"application/json\".toMediaType())");
                }
                else
                {
                    // Fallback: rely on the polymorphic default serializer.
                    lines.Add($"{Inner}val jsonBody = json.encodeToString(body)");
                    lines.Add($"{Inner}val requestBody = jsonBody.toRequestBody(\"application/json\".toMediaType())");
                }
            }
            else
            {
                // Non-JSON body codecs retain their existing representation in this cancellation slice.
                lines.Add($"{Inner}val requestBody = (body?.toString() ?: \"\").toRequestBody(\"application/octet-stream\".toMediaType())");
            }
            lines.Add($"{Inner}requestBuilder.method(\"{httpMethod}\", requestBody)");
        }
        else if (httpMethod is "POST" or "PUT" or "PATCH" or "DELETE")
        {
            lines.Add($"{Inner}requestBuilder.method(\"{httpMethod}\", \"\".toRequestBody(\"application/json\".toMediaType()))");
        }
        else
        {
            lines.Add($"{Inner}requestBuilder.method(\"{httpMethod}\", null)");
        }

        lines.Add($"{Inner}val request = requestBuilder.build()");
        lines.Add($"{Inner}{KotlinCancellableCallAstBuilder.MethodName}(request).use {{ response ->");
        lines.Add($"{Inner}{Inner}if (!response.isSuccessful) error(\"HTTP ${{response.code}}: ${{response.message}}\")");

        if (isVoidReturn)
        {
            lines.Add($"{Inner}{Inner}Unit");
        }
        else if (isBinaryReturn)
        {
            lines.Add($"{Inner}{Inner}response.body?.bytes() ?: ByteArray(0)");
        }
        else if (responseModelName != null)
        {
            lines.Add($"{Inner}{Inner}val raw = response.body?.string() ?: error(\"Empty body\")");
            lines.Add($"{Inner}{Inner}json.decodeFromString({responseModelName}.serializer(), raw)");
        }
        else if (responseModelNameForList != null)
        {
            lines.Add($"{Inner}{Inner}val raw = response.body?.string() ?: error(\"Empty body\")");
            lines.Add($"{Inner}{Inner}json.decodeFromString(kotlinx.serialization.builtins.ListSerializer({responseModelNameForList}.serializer()), raw)");
        }
        else
        {
            // Plain primitive — caller may need to parse the body manually.
            lines.Add($"{Inner}{Inner}response.body?.string() ?: error(\"Empty body\")");
        }

        lines.Add($"{Inner}}}");
        lines.Add("}");
        return lines;
    }

    /// <summary>Finds a model reference beneath nullable or optional wrappers.</summary>
    /// <param name="type">The response type.</param>
    /// <returns>The model name, or null for another value shape.</returns>
    private static string? TryGetSimpleModelName(IrType type) => type switch
    {
        IrModelReference r => r.ModelName,
        IrNullableType n   => TryGetSimpleModelName(n.InnerType),
        IrOptionalType o   => TryGetSimpleModelName(o.InnerType),
        _ => null,
    };

    /// <summary>Returns the inner model name when <paramref name="type"/> is <c>List&lt;Model&gt;</c>.</summary>
    /// <param name="type">The response type.</param>
    /// <returns>The model collection element name, or null.</returns>
    private static string? TryGetArrayOfSimpleModelName(IrType type) => type switch
    {
        IrArrayType a       => TryGetSimpleModelName(a.ElementType),
        IrNullableType n    => TryGetArrayOfSimpleModelName(n.InnerType),
        IrOptionalType o    => TryGetArrayOfSimpleModelName(o.InnerType),
        _ => null,
    };

    /// <summary>Derives the generated service class identifier.</summary>
    /// <param name="clientName">The source group name.</param>
    /// <returns>The Kotlin API class name.</returns>
    private string ToClassName(string clientName)
    {
        var pascal = _namingConverter.ToPascalCase(clientName);
        return pascal.EndsWith("Api", StringComparison.Ordinal) ? pascal : pascal + "Api";
    }

    /// <summary>Escapes source values embedded in legacy Kotlin string literals.</summary>
    /// <param name="s">The unescaped value.</param>
    /// <returns>The escaped literal contents.</returns>
    private static string EscapeStringLiteral(string s) =>
        s.Replace("\\", "\\\\", StringComparison.Ordinal)
         .Replace("\"", "\\\"", StringComparison.Ordinal);
}
