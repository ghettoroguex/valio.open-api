using System.Collections.Immutable;
using System.Text;
using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Expressions;
using ValIO.Core.Ast.Nodes.Statements;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Core.Interfaces;
using ValIO.Core.IR;
using ValIO.Core.IR.Types;
using ValIO.Generators.Java.Ast.Nodes;

namespace ValIO.Generators.Java.Ast.Builders;

/// <summary>
/// Builds a typed OkHttp client using caller-owned HTTP and JSON pipelines.
/// </summary>
public sealed class OkHttpClientAstBuilder
{
    /// <summary>Converts operation and parameter identifiers to Java naming conventions.</summary>
    private readonly INamingConverter _namingConverter;
    /// <summary>Maps shared IR payloads to Java types.</summary>
    private readonly JavaIrTypeMapper _typeMapper;

    /// <summary>Creates a client builder with shared naming and type mapping services.</summary>
    /// <param name="namingConverter">The Java identifier converter.</param>
    /// <param name="typeMapper">The Java type mapper.</param>
    public OkHttpClientAstBuilder(INamingConverter namingConverter, JavaIrTypeMapper typeMapper)
    {
        _namingConverter = namingConverter ?? throw new ArgumentNullException(nameof(namingConverter));
        _typeMapper = typeMapper ?? throw new ArgumentNullException(nameof(typeMapper));
    }

    /// <summary>Builds a client whose injected OkHttpClient retains interceptors, authentication and connection pooling.</summary>
    /// <param name="client">The operation group.</param>
    /// <param name="rootPackage">The root Java package.</param>
    /// <returns>The generated source AST.</returns>
    public JavaSourceFileNode BuildSourceFile(IrClient client, string rootPackage)
    {
        var pkg = NormalizePackage($"{rootPackage}.api");
        var className = BuildClassName(client.Name);

        var importTargets = new SortedSet<string>(StringComparer.Ordinal)
        {
            "com.fasterxml.jackson.databind.ObjectMapper",
            "okhttp3.HttpUrl",
            "okhttp3.OkHttpClient",
            "okhttp3.Request",
            "okhttp3.Response",
            "java.io.IOException",
            "java.util.function.Consumer",
            "okhttp3.Call",
        };
        var modelsPackage = NormalizePackage($"{rootPackage}.models");

        // Fields.
        var clientField = new PropertyDeclarationNode(
            name: new IdentifierNode("client"),
            type: new SimpleTypeNode(new IdentifierNode("OkHttpClient")),
            isReadOnly: true);
        var baseUrlField = new PropertyDeclarationNode(
            name: new IdentifierNode("baseUrl"),
            type: new SimpleTypeNode(new IdentifierNode("HttpUrl")),
            isReadOnly: true);
        var mapperField = new PropertyDeclarationNode(
            name: new IdentifierNode("mapper"),
            type: new SimpleTypeNode(new IdentifierNode("ObjectMapper")),
            isReadOnly: true);

        // Constructor.
        var ctorParams = new[]
        {
            new ParameterDeclarationNode(new IdentifierNode("client"),  new SimpleTypeNode(new IdentifierNode("OkHttpClient"))),
            new ParameterDeclarationNode(new IdentifierNode("baseUrl"), new SimpleTypeNode(new IdentifierNode("HttpUrl"))),
            new ParameterDeclarationNode(new IdentifierNode("mapper"),  new SimpleTypeNode(new IdentifierNode("ObjectMapper"))),
        };
        var ctorBody = new BlockNode(
        [
            new ExpressionStatementNode(AssignThis("client",  "client")),
            new ExpressionStatementNode(AssignThis("baseUrl", "baseUrl")),
            new ExpressionStatementNode(AssignThis("mapper",  "mapper")),
        ]);
        var ctor = new MethodDeclarationNode(
            name: new IdentifierNode(className),
            returnType: null,
            parameters: ctorParams,
            body: ctorBody,
            accessModifier: AccessModifier.Public);

        var methods = new List<DeclarationNode>();
        foreach (var op in client.Operations)
        {
            methods.Add(BuildOperationMethod(op, importTargets, modelsPackage));
            methods.Add(BuildOperationMethod(op, importTargets, modelsPackage, exposeCall: true));
        }

        var cls = new JavaClassDeclarationNode(
            name: new IdentifierNode(className),
            isFinal: true,
            fields: new[] { clientField, baseUrlField, mapperField },
            constructor: ctor,
            members: methods);

        var imports = importTargets
            .Select(t => new JavaImportNode(t))
            .ToImmutableList();

        return new JavaSourceFileNode(
            package: pkg,
            imports: imports,
            declarations: new DeclarationNode[] { cls }.ToImmutableList());
    }

