using ValIO.Core.Ast.Builders;
using System.Collections.Immutable;
using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Expressions;
using ValIO.Core.Ast.Nodes.Statements;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Core.Interfaces;
using ValIO.Core.IR;
using ValIO.Generators.TypeScript.Ast.Builders;
using ValIO.Generators.TypeScript.Ast.JsDoc;
using ValIO.Generators.TypeScript.Ast.Nodes;
using ValIO.Generators.TypeScript.Ast.Nodes.Expressions;
namespace ValIO.Generators.TypeScript.TanStackQuery.Ast.Builders;

/// <summary>
/// Строит AST (SourceFileNode) для TanStack Query hook-файла.
/// Использует AST-узлы для типов, тел методов и объявлений.
/// </summary>
public sealed class TanStackQueryHookAstBuilder : AstBuilderBase
{
    private readonly INamingConverter _namingConverter;
    private readonly TypeScriptParamsAstBuilder _paramsBuilder;
    private readonly TypeScriptResultsAstBuilder _resultsBuilder;
    private readonly TanStackQueryTypeBuilder _typeBuilder;
    private readonly bool _hookUsesAxios;
    private readonly string _clientSuffix;

    /// <summary>Creates a TanStackQueryHookAstBuilder for one finalized IR document.</summary>
    /// <param name="documentContext">The shared immutable indexes of the finalized IR document.</param>
    /// <param name="namingConverter">The generated identifier naming policy.</param>
    /// <param name="typeBuilder">The language type mapping function.</param>
    /// <param name="clientSuffix">The selected client transport suffix.</param>
    /// <param name="resultSuffix">The optional operation result type suffix.</param>
    /// <param name="resultPrefix">The optional operation result type prefix.</param>
    /// <exception cref="ArgumentNullException">A required context or collaborator is null.</exception>
    public TanStackQueryHookAstBuilder(IrDocumentContext documentContext,
        INamingConverter namingConverter,
        Func<ValIO.Core.IR.Types.IrType, ValIO.Core.Ast.Nodes.TypeNode> typeBuilder,
        string clientSuffix = "axios",
        string? resultSuffix = null,
        string? resultPrefix = null)
        : base(documentContext)
    {
        _namingConverter = namingConverter ?? throw new ArgumentNullException(nameof(namingConverter));
        _clientSuffix = string.IsNullOrWhiteSpace(clientSuffix) ? "fetch" : clientSuffix;
        _hookUsesAxios = _clientSuffix.Equals("axios", StringComparison.OrdinalIgnoreCase);
        _paramsBuilder = new TypeScriptParamsAstBuilder(documentContext, namingConverter, typeBuilder);
        _resultsBuilder = new TypeScriptResultsAstBuilder(documentContext, namingConverter, typeBuilder, resultSuffix, resultPrefix);
        _typeBuilder = new TanStackQueryTypeBuilder(namingConverter, _hookUsesAxios);
    }

    /// <summary>
    /// Строит SourceFileNode для hook-файла операции.
    /// </summary>
    public SourceFileNode BuildHookSourceFile(
        IrOperation operation,
        string operationFileName,
        bool isMutation)
    {
        var hookBaseName = _namingConverter.ToPascalCase(operation.MethodName);
        var hasParams = _paramsBuilder.HasParameters(operation);
        var paramsTypeName = hasParams ? _paramsBuilder.ParamsInterfaceName(operation.MethodName) : null;

        var resultTypeName = _resultsBuilder.HasResponsesWithBody(operation)
            ? _resultsBuilder.ResultTypeName(operation.MethodName)
            : null;

        var dataType = _typeBuilder.BuildDataType(resultTypeName);
        var variablesType = _typeBuilder.BuildVariablesType(paramsTypeName);

        var imports = BuildImports(operation, operationFileName, hookBaseName, hasParams, paramsTypeName, resultTypeName, isMutation, _clientSuffix, _hookUsesAxios);
        var declarations = isMutation
            ? BuildMutationDeclarations(operation, hookBaseName, hasParams, paramsTypeName, resultTypeName, dataType, variablesType)
            : BuildQueryDeclarations(operation, hookBaseName, paramsTypeName, hasParams, resultTypeName, dataType);

        return new SourceFileNode(imports: imports, declarations: declarations,
            fileDocumentation: TypeScriptDoc.FileHeader(
                $"{operationFileName}.query.ts",
                $"endpoints/{operationFileName}",
                $"TanStack Query (React) hooks for the {operation.MethodName} operation."));
    }

