using ValIO.Core.Ast.Builders;
using System.Collections.Immutable;
using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Expressions;
using ValIO.Core.Ast.Nodes.Statements;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Core.Interfaces;
using ValIO.Core.IR;
using ValIO.Core.IR.Types;
using ValIO.Core.Models;
using ValIO.Generators.TypeScript.Angular.Ast.Nodes;
using ValIO.Generators.TypeScript.Ast.Builders;
using ValIO.Generators.TypeScript.Ast.JsDoc;
using ValIO.Generators.TypeScript.Ast.Nodes;
using ValIO.Generators.TypeScript.Ast.Nodes.Expressions;
using ValIO.Generators.TypeScript.Options;
using ValIO.Generators.TypeScript.Validation;

namespace ValIO.Generators.TypeScript.Angular.Ast.Builders;

/// <summary>
/// Builds a single <c>@Injectable({ providedIn: 'root' })</c> service per
/// <see cref="IrClient"/>, emitting one method per <see cref="IrOperation"/>.
/// Methods return observables or promises and delegate to
/// <c>this.http.{verb}&lt;T&gt;(url, { params, headers, body? })</c>.
/// </summary>
public sealed class AngularServiceAstBuilder : AstBuilderBase
{

    private readonly INamingConverter _namingConverter;
    private readonly TypeScriptParamsAstBuilder _paramsBuilder;
    private readonly TypeScriptResultsAstBuilder _resultsBuilder;
    /// <summary>Whether generated methods adapt the HTTP observable to a promise.</summary>
    private readonly bool _promiseMode;

    /// <summary>The configured request and response validation strategy.</summary>
    private readonly IClientValidationStrategy _validation;

    /// <summary>Maps declared response bodies to their TypeScript assertion types when validation is disabled.</summary>
    private readonly Func<IrType, TypeNode> _typeBuilder;


    /// <summary>Creates a AngularServiceAstBuilder for one finalized IR document.</summary>
    /// <param name="documentContext">The shared immutable indexes of the finalized IR document.</param>
    /// <param name="namingConverter">The generated identifier naming policy.</param>
    /// <param name="options">The TypeScript generation settings.</param>
    /// <param name="promiseMode">Whether methods adapt HTTP observables to promises.</param>
    /// <exception cref="ArgumentNullException">A required context or collaborator is null.</exception>
    public AngularServiceAstBuilder(IrDocumentContext documentContext,
        INamingConverter namingConverter, TypeScriptGenerationOptions? options, bool promiseMode = false)
        : base(documentContext)
    {
        _namingConverter = namingConverter ?? throw new ArgumentNullException(nameof(namingConverter));
        _promiseMode = promiseMode;
        _validation = ClientValidationStrategyFactory.Create(options);
        var typeBuilder = new TypeScriptAstBuilder(documentContext, namingConverter, options);
        _typeBuilder = typeBuilder.BuildType;
        _paramsBuilder = new TypeScriptParamsAstBuilder(documentContext, namingConverter, typeBuilder.BuildType);
        _resultsBuilder = new TypeScriptResultsAstBuilder(documentContext, namingConverter, typeBuilder.BuildType, options?.ResultTypeSuffix, options?.ResultTypePrefix);
    }

    /// <summary>
    /// Builds a service class name with a distinct Promise suffix for the promise transport.
    /// </summary>
    /// <param name="clientName">The client resource name.</param>
    /// <returns>The exported Angular service class name.</returns>
    public string ServiceClassName(string clientName) =>
        $"{_namingConverter.ToPascalCase(clientName)}{(_promiseMode ? "Promise" : "")}Service";

    /// <summary>
    /// Builds a distinct file basename for each Angular return style.
    /// </summary>
    /// <param name="clientName">The client resource name.</param>
    /// <returns>The service file basename without its extension.</returns>
    public string ServiceFileName(string clientName) =>
        $"{_namingConverter.ToCamelCase(clientName)}{(_promiseMode ? "Promise" : "")}Service";

