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
using ValIO.Generators.TypeScript.Ast.Builders;
using ValIO.Generators.TypeScript.Ast.JsDoc;
using ValIO.Generators.TypeScript.Ast.Nodes;
using ValIO.Generators.TypeScript.Ast.Nodes.Expressions;
using ValIO.Generators.TypeScript.Options;

namespace ValIO.Generators.TypeScript.Nextjs.Ast.Builders;

/// <summary>
/// Builds a Next.js (App Router) <c>fetch</c> client for an IR operation as TypeScript AST,
/// emitting one server-safe <c>{op}.nextjs.ts</c> per operation.
/// </summary>
/// <remarks>
/// Unlike the plain fetch client this transport is built for React Server Components / Server Actions:
/// it emits <b>no</b> <c>"use client"</c> directive, types its options as
/// <c>RequestInit &amp; { next?: { revalidate?, tags? } }</c> (the Next.js fetch augmentation), and for
/// cacheable <c>GET</c> operations attaches a per-operation cache <b>tag</b> plus an optional
/// <c>revalidate&lt;Op&gt;()</c> helper that calls <c>revalidateTag(tag, "max")</c> from <c>next/cache</c>
/// for on-demand invalidation. Caller-supplied <c>options</c> (cache, credentials, headers, next-config)
/// are merged in, with per-field override. Everything else — the parameter/result types, schemas,
/// validation and the discriminable <c>{Op}Results</c> union — is shared verbatim with the other transports.
/// </remarks>
public sealed class NextjsClientAstBuilder : AstBuilderBase
{

    private readonly INamingConverter _namingConverter;
    private readonly TypeScriptParamsAstBuilder _paramsBuilder;
    private readonly TypeScriptResultsAstBuilder _resultsBuilder;
    private readonly Validation.IClientValidationStrategy _validation;
    private readonly int? _defaultRevalidate;
    private readonly bool _generateRevalidateHelpers;


    /// <summary>Creates a NextjsClientAstBuilder for one finalized IR document.</summary>
    /// <param name="documentContext">The shared immutable indexes of the finalized IR document.</param>
    /// <param name="namingConverter">The generated identifier naming policy.</param>
    /// <param name="options">The TypeScript generation settings.</param>
    /// <exception cref="ArgumentNullException">A required context or collaborator is null.</exception>
    public NextjsClientAstBuilder(IrDocumentContext documentContext,
        INamingConverter namingConverter, TypeScriptGenerationOptions? options)
        : base(documentContext)
    {
        _namingConverter = namingConverter ?? throw new ArgumentNullException(nameof(namingConverter));
        _validation = Validation.ClientValidationStrategyFactory.Create(options);

        var typeBuilder = new TypeScriptAstBuilder(documentContext, namingConverter, options);
        _paramsBuilder = new TypeScriptParamsAstBuilder(documentContext, namingConverter, typeBuilder.BuildType);
        _resultsBuilder = new TypeScriptResultsAstBuilder(documentContext, namingConverter, typeBuilder.BuildType, options?.ResultTypeSuffix, options?.ResultTypePrefix);

        _defaultRevalidate = options?.NextJs?.DefaultRevalidate;
        _generateRevalidateHelpers = options?.NextJs?.GenerateRevalidateHelpers ?? true;
    }

