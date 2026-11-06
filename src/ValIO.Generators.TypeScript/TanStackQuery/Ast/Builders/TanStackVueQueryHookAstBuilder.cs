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
/// Builds AST for TanStack Vue Query hook files.
/// </summary>
public sealed class TanStackVueQueryHookAstBuilder : AstBuilderBase
{
    private readonly INamingConverter _namingConverter;
    private readonly TypeScriptParamsAstBuilder _paramsBuilder;
    private readonly TypeScriptResultsAstBuilder _resultsBuilder;

    /// <summary>Creates a TanStackVueQueryHookAstBuilder for one finalized IR document.</summary>
    /// <param name="documentContext">The shared immutable indexes of the finalized IR document.</param>
    /// <param name="namingConverter">The generated identifier naming policy.</param>
    /// <param name="typeBuilder">The language type mapping function.</param>
    /// <param name="resultSuffix">The optional operation result type suffix.</param>
    /// <param name="resultPrefix">The optional operation result type prefix.</param>
    /// <exception cref="ArgumentNullException">A required context or collaborator is null.</exception>
    public TanStackVueQueryHookAstBuilder(IrDocumentContext documentContext,
        INamingConverter namingConverter,
        Func<ValIO.Core.IR.Types.IrType, TypeNode> typeBuilder,
        string? resultSuffix = null,
        string? resultPrefix = null)
        : base(documentContext)
    {
        _namingConverter = namingConverter ?? throw new ArgumentNullException(nameof(namingConverter));
        _paramsBuilder = new TypeScriptParamsAstBuilder(documentContext, namingConverter, typeBuilder);
        _resultsBuilder = new TypeScriptResultsAstBuilder(documentContext, namingConverter, typeBuilder, resultSuffix, resultPrefix);
    }

    public SourceFileNode BuildHookSourceFile(
        IrOperation operation,
        string operationFileName,
        string clientSuffix,
        bool isMutation)
    {
        var hookBaseName = _namingConverter.ToPascalCase(operation.MethodName);
        var hasParams = _paramsBuilder.HasParameters(operation);
        var paramsTypeName = hasParams ? _paramsBuilder.ParamsInterfaceName(operation.MethodName) : null;
        var resultTypeName = _resultsBuilder.HasResponsesWithBody(operation)
            ? _resultsBuilder.ResultTypeName(operation.MethodName)
            : null;

        var imports = BuildImports(
            operation,
            operationFileName,
            clientSuffix,
            hookBaseName,
            hasParams,
            paramsTypeName,
            resultTypeName,
            isMutation);

        var declarations = isMutation
            ? ImmutableList<DeclarationNode>.Empty.Add(BuildUseMutationFn(operation, hookBaseName, paramsTypeName, resultTypeName, hasParams))
            : ImmutableList<DeclarationNode>.Empty
                .Add(BuildUseQueryFn(operation, hookBaseName, paramsTypeName, resultTypeName, hasParams));

        return new SourceFileNode(imports: imports, declarations: declarations,
            fileDocumentation: TypeScriptDoc.FileHeader(
                $"{operationFileName}.query.ts",
                $"endpoints/{operationFileName}",
                $"TanStack Query (Vue) hooks for the {operation.MethodName} operation."));
    }

    private static IReadOnlyList<ImportNode> BuildImports(
        IrOperation operation,
        string operationFileName,
        string clientSuffix,
        string hookBaseName,
        bool hasParams,
        string? paramsTypeName,
        string? resultTypeName,
        bool isMutation)
    {
        var imports = ImmutableList.CreateBuilder<ImportNode>();
        imports.Add(new ImportNode(
            items: ImmutableList.Create(new IdentifierNode(isMutation ? "useMutation" : "useQuery")),
            from: "@tanstack/vue-query",
            kind: ImportKind.Named));

        if (!isMutation)
        {
            imports.Add(new ImportNode(
                items: ImmutableList.Create(new IdentifierNode("computed")),
                from: "vue",
                kind: ImportKind.Named));
        }

        if (hasParams && !isMutation)
        {
            imports.Add(new ImportNode(
                items: ImmutableList.Create(new IdentifierNode("unref")),
                from: "vue",
                kind: ImportKind.Named));
            imports.Add(new ImportNode(
                items: ImmutableList.Create(new IdentifierNode("MaybeRef")),
                from: "vue",
                kind: ImportKind.Named,
                isTypeOnly: true));
        }

        imports.Add(new ImportNode(
            items: ImmutableList.Create(new IdentifierNode(operation.MethodName)),
            from: $"./{operationFileName}.{clientSuffix}",
            kind: ImportKind.Named));

        // Query key factory is imported from the shared {op}.query-keys.ts module.
        if (!isMutation)
            imports.Add(new ImportNode(
                items: ImmutableList.Create(new IdentifierNode($"get{hookBaseName}QueryKey")),
                from: $"./{operationFileName}.query-keys",
                kind: ImportKind.Named));

        if (hasParams && paramsTypeName != null)
        {
            imports.Add(new ImportNode(
                items: ImmutableList.Create(new IdentifierNode(paramsTypeName)),
                from: $"./{operationFileName}.params",
                kind: ImportKind.Named,
                isTypeOnly: true));
        }

        if (resultTypeName != null)
        {
            imports.Add(new ImportNode(
                items: ImmutableList.Create(new IdentifierNode(resultTypeName)),
                from: $"./{operationFileName}.results",
                kind: ImportKind.Named,
                isTypeOnly: true));
        }

        return imports.ToImmutable();
    }