    /// <summary>
    /// Builds a complete <see cref="SourceFileNode"/> for one IrClient,
    /// containing the Angular @Injectable service declaration.
    /// </summary>
    /// <param name="client">The API client whose operations become service methods.</param>
    /// <param name="fileOrganization">The generated-file layout used for imports.</param>
    /// <param name="options">The optional generation settings retained for caller compatibility.</param>
    /// <returns>The complete Angular service source file.</returns>
    /// <exception cref="ArgumentNullException">The client is null.</exception>
    public SourceFileNode BuildServiceSourceFile(
        IrClient client,
        FileOrganization? fileOrganization,
        TypeScriptGenerationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        var imports = BuildImports(client, fileOrganization);
        var serviceClass = BuildServiceClass(client);

        var fileBase = ServiceFileName(client.Name);
        return new SourceFileNode(
            imports: imports,
            declarations: ImmutableList.Create<DeclarationNode>(serviceClass),
            fileDocumentation: TypeScriptDoc.FileHeader(
                $"{fileBase}.service.ts",
                $"services/{fileBase}",
                $"Angular HttpClient service for the {client.Name} API."));
    }

    /// <summary>Builds the injectable service class and its shared HTTP client dependency.</summary>
    /// <param name="client">The grouped API operations.</param>
    /// <returns>The service declaration.</returns>
    private AngularServiceClassNode BuildServiceClass(IrClient client)
    {
        var className = ServiceClassName(client.Name);

        // @Injectable({ providedIn: "root" })
        var providedInProperty = new PropertyAssignmentNode(
            new IdentifierNode("providedIn"),
            new LiteralExpressionNode("root", LiteralKind.String));
        var injectableArgs = ImmutableList.Create<ExpressionNode>(
            new ObjectLiteralExpressionNode(
                ImmutableList.Create(providedInProperty),
                preferMultilineFormat: false));
        var injectableAttribute = new AttributeNode(
            new IdentifierNode("Injectable"),
            injectableArgs);

        // constructor(private readonly http: HttpClient) {}
        var httpParameter = new ParameterDeclarationNode(
            name: new IdentifierNode("http"),
            type: new SimpleTypeNode(new IdentifierNode("HttpClient"), isPrimitive: false),
            isRequired: true,
            metadata: new Dictionary<string, object> { ["accessModifier"] = "private readonly" });

        var methods = client.Operations
            .Select(BuildOperationMethod)
            .ToImmutableList();

        return new AngularServiceClassNode(
            name: new IdentifierNode(className),
            attributes: ImmutableList.Create(injectableAttribute),
            constructorParameters: ImmutableList.Create(httpParameter),
            methods: methods,
            isExported: true,
            documentation: new CommentNode(
                $"Angular {(_promiseMode ? "Promise" : "RxJS")} HttpClient service for {client.Name} operations.",
                CommentKind.JSDoc));
    }

    /// <summary>Builds an operation method with the selected asynchronous return style.</summary>
    /// <param name="operation">The HTTP operation contract.</param>
    /// <returns>The generated service method.</returns>
    private MethodDeclarationNode BuildOperationMethod(IrOperation operation)
    {
        var hasParameters = _paramsBuilder.HasParameters(operation);
        var hasResultType = _resultsBuilder.HasResponsesWithBody(operation);
        var resultTypeName = _resultsBuilder.ResultTypeName(operation.MethodName);

        // Return type: Observable<{Op}Result> when there is a body, otherwise Observable<void>.
        var resultTypeNode = hasResultType
            ? new SimpleTypeNode(new IdentifierNode(resultTypeName), isPrimitive: false)
            : new SimpleTypeNode(new IdentifierNode("void"), isPrimitive: true);
        var returnType = new GenericTypeNode(
            new IdentifierNode(_promiseMode ? "Promise" : "Observable"),
            ImmutableList.Create<TypeNode>(resultTypeNode));

        // Parameters.
        var parameters = ImmutableList.CreateBuilder<ParameterDeclarationNode>();
        if (hasParameters)
        {
            parameters.Add(new ParameterDeclarationNode(
                name: new IdentifierNode("params"),
                type: new SimpleTypeNode(
                    new IdentifierNode(_paramsBuilder.ParamsInterfaceName(operation.MethodName)),
                    isPrimitive: false),
                isRequired: true));
        }

        if (_promiseMode)
            parameters.Add(new ParameterDeclarationNode(
                new IdentifierNode("signal"), new SimpleTypeNode(new IdentifierNode("AbortSignal")), isRequired: false,
                documentation: new CommentNode("Optional AbortSignal; abort unsubscribes the active HttpClient request.", CommentKind.JSDoc)));

        return new MethodDeclarationNode(
            name: new IdentifierNode(operation.MethodName),
            returnType: returnType,
            parameters: parameters.ToImmutable(),
            body: BuildMethodBody(operation, hasResultType, resultTypeName),
            isAsync: false,
            accessModifier: AccessModifier.Public,
            documentation: BuildDocumentation(operation));
    }