    /// <summary>
    /// Builds the import statements for a generated TanStack Query (React) hook file.
    /// </summary>
    /// <remarks>
    /// The <c>@tanstack/react-query</c> symbols are split into two <see cref="ImportNode"/>s — a plain
    /// value import for the runtime hooks (<c>useQuery</c>, <c>useMutation</c>, …) and an
    /// <see cref="ImportNode.IsTypeOnly"/> import for the type-only symbols (<c>UseQueryOptions</c>,
    /// <c>QueryFunctionContext</c>, <c>QueryClient</c>, …). Emitting the type symbols as a value import
    /// breaks TypeScript's <c>verbatimModuleSyntax</c> (TS1484), so they must use <c>import type</c>.
    /// <c>QueryClient</c> is a runtime class but is referenced here exclusively in type position
    /// (parameter/return annotations), so it belongs in the type-only group.
    /// </remarks>
    /// <param name="operation">The operation the hook wraps.</param>
    /// <param name="operationFileName">The operation's file-name stem, used to build relative import paths.</param>
    /// <param name="hasParams">Whether the operation has request parameters (drives the params import).</param>
    /// <param name="paramsTypeName">The generated params interface name, or <see langword="null"/> when the operation has none.</param>
    /// <param name="resultTypeName">The generated result type name, or <see langword="null"/> when the operation has no response body.</param>
    /// <param name="isMutation">Whether the hook is a mutation (<see langword="true"/>) or a query (<see langword="false"/>).</param>
    /// <param name="clientSuffix">The transport file suffix (e.g. <c>axios</c>) of the client function module to import.</param>
    /// <param name="hookUsesAxios">Whether the hook references <c>AxiosRequestConfig</c> and therefore needs the axios type import.</param>
    /// <returns>The ordered list of <see cref="ImportNode"/>s for the hook file.</returns>
    /// <seealso cref="TypeScriptAstRenderer"/>
    private static IReadOnlyList<ImportNode> BuildImports(
        IrOperation operation,
        string operationFileName,
        string hookBaseName,
        bool hasParams,
        string? paramsTypeName,
        string? resultTypeName,
        bool isMutation,
        string clientSuffix,
        bool hookUsesAxios)
    {
        // Runtime hook functions — must stay a plain value import.
        var tanstackValues = isMutation
            ? new[]
            {
                new IdentifierNode("useMutation"),
                new IdentifierNode("useIsMutating"),
            }
            : new[]
            {
                new IdentifierNode("useInfiniteQuery"),
                new IdentifierNode("useQuery"),
                new IdentifierNode("useIsFetching"),
            };

        // Type-only symbols — must use `import type` under verbatimModuleSyntax (TS1484).
        // QueryClient is a runtime class but is only referenced in type position in this file.
        var tanstackTypes = isMutation
            ? new[]
            {
                new IdentifierNode("QueryClient"),
                new IdentifierNode("UseMutationOptions"),
                new IdentifierNode("UseMutationResult"),
                new IdentifierNode("MutationFunctionContext"),
            }
            : new[]
            {
                new IdentifierNode("QueryClient"),
                new IdentifierNode("QueryFunctionContext"),
                new IdentifierNode("UseQueryOptions"),
                new IdentifierNode("UseInfiniteQueryOptions"),
                new IdentifierNode("UseQueryResult"),
                new IdentifierNode("UseInfiniteQueryResult"),
                new IdentifierNode("InfiniteData"),
            };

        var items = new List<ImportNode>
        {
            new(items: tanstackValues.ToImmutableList(), from: "@tanstack/react-query", kind: ImportKind.Named),
            new(items: tanstackTypes.ToImmutableList(), from: "@tanstack/react-query", kind: ImportKind.Named, isTypeOnly: true),
            new(items: new[] { new IdentifierNode(operation.MethodName) }.ToImmutableList(), from: $"./{operationFileName}.{clientSuffix}", kind: ImportKind.Named)
        };

        // Query key factories live in the shared, framework-agnostic {op}.query-keys.ts so the Next.js transport
        // and the client-side hooks share one cache identity (see TypeScriptQueryKeysAstBuilder).
        if (!isMutation)
            items.Add(new ImportNode(
                items: new[]
                {
                    new IdentifierNode($"get{hookBaseName}QueryKey"),
                    new IdentifierNode($"get{hookBaseName}InfiniteQueryKey"),
                }.ToImmutableList(),
                from: $"./{operationFileName}.query-keys",
                kind: ImportKind.Named));

        if (hasParams && paramsTypeName != null)
            items.Add(new ImportNode(items: new[] { new IdentifierNode(paramsTypeName) }.ToImmutableList(), from: $"./{operationFileName}.params", kind: ImportKind.Named, isTypeOnly: true));

        if (resultTypeName != null)
            items.Add(new ImportNode(items: new[] { new IdentifierNode(resultTypeName) }.ToImmutableList(), from: $"./{operationFileName}.results", kind: ImportKind.Named, isTypeOnly: true));

        if (hookUsesAxios)
            items.Add(new ImportNode(items: new[] { new IdentifierNode("AxiosRequestConfig") }.ToImmutableList(), from: "axios", kind: ImportKind.Named, isTypeOnly: true));

        return items;
    }