    /// <summary>
    /// Builds the <c>{op}.nextjs.ts</c> source file for one operation: the client function plus, for a
    /// cacheable GET, a cache-tag constant and (optionally) a <c>revalidate&lt;Op&gt;()</c> helper.
    /// </summary>
    /// <param name="operation">The IR operation.</param>
    /// <param name="fileOrganization">File-layout options (paths, separate-models).</param>
    /// <returns>The rendered source file node.</returns>
    public SourceFileNode BuildOperationClientSourceFile(IrOperation operation, FileOrganization fileOrganization)
    {
        ArgumentNullException.ThrowIfNull(operation);


        var operationFileName = _namingConverter.ToCamelCase(operation.OriginalMethodName);
        var hasParameters = _paramsBuilder.HasParameters(operation);
        var hasResultType = _resultsBuilder.HasResponsesWithBody(operation);
        // Only GET responses participate in Next's persistent cache, so tags/revalidate apply to GET only.
        var isCacheable = string.Equals(operation.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase);
        // Cache tags are derived in the shared {op}.query-keys.ts module so a server-side revalidateTag mirrors a
        // client-side prefix invalidateQueries (see TypeScriptQueryKeysAstBuilder / TypeScriptCacheKey).
        var cacheTagsFnName = $"get{_namingConverter.ToPascalCase(operation.MethodName)}CacheTags";
        var broadTagName = $"{operationFileName}CacheTag";

        var declarations = ImmutableList.CreateBuilder<DeclarationNode>();
        declarations.Add(BuildOperationFunction(operation, hasParameters, hasResultType, isCacheable, cacheTagsFnName));

        if (isCacheable && _generateRevalidateHelpers)
            declarations.Add(BuildRevalidateHelper(operation, hasParameters, broadTagName, cacheTagsFnName));

        return new SourceFileNode(
            imports: BuildImports(operation, operationFileName, fileOrganization, hasParameters, hasResultType, isCacheable),
            declarations: declarations.ToImmutable(),
            fileDocumentation: TypeScriptDoc.FileHeader(
                $"{operationFileName}.nextjs.ts",
                $"endpoints/{operationFileName}",
                $"Next.js (App Router) fetch client for the {operation.MethodName} operation."));
    }

    /// <summary>Imports the native transport types and operation-specific models and validation schemas.</summary>
    /// <param name="operation">The operation whose imports are generated.</param>
    /// <param name="operationFileName">The original operation file name.</param>
    /// <param name="fileOrganization">The configured output layout.</param>
    /// <param name="hasParameters">Whether a generated parameter type is needed.</param>
    /// <param name="hasResultType">Whether a generated result union is needed.</param>
    /// <returns>The imports required by the native client operation.</returns>
    private IReadOnlyList<ImportNode> BuildImports(
        IrOperation operation,
        string operationFileName,
        FileOrganization fileOrganization,
        bool hasParameters,
        bool hasResultType,
        bool isCacheable)
    {
        var imports = ImmutableList.CreateBuilder<ImportNode>();

        // No transport import — `fetch` is global. Only the revalidate helper needs next/cache.
        if (isCacheable && _generateRevalidateHelpers)
            imports.Add(new ImportNode(
                items: ImmutableList.Create(new IdentifierNode("revalidateTag")),
                from: "next/cache",
                kind: ImportKind.Named));

        // Cache identity comes from the framework-agnostic {op}.query-keys.ts (see TypeScriptQueryKeysAstBuilder):
        // getXCacheTags(params) feeds next.tags so a server-side revalidateTag mirrors the client-side prefix
        // invalidateQueries; the broad {op}CacheTag const is the no-params fallback for the revalidate helper.
        if (isCacheable)
        {
            var queryKeyImports = new List<IdentifierNode>
            {
                new($"get{_namingConverter.ToPascalCase(operation.MethodName)}CacheTags"),
            };
            if (_generateRevalidateHelpers)
                queryKeyImports.Add(new IdentifierNode($"{operationFileName}CacheTag"));
            imports.Add(new ImportNode(
                items: queryKeyImports.ToImmutableList(),
                from: $"./{operationFileName}.query-keys",
                kind: ImportKind.Named));
        }

        // Validation runtime imports (z / v / Type / Value), driven by the generated expressions.
        imports.AddRange(_validation.RuntimeImports(
            ClientInlineConstructedTypes(operation),
            hasParams: operation.PathParameters.Count > 0 || operation.QueryParameters.Count > 0,
            hasNamedSchemaParse: Validation.ClientResponseSchemas.ValidatedResponses(operation).Any()));

        if (hasParameters)
            imports.Add(new ImportNode(
                items: ImmutableList.Create(new IdentifierNode(_paramsBuilder.ParamsInterfaceName(operation.MethodName))),
                from: $"./{operationFileName}.params",
                kind: ImportKind.Named,
                isTypeOnly: true));

        if (hasResultType)
            imports.Add(new ImportNode(
                items: ImmutableList.Create(new IdentifierNode(_resultsBuilder.ResultTypeName(operation.MethodName))),
                from: $"./{operationFileName}.results",
                kind: ImportKind.Named,
                isTypeOnly: true));

        if (_validation.IsEnabled)
        {
            AddParamsSchemaImports(imports, operation, operationFileName);

            foreach (var modelName in Validation.ClientResponseSchemas.ModelSchemaNames(operation))
                imports.Add(new ImportNode(
                    items: ImmutableList.Create(new IdentifierNode($"{modelName}Schema")),
                    from: BuildSchemaImportPath(modelName, fileOrganization),
                    kind: ImportKind.Named));

            var resultSchemaNames = Validation.ClientResponseSchemas.ConstructedResultSchemaNames(_namingConverter, operation);
            if (resultSchemaNames.Count > 0)
                imports.Add(new ImportNode(
                    items: resultSchemaNames.Select(n => new IdentifierNode(n)).ToImmutableList(),
                    from: $"./{operationFileName}.results.schemas",
                    kind: ImportKind.Named));
        }

        return imports.ToImmutable();
    }