    /// <summary>Builds an Angular request with serialized form data and wire parameter names.</summary>
    /// <param name="operation">The operation whose request is generated.</param>
    /// <param name="hasResultType">Whether the operation returns a generated result union.</param>
    /// <param name="resultTypeName">The generated operation result type name.</param>
    /// <returns>The Angular operation method body.</returns>
    private BlockNode BuildMethodBody(IrOperation operation, bool hasResultType, string resultTypeName)
    {
        var statements = new List<StatementNode>();
        var sources = TypeScriptParameterSources.Create(operation);

        if (_promiseMode && _validation.IsEnabled)
            AddValidationStatements(operation, sources, statements);

        if (operation.RequestBody is { IsForm: true } requestBody && sources.BodySource != null)
        {
            sources.BodySource = TypeScriptFormBody.Build(requestBody, sources.BodySource, ModelsByName, statements);
            statements.Add(new BlankLineStatementNode());
        }

        // const url = `/path/${params.id}`;
        statements.Add(new VariableDeclarationStatementNode(
            new IdentifierNode("url"),
            type: null,
            initializer: TypeScriptRequestPath.BuildUrl(operation, sources.RouteSource)));

        // Build HttpParams + HttpHeaders only if relevant.
        var hasOptionsObject = _promiseMode && hasResultType;
        var optionsProperties = new List<PropertyAssignmentNode>();
        if (hasOptionsObject)
            optionsProperties.Add(new PropertyAssignmentNode(new IdentifierNode("observe"), new LiteralExpressionNode("response", LiteralKind.String)));

        if (operation.QueryParameters.Count > 0 && sources.QuerySource != null)
        {
            statements.Add(new BlankLineStatementNode());
            statements.Add(new VariableDeclarationStatementNode(
                new IdentifierNode("queryParams"),
                type: null,
                initializer: BuildHttpParamsChain(operation, sources)));
            optionsProperties.Add(new PropertyAssignmentNode(
                new IdentifierNode("params"),
                Id("queryParams")));
            hasOptionsObject = true;
        }

        if ((operation.HeaderParameters.Count > 0 && sources.HeadersSource != null) || operation.RequestBody is { IsFormUrlEncoded: true })
        {
            statements.Add(new BlankLineStatementNode());
            statements.Add(new VariableDeclarationStatementNode(
                new IdentifierNode("headers"),
                type: null,
                initializer: BuildHttpHeadersChain(operation, sources)));
            optionsProperties.Add(new PropertyAssignmentNode(
                new IdentifierNode("headers"),
                Id("headers")));
            hasOptionsObject = true;
        }

        // Build options object — only when there is at least one option.
        var optionsObject = hasOptionsObject
            ? (ExpressionNode)new ObjectLiteralExpressionNode(
                optionsProperties.ToImmutableList(),
                preferMultilineFormat: optionsProperties.Count > 1)
            : null;

        if (statements.Count > 0)
            statements.Add(new BlankLineStatementNode());

        // return this.http.{verb}<T>(url, body?, options?);
        var httpCall = BuildHttpCall(operation, sources, hasResultType, resultTypeName, optionsObject);
        statements.Add(new ReturnStatementNode(_promiseMode
            ? BuildPromiseCall(operation, hasResultType, resultTypeName, httpCall)
            : httpCall));

        return new BlockNode(statements.ToImmutableList());
    }