    private IReadOnlyList<DeclarationNode> BuildQueryDeclarations(
        IrOperation operation,
        string hookBaseName,
        string? paramsTypeName,
        bool hasParams,
        string? resultTypeName,
        TypeNode dataType)
    {
        // get{Hook}QueryKey / get{Hook}InfiniteQueryKey are no longer declared here — they are imported from the
        // shared {op}.query-keys.ts module (see BuildImports and TypeScriptQueryKeysAstBuilder).
        var decls = new List<DeclarationNode>();

        decls.Add(_typeBuilder.BuildQueryOptionsParamsInterface(hookBaseName, dataType, isInfinite: false));
        decls.Add(_typeBuilder.BuildQueryOptionsParamsInterface(hookBaseName, dataType, isInfinite: true));
        decls.Add(_typeBuilder.BuildQueryOptionsResultTypeAlias(hookBaseName, dataType, isInfinite: false));
        decls.Add(_typeBuilder.BuildQueryOptionsResultTypeAlias(hookBaseName, dataType, isInfinite: true));

        var paramsType = paramsTypeName != null ? TanStackQueryTypeBuilder.SimpleType(paramsTypeName) : null;
        var queryOptionsParamsType = new IdentifierNode($"Get{hookBaseName}QueryOptionsParams");
        var infiniteQueryOptionsParamsType = new IdentifierNode($"Get{hookBaseName}InfiniteQueryOptionsParams");
        var queryOptionsResultType = new IdentifierNode($"Get{hookBaseName}QueryOptionsResult");
        var infiniteQueryOptionsResultType = new IdentifierNode($"Get{hookBaseName}InfiniteQueryOptionsResult");

        decls.Add(BuildGetInfiniteQueryOptionsFn(operation, hookBaseName, paramsType, hasParams, dataType, new IdentifierNode($"get{hookBaseName}InfiniteQueryKey"), new IdentifierNode($"get{hookBaseName}InfiniteQueryOptions"), infiniteQueryOptionsParamsType, infiniteQueryOptionsResultType));
        decls.Add(BuildGetQueryOptionsFn(operation, hookBaseName, paramsType, hasParams, dataType, new IdentifierNode($"get{hookBaseName}QueryKey"), new IdentifierNode($"get{hookBaseName}QueryOptions"), queryOptionsParamsType, queryOptionsResultType));
        decls.Add(new TypeAliasDeclarationNode(new IdentifierNode($"{hookBaseName}QueryResult"), dataType));
        decls.Add(new TypeAliasDeclarationNode(new IdentifierNode($"{hookBaseName}QueryError"), TanStackQueryTypeBuilder.SimpleType("Error")));
        decls.Add(BuildUseQueryFn(operation, hookBaseName, paramsType, hasParams, queryOptionsParamsType, queryOptionsResultType, dataType));
        decls.Add(BuildUseInfiniteQueryFn(operation, hookBaseName, paramsType, hasParams, infiniteQueryOptionsParamsType, infiniteQueryOptionsResultType, dataType));
        decls.Add(BuildPrefetchQueryFn(operation, hookBaseName, paramsType, hasParams, queryOptionsParamsType, queryOptionsResultType));
        decls.Add(BuildPrefetchInfiniteQueryFn(operation, hookBaseName, paramsType, hasParams, infiniteQueryOptionsParamsType, infiniteQueryOptionsResultType));
        decls.Add(BuildUseIsFetchingFn(hookBaseName, paramsType, hasParams));

        return decls;
    }

    /// <summary>
    /// Builds the declarations (options interfaces, the <c>get…MutationOptions</c> factory, convenience
    /// aliases and the <c>use…</c> hook) for a mutation operation's hook file.
    /// </summary>
    /// <param name="operation">The mutation operation.</param>
    /// <param name="hookBaseName">The PascalCase hook base name (e.g. <c>ClearPinCode</c>).</param>
    /// <param name="hasParams">
    /// Whether the operation has request parameters. When <see langword="false"/> the mutation's
    /// variables type is <c>void</c> and the generated <c>mutationFn</c> must not forward a variables
    /// argument to the client function (see <see cref="BuildGetMutationOptionsFn"/>).
    /// </param>
    /// <param name="paramsTypeName">The params interface name, or <see langword="null"/> when absent.</param>
    /// <param name="resultTypeName">The result type name, or <see langword="null"/> when the operation has no response body.</param>
    /// <param name="dataType">The hook's <c>TData</c> type node.</param>
    /// <param name="variablesType">The hook's <c>TVariables</c> type node (<c>void</c> when <paramref name="hasParams"/> is <see langword="false"/>).</param>
    /// <returns>The ordered declaration nodes for the mutation hook file.</returns>
    private IReadOnlyList<DeclarationNode> BuildMutationDeclarations(
        IrOperation operation,
        string hookBaseName,
        bool hasParams,
        string? paramsTypeName,
        string? resultTypeName,
        TypeNode dataType,
        TypeNode variablesType)
    {
        var decls = new List<DeclarationNode>();

        decls.Add(_typeBuilder.BuildMutationOptionsParamsInterface(hookBaseName, dataType, variablesType));
        decls.Add(_typeBuilder.BuildMutationOptionsResultTypeAlias(hookBaseName, dataType, variablesType));

        var optionsParamsType = new IdentifierNode($"Get{hookBaseName}MutationOptionsParams");
        var optionsResultType = new IdentifierNode($"Get{hookBaseName}MutationOptionsResult");

        decls.Add(BuildGetMutationOptionsFn(operation, hookBaseName, hasParams, variablesType, optionsParamsType, optionsResultType));
        decls.Add(new TypeAliasDeclarationNode(new IdentifierNode($"{hookBaseName}MutationResult"), dataType));
        decls.Add(new TypeAliasDeclarationNode(new IdentifierNode($"{hookBaseName}MutationError"), TanStackQueryTypeBuilder.SimpleType("Error")));
        decls.Add(BuildUseMutationFn(operation, hookBaseName, optionsParamsType, optionsResultType, dataType, variablesType));
        decls.Add(BuildUseIsMutatingFn(operation, hookBaseName));

        return decls;
    }