    /// <summary>Builds one operation with checked HTTP status and typed JSON deserialization.</summary>
    /// <param name="op">The source operation.</param>
    /// <param name="importTargets">The imports accumulated for this source file.</param>
    /// <param name="modelsPackage">The generated declaration package.</param>
    /// <param name="exposeCall">Whether a callback exposes the native Call before transport execution for cancellation.</param>
    /// <returns>The generated method declaration.</returns>
    private MethodDeclarationNode BuildOperationMethod(IrOperation op, ISet<string> importTargets, string modelsPackage, bool exposeCall = false)
    {
        var methodName = _namingConverter.ToCamelCase(op.MethodName);

        var parameters = new List<ParameterDeclarationNode>();
        foreach (var p in op.PathParameters)
            parameters.Add(BuildParam(p, importTargets, modelsPackage));
        foreach (var p in op.QueryParameters)
            parameters.Add(BuildParam(p, importTargets, modelsPackage));
        foreach (var p in op.HeaderParameters)
            parameters.Add(BuildParam(p, importTargets, modelsPackage));
        if (op.RequestBody != null)
        {
            var bodyType = _typeMapper.MapType(op.RequestBody.Type, preferUnboxed: false);
            foreach (var im in _typeMapper.CollectImports(op.RequestBody.Type)) importTargets.Add(im);
            CollectModelImports(op.RequestBody.Type, importTargets, modelsPackage);
            parameters.Add(new ParameterDeclarationNode(
                name: new IdentifierNode("body"),
                type: bodyType,
                isRequired: op.RequestBody.IsRequired));
        }

        var returnType = ResolveReturnType(op, importTargets, modelsPackage, out var returnTypeToken);
        if (exposeCall)
            parameters.Add(new ParameterDeclarationNode(new IdentifierNode("callReady"),
                new GenericTypeNode(new IdentifierNode("Consumer"), [new SimpleTypeNode(new IdentifierNode("Call"))])));

        var statements = new List<StatementNode>();

        // HttpUrl url = baseUrl.newBuilder().addPathSegments(...).build();
        var urlInit = BuildUrlExpression(op, parameters);
        statements.Add(new VariableDeclarationStatementNode(
            name: new IdentifierNode("url"),
            type: new SimpleTypeNode(new IdentifierNode("HttpUrl")),
            initializer: urlInit));

        // Request request = new Request.Builder().url(url).{method}(...).build();
        var requestInit = BuildRequestExpression(op, hasBody: op.RequestBody != null);
        statements.Add(new VariableDeclarationStatementNode(
            name: new IdentifierNode("request"),
            type: new SimpleTypeNode(new IdentifierNode("Request")),
            initializer: requestInit));

        // try (Response response = client.newCall(request).execute()) { ... }
        var responseInitExpr = new CallExpressionNode(
            target: new MemberAccessExpressionNode(
                @object: new CallExpressionNode(
                    target: new MemberAccessExpressionNode(
                        @object: new IdentifierExpressionNode(new IdentifierNode("client")),
                        member: new IdentifierNode("newCall")),
                    arguments: new ExpressionNode[] { new IdentifierExpressionNode(new IdentifierNode("request")) }),
                member: new IdentifierNode("execute")),
            arguments: ImmutableList<ExpressionNode>.Empty);
        if (exposeCall)
        {
            statements.Add(new VariableDeclarationStatementNode(new IdentifierNode("call"),
                new SimpleTypeNode(new IdentifierNode("Call")),
                new CallExpressionNode(new MemberAccessExpressionNode(new IdentifierExpressionNode(new IdentifierNode("client")), new IdentifierNode("newCall")),
                    [new IdentifierExpressionNode(new IdentifierNode("request"))])));
            statements.Add(new IfStatementNode(new BinaryExpressionNode(new IdentifierExpressionNode(new IdentifierNode("callReady")),
                BinaryOperator.NotEqual, new LiteralExpressionNode(null, LiteralKind.Null)), new BlockNode([
                    new ExpressionStatementNode(new CallExpressionNode(new MemberAccessExpressionNode(new IdentifierExpressionNode(new IdentifierNode("callReady")), new IdentifierNode("accept")),
                        [new IdentifierExpressionNode(new IdentifierNode("call"))])),
                ])));
            statements.Add(new IfStatementNode(new CallExpressionNode(new MemberAccessExpressionNode(
                new IdentifierExpressionNode(new IdentifierNode("call")), new IdentifierNode("isCanceled"))), new BlockNode([
                    new ThrowStatementNode(new CallExpressionNode(new IdentifierExpressionNode(new IdentifierNode("IOException")),
                        [new LiteralExpressionNode("Canceled", LiteralKind.String)], isNewExpression: true)),
                ])));
        }
        var responseResource = exposeCall ? "Response response = call.execute()" : $"Response response = {ExpressionToString(responseInitExpr)}";

        var status = new CallExpressionNode(new MemberAccessExpressionNode(
            new IdentifierExpressionNode(new IdentifierNode("response")), new IdentifierNode("isSuccessful")));
        var tryStatements = new List<StatementNode>
        {
            new IfStatementNode(new BinaryExpressionNode(status, BinaryOperator.Equal,
                new LiteralExpressionNode(false, LiteralKind.Boolean)), new BlockNode([
                    new ThrowStatementNode(new CallExpressionNode(new IdentifierExpressionNode(new IdentifierNode("IOException")),
                        [new BinaryExpressionNode(new LiteralExpressionNode("HTTP ", LiteralKind.String), BinaryOperator.Add,
                            new CallExpressionNode(new MemberAccessExpressionNode(new IdentifierExpressionNode(new IdentifierNode("response")), new IdentifierNode("code"))))],
                        isNewExpression: true)),
                ])),
        };
        if (returnTypeToken != null)
        {
            tryStatements.Add(new IfStatementNode(new BinaryExpressionNode(
                new CallExpressionNode(new MemberAccessExpressionNode(new IdentifierExpressionNode(new IdentifierNode("response")), new IdentifierNode("body"))),
                BinaryOperator.Equal, new LiteralExpressionNode(null, LiteralKind.Null)), new BlockNode([
                    new ThrowStatementNode(new CallExpressionNode(new IdentifierExpressionNode(new IdentifierNode("IOException")),
                        [new LiteralExpressionNode("The successful HTTP response did not contain a payload.", LiteralKind.String)], isNewExpression: true)),
                ])));
            var readValueCall = new CallExpressionNode(
                target: new MemberAccessExpressionNode(
                    @object: new IdentifierExpressionNode(new IdentifierNode("mapper")),
                    member: new IdentifierNode("readValue")),
                arguments: new ExpressionNode[]
                {
                    new CallExpressionNode(
                        target: new MemberAccessExpressionNode(
                            @object: new CallExpressionNode(
                                target: new MemberAccessExpressionNode(
                                    @object: new IdentifierExpressionNode(new IdentifierNode("response")),
                                    member: new IdentifierNode("body")),
                                arguments: ImmutableList<ExpressionNode>.Empty),
                            member: new IdentifierNode("byteStream")),
                        arguments: ImmutableList<ExpressionNode>.Empty),
                    returnTypeToken,
                });
            tryStatements.Add(new ReturnStatementNode(readValueCall));
        }
        else
        {
            // response.close(); is implicit because of try-with-resources; just emit empty body / return.
        }
        var tryBlock = new BlockNode(tryStatements);
        var tryMetadata = new Dictionary<string, object>
        {
            ["withResources"] = new[] { responseResource }
        };
        var tryCatch = new TryCatchStatementNode(
            tryBlock: tryBlock,
            catchClauses: null,
            finallyBlock: null,
            documentation: null,
            metadata: tryMetadata);
        statements.Add(tryCatch);

        var body = new BlockNode(statements);

        var docLines = new List<string>();
        if (!string.IsNullOrWhiteSpace(op.Summary))
            docLines.Add(op.Summary!);
        if (!string.IsNullOrWhiteSpace(op.Description))
        {
            if (docLines.Count > 0) docLines.Add("");
            docLines.Add(op.Description!);
        }
        if (exposeCall)
            docLines.Add("The callReady callback receives the native OkHttp Call before sending; retain it and invoke cancel() to abort this request. The injected client retains its interceptors and authenticator.");
        var doc = docLines.Count > 0 ? new CommentNode(string.Join("\n", docLines), CommentKind.MultiLine) : null;

        var methodMetadata = new Dictionary<string, object>
        {
            ["throws"] = (IReadOnlyList<string>)new[] { "IOException" },
        };

        return new MethodDeclarationNode(
            name: new IdentifierNode(methodName),
            returnType: returnType ?? new SimpleTypeNode(new IdentifierNode("void"), isPrimitive: true),
            parameters: parameters,
            body: body,
            accessModifier: AccessModifier.Public,
            documentation: doc,
            metadata: methodMetadata);
    }