    /// <summary>Builds the common Angular HTTP verb invocation.</summary>
    /// <param name="operation">The HTTP operation.</param>
    /// <param name="sources">The validated request value sources.</param>
    /// <param name="hasResultType">Whether the response has a generated result union.</param>
    /// <param name="resultTypeName">The generated result type name.</param>
    /// <param name="optionsObject">The optional query, header, and observation configuration.</param>
    /// <returns>The Angular HttpClient invocation.</returns>
    private ExpressionNode BuildHttpCall(
        IrOperation operation,
        TypeScriptParameterSources sources,
        bool hasResultType,
        string resultTypeName,
        ExpressionNode? optionsObject)
    {
        var verb = operation.HttpMethod.ToLowerInvariant();
        // HttpClient.get/delete/head signatures: (url, options?). For these verbs,
        // body — if any — would be ignored on the client side and so omitted.
        var verbHasBody = verb is "post" or "put" or "patch";
        var hasBody = verbHasBody && operation.RequestBody != null && sources.BodySource != null;

        var arguments = new List<ExpressionNode> { Id("url") };

        if (hasBody)
        {
            // For binary body (Blob / Uint8Array), Angular HttpClient accepts
            // many ad-hoc body shapes; the body interface from our params
            // doesn't always line up with the union, so cast through unknown.
            var bodyExpression = operation.RequestBody!.IsJson
                ? sources.BodySource!
                : (ExpressionNode)new CastExpressionNode(
                    new SimpleTypeNode(new IdentifierNode("unknown"), isPrimitive: true),
                    sources.BodySource!,
                    useAsOperator: true);
            arguments.Add(bodyExpression);
        }

        if (verbHasBody && !hasBody)
            arguments.Add(Id("null"));

        if (optionsObject != null)
            arguments.Add(optionsObject);

        // Generic type argument: e.g. `this.http.get<GetUserResult>(...)`.
        // Skip when the body is void — generic isn't useful and stops tsc
        // from inferring `Observable<Object>`.
        var genericTypeArg = hasResultType
            ? (TypeNode)new SimpleTypeNode(new IdentifierNode(_promiseMode ? "unknown" : resultTypeName), isPrimitive: _promiseMode)
            : new SimpleTypeNode(new IdentifierNode("void"), isPrimitive: true);

        return new CallExpressionNode(
            target: new MemberAccessExpressionNode(
                @object: new MemberAccessExpressionNode(
                    @object: new IdentifierExpressionNode(new IdentifierNode("this")),
                    member: new IdentifierNode("http")),
                member: new IdentifierNode(verb)),
            arguments: arguments.ToImmutableList(),
            genericTypeArgument: genericTypeArg);
    }

    /// <summary>Adapts one HTTP observable to a promise and maps documented response statuses.</summary>
    /// <param name="operation">The operation response contract.</param>
    /// <param name="hasResultType">Whether a response union is generated.</param>
    /// <param name="resultTypeName">The generated result union name.</param>
    /// <param name="httpCall">The shared HTTP invocation to subscribe once.</param>
    /// <returns>The promise expression with bodyless values normalized to undefined.</returns>
    private ExpressionNode BuildPromiseCall(IrOperation operation, bool hasResultType, string resultTypeName, ExpressionNode httpCall)
    {
        var abortableRequest = new CallExpressionNode(Id("race"), [BuildAbortObservable(), httpCall]);
        var promise = new CallExpressionNode(Id("firstValueFrom"), [abortableRequest]);
        if (!hasResultType)
            return new CallExpressionNode(Member(promise, "then"), ImmutableList.Create<ExpressionNode>(
                new ArrowFunctionExpressionNode([], Id("undefined"))));

        var responseStatements = new List<StatementNode>();
        var status = Member(Id("response"), "status");
        foreach (var response in operation.Responses.Where(response => int.TryParse(response.StatusCode, out _)))
        {
            var statusCode = int.Parse(response.StatusCode);
            var successful = statusCode >= 200 && statusCode < 300;
            ExpressionNode body = response.Type == null || statusCode is 204 or 205 || string.Equals(operation.HttpMethod, "HEAD", StringComparison.OrdinalIgnoreCase)
                ? Id("undefined")
                : Member(Id("response"), "body");
            if (response.Type != null && body is not IdentifierExpressionNode && _validation.IsEnabled && ValidationTypes.IsValidatable(response.Type))
            {
                var schemaName = response.Type is IrModelReference model
                    ? $"{model.ModelName}Schema"
                    : ClientResponseSchemas.ResultSchemaName(_namingConverter, operation.MethodName, response.StatusCode);
                body = _validation.ValidateNamed(schemaName, body);
            }
            else if (response.Type != null && body is not IdentifierExpressionNode)
            {
                body = new CastExpressionNode(_typeBuilder(response.Type), body, useAsOperator: true);
            }
            responseStatements.Add(new IfStatementNode(
                new BinaryExpressionNode(status, BinaryOperator.Equal, new LiteralExpressionNode(statusCode, LiteralKind.Number)),
                new BlockNode(ImmutableList.Create<StatementNode>(new ReturnStatementNode(BuildResultObject(
                    new LiteralExpressionNode(statusCode, LiteralKind.Number), body, successful))))));
        }
        responseStatements.Add(new ReturnStatementNode(BuildResultObject(status, Member(Id("response"), "body"), successful: false)));
        var callback = new ArrowFunctionExpressionNode(
            [new ParameterDeclarationNode(new IdentifierNode("response"), type: null)],
            new BlockNode(responseStatements.ToImmutableList()),
            returnType: new SimpleTypeNode(new IdentifierNode(resultTypeName)));
        return new CallExpressionNode(Member(promise, "then"), ImmutableList.Create<ExpressionNode>(callback));
    }