    private FunctionDeclarationNode BuildUseQueryFn(
        IrOperation operation,
        string hookBaseName,
        string? paramsTypeName,
        string? resultTypeName,
        bool hasParams)
    {
        var queryOptions = new List<PropertyAssignmentNode>
        {
            new(new IdentifierNode("queryKey"), new CallExpressionNode(
                Id("computed"),
                ImmutableList.Create<ExpressionNode>(new ArrowFunctionExpressionNode(
                    ImmutableList<ParameterDeclarationNode>.Empty,
                    new CallExpressionNode(
                        Id($"get{hookBaseName}QueryKey"),
                        BuildMaybeUnrefParamsArgs(hasParams)))))),
            new(new IdentifierNode("queryFn"), new ArrowFunctionExpressionNode(
                ImmutableList<ParameterDeclarationNode>.Empty,
                new CallExpressionNode(Id(operation.MethodName), BuildMaybeUnrefParamsArgs(hasParams))))
        };
        // Защита от undefined и нарушения spread‑контракта (unknown нельзя спредить).
        queryOptions.Add(new PropertyAssignmentNode(
            new IdentifierNode("options"),
            new BinaryExpressionNode(Id("options"), BinaryOperator.NullishCoalescing, new ObjectLiteralExpressionNode(ImmutableList<PropertyAssignmentNode>.Empty)),
            isSpread: true));

        var useQueryCall = new CallExpressionNode(
            Id("useQuery"),
            ImmutableList.Create<ExpressionNode>(new ObjectLiteralExpressionNode(queryOptions.ToImmutableList(), preferMultilineFormat: true)),
            genericTypeName: resultTypeName);

        var parameters = new List<ParameterDeclarationNode>();
        if (hasParams && paramsTypeName != null)
            parameters.Add(new ParameterDeclarationNode(new IdentifierNode("params"), MaybeRef(Simple(paramsTypeName)), isRequired: true));
        parameters.Add(new ParameterDeclarationNode(
            new IdentifierNode("options"),
            new SimpleTypeNode(new IdentifierNode("object"), isPrimitive: true),
            isRequired: false));

        return new FunctionDeclarationNode(
            name: new IdentifierNode($"use{hookBaseName}"),
            // Возвращаемый тип не аннотируем — пусть TS выведет конкретный
            // UseQueryReturnType<TResult, Error> из useQuery<TResult>(...).
            returnType: null,
            parameters: parameters.ToImmutableList(),
            body: new BlockNode(ImmutableList.Create<StatementNode>(new ReturnStatementNode(useQueryCall))),
            isArrowFunction: true,
            isExported: true);
    }

    private FunctionDeclarationNode BuildUseMutationFn(
        IrOperation operation,
        string hookBaseName,
        string? paramsTypeName,
        string? resultTypeName,
        bool hasParams)
    {
        var variablesType = hasParams && paramsTypeName != null
            ? Simple(paramsTypeName)
            : Simple("void", isPrimitive: true);
        var mutationFnParameters = hasParams
            ? ImmutableList.Create(new ParameterDeclarationNode(new IdentifierNode("variables"), variablesType, isRequired: true))
            : ImmutableList<ParameterDeclarationNode>.Empty;
        var clientArgs = hasParams
            ? ImmutableList.Create<ExpressionNode>(Id("variables"))
            : ImmutableList<ExpressionNode>.Empty;

        var mutationOptions = new List<PropertyAssignmentNode>
        {
            new(new IdentifierNode("mutationFn"), new ArrowFunctionExpressionNode(
                mutationFnParameters,
                new CallExpressionNode(Id(operation.MethodName), clientArgs))),
            new(
                new IdentifierNode("options"),
                new BinaryExpressionNode(Id("options"), BinaryOperator.NullishCoalescing, new ObjectLiteralExpressionNode(ImmutableList<PropertyAssignmentNode>.Empty)),
                isSpread: true)
        };

        var generic = resultTypeName != null
            ? $"{resultTypeName}, Error, {RenderTypeName(variablesType)}"
            : null;
        var useMutationCall = new CallExpressionNode(
            Id("useMutation"),
            ImmutableList.Create<ExpressionNode>(new ObjectLiteralExpressionNode(mutationOptions.ToImmutableList(), preferMultilineFormat: true)),
            genericTypeName: generic);

        return new FunctionDeclarationNode(
            name: new IdentifierNode($"use{hookBaseName}"),
            returnType: null,
            parameters: ImmutableList.Create(new ParameterDeclarationNode(
                new IdentifierNode("options"),
                new SimpleTypeNode(new IdentifierNode("object"), isPrimitive: true),
                isRequired: false)),
            body: new BlockNode(ImmutableList.Create<StatementNode>(new ReturnStatementNode(useMutationCall))),
            isArrowFunction: true,
            isExported: true);
    }

    private static IReadOnlyList<ExpressionNode> BuildMaybeUnrefParamsArgs(bool hasParams)
    {
        return hasParams
            ? ImmutableList.Create<ExpressionNode>(new CallExpressionNode(Id("unref"), ImmutableList.Create<ExpressionNode>(Id("params"))))
            : ImmutableList<ExpressionNode>.Empty;
    }

    private static TypeNode MaybeRef(TypeNode type) =>
        new GenericTypeNode(new IdentifierNode("MaybeRef"), ImmutableList.Create(type));

    private static SimpleTypeNode Simple(string name, bool isPrimitive = false) =>
        new(new IdentifierNode(name), isPrimitive);

    private static IdentifierExpressionNode Id(string name) =>
        new(new IdentifierNode(name));

    private static LiteralExpressionNode String(string value) =>
        new(value, LiteralKind.String);

    private static string RenderTypeName(TypeNode type) =>
        type is SimpleTypeNode simple ? simple.Name.Name : type.ToString() ?? "unknown";
}