    /// <summary>Builds a Java method parameter and its recursive declaration imports.</summary>
    /// <param name="p">The source parameter.</param>
    /// <param name="importTargets">The source file imports.</param>
    /// <param name="modelsPackage">The generated declaration package.</param>
    /// <returns>The mapped method parameter.</returns>
    private ParameterDeclarationNode BuildParam(IrParameter p, ISet<string> importTargets, string modelsPackage)
    {
        var typeNode = _typeMapper.MapType(p.Type, preferUnboxed: p.IsRequired);
        foreach (var im in _typeMapper.CollectImports(p.Type)) importTargets.Add(im);
        CollectModelImports(p.Type, importTargets, modelsPackage);
        var name = _namingConverter.ToCamelCase(p.Name);
        if (JavaReservedWords.Reserved.Contains(name)) name += "_";
        return new ParameterDeclarationNode(
            name: new IdentifierNode(name),
            type: typeNode,
            isRequired: p.IsRequired);
    }

    /// <summary>Resolves the success payload without erasing generic collection element types.</summary>
    /// <param name="op">The source operation.</param>
    /// <param name="importTargets">The source file imports.</param>
    /// <param name="modelsPackage">The generated declaration package.</param>
    /// <param name="returnClassToken">The Jackson class or JavaType expression used for deserialization.</param>
    /// <returns>The Java payload type or void.</returns>
    private TypeNode? ResolveReturnType(IrOperation op, ISet<string> importTargets, string modelsPackage, out ExpressionNode? returnClassToken)
    {
        returnClassToken = null;
        // Pick the first 2xx success response with a payload; else void.
        var success = op.Responses.FirstOrDefault(r =>
            (int.TryParse(r.StatusCode, out var code) && code >= 200 && code < 300 || r.StatusCode == "2XX") && r.Type != null);
        if (success?.Type == null)
        {
            return new SimpleTypeNode(new IdentifierNode("void"), isPrimitive: true);
        }

        var typeNode = _typeMapper.MapType(success.Type, preferUnboxed: false);
        foreach (var im in _typeMapper.CollectImports(success.Type)) importTargets.Add(im);

        CollectModelImports(success.Type, importTargets, modelsPackage);
        returnClassToken = typeNode is SimpleTypeNode simple
            ? new IdentifierExpressionNode(new IdentifierNode(simple.Name.Name + ".class"))
            : BuildJacksonType(typeNode);

        return typeNode;
    }