    /// <summary>Creates an abort-only observable whose teardown removes its listener after the HTTP request settles.</summary>
    /// <remarks>The abort source is subscribed first so a pre-aborted signal never subscribes the HTTP request.</remarks>
    /// <returns>An observable that errors with the signal reason and never emits a response value.</returns>
    private static ExpressionNode BuildAbortObservable()
    {
        var abortReason = new BinaryExpressionNode(
            Member(Id("signal"), "reason", optional: true), BinaryOperator.NullishCoalescing,
            new CallExpressionNode(Id("DOMException"),
                [new LiteralExpressionNode("The operation was aborted", LiteralKind.String), new LiteralExpressionNode("AbortError", LiteralKind.String)],
                isNewExpression: true));
        var onAbort = new ArrowFunctionExpressionNode([], new CallExpressionNode(Member(Id("subscriber"), "error"), [abortReason]));
        var subscribe = new ArrowFunctionExpressionNode(
            [new ParameterDeclarationNode(new IdentifierNode("subscriber"), type: null)],
            new BlockNode([
                new VariableDeclarationStatementNode(new IdentifierNode("onAbort"), type: null, initializer: onAbort),
                new IfStatementNode(Member(Id("signal"), "aborted", optional: true), new BlockNode([
                    new ExpressionStatementNode(new CallExpressionNode(Id("onAbort"))),
                    new ReturnStatementNode(),
                ])),
                new ExpressionStatementNode(new CallExpressionNode(Member(Id("signal"), "addEventListener", optional: true),
                    [new LiteralExpressionNode("abort", LiteralKind.String), Id("onAbort"),
                        new ObjectLiteralExpressionNode([new PropertyAssignmentNode(new IdentifierNode("once"), new LiteralExpressionNode(true, LiteralKind.Boolean))])])),
                new ReturnStatementNode(new ArrowFunctionExpressionNode([], new CallExpressionNode(
                    Member(Id("signal"), "removeEventListener", optional: true), [new LiteralExpressionNode("abort", LiteralKind.String), Id("onAbort")]))),
            ]));
        return new CallExpressionNode(Id("Observable"), [subscribe], isNewExpression: true,
            genericTypeArgument: new SimpleTypeNode(new IdentifierNode("never"), isPrimitive: true));
    }

    /// <summary>Builds a discriminated operation response value.</summary>
    /// <param name="status">The numeric response status.</param>
    /// <param name="body">The validated response value or undefined.</param>
    /// <param name="successful">Whether the documented response is successful.</param>
    /// <returns>The result object literal.</returns>
    private static ObjectLiteralExpressionNode BuildResultObject(ExpressionNode status, ExpressionNode body, bool successful) =>
        new(ImmutableList.Create(
            new PropertyAssignmentNode(new IdentifierNode("status"), status),
            new PropertyAssignmentNode(new IdentifierNode("body"), body),
            new PropertyAssignmentNode(new IdentifierNode("isSuccessful"), new LiteralExpressionNode(successful, LiteralKind.Boolean)),
            new PropertyAssignmentNode(new IdentifierNode("isFailed"), new LiteralExpressionNode(!successful, LiteralKind.Boolean))));