    private void AddParamsSchemaImports(ImmutableList<ImportNode>.Builder imports, IrOperation operation, string operationFileName)
    {
        var opName = _namingConverter.ToPascalCase(operation.MethodName);
        var names = new List<IdentifierNode>();
        if (operation.PathParameters.Count > 0)
            names.Add(new IdentifierNode($"{opName}RouteParamsSchema"));
        if (operation.QueryParameters.Count > 0)
            names.Add(new IdentifierNode($"{opName}QueryParamsSchema"));
        if (names.Count > 0)
            imports.Add(new ImportNode(
                items: names.ToImmutableList(),
                from: $"./{operationFileName}.params.schema",
                kind: ImportKind.Named));
    }

    private static IReadOnlyCollection<IrType> ClientInlineConstructedTypes(IrOperation operation) =>
        operation.RequestBody != null && Validation.ValidationTypes.IsValidatable(operation.RequestBody.Type)
            ? new[] { operation.RequestBody.Type }
            : Array.Empty<IrType>();

    /// <summary>
    /// Builds <c>export function revalidate{Op}(params?)</c> — on-demand cache invalidation for the operation.
    /// </summary>
    /// <remarks>
    /// When the operation has parameters, the helper invalidates the <em>most specific</em> cache tag produced by
    /// <c>get{Op}CacheTags(params)</c> (the last, deepest hierarchical prefix — e.g.
    /// <c>api:apartments:&lt;id&gt;:investor</c>), so a call clears exactly that entity's cached result rather than
    /// every result of the operation. When no <c>params</c> are supplied (or the operation is parameterless) it
    /// falls back to the broad <c>{op}CacheTag</c>, which revalidates all of the operation's cached responses.
    /// Both the fetch (<see cref="BuildNextConfig"/>) and this helper draw their tags from the shared
    /// <c>{op}.query-keys.ts</c>, so server-side invalidation mirrors the client-side TanStack query key.
    /// </remarks>
    /// <param name="operation">The IR operation.</param>
    /// <param name="hasParameters">Whether the operation exposes a <c>params</c> argument.</param>
    /// <param name="broadTagName">The imported broad operation-tag const name (<c>{op}CacheTag</c>).</param>
    /// <param name="cacheTagsFnName">The imported hierarchical cache-tags function name (<c>get{Op}CacheTags</c>).</param>
    /// <returns>The exported <c>revalidate{Op}</c> function declaration.</returns>
    /// <seealso cref="BuildNextConfig"/>
    private FunctionDeclarationNode BuildRevalidateHelper(
        IrOperation operation,
        bool hasParameters,
        string broadTagName,
        string cacheTagsFnName)
    {
        var parameters = ImmutableList.CreateBuilder<ParameterDeclarationNode>();
        BlockNode body;

        if (hasParameters)
        {
            parameters.Add(new ParameterDeclarationNode(
                name: new IdentifierNode("params"),
                type: Simple(_paramsBuilder.ParamsInterfaceName(operation.MethodName)),
                isRequired: false,
                documentation: new CommentNode("When provided, revalidates only this entity's cached result; otherwise revalidates the whole operation.", CommentKind.JSDoc)));

            // const tags = params ? get{Op}CacheTags(params) : [{op}CacheTag];
            var tagsDecl = new VariableDeclarationStatementNode(
                new IdentifierNode("tags"),
                type: null,
                initializer: new ConditionalExpressionNode(
                    Id("params"),
                    new CallExpressionNode(Id(cacheTagsFnName), ImmutableList.Create<ExpressionNode>(Id("params"))),
                    new ArrayLiteralExpressionNode(ImmutableList.Create<ExpressionNode>(Id(broadTagName)))));

            // revalidateTag(tags.at(-1) ?? {op}CacheTag, "max");
            var mostSpecific = new BinaryExpressionNode(
                new CallExpressionNode(Member(Id("tags"), "at"), ImmutableList.Create<ExpressionNode>(Number(-1))),
                BinaryOperator.NullishCoalescing,
                Id(broadTagName));
            var call = new ExpressionStatementNode(new CallExpressionNode(
                Id("revalidateTag"),
                ImmutableList.Create<ExpressionNode>(mostSpecific, String("max"))));

            body = new BlockNode(ImmutableList.Create<StatementNode>(tagsDecl, call));
        }
        else
        {
            var call = new ExpressionStatementNode(new CallExpressionNode(
                Id("revalidateTag"),
                ImmutableList.Create<ExpressionNode>(Id(broadTagName), String("max"))));
            body = new BlockNode(ImmutableList.Create<StatementNode>(call));
        }

        return new FunctionDeclarationNode(
            name: new IdentifierNode($"revalidate{_namingConverter.ToPascalCase(operation.MethodName)}"),
            returnType: Simple("void", isPrimitive: true),
            parameters: parameters.ToImmutable(),
            body: body,
            isAsync: false,
            documentation: new CommentNode(
                $"On-demand invalidation of the {operation.MethodName} cache. Call from a Server Action or Route Handler.",
                CommentKind.JSDoc),
            isExported: true);
    }