    /// <summary>Builds a Jackson JavaType recursively so list and map payloads remain typed.</summary>
    /// <param name="type">The mapped Java type.</param>
    /// <returns>The runtime Jackson type descriptor.</returns>
    private static ExpressionNode BuildJacksonType(TypeNode type)
    {
        var factory = new CallExpressionNode(new MemberAccessExpressionNode(new IdentifierExpressionNode(new IdentifierNode("mapper")), new IdentifierNode("getTypeFactory")));
        if (type is GenericTypeNode generic)
        {
            var arguments = new List<ExpressionNode> { new IdentifierExpressionNode(new IdentifierNode(generic.Name.Name + ".class")) };
            arguments.AddRange(generic.TypeArguments.Select(BuildJacksonType));
            return new CallExpressionNode(new MemberAccessExpressionNode(factory, new IdentifierNode("constructParametricType")), arguments);
        }
        if (type is SimpleTypeNode simple)
            return new CallExpressionNode(new MemberAccessExpressionNode(factory, new IdentifierNode("constructType")),
                [new IdentifierExpressionNode(new IdentifierNode(simple.Name.Name + ".class"))]);
        throw new NotSupportedException($"Java response type '{type.GetType().Name}' cannot be deserialized safely.");
    }

    /// <summary>Collects declaration imports nested in optional values and containers.</summary>
    /// <param name="type">The IR type to inspect.</param>
    /// <param name="imports">The source file imports.</param>
    /// <param name="package">The generated declaration package.</param>
    private static void CollectModelImports(IrType type, ISet<string> imports, string package)
    {
        switch (type)
        {
            case IrModelReference reference: imports.Add($"{package}.{reference.ModelName}"); break;
            case IrArrayType array: CollectModelImports(array.ElementType, imports, package); break;
            case IrDictionaryType dictionary:
                CollectModelImports(dictionary.KeyType, imports, package);
                CollectModelImports(dictionary.ValueType, imports, package);
                break;
            case IrNullableType nullable: CollectModelImports(nullable.InnerType, imports, package); break;
            case IrOptionalType optional: CollectModelImports(optional.InnerType, imports, package); break;
        }
    }