    private FunctionDeclarationNode BuildGetInfiniteQueryOptionsFn(
        IrOperation operation,
        string hookBaseName,
        TypeNode? paramsType,
        bool hasParams,
        TypeNode dataType,
        IdentifierNode getQueryKeyFnName,
        IdentifierNode getQueryOptionsFnName,
        IdentifierNode optionsParamsType,
        IdentifierNode optionsResultType)
    {
        return BuildGetQueryOptionsFnInternal(operation, hookBaseName, paramsType, hasParams, dataType, getQueryKeyFnName, getQueryOptionsFnName, optionsParamsType, optionsResultType, isInfinite: true);
    }

    private FunctionDeclarationNode BuildGetQueryOptionsFn(
        IrOperation operation,
        string hookBaseName,
        TypeNode? paramsType,
        bool hasParams,
        TypeNode dataType,
        IdentifierNode getQueryKeyFnName,
        IdentifierNode getQueryOptionsFnName,
        IdentifierNode optionsParamsType,
        IdentifierNode optionsResultType)
    {
        return BuildGetQueryOptionsFnInternal(operation, hookBaseName, paramsType, hasParams, dataType, getQueryKeyFnName, getQueryOptionsFnName, optionsParamsType, optionsResultType, isInfinite: false);
    }

    private FunctionDeclarationNode BuildGetQueryOptionsFnInternal(
        IrOperation operation,
        string hookBaseName,
        TypeNode? paramsType,
        bool hasParams,
        TypeNode dataType,
        IdentifierNode getQueryKeyFnName,
        IdentifierNode getQueryOptionsFnName,
        IdentifierNode optionsParamsType,
        IdentifierNode optionsResultType,
        bool isInfinite)
    {
        var optionsRef = new IdentifierExpressionNode(new IdentifierNode("options"));
        var queryOptsRef = new MemberAccessExpressionNode(optionsRef, new IdentifierNode("query"), useOptionalChaining: true);
        var queryKeyFromOpts = new MemberAccessExpressionNode(queryOptsRef, new IdentifierNode("queryKey"), useOptionalChaining: true);

        var queryKeyVar = new VariableDeclarationStatementNode(
            new IdentifierNode("queryKey"),
            _typeBuilder.BuildQueryKeyReturnType(),
            new BinaryExpressionNode(
                queryKeyFromOpts,
                BinaryOperator.NullishCoalescing,
                new CallExpressionNode(
                    new IdentifierExpressionNode(getQueryKeyFnName),
                    hasParams ? new[] { new IdentifierExpressionNode(new IdentifierNode("params")) }.ToImmutableList() : ImmutableList<ExpressionNode>.Empty)));

        var apiCallArgs = new List<ExpressionNode>();
        if (hasParams)
            apiCallArgs.Add(new IdentifierExpressionNode(new IdentifierNode("params")));
        apiCallArgs.Add(new MemberAccessExpressionNode(optionsRef, new IdentifierNode("request"), useOptionalChaining: true));
        apiCallArgs.Add(new MemberAccessExpressionNode(new IdentifierExpressionNode(new IdentifierNode("context")), new IdentifierNode("signal")));

        var queryFnExpr = new CallExpressionNode(
            new IdentifierExpressionNode(new IdentifierNode(operation.MethodName)),
            apiCallArgs.ToImmutableList());

        var contextType = isInfinite
            ? _typeBuilder.BuildQueryFunctionContextForInfiniteType()
            : _typeBuilder.BuildQueryFunctionContextType();
        var queryFnArrow = new ArrowFunctionExpressionNode(
            new[] { new ParameterDeclarationNode(new IdentifierNode("context"), contextType, isRequired: true) }.ToImmutableList(),
            queryFnExpr);

        // BUG #11: spread the caller's query options first so enabled / placeholderData / retry / staleTime /
        // select / … survive. The generated queryKey/queryFn (and the infinite-only fields below) are appended
        // afterwards so they always win over anything the caller passed.
        var returnObjProps = new List<PropertyAssignmentNode>
        {
            new(new IdentifierNode("query"), queryOptsRef, isSpread: true),
            new(new IdentifierNode("queryKey"), new IdentifierExpressionNode(new IdentifierNode("queryKey"))),
            new(new IdentifierNode("queryFn"), queryFnArrow),
        };
        if (isInfinite)
        {
            returnObjProps.Add(new PropertyAssignmentNode(new IdentifierNode("initialPageParam"), new LiteralExpressionNode(0, LiteralKind.Number)));
            returnObjProps.Add(new PropertyAssignmentNode(new IdentifierNode("getNextPageParam"), new ArrowFunctionExpressionNode(
                ImmutableList<ParameterDeclarationNode>.Empty,
                new LiteralExpressionNode(null, LiteralKind.Null))));
        }
        var returnObj = new ObjectLiteralExpressionNode(returnObjProps.ToImmutableList(), type: null, metadata: null, preferMultilineFormat: true);

        var resultType = isInfinite
            ? _typeBuilder.BuildUseInfiniteQueryOptionsType(dataType)
            : _typeBuilder.BuildUseQueryOptionsType(dataType);
        var statements = new List<StatementNode>
        {
            queryKeyVar,
            new VariableDeclarationStatementNode(new IdentifierNode("result"), resultType, returnObj),
            new ReturnStatementNode(new IdentifierExpressionNode(new IdentifierNode("result")))
        };

        var parameters = new List<ParameterDeclarationNode>();
        if (hasParams && paramsType != null)
            parameters.Add(new ParameterDeclarationNode(new IdentifierNode("params"), paramsType, isRequired: true));
        parameters.Add(new ParameterDeclarationNode(new IdentifierNode("options"), new SimpleTypeNode(optionsParamsType, isPrimitive: false), isRequired: false));

        return new FunctionDeclarationNode(
            getQueryOptionsFnName,
            returnType: resultType,
            parameters: parameters.ToImmutableList(),
            body: new BlockNode(statements.ToImmutableList()),
            isArrowFunction: true,
            documentation: new CommentNode(isInfinite ? "Options for useInfiniteQuery." : "Options for useQuery.", CommentKind.JSDoc),
            isExported: true);
    }