    /// <summary>Builds a native client operation with backward-compatible options, cancellation, and client injection.</summary>
    /// <param name="operation">The operation whose callable API is generated.</param>
    /// <param name="hasParameters">Whether the operation accepts a parameter object.</param>
    /// <param name="hasResultType">Whether the operation returns a response union.</param>
    /// <param name="isCacheable">Whether Next.js cache metadata is generated.</param>
    /// <param name="cacheTagsFnName">The generated cache-tag helper name.</param>
    /// <returns>The generated operation declaration.</returns>
    private FunctionDeclarationNode BuildOperationFunction(
        IrOperation operation,
        bool hasParameters,
        bool hasResultType,
        bool isCacheable,
        string cacheTagsFnName)
    {
        var returnType = hasResultType
            ? Simple(_resultsBuilder.ResultTypeName(operation.MethodName))
            : Simple("void", isPrimitive: true);

        var parameters = ImmutableList.CreateBuilder<ParameterDeclarationNode>();
        if (hasParameters)
            parameters.Add(new ParameterDeclarationNode(
                name: new IdentifierNode("params"),
                type: Simple(_paramsBuilder.ParamsInterfaceName(operation.MethodName)),
                isRequired: true));

        // Next.js fetch options: RequestInit augmented with { next?: { revalidate, tags } }.
        parameters.Add(new ParameterDeclarationNode(
            name: new IdentifierNode("options"),
            type: BuildOptionsType(),
            isRequired: false,
            documentation: new CommentNode("Optional Next.js fetch options (cache, next.revalidate, next.tags, headers, …).", CommentKind.JSDoc)));

        parameters.Add(new ParameterDeclarationNode(
            name: new IdentifierNode("signal"),
            type: Simple("AbortSignal"),
            isRequired: false));

        parameters.Add(TypeScriptHttpClientInjection.Parameter(new TypeQueryTypeNode(Id("fetch"))));

        return new FunctionDeclarationNode(
            name: new IdentifierNode(operation.MethodName),
            returnType: new GenericTypeNode(new IdentifierNode("Promise"), ImmutableList.Create(returnType)),
            parameters: parameters.ToImmutable(),
            body: BuildOperationFunctionBody(operation, hasParameters, hasResultType, isCacheable, cacheTagsFnName),
            isAsync: true,
            documentation: BuildDocumentation(operation),
            isExported: true);
    }