    /// <summary>Builds a member access to a generated client field.</summary>
    /// <param name="field">The field identifier.</param>
    /// <returns>The this-qualified field expression.</returns>
    private static MemberAccessExpressionNode BuildThis(string field) =>
        new MemberAccessExpressionNode(
            @object: new IdentifierExpressionNode(new IdentifierNode("this")),
            member: new IdentifierNode(field));

    /// <summary>Builds a constructor assignment to an injected dependency.</summary>
    /// <param name="field">The destination field.</param>
    /// <param name="value">The constructor parameter.</param>
    /// <returns>The assignment expression.</returns>
    private static BinaryExpressionNode AssignThis(string field, string value) =>
        new BinaryExpressionNode(
            left: BuildThis(field),
            @operator: BinaryOperator.Assign,
            right: new IdentifierExpressionNode(new IdentifierNode(value)));

    /// <summary>Builds the request URL while preserving original query names.</summary>
    /// <param name="op">The source operation.</param>
    /// <param name="parameters">The mapped operation parameters.</param>
    /// <returns>The final HttpUrl expression.</returns>
    private ExpressionNode BuildUrlExpression(IrOperation op, IReadOnlyList<ParameterDeclarationNode> parameters)
    {
        // baseUrl.newBuilder().addPathSegments(<path expr>).addQueryParameter(...)...build()
        ExpressionNode chain = new CallExpressionNode(
            target: new MemberAccessExpressionNode(
                @object: new IdentifierExpressionNode(new IdentifierNode("baseUrl")),
                member: new IdentifierNode("newBuilder")),
            arguments: ImmutableList<ExpressionNode>.Empty);

        foreach (var segment in op.Path.TrimStart('/').Split('/'))
        {
            chain = new CallExpressionNode(
                new MemberAccessExpressionNode(chain, new IdentifierNode("addPathSegment")),
                [BuildPathExpression(op with { Path = segment })]);
        }

        foreach (var q in op.QueryParameters)
        {
            var paramName = _namingConverter.ToCamelCase(q.Name);
            if (JavaReservedWords.Reserved.Contains(paramName)) paramName += "_";
            ExpressionNode valueExpr = new IdentifierExpressionNode(new IdentifierNode(paramName));
            // Coerce non-strings to String via String.valueOf(...)
            if (!IsStringType(q.Type))
            {
                valueExpr = new CallExpressionNode(
                    target: new MemberAccessExpressionNode(
                        @object: new IdentifierExpressionNode(new IdentifierNode("String")),
                        member: new IdentifierNode("valueOf")),
                    arguments: new ExpressionNode[] { valueExpr });
            }
            var withQuery = new CallExpressionNode(
                target: new MemberAccessExpressionNode(
                    @object: chain,
                    member: new IdentifierNode("addQueryParameter")),
                arguments: new ExpressionNode[]
                {
                    new LiteralExpressionNode(q.OriginalName, LiteralKind.String),
                    valueExpr,
                });
            chain = q.IsRequired ? withQuery : new ConditionalExpressionNode(
                new BinaryExpressionNode(new IdentifierExpressionNode(new IdentifierNode(paramName)), BinaryOperator.Equal,
                    new LiteralExpressionNode(null, LiteralKind.Null)), chain, withQuery);
        }

        chain = new CallExpressionNode(
            target: new MemberAccessExpressionNode(
                @object: chain,
                member: new IdentifierNode("build")),
            arguments: ImmutableList<ExpressionNode>.Empty);

        return chain;
    }