    /// <summary>Validates request inputs before URL interpolation or form serialization.</summary>
    /// <param name="operation">The operation input contract.</param>
    /// <param name="sources">The request sources rebound to validated locals.</param>
    /// <param name="statements">The method statements receiving validation declarations.</param>
    private void AddValidationStatements(IrOperation operation, TypeScriptParameterSources sources, List<StatementNode> statements)
    {
        var operationName = _namingConverter.ToPascalCase(operation.MethodName);
        if (sources.RouteSource != null)
        {
            statements.Add(new VariableDeclarationStatementNode(new IdentifierNode("parsedRoute"), type: null,
                initializer: _validation.ValidateNamed($"{operationName}RouteParamsSchema", sources.RouteSource)));
            sources.RouteSource = Id("parsedRoute");
        }
        if (sources.QuerySource != null)
        {
            var input = sources.QuerySourceMayBeUndefined
                ? new BinaryExpressionNode(sources.QuerySource, BinaryOperator.NullishCoalescing, new ObjectLiteralExpressionNode([]))
                : sources.QuerySource;
            statements.Add(new VariableDeclarationStatementNode(new IdentifierNode("parsedQuery"), type: null,
                initializer: _validation.ValidateNamed($"{operationName}QueryParamsSchema", input)));
            sources.QuerySource = Id("parsedQuery");
            sources.QuerySourceMayBeUndefined = false;
        }
        if (sources.BodySource != null && operation.RequestBody != null && ValidationTypes.IsValidatable(operation.RequestBody.Type))
        {
            var parsed = _validation.Validate(operation.RequestBody.Type, sources.BodySource);
            statements.Add(new VariableDeclarationStatementNode(new IdentifierNode("parsedBody"), type: null,
                initializer: sources.BodySourceMayBeUndefined
                    ? new ConditionalExpressionNode(new BinaryExpressionNode(sources.BodySource, BinaryOperator.NotEqual, Id("undefined")), parsed, Id("undefined"))
                    : parsed));
            sources.BodySource = Id("parsedBody");
        }
    }

    /// <summary>Builds immutable Angular query parameters preserving original wire keys.</summary>
    /// <param name="operation">The operation query declarations.</param>
    /// <param name="sources">The validated or raw query parameter source.</param>
    /// <returns>The Angular HTTP parameter expression.</returns>
    private ExpressionNode BuildHttpParamsChain(IrOperation operation, TypeScriptParameterSources sources)
    {
        // Start with `new HttpParams()` then chain `.set('a', String(params.query.a))`
        // for each query parameter. Conditional sets for optional params via
        // `someOptional !== undefined ? base.set(...) : base` — but to keep the
        // chain readable we always coerce the value.
        ExpressionNode chain = new CallExpressionNode(
            new IdentifierExpressionNode(new IdentifierNode("HttpParams")),
            ImmutableList<ExpressionNode>.Empty,
            isNewExpression: true);

        foreach (var parameter in operation.QueryParameters)
        {
            var access = TypeScriptRequestPath.BuildParameterAccess(sources.QuerySource!, parameter.Name, sources.QuerySourceMayBeUndefined);
            var coerced = new CallExpressionNode(
                new IdentifierExpressionNode(new IdentifierNode("String")),
                ImmutableList.Create<ExpressionNode>(access));

            // Ключ — «проводное» имя (Paging.Offset), значение читается по sanitized-свойству
            // (Paging_Offset): ASP.NET биндит вложенную модель по точечному имени.
            var setCall = new CallExpressionNode(
                new MemberAccessExpressionNode(chain, new IdentifierNode("set")),
                ImmutableList.Create<ExpressionNode>(
                    new LiteralExpressionNode(parameter.OriginalName, LiteralKind.String),
                    coerced));

            if (!parameter.IsRequired)
            {
                // For optional params, skip `set` when the value is undefined so
                // we don't send literal "undefined" strings on the wire.
                chain = new ConditionalExpressionNode(
                    new BinaryExpressionNode(
                        access,
                        BinaryOperator.NotEqual,
                        new IdentifierExpressionNode(new IdentifierNode("undefined"))),
                    setCall,
                    chain);
            }
            else
            {
                chain = setCall;
            }
        }

        return chain;
    }