    /// <summary>Builds validation, serialized request data, and response dispatch for one operation.</summary>
    /// <param name="operation">The operation whose request is generated.</param>
    /// <param name="hasParameters">Whether the operation accepts a parameter object.</param>
    /// <param name="hasResultType">Whether the operation returns a generated result union.</param>
    /// <param name="isCacheable">Whether Next.js cache metadata is emitted.</param>
    /// <param name="cacheTagsFnName">The generated operation cache-tag helper name.</param>
    /// <returns>The operation function body.</returns>
    private BlockNode BuildOperationFunctionBody(IrOperation operation, bool hasParameters, bool hasResultType, bool isCacheable, string cacheTagsFnName)
    {
        var statements = new List<StatementNode>();
        statements.Add(TypeScriptHttpClientInjection.Declaration("fetch"));
        var sources = TypeScriptParameterSources.Create(operation);

        if (_validation.IsEnabled)
            AddValidationParsingStatements(operation, sources, statements);

        if (operation.RequestBody is { IsForm: true } requestBody && sources.BodySource != null)
        {
            sources.BodySource = TypeScriptFormBody.Build(requestBody, sources.BodySource, ModelsByName, statements);
            statements.Add(new BlankLineStatementNode());
        }

        // fetch has no structured `query` option → build a URLSearchParams and fold it into the URL.
        var urlExpression = BuildUrlWithQuery(operation, sources, statements);

        statements.Add(new VariableDeclarationStatementNode(new IdentifierNode("requestUrl"), type: null, initializer: urlExpression));
        statements.Add(new BlankLineStatementNode());

        var requestHeaders = TypeScriptFetchHeaders.Build(
            operation, sources.HeadersSource, sources.HeadersSourceMayBeUndefined,
            Member(Id("options"), "headers", optional: true), statements);

        statements.Add(new VariableDeclarationStatementNode(
            new IdentifierNode("requestInit"),
            type: null,
            initializer: BuildFetchInit(operation, sources, requestHeaders, hasParameters, isCacheable, cacheTagsFnName)));
        statements.Add(new BlankLineStatementNode());

        var fetchCall = new CallExpressionNode(
            Id("httpClient"),
            ImmutableList.Create<ExpressionNode>(Id("requestUrl"), Id("requestInit")));

        if (!hasResultType)
        {
            statements.Add(new ExpressionStatementNode(new AwaitExpressionNode(fetchCall)));
            return new BlockNode(statements.ToImmutableList());
        }

        statements.Add(new VariableDeclarationStatementNode(
            new IdentifierNode("response"), type: null, initializer: new AwaitExpressionNode(fetchCall)));

        // responseData = response.status === 204 ? undefined : await response.json() as unknown
        var bodyJson = new CastExpressionNode(
            Simple("unknown", isPrimitive: true),
            new AwaitExpressionNode(new CallExpressionNode(Member(Id("response"), "json"), ImmutableList<ExpressionNode>.Empty)),
            useAsOperator: true);
        statements.Add(new VariableDeclarationStatementNode(
            new IdentifierNode("responseData"),
            type: null,
            initializer: TypeScriptResponseBody.Read(operation, Member(Id("response"), "status"), bodyJson)));
        statements.Add(new BlankLineStatementNode());

        AddStatusResultBranches(operation, statements);
        statements.Add(BuildFallbackResult(operation));

        return new BlockNode(statements.ToImmutableList());
    }

    /// <summary>
    /// Builds the request URL expression. When query params exist, emits a <c>URLSearchParams</c> populated
    /// from the params and returns <c>`{path}` + (query.size > 0 ? `?${query}` : "")</c>; otherwise the path.
    /// </summary>
    private static ExpressionNode BuildUrlWithQuery(IrOperation operation, TypeScriptParameterSources sources, List<StatementNode> statements)
    {
        // Path templating + query-string building are shared with the fetch transport (see TypeScriptQueryString).
        var pathExpression = TypeScriptRequestPath.BuildUrl(operation, sources.RouteSource);
        return TypeScriptQueryString.AppendQueryToUrl(
            operation, pathExpression, sources.QuerySource, sources.QuerySourceMayBeUndefined, statements);
    }