    private FunctionDeclarationNode BuildUseQueryFn(
        IrOperation operation,
        string hookBaseName,
        TypeNode? paramsType,
        bool hasParams,
        IdentifierNode optionsParamsType,
        IdentifierNode optionsResultType,
        TypeNode dataType)
    {
        var getOptsArgs = hasParams
            ? new[] { new IdentifierExpressionNode(new IdentifierNode("params")), new IdentifierExpressionNode(new IdentifierNode("options")) }.ToImmutableList()
            : new[] { new IdentifierExpressionNode(new IdentifierNode("options")) }.ToImmutableList();

        var body = new BlockNode(new StatementNode[]
        {
            new VariableDeclarationStatementNode(
                new IdentifierNode("queryOptions"),
                new SimpleTypeNode(optionsResultType, isPrimitive: false),
                new CallExpressionNode(new IdentifierExpressionNode(new IdentifierNode($"get{hookBaseName}QueryOptions")), getOptsArgs)),
            new ReturnStatementNode(new CallExpressionNode(
                new IdentifierExpressionNode(new IdentifierNode("useQuery")),
                new[] { new IdentifierExpressionNode(new IdentifierNode("queryOptions")), new IdentifierExpressionNode(new IdentifierNode("queryClient")) }.ToImmutableList()))
        }.ToImmutableList());

        var parameters = BuildUseQueryParams(paramsType, hasParams, optionsParamsType);
        return new FunctionDeclarationNode(
            new IdentifierNode($"use{hookBaseName}"),
            returnType: _typeBuilder.BuildUseQueryResultType(dataType),
            parameters: parameters,
            body: body,
            isArrowFunction: true,
            documentation: new CommentNode("Fetches and caches data.", CommentKind.JSDoc),
            isExported: true);
    }

    private FunctionDeclarationNode BuildUseInfiniteQueryFn(
        IrOperation operation,
        string hookBaseName,
        TypeNode? paramsType,
        bool hasParams,
        IdentifierNode optionsParamsType,
        IdentifierNode optionsResultType,
        TypeNode dataType)
    {
        var getOptsArgs = hasParams
            ? new[] { new IdentifierExpressionNode(new IdentifierNode("params")), new IdentifierExpressionNode(new IdentifierNode("options")) }.ToImmutableList()
            : new[] { new IdentifierExpressionNode(new IdentifierNode("options")) }.ToImmutableList();

        var body = new BlockNode(new StatementNode[]
        {
            new VariableDeclarationStatementNode(
                new IdentifierNode("queryOptions"),
                new SimpleTypeNode(optionsResultType, isPrimitive: false),
                new CallExpressionNode(new IdentifierExpressionNode(new IdentifierNode($"get{hookBaseName}InfiniteQueryOptions")), getOptsArgs)),
            new ReturnStatementNode(new CallExpressionNode(
                new IdentifierExpressionNode(new IdentifierNode("useInfiniteQuery")),
                new[] { new IdentifierExpressionNode(new IdentifierNode("queryOptions")), new IdentifierExpressionNode(new IdentifierNode("queryClient")) }.ToImmutableList()))
        }.ToImmutableList());

        return new FunctionDeclarationNode(
            new IdentifierNode($"use{hookBaseName}Infinite"),
            returnType: _typeBuilder.BuildUseInfiniteQueryResultType(dataType),
            parameters: BuildUseQueryParams(paramsType, hasParams, optionsParamsType),
            body: body,
            isArrowFunction: true,
            documentation: new CommentNode("Fetches paginated data.", CommentKind.JSDoc),
            isExported: true);
    }