    /// <summary>Builds an operation path from literal fragments and declared path parameters.</summary>
    /// <param name="op">The source operation.</param>
    /// <returns>The Java path expression.</returns>
    private ExpressionNode BuildPathExpression(IrOperation op)
    {
        // Build a Java string concatenation expression of literal segments and path params.
        // Strip leading slash to play nice with addPathSegments.
        var path = op.Path.TrimStart('/');
        if (!path.Contains('{'))
        {
            return new LiteralExpressionNode(path, LiteralKind.String);
        }

        var parts = new List<ExpressionNode>();
        var sb = new StringBuilder();
        int i = 0;
        while (i < path.Length)
        {
            if (path[i] == '{')
            {
                if (sb.Length > 0)
                {
                    parts.Add(new LiteralExpressionNode(sb.ToString(), LiteralKind.String));
                    sb.Clear();
                }
                var end = path.IndexOf('}', i);
                if (end < 0) { sb.Append(path[i++]); continue; }
                var paramName = path.Substring(i + 1, end - i - 1);
                var parameter = op.PathParameters.FirstOrDefault(candidate => candidate.OriginalName == paramName);
                var sanitized = _namingConverter.ToCamelCase(parameter?.Name ?? paramName);
                if (JavaReservedWords.Reserved.Contains(sanitized)) sanitized += "_";
                // Wrap with String.valueOf(...) to support any param type.
                parts.Add(new CallExpressionNode(
                    target: new MemberAccessExpressionNode(
                        @object: new IdentifierExpressionNode(new IdentifierNode("String")),
                        member: new IdentifierNode("valueOf")),
                    arguments: new ExpressionNode[] { new IdentifierExpressionNode(new IdentifierNode(sanitized)) }));
                i = end + 1;
            }
            else
            {
                sb.Append(path[i++]);
            }
        }
        if (sb.Length > 0) parts.Add(new LiteralExpressionNode(sb.ToString(), LiteralKind.String));

        // Chain "+" left-to-right; ensure first part is a string literal.
        if (parts.Count == 0) return new LiteralExpressionNode(string.Empty, LiteralKind.String);
        if (parts[0] is not LiteralExpressionNode)
            parts.Insert(0, new LiteralExpressionNode(string.Empty, LiteralKind.String));
        ExpressionNode current = parts[0];
        for (int j = 1; j < parts.Count; j++)
        {
            current = new BinaryExpressionNode(
                left: current,
                @operator: BinaryOperator.Add,
                right: parts[j]);
        }
        return current;
    }