    /// <summary>Validates present parameter groups while preserving optional request-body absence.</summary>
    /// <param name="operation">The operation whose request is generated.</param>
    /// <param name="sources">The current raw or validated parameter group expressions.</param>
    /// <param name="statements">The endpoint statements receiving validation or request construction.</param>
    private void AddValidationParsingStatements(IrOperation operation, TypeScriptParameterSources sources, List<StatementNode> statements)
    {
        var opName = _namingConverter.ToPascalCase(operation.MethodName);

        if (operation.PathParameters.Count > 0 && sources.RouteSource != null)
        {
            statements.Add(new VariableDeclarationStatementNode(
                new IdentifierNode("parsedRoute"), type: null,
                initializer: _validation.ValidateNamed($"{opName}RouteParamsSchema", sources.RouteSource)));
            sources.RouteSource = Id("parsedRoute");
        }

        if (operation.QueryParameters.Count > 0 && sources.QuerySource != null)
        {
            var queryInput = sources.QuerySourceMayBeUndefined
                ? new BinaryExpressionNode(sources.QuerySource, BinaryOperator.NullishCoalescing, EmptyObject())
                : sources.QuerySource;
            statements.Add(new VariableDeclarationStatementNode(
                new IdentifierNode("parsedQuery"), type: null,
                initializer: _validation.ValidateNamed($"{opName}QueryParamsSchema", queryInput)));
            sources.QuerySource = Id("parsedQuery");
            sources.QuerySourceMayBeUndefined = false;
        }

        if (operation.RequestBody != null
            && Validation.ValidationTypes.IsValidatable(operation.RequestBody.Type)
            && sources.BodySource != null)
        {
            statements.Add(new VariableDeclarationStatementNode(
                new IdentifierNode("parsedBody"), type: null,
                initializer: sources.BodySourceMayBeUndefined
                    ? new ConditionalExpressionNode(
                        new BinaryExpressionNode(sources.BodySource, BinaryOperator.NotEqual, Id("undefined")),
                        _validation.Validate(operation.RequestBody.Type, sources.BodySource),
                        Id("undefined"))
                    : _validation.Validate(operation.RequestBody.Type, sources.BodySource)));
            sources.BodySource = Id("parsedBody");
        }

        if (statements.Count > 0)
            statements.Add(new BlankLineStatementNode());
    }

    /// <summary>
    /// Builds the fetch <c>RequestInit</c>: caller <c>options</c> first (so the fixed parts override them),
    /// then <c>method</c>, merged <c>headers</c>, JSON <c>body</c>, the Next <c>next</c> config (GET only,
    /// caller-overridable per field), and the optional <c>signal</c>.
    /// </summary>
    /// <param name="operation">The operation whose fetch initialization object is generated.</param>
    /// <param name="sources">The validated parameter sources used to populate headers and the request body.</param>
    /// <param name="requestHeaders">The normalized headers with caller overrides and operation defaults.</param>
    /// <param name="hasParameters">Whether the generated operation accepts a parameter object.</param>
    /// <param name="isCacheable">Whether the operation supports Next.js cache configuration.</param>
    /// <param name="cacheTagsFnName">The generated helper that derives cache tags for the operation.</param>
    /// <returns>The generated <c>RequestInit</c> object literal.</returns>
    private ObjectLiteralExpressionNode BuildFetchInit(IrOperation operation, TypeScriptParameterSources sources, ExpressionNode requestHeaders, bool hasParameters, bool isCacheable, string cacheTagsFnName)
    {
        var props = new List<PropertyAssignmentNode>
        {
            // Caller options first; the operation-fixed parts below override them.
            SpreadProperty(new BinaryExpressionNode(Id("options"), BinaryOperator.NullishCoalescing, EmptyObject())),
            new(new IdentifierNode("method"), String(operation.HttpMethod.ToUpperInvariant())),
        };

        props.Add(new PropertyAssignmentNode(new IdentifierNode("headers"), requestHeaders));

        if (operation.RequestBody != null && sources.BodySource != null)
        {
            ExpressionNode bodyExpression = operation.RequestBody.IsJson
                ? new CallExpressionNode(Member(Id("JSON"), "stringify"), ImmutableList.Create<ExpressionNode>(sources.BodySource))
                : new CastExpressionNode(Simple("string", isPrimitive: true),
                    new CastExpressionNode(Simple("unknown", isPrimitive: true), sources.BodySource, useAsOperator: true), useAsOperator: true);

            if (sources.BodySourceMayBeUndefined)
                props.Add(SpreadProperty(new ConditionalExpressionNode(
                    new BinaryExpressionNode(sources.BodySource, BinaryOperator.NotEqual, Id("undefined")),
                    Object(("body", bodyExpression)), EmptyObject())));
            else
                props.Add(new PropertyAssignmentNode(new IdentifierNode("body"), bodyExpression));
        }

        if (isCacheable)
            props.Add(new PropertyAssignmentNode(new IdentifierNode("next"), BuildNextConfig(cacheTagsFnName, hasParameters)));

        props.Add(SpreadProperty(new ConditionalExpressionNode(Id("signal"), Object(("signal", Id("signal"))), EmptyObject())));

        return new ObjectLiteralExpressionNode(props.ToImmutableList(), preferMultilineFormat: true);
    }