    private FunctionDeclarationNode BuildPrefetchQueryFn(
        IrOperation operation,
        string hookBaseName,
        TypeNode? paramsType,
        bool hasParams,
        IdentifierNode optionsParamsType,
        IdentifierNode optionsResultType)
    {
        var getOptsArgs = hasParams
            ? new[] { new IdentifierExpressionNode(new IdentifierNode("params")), new IdentifierExpressionNode(new IdentifierNode("options")) }.ToImmutableList()
            : new[] { new IdentifierExpressionNode(new IdentifierNode("options")) }.ToImmutableList();

        var body = new BlockNode(new StatementNode[]
        {
            new VariableDeclarationStatementNode(
                new IdentifierNode("queryOptions"),
                new SimpleTypeNode(optionsResultType, isPrimitive: false),
                new CallExpressionNode(new IdentifierExpressionNode(new IdentifierNode($"get{hookBaseName}QueryOptions")), getOptsArgs)),
            new ExpressionStatementNode(new AwaitExpressionNode(new CallExpressionNode(
                new MemberAccessExpressionNode(new IdentifierExpressionNode(new IdentifierNode("queryClient")), new IdentifierNode("prefetchQuery")),
                new[] { new IdentifierExpressionNode(new IdentifierNode("queryOptions")) }.ToImmutableList()))),
            new ReturnStatementNode(new IdentifierExpressionNode(new IdentifierNode("queryClient")))
        }.ToImmutableList());

        var parameters = new List<ParameterDeclarationNode>
        {
            new(new IdentifierNode("queryClient"), TanStackQueryTypeBuilder.SimpleType("QueryClient"), isRequired: true)
        };
        if (hasParams && paramsType != null)
            parameters.Add(new ParameterDeclarationNode(new IdentifierNode("params"), paramsType, isRequired: true));
        parameters.Add(new ParameterDeclarationNode(new IdentifierNode("options"), new SimpleTypeNode(optionsParamsType, isPrimitive: false), isRequired: false));

        return new FunctionDeclarationNode(
            new IdentifierNode($"prefetch{hookBaseName}Query"),
            returnType: _typeBuilder.BuildPromiseQueryClientType(),
            parameters: parameters.ToImmutableList(),
            body: body,
            isArrowFunction: true,
            isAsync: true,
            documentation: new CommentNode("Prefetches data into cache.", CommentKind.JSDoc),
            isExported: true);
    }

    private FunctionDeclarationNode BuildPrefetchInfiniteQueryFn(
        IrOperation operation,
        string hookBaseName,
        TypeNode? paramsType,
        bool hasParams,
        IdentifierNode optionsParamsType,
        IdentifierNode optionsResultType)
    {
        var getOptsArgs = hasParams
            ? new[] { new IdentifierExpressionNode(new IdentifierNode("params")), new IdentifierExpressionNode(new IdentifierNode("options")) }.ToImmutableList()
            : new[] { new IdentifierExpressionNode(new IdentifierNode("options")) }.ToImmutableList();

        var body = new BlockNode(new StatementNode[]
        {
            new VariableDeclarationStatementNode(
                new IdentifierNode("queryOptions"),
                new SimpleTypeNode(optionsResultType, isPrimitive: false),
                new CallExpressionNode(new IdentifierExpressionNode(new IdentifierNode($"get{hookBaseName}InfiniteQueryOptions")), getOptsArgs)),
            new ExpressionStatementNode(new AwaitExpressionNode(new CallExpressionNode(
                new MemberAccessExpressionNode(new IdentifierExpressionNode(new IdentifierNode("queryClient")), new IdentifierNode("prefetchInfiniteQuery")),
                new[] { new IdentifierExpressionNode(new IdentifierNode("queryOptions")) }.ToImmutableList()))),
            new ReturnStatementNode(new IdentifierExpressionNode(new IdentifierNode("queryClient")))
        }.ToImmutableList());

        var parameters = new List<ParameterDeclarationNode>
        {
            new(new IdentifierNode("queryClient"), TanStackQueryTypeBuilder.SimpleType("QueryClient"), isRequired: true)
        };
        if (hasParams && paramsType != null)
            parameters.Add(new ParameterDeclarationNode(new IdentifierNode("params"), paramsType, isRequired: true));
        parameters.Add(new ParameterDeclarationNode(new IdentifierNode("options"), new SimpleTypeNode(optionsParamsType, isPrimitive: false), isRequired: false));

        return new FunctionDeclarationNode(
            new IdentifierNode($"prefetch{hookBaseName}InfiniteQuery"),
            returnType: _typeBuilder.BuildPromiseQueryClientType(),
            parameters: parameters.ToImmutableList(),
            body: body,
            isArrowFunction: true,
            isAsync: true,
            documentation: new CommentNode("Prefetches infinite query data.", CommentKind.JSDoc),
            isExported: true);
    }