    /// <summary>Builds immutable Angular headers with original wire names and the request encoding default.</summary>
    /// <param name="operation">The operation whose header parameters and body encoding are serialized.</param>
    /// <param name="sources">The validated or raw operation parameter expressions.</param>
    /// <returns>The expression constructing the Angular HTTP headers.</returns>
    private ExpressionNode BuildHttpHeadersChain(IrOperation operation, TypeScriptParameterSources sources)
    {
        ExpressionNode chain = new CallExpressionNode(
            new IdentifierExpressionNode(new IdentifierNode("HttpHeaders")),
            ImmutableList<ExpressionNode>.Empty,
            isNewExpression: true);

        if (operation.RequestBody is { IsFormUrlEncoded: true })
        {
            chain = new CallExpressionNode(new MemberAccessExpressionNode(chain, new IdentifierNode("set")),
                ImmutableList.Create<ExpressionNode>(
                    new LiteralExpressionNode("Content-Type", LiteralKind.String),
                    new LiteralExpressionNode("application/x-www-form-urlencoded", LiteralKind.String)));
        }

        foreach (var parameter in operation.HeaderParameters)
        {
            var access = TypeScriptRequestPath.BuildParameterAccess(sources.HeadersSource!, parameter.Name, sources.HeadersSourceMayBeUndefined);
            var coerced = new CallExpressionNode(
                new IdentifierExpressionNode(new IdentifierNode("String")),
                ImmutableList.Create<ExpressionNode>(access));

            // Ключ — «проводное» имя заголовка (X-Request-Id), значение читается по sanitized-свойству
            // (X_Request_Id): в HTTP уходит исходное имя, дефисы в идентификатор не помещаются.
            var setCall = new CallExpressionNode(
                new MemberAccessExpressionNode(chain, new IdentifierNode("set")),
                ImmutableList.Create<ExpressionNode>(
                    new LiteralExpressionNode(parameter.OriginalName, LiteralKind.String),
                    coerced));

            if (!parameter.IsRequired)
            {
                chain = new ConditionalExpressionNode(
                    new BinaryExpressionNode(
                        access,
                        BinaryOperator.NotEqual,
                        new IdentifierExpressionNode(new IdentifierNode("undefined"))),
                    setCall,
                    chain);
            }
            else
            {
                chain = setCall;
            }
        }

        return chain;
    }

    /// <summary>Builds a member access expression.</summary>
    /// <param name="source">The object source.</param>
    /// <param name="name">The member identifier.</param>
    /// <param name="optional">Whether the object may be absent.</param>
    /// <returns>The member access.</returns>
    private static MemberAccessExpressionNode Member(ExpressionNode source, string name, bool optional = false) =>
        new(source, new IdentifierNode(name), useOptionalChaining: optional);

    /// <summary>Builds a runtime identifier expression.</summary>
    /// <param name="name">The identifier name.</param>
    /// <returns>The identifier expression.</returns>
    private static IdentifierExpressionNode Id(string name) =>
        new(new IdentifierNode(name));