    /// <summary>
    /// Builds the Next config object:
    /// <c>{ [revalidate: N,] tags: get{Op}CacheTags(params), ...(options?.next ?? {}) }</c>.
    /// </summary>
    /// <remarks>
    /// The <c>tags</c> come from the shared <c>{op}.query-keys.ts</c> (<paramref name="cacheTagsFnName"/>), so the
    /// server-side fetch is tagged with exactly the hierarchical prefixes that the client-side TanStack query key
    /// is built from — a single cache identity. The trailing spread lets the caller override
    /// <c>revalidate</c>/<c>tags</c> per field.
    /// </remarks>
    /// <param name="cacheTagsFnName">The imported hierarchical cache-tags function name (<c>get{Op}CacheTags</c>).</param>
    /// <param name="hasParameters">Whether to pass the <c>params</c> argument to the cache-tags function.</param>
    /// <returns>The Next.js <c>next</c> config object literal.</returns>
    /// <seealso cref="BuildRevalidateHelper"/>
    private ObjectLiteralExpressionNode BuildNextConfig(string cacheTagsFnName, bool hasParameters)
    {
        var nextProps = new List<PropertyAssignmentNode>();
        if (_defaultRevalidate.HasValue)
            nextProps.Add(new PropertyAssignmentNode(new IdentifierNode("revalidate"), Number(_defaultRevalidate.Value)));
        var tagsArgs = hasParameters
            ? ImmutableList.Create<ExpressionNode>(Id("params"))
            : ImmutableList<ExpressionNode>.Empty;
        nextProps.Add(new PropertyAssignmentNode(new IdentifierNode("tags"),
            new CallExpressionNode(Id(cacheTagsFnName), tagsArgs)));
        nextProps.Add(SpreadProperty(new BinaryExpressionNode(
            Member(Id("options"), "next", optional: true), BinaryOperator.NullishCoalescing, EmptyObject())));
        return new ObjectLiteralExpressionNode(nextProps.ToImmutableList(), preferMultilineFormat: true);
    }

    /// <summary>Adds a result branch for every documented numeric status, including responses without content.</summary>
    /// <param name="operation">The operation whose response contract is generated.</param>
    /// <param name="statements">The function statements receiving the response branches.</param>
    private void AddStatusResultBranches(IrOperation operation, List<StatementNode> statements)
    {
        var resultTypeName = _resultsBuilder.ResultTypeName(operation.MethodName);
        var responseStatus = Member(Id("response"), "status");
        var responseData = Id("responseData");

        foreach (var response in operation.Responses.Where(r => int.TryParse(r.StatusCode, out _)))
        {
            var statusCode = int.Parse(response.StatusCode);
            var isSuccessful = statusCode >= 200 && statusCode < 300;
            var body = BuildResponseBody(operation, response, responseData);

            var returnObject = new CastExpressionNode(
                Simple(resultTypeName),
                Object(
                    ("status", Number(statusCode)),
                    ("body", body),
                    ("isSuccessful", Bool(isSuccessful)),
                    ("isFailed", Bool(!isSuccessful))),
                useAsOperator: true);

            statements.Add(new IfStatementNode(
                new BinaryExpressionNode(responseStatus, BinaryOperator.Equal, Number(statusCode)),
                new BlockNode(ImmutableList.Create<StatementNode>(new ReturnStatementNode(returnObject)))));
        }
    }

    private ReturnStatementNode BuildFallbackResult(IrOperation operation)
    {
        var resultTypeName = _resultsBuilder.ResultTypeName(operation.MethodName);
        // Undocumented status → catch-all `…ResultOther`, a literal failure so the union stays discriminable.
        return new ReturnStatementNode(new CastExpressionNode(
            Simple(resultTypeName),
            Object(
                ("status", Member(Id("response"), "status")),
                ("body", Id("responseData")),
                ("isSuccessful", Bool(false)),
                ("isFailed", Bool(true))),
            useAsOperator: true));
    }