    /// <summary>Builds a request with a real JSON body or an empty body required by the HTTP verb.</summary>
    /// <param name="op">The source operation.</param>
    /// <param name="hasBody">Whether an explicit source request payload exists.</param>
    /// <returns>The built OkHttp request.</returns>
    private ExpressionNode BuildRequestExpression(IrOperation op, bool hasBody)
    {
        // new Request.Builder().url(url).{method}(body|nothing).build()
        ExpressionNode chain = new CallExpressionNode(
            target: new IdentifierExpressionNode(new IdentifierNode("Request.Builder")),
            arguments: ImmutableList<ExpressionNode>.Empty,
            isNewExpression: true);

        chain = new CallExpressionNode(
            target: new MemberAccessExpressionNode(
                @object: chain,
                member: new IdentifierNode("url")),
            arguments: new ExpressionNode[] { new IdentifierExpressionNode(new IdentifierNode("url")) });

        var method = op.HttpMethod.ToLowerInvariant();
        switch (method)
        {
            case "get":
            case "delete":
            case "head":
                chain = new CallExpressionNode(
                    target: new MemberAccessExpressionNode(
                        @object: chain,
                        member: new IdentifierNode(method)),
                    arguments: ImmutableList<ExpressionNode>.Empty);
                break;
            default:
                // For POST/PUT/PATCH: pass a RequestBody if provided, else use empty body.
                if (hasBody)
                {
                    // The injected ObjectMapper serializes the actual request payload.
                    chain = new CallExpressionNode(
                        target: new MemberAccessExpressionNode(
                            @object: chain,
                            member: new IdentifierNode(method)),
                        arguments: new ExpressionNode[]
                        {
                            new CallExpressionNode(
                                target: new MemberAccessExpressionNode(
                                    @object: new IdentifierExpressionNode(new IdentifierNode("okhttp3.RequestBody")),
                                    member: new IdentifierNode("create")),
                                arguments: new ExpressionNode[]
                                {
                                    new CallExpressionNode(
                                        target: new MemberAccessExpressionNode(
                                            @object: new IdentifierExpressionNode(new IdentifierNode("mapper")),
                                            member: new IdentifierNode("writeValueAsBytes")),
                                        arguments: new ExpressionNode[]
                                        {
                                            new IdentifierExpressionNode(new IdentifierNode("body")),
                                        }),
                                    new CallExpressionNode(new MemberAccessExpressionNode(new IdentifierExpressionNode(new IdentifierNode("okhttp3.MediaType")), new IdentifierNode("get")),
                                        [new LiteralExpressionNode("application/json", LiteralKind.String)]),
                                }),
                        });
                }
                else
                {
                    chain = new CallExpressionNode(
                        target: new MemberAccessExpressionNode(
                            @object: chain,
                            member: new IdentifierNode(method)),
                        arguments: new ExpressionNode[]
                        {
                            new CallExpressionNode(
                                target: new MemberAccessExpressionNode(
                                    @object: new IdentifierExpressionNode(new IdentifierNode("okhttp3.RequestBody")),
                                    member: new IdentifierNode("create")),
                                arguments: new ExpressionNode[]
                                {
                                    new LiteralExpressionNode(string.Empty, LiteralKind.String),
                                    new LiteralExpressionNode(null, LiteralKind.Null),
                                }),
                        });
                }
                break;
        }

        foreach (var header in op.HeaderParameters)
        {
            var name = _namingConverter.ToCamelCase(header.Name);
            if (JavaReservedWords.Reserved.Contains(name)) name += "_";
            var value = new IdentifierExpressionNode(new IdentifierNode(name));
            var withHeader = new CallExpressionNode(new MemberAccessExpressionNode(chain, new IdentifierNode("header")),
                [new LiteralExpressionNode(header.OriginalName, LiteralKind.String),
                    new CallExpressionNode(new MemberAccessExpressionNode(new IdentifierExpressionNode(new IdentifierNode("String")), new IdentifierNode("valueOf")), [value])]);
            chain = header.IsRequired ? withHeader : new ConditionalExpressionNode(
                new BinaryExpressionNode(value, BinaryOperator.Equal, new LiteralExpressionNode(null, LiteralKind.Null)), chain, withHeader);
        }
        chain = new CallExpressionNode(
            target: new MemberAccessExpressionNode(
                @object: chain,
                member: new IdentifierNode("build")),
            arguments: ImmutableList<ExpressionNode>.Empty);

        return chain;
    }