    private FunctionDeclarationNode BuildUseIsFetchingFn(string hookBaseName, TypeNode? paramsType, bool hasParams)
    {
        var getKeyCall = new CallExpressionNode(
            new IdentifierExpressionNode(new IdentifierNode($"get{hookBaseName}QueryKey")),
            hasParams ? new[] { new IdentifierExpressionNode(new IdentifierNode("params")) }.ToImmutableList() : ImmutableList<ExpressionNode>.Empty);

        var body = new BlockNode(new[]
        {
            new ReturnStatementNode(new CallExpressionNode(
                new IdentifierExpressionNode(new IdentifierNode("useIsFetching")),
                new[] { new ObjectLiteralExpressionNode(new[] { new PropertyAssignmentNode(new IdentifierNode("queryKey"), getKeyCall) }.ToImmutableList()) }.ToImmutableList()))
        }.ToImmutableList());

        // params is required because get{Hook}QueryKey inlines params.route.id / params.query and requires it.
        var parameters = hasParams && paramsType != null
            ? new[] { new ParameterDeclarationNode(new IdentifierNode("params"), paramsType, isRequired: true) }.ToImmutableList()
            : ImmutableList<ParameterDeclarationNode>.Empty;

        return new FunctionDeclarationNode(
            new IdentifierNode($"useIsFetching{hookBaseName}"),
            returnType: TanStackQueryTypeBuilder.SimpleType("number", isPrimitive: true),
            parameters: parameters,
            body: body,
            isArrowFunction: true,
            documentation: new CommentNode("Returns true if this query is currently fetching.", CommentKind.JSDoc),
            isExported: true);
    }

    private static IReadOnlyList<ParameterDeclarationNode> BuildUseQueryParams(
        TypeNode? paramsType,
        bool hasParams,
        IdentifierNode optionsParamsType)
    {
        var list = new List<ParameterDeclarationNode>();
        if (hasParams && paramsType != null)
            list.Add(new ParameterDeclarationNode(new IdentifierNode("params"), paramsType, isRequired: true));
        list.Add(new ParameterDeclarationNode(new IdentifierNode("options"), new SimpleTypeNode(optionsParamsType, isPrimitive: false), isRequired: false));
        list.Add(new ParameterDeclarationNode(new IdentifierNode("queryClient"), TanStackQueryTypeBuilder.SimpleType("QueryClient"), isRequired: false));
        return list;
    }

    /// <summary>
    /// Builds the <c>get…MutationOptions</c> factory whose <c>mutationFn</c> calls the generated client function.
    /// </summary>
    /// <remarks>
    /// For a <b>parameterless</b> operation the mutation's variables type is <c>void</c>, and the client
    /// function's first parameter is its <c>options</c> slot (e.g. <c>AxiosRequestConfig</c>). Forwarding the
    /// <c>variables</c> (typed <c>void</c>) argument would land <c>void</c> in that slot — TS2345. So when
    /// <paramref name="hasParams"/> is <see langword="false"/> the arrow keeps a <c>_variables</c> placeholder
    /// (named with a leading underscore to satisfy <c>noUnusedParameters</c>, and still typed to keep the
    /// arrow assignable to <c>MutationFunction&lt;TData, void&gt;</c>) but calls the client function with
    /// <c>requestOptions</c> only, so <c>requestOptions</c> occupies the correct <c>options</c> slot.
    /// </remarks>
    /// <param name="operation">The mutation operation.</param>
    /// <param name="hookBaseName">The PascalCase hook base name.</param>
    /// <param name="hasParams">Whether the operation has request parameters; see the remarks for the parameterless behavior.</param>
    /// <param name="variablesType">The <c>TVariables</c> type node (<c>void</c> when <paramref name="hasParams"/> is <see langword="false"/>).</param>
    /// <param name="optionsParamsType">The generated <c>Get…MutationOptionsParams</c> type name.</param>
    /// <param name="optionsResultType">The generated <c>Get…MutationOptionsResult</c> type name.</param>
    /// <returns>The exported <c>get…MutationOptions</c> arrow function declaration.</returns>
    private FunctionDeclarationNode BuildGetMutationOptionsFn(
        IrOperation operation,
        string hookBaseName,
        bool hasParams,
        TypeNode variablesType,
        IdentifierNode optionsParamsType,
        IdentifierNode optionsResultType)
    {
        var mutationKey = new ArrayLiteralExpressionNode(
            new[] { new LiteralExpressionNode(_namingConverter.ToCamelCase(operation.MethodName), LiteralKind.String) }.ToImmutableList());

        // MutationFunction<TData, TVariables> = (variables, context: MutationFunctionContext) => Promise<TData>.
        // Parameterless mutations have TVariables = void: keep a (`_`-prefixed) placeholder param but do NOT
        // forward it to the client function, otherwise `void` lands in the client's options slot (TS2345).
        var contextParam = new ParameterDeclarationNode(new IdentifierNode("_context"), _typeBuilder.BuildMutationFunctionContextType(), isRequired: true);
        var variablesParamName = hasParams ? "variables" : "_variables";
        var clientCallArgs = hasParams
            ? new[]
            {
                new IdentifierExpressionNode(new IdentifierNode("variables")),
                new IdentifierExpressionNode(new IdentifierNode("requestOptions")),
            }
            : new[]
            {
                new IdentifierExpressionNode(new IdentifierNode("requestOptions")),
            };
        var mutationFn = new ArrowFunctionExpressionNode(
            new[] { new ParameterDeclarationNode(new IdentifierNode(variablesParamName), variablesType, isRequired: true), contextParam }.ToImmutableList(),
            new CallExpressionNode(
                new IdentifierExpressionNode(new IdentifierNode(operation.MethodName)),
                clientCallArgs.ToImmutableList()));

        // BUG #11 (mutation): spread the caller's mutation options first (onSuccess / onError / retry /
        // onMutate / …); the generated mutationKey/mutationFn are appended afterwards so they win.
        var mutationOptsRef = new MemberAccessExpressionNode(
            new IdentifierExpressionNode(new IdentifierNode("options")),
            new IdentifierNode("mutation"),
            useOptionalChaining: true);
        var resultObj = new ObjectLiteralExpressionNode(new PropertyAssignmentNode[]
        {
            new(new IdentifierNode("mutation"), mutationOptsRef, isSpread: true),
            new(new IdentifierNode("mutationKey"), mutationKey),
            new(new IdentifierNode("mutationFn"), mutationFn),
        }.ToImmutableList(), type: null, metadata: null, preferMultilineFormat: true);

        var body = new BlockNode(new StatementNode[]
        {
            new VariableDeclarationStatementNode(
                new IdentifierNode("requestOptions"),
                _typeBuilder.BuildRequestOptionsType(),
                new MemberAccessExpressionNode(
                    new BinaryExpressionNode(
                        new IdentifierExpressionNode(new IdentifierNode("options")),
                        BinaryOperator.NullishCoalescing,
                        new ObjectLiteralExpressionNode(ImmutableList<PropertyAssignmentNode>.Empty)),
                    new IdentifierNode("request"))),
            new VariableDeclarationStatementNode(
                new IdentifierNode("result"),
                new SimpleTypeNode(optionsResultType, isPrimitive: false),
                resultObj),
            new ReturnStatementNode(new IdentifierExpressionNode(new IdentifierNode("result")))
        }.ToImmutableList());

        return new FunctionDeclarationNode(
            new IdentifierNode($"get{hookBaseName}MutationOptions"),
            returnType: new SimpleTypeNode(optionsResultType, isPrimitive: false),
            parameters: new[] { new ParameterDeclarationNode(new IdentifierNode("options"), new SimpleTypeNode(optionsParamsType, isPrimitive: false), isRequired: false) }.ToImmutableList(),
            body: body,
            isArrowFunction: true,
            documentation: new CommentNode("Options for useMutation.", CommentKind.JSDoc),
            isExported: true);
    }