    /// <summary>Builds undefined for bodyless responses or validates the documented response value.</summary>
    /// <param name="operation">The operation whose response contract is generated.</param>
    /// <param name="response">The documented response variant.</param>
    /// <param name="responseData">The decoded response data expression.</param>
    /// <returns>The decoded or validated response body value.</returns>
    private ExpressionNode BuildResponseBody(IrOperation operation, IrResponse response, ExpressionNode responseData)
    {
        if (response.Type == null)
            return Id("undefined");
        if (!_validation.IsEnabled || !Validation.ValidationTypes.IsValidatable(response.Type!))
            return responseData;
        if (response.Type is IrModelReference modelRef)
            return _validation.ValidateNamed($"{modelRef.ModelName}Schema", responseData);
        return _validation.ValidateNamed(
            Validation.ClientResponseSchemas.ResultSchemaName(_namingConverter, operation.MethodName, response.StatusCode),
            responseData);
    }

    private string BuildSchemaImportPath(string modelName, FileOrganization fileOrganization)
    {
        var modelsPath = fileOrganization.ModelsPath ?? "models";
        var modelFileName = _namingConverter.ToCamelCase(OriginalNameMap.GetTypeOriginalName(DocumentContext, modelName));
        return fileOrganization.SeparateModels
            ? $"../../{modelsPath}/{modelFileName}/{modelFileName}.schema"
            : $"../../{modelsPath}/{modelFileName}.schema";
    }

    private static CommentNode? BuildDocumentation(IrOperation operation)
    {
        var doc = operation.Description ?? operation.Summary;
        return string.IsNullOrWhiteSpace(doc) ? null : new CommentNode(doc, CommentKind.JSDoc);
    }

    private static PropertyAssignmentNode SpreadProperty(ExpressionNode value) =>
        new(new IdentifierNode("_"), value, isSpread: true);

    private static ObjectLiteralExpressionNode Object(params (string Name, ExpressionNode Value)[] properties) =>
        new(properties
            .Select(property => property.Name == "_"
                ? SpreadProperty(property.Value)
                : new PropertyAssignmentNode(new IdentifierNode(property.Name), property.Value))
            .ToImmutableList(),
            preferMultilineFormat: true);

    private static ObjectLiteralExpressionNode EmptyObject() => new(ImmutableList<PropertyAssignmentNode>.Empty);

    private static IdentifierExpressionNode Id(string name) => new(new IdentifierNode(name));

    private static MemberAccessExpressionNode Member(ExpressionNode source, string name, bool optional = false) =>
        new(source, new IdentifierNode(name), useOptionalChaining: optional);

    /// <summary>
    /// Builds the Next.js fetch options type
    /// <c>RequestInit &amp; { next?: { revalidate?: number | false; tags?: string[] } }</c> as fully-composed
    /// AST type nodes: an <see cref="IntersectionTypeNode"/> of the <c>RequestInit</c> identifier and an inline
    /// <see cref="TypeLiteralNode"/> for the Next.js fetch augmentation.
    /// </summary>
    /// <returns>The composed options parameter type (no raw type strings).</returns>
    private static TypeNode BuildOptionsType()
    {
        // { revalidate?: number | false; tags?: string[] }
        var nextConfigType = new TypeLiteralNode(ImmutableList.Create(
            new TypeLiteralMember(
                "revalidate",
                new UnionTypeNode(ImmutableList.Create<TypeNode>(
                    Simple("number", isPrimitive: true),
                    new SimpleTypeNode(new IdentifierNode("false"), literalValue: false))),
                isOptional: true),
            new TypeLiteralMember(
                "tags",
                new ArrayTypeNode(Simple("string", isPrimitive: true)),
                isOptional: true)));

        // { next?: { … } }
        var augmentation = new TypeLiteralNode(ImmutableList.Create(
            new TypeLiteralMember("next", nextConfigType, isOptional: true)));

        // RequestInit & { next?: { … } }
        return new IntersectionTypeNode(ImmutableList.Create<TypeNode>(Simple("RequestInit"), augmentation));
    }

    private static SimpleTypeNode Simple(string name, bool isPrimitive = false) => new(new IdentifierNode(name), isPrimitive);

    private static LiteralExpressionNode String(string value) => new(value, LiteralKind.String);

    private static LiteralExpressionNode Number(int value) => new(value, LiteralKind.Number);

    private static LiteralExpressionNode Bool(bool value) => new(value, LiteralKind.Boolean);
}