    /// <summary>Determines whether a query value can be passed directly as a string.</summary>
    /// <param name="t">The source query type.</param>
    /// <returns>Whether the unwrapped value is a string.</returns>
    private static bool IsStringType(IrType t) => t switch
    {
        IrPrimitiveType p => p.PrimitiveKind == ValIO.Core.IR.Enums.IrPrimitiveKind.String,
        IrNullableType n => IsStringType(n.InnerType),
        IrOptionalType o => IsStringType(o.InnerType),
        _ => false,
    };

    /// <summary>Normalizes a configured package into Java dotted notation.</summary>
    /// <param name="pkg">The configured package.</param>
    /// <returns>The normalized package.</returns>
    private static string NormalizePackage(string pkg) => pkg.Trim('.').Replace('/', '.');

    /// <summary>Derives a stable public Java client class name.</summary>
    /// <param name="clientName">The source operation group.</param>
    /// <returns>The generated public class identifier.</returns>
    private static string BuildClassName(string clientName)
    {
        if (string.IsNullOrWhiteSpace(clientName)) return "ApiClient";
        return clientName.EndsWith("Api", StringComparison.OrdinalIgnoreCase)
            ? clientName
            : clientName + "Api";
    }

    /// <summary>Formats the restricted expression subset supported by try-with-resources metadata.</summary>
    /// <param name="node">The resource initializer expression.</param>
    /// <returns>The resource initializer source text.</returns>
    private string ExpressionToString(ExpressionNode node)
    {
        // Limited inline visitor used only when generating try-with-resources source line.
        // Mirrors a subset of JavaAstRenderer behaviour.
        return node switch
        {
            CallExpressionNode call => $"{(call.IsNewExpression ? "new " : "")}{ExpressionToString(call.Target)}({string.Join(", ", call.Arguments.Select(ExpressionToString))})",
            MemberAccessExpressionNode m => $"{ExpressionToString(m.Object)}.{m.Member.Name}",
            IdentifierExpressionNode id => id.Identifier.Name,
            LiteralExpressionNode lit when lit.LiteralKind == LiteralKind.String => $"\"{lit.Value}\"",
            LiteralExpressionNode lit when lit.LiteralKind == LiteralKind.Null   => "null",
            LiteralExpressionNode lit => lit.Value?.ToString() ?? "null",
            _ => node.ToString() ?? string.Empty,
        };
    }
}