    private FunctionDeclarationNode BuildUseMutationFn(
        IrOperation operation,
        string hookBaseName,
        IdentifierNode optionsParamsType,
        IdentifierNode optionsResultType,
        TypeNode dataType,
        TypeNode variablesType)
    {
        var body = new BlockNode(new StatementNode[]
        {
            new VariableDeclarationStatementNode(
                new IdentifierNode("mutationOptions"),
                new SimpleTypeNode(optionsResultType, isPrimitive: false),
                new CallExpressionNode(new IdentifierExpressionNode(new IdentifierNode($"get{hookBaseName}MutationOptions")), new[] { new IdentifierExpressionNode(new IdentifierNode("options")) }.ToImmutableList())),
            new ReturnStatementNode(new CallExpressionNode(
                new IdentifierExpressionNode(new IdentifierNode("useMutation")),
                new[] { new IdentifierExpressionNode(new IdentifierNode("mutationOptions")), new IdentifierExpressionNode(new IdentifierNode("queryClient")) }.ToImmutableList()))
        }.ToImmutableList());

        return new FunctionDeclarationNode(
            new IdentifierNode($"use{hookBaseName}"),
            returnType: _typeBuilder.BuildUseMutationResultType(dataType, variablesType),
            parameters: new[]
            {
                new ParameterDeclarationNode(new IdentifierNode("options"), new SimpleTypeNode(optionsParamsType, isPrimitive: false), isRequired: false),
                new ParameterDeclarationNode(new IdentifierNode("queryClient"), TanStackQueryTypeBuilder.SimpleType("QueryClient"), isRequired: false)
            }.ToImmutableList(),
            body: body,
            isArrowFunction: true,
            documentation: new CommentNode("Mutation hook.", CommentKind.JSDoc),
            isExported: true);
    }

    private FunctionDeclarationNode BuildUseIsMutatingFn(IrOperation operation, string hookBaseName)
    {
        var mutationKey = new ArrayLiteralExpressionNode(
            new[] { new LiteralExpressionNode(_namingConverter.ToCamelCase(operation.MethodName), LiteralKind.String) }.ToImmutableList());

        var body = new BlockNode(new[]
        {
            new ReturnStatementNode(new CallExpressionNode(
                new IdentifierExpressionNode(new IdentifierNode("useIsMutating")),
                new[] { new ObjectLiteralExpressionNode(new[] { new PropertyAssignmentNode(new IdentifierNode("mutationKey"), mutationKey) }.ToImmutableList()) }.ToImmutableList()))
        }.ToImmutableList());

        return new FunctionDeclarationNode(
            new IdentifierNode($"useIsMutating{hookBaseName}"),
            returnType: TanStackQueryTypeBuilder.SimpleType("number", isPrimitive: true),
            parameters: ImmutableList<ParameterDeclarationNode>.Empty,
            body: body,
            isArrowFunction: true,
            documentation: new CommentNode("Returns true if this mutation is in progress.", CommentKind.JSDoc),
            isExported: true);
    }
}