    /// <summary>Builds framework and operation-contract imports required by the generated service.</summary>
    /// <param name="client">The service operations to inspect.</param>
    /// <param name="fileOrganization">The optional generated-file layout.</param>
    /// <returns>The required imports for this service.</returns>
    private IReadOnlyList<ImportNode> BuildImports(IrClient client, FileOrganization? fileOrganization)
    {
        var imports = new List<ImportNode>
        {
            new(
                items: ImmutableList.Create(new IdentifierNode("Injectable")),
                from: "@angular/core",
                kind: ImportKind.Named),
        };

        // HttpClient + co. Only include HttpHeaders/HttpParams when used.
        var commonHttpItems = new List<IdentifierNode> { new("HttpClient") };
        var anyQuery = client.Operations.Any(op => op.QueryParameters.Count > 0);
        var anyHeaders = client.Operations.Any(op => op.HeaderParameters.Count > 0 || op.RequestBody is { IsFormUrlEncoded: true });
        if (anyHeaders) commonHttpItems.Add(new IdentifierNode("HttpHeaders"));
        if (anyQuery) commonHttpItems.Add(new IdentifierNode("HttpParams"));
        imports.Add(new ImportNode(
            items: commonHttpItems.ToImmutableList(),
            from: "@angular/common/http",
            kind: ImportKind.Named));

        imports.Add(new ImportNode(
            items: _promiseMode
                ? [new IdentifierNode("firstValueFrom"), new IdentifierNode("Observable"), new IdentifierNode("race")]
                : [new IdentifierNode("Observable")],
            from: "rxjs",
            kind: ImportKind.Named));

        // Type imports for Params/Result interfaces per operation.
        foreach (var operation in client.Operations)
        {
            var operationFileName = _namingConverter.ToCamelCase(operation.OriginalMethodName);
            if (_paramsBuilder.HasParameters(operation))
            {
                imports.Add(new ImportNode(
                    items: ImmutableList.Create(new IdentifierNode(_paramsBuilder.ParamsInterfaceName(operation.MethodName))),
                    from: $"./{operationFileName}/{operationFileName}.params",
                    kind: ImportKind.Named,
                    isTypeOnly: true));
            }
            if (_resultsBuilder.HasResponsesWithBody(operation))
            {
                imports.Add(new ImportNode(
                    items: ImmutableList.Create(new IdentifierNode(_resultsBuilder.ResultTypeName(operation.MethodName))),
                    from: $"./{operationFileName}/{operationFileName}.results",
                    kind: ImportKind.Named,
                    isTypeOnly: true));
            }
        }

        if (_promiseMode && !_validation.IsEnabled)
        {
            var responseModels = new HashSet<string>(StringComparer.Ordinal);
            foreach (var response in client.Operations.SelectMany(operation => operation.Responses).Where(response => response.Type != null))
                ValidationTypes.CollectModelSchemaNames(response.Type!, responseModels);
            foreach (var modelName in responseModels)
                imports.Add(new ImportNode([new IdentifierNode(modelName)], BuildModelImportPath(modelName, fileOrganization, schema: false), isTypeOnly: true));
        }

        if (_promiseMode && _validation.IsEnabled)
        {
            var bodyTypes = client.Operations.Where(operation => operation.RequestBody != null && ValidationTypes.IsValidatable(operation.RequestBody.Type))
                .Select(operation => operation.RequestBody!.Type).ToArray();
            imports.AddRange(_validation.RuntimeImports(bodyTypes,
                hasParams: client.Operations.Any(operation => operation.PathParameters.Count > 0 || operation.QueryParameters.Count > 0),
                hasNamedSchemaParse: client.Operations.Any(operation => ClientResponseSchemas.ValidatedResponses(operation).Any())));
            foreach (var operation in client.Operations)
            {
                var operationName = _namingConverter.ToPascalCase(operation.MethodName);
                var operationFile = _namingConverter.ToCamelCase(operation.OriginalMethodName);
                var paramsSchemas = new List<IdentifierNode>();
                if (operation.PathParameters.Count > 0)
                    paramsSchemas.Add(new IdentifierNode($"{operationName}RouteParamsSchema"));
                if (operation.QueryParameters.Count > 0)
                    paramsSchemas.Add(new IdentifierNode($"{operationName}QueryParamsSchema"));
                if (paramsSchemas.Count > 0)
                    imports.Add(new ImportNode(paramsSchemas, $"./{operationFile}/{operationFile}.params.schema"));
                foreach (var modelName in ClientResponseSchemas.ModelSchemaNames(operation))
                {
                    imports.Add(new ImportNode([new IdentifierNode($"{modelName}Schema")], BuildModelImportPath(modelName, fileOrganization, schema: true)));
                }
                var responseSchemas = ClientResponseSchemas.ConstructedResultSchemaNames(_namingConverter, operation);
                if (responseSchemas.Count > 0)
                    imports.Add(new ImportNode(responseSchemas.Select(name => new IdentifierNode(name)).ToList(), $"./{operationFile}/{operationFile}.results.schemas"));
            }
        }

        return imports.GroupBy(import => (import.From, import.ImportKind, import.IsTypeOnly))
            .Select(group => new ImportNode(group.SelectMany(import => import.Items).DistinctBy(item => item.Name).ToList(),
                group.Key.From, group.Key.ImportKind, group.Key.IsTypeOnly)).ToList();
    }

    /// <summary>Builds the relative model or validation-schema import from the service directory.</summary>
    /// <param name="modelName">The generated model name.</param>
    /// <param name="fileOrganization">The generated source layout.</param>
    /// <param name="schema">Whether to import the runtime schema rather than the model type.</param>
    /// <returns>The normalized relative module path.</returns>
    private string BuildModelImportPath(string modelName, FileOrganization? fileOrganization, bool schema)
    {
        var modelFile = _namingConverter.ToCamelCase(OriginalNameMap.GetTypeOriginalName(DocumentContext, modelName));
        var suffix = schema ? ".schema" : "";
        var modelPath = fileOrganization?.SeparateModels == true
            ? $"{fileOrganization.ModelsPath ?? "models"}/{modelFile}/{modelFile}{suffix}"
            : $"{fileOrganization?.ModelsPath ?? "models"}/{modelFile}{suffix}";
        var relativePath = Path.GetRelativePath(fileOrganization?.ClientsPath ?? "api", modelPath).Replace('\\', '/');
        return relativePath.StartsWith('.') ? relativePath : "./" + relativePath;
    }

    private static CommentNode? BuildDocumentation(IrOperation operation)
    {
        var doc = operation.Description ?? operation.Summary;
        return string.IsNullOrWhiteSpace(doc)
            ? null
            : new CommentNode(doc, CommentKind.JSDoc);
    }

}
