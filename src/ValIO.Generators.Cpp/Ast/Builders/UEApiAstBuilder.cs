using ValIO.Core.Ast.Builders;
using System.Collections.Immutable;
using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Expressions;
using ValIO.Core.Ast.Nodes.Statements;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Core.IR;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Generators.Cpp.Ast.Nodes;
using ValIO.Generators.Cpp.Ast.Nodes.UE;
using ValIO.Generators.Cpp.Ast.Nodes.UE.Specifiers;
using ValIO.Generators.Cpp.Models;
using ValIO.Generators.Cpp.Services;

namespace ValIO.Generators.Cpp.Ast.Builders;

/// <summary>Builds Unreal endpoint artifacts with shared document declaration indexes.</summary>
public sealed class UEApiAstBuilder : AstBuilderBase
{
    /// <summary>Maps types using the shared document context.</summary>
    private readonly UETypeMapper _typeMapper;
    /// <summary>Supplies Unreal identifier and path naming.</summary>
    private readonly UENamingConvention _naming;
    /// <summary>Supplies the selected Unreal generation options.</summary>
    private readonly CppGenerationOptions _options;
    /// <summary>Classifies typed operation response contracts.</summary>
    private readonly UEResultAstBuilder _resultBuilder;

    /// <summary>Requires one document context shared with the type mapper.</summary>
    /// <param name="typeMapper">The document-aware Unreal type mapper.</param>
    /// <param name="naming">The Unreal identifier and file naming rules.</param>
    /// <param name="options">The Unreal generation options.</param>
    /// <param name="resultBuilder">The result builder used for response classification.</param>
    /// <param name="documentContext">The mandatory finalized document indexes.</param>
    /// <exception cref="ArgumentNullException">A dependency is null.</exception>
    /// <exception cref="ArgumentException">The mapper belongs to a different document context.</exception>
    public UEApiAstBuilder(
        UETypeMapper typeMapper,
        UENamingConvention naming,
        CppGenerationOptions options,
        UEResultAstBuilder resultBuilder,
        IrDocumentContext documentContext) : base(documentContext)
    {
        _typeMapper = typeMapper ?? throw new ArgumentNullException(nameof(typeMapper));
        if (!ReferenceEquals(typeMapper.DocumentContext, documentContext))
            throw new ArgumentException("The mapper and builder must share a document context.", nameof(typeMapper));
        _naming = naming ?? throw new ArgumentNullException(nameof(naming));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _resultBuilder = resultBuilder ?? throw new ArgumentNullException(nameof(resultBuilder));
    }

    /// <summary>Builds an endpoint header and source using the shared model lookup.</summary>
    /// <param name="client">The owning client contract.</param>
    /// <param name="operation">The endpoint operation contract.</param>
    /// <returns>The endpoint declaration and implementation ASTs.</returns>
    public (CppHeaderFileNode Header, CppSourceFileNode Source) BuildEndpoint(
        IrClient client,
        IrOperation operation)
    {
        var modelLookup = ModelsByName;
        var header = BuildEndpointHeader(client, operation);
        var source = BuildEndpointSource(client, operation, modelLookup);
        return (header, source);
    }

    /// <summary>
    /// Строит заголовочный файл (.h) для одного endpoint
    /// </summary>
    private CppHeaderFileNode BuildEndpointHeader(IrClient client, IrOperation operation)
    {
        // File name uses the ORIGINAL (pre-affix) method name so the emitted file/include doesn't change with affixes.
        var headerFileName = _naming.EndpointHeaderFileName(operation.OriginalMethodName);
        var declarations = new List<AstNode>
        {
            BuildDelegateDeclaration(operation),
            BuildOperationLibraryClass(client.Name, operation),
            BuildAsyncActionClass(client.Name, operation)
        };

        var includes = BuildEndpointHeaderIncludes(operation, headerFileName);
        var forwardDecls = BuildForwardDeclarations();
        var usingAliases = BuildUsingAliases();

        return new CppHeaderFileNode(
            fileName: headerFileName,
            headerComment: _options.Copyright != null ? new CommentNode(_options.Copyright, CommentKind.MultiLine) : null,
            includes: includes,
            forwardDeclarations: forwardDecls,
            usingAliases: usingAliases,
            declarations: declarations);
    }

    /// <summary>
    /// Строит файл реализации (.cpp) для одного endpoint
    /// </summary>
    private CppSourceFileNode BuildEndpointSource(
        IrClient client,
        IrOperation operation,
        IReadOnlyDictionary<string, IrModel> modelLookup)
    {
        // File name + include path use the ORIGINAL (pre-affix) method name so emitted files/includes don't change with affixes.
        var sourceFileName = _naming.EndpointSourceFileName(operation.OriginalMethodName);
        // Header в Public/Endpoints/{OpId}/, source в Private/Endpoints/
        var headerIncludePath = _naming.EndpointHeaderIncludePath(operation.OriginalMethodName);

        var includes = new List<CppIncludeDirectiveNode>
        {
            new(headerIncludePath),
            new(_options.ValIOClientIncludePath),
            new(_options.ValIORequestBuilderIncludePath),
            new("Blueprint/UValIORequestBuilder.h"),
            new(_options.ValIOMultipartFormDataIncludePath),
            new(_options.ValIOTypesIncludePath),
            new(_options.ValIOResponseIncludePath),
            new("Async/Async.h"),
            new("Misc/Guid.h")
        };

        var libraryMembers = BuildOperationLibraryImplementations(client.Name, operation, modelLookup);
        var asyncMembers = BuildAsyncActionImplementations(client.Name, operation);

        var sections = new List<CppSourceSection>
        {
            new("Operation Library", wrapInAnonymousNamespace: false, libraryMembers),
            new("Blueprint async actions", wrapInAnonymousNamespace: false, asyncMembers)
        };

        return new CppSourceFileNode(
            fileName: sourceFileName,
            headerComment: _options.Copyright != null ? new CommentNode(_options.Copyright, CommentKind.MultiLine) : null,
            includes: includes,
            sections: sections);
    }

    // ===== Include building =====

    private List<CppIncludeDirectiveNode> BuildEndpointHeaderIncludes(IrOperation operation, string headerFileName)
    {
        var includes = new List<CppIncludeDirectiveNode>();

        if (_options.AddUnrealEngineIncludesInHeaders)
        {
            includes.Add(new("CoreMinimal.h"));
            includes.Add(new("Async/Future.h"));
            includes.Add(new(_options.ValIOResponseIncludePath));
        }

        includes.Add(new(_options.BaseAsyncActionIncludePath));
        // Result/Params include paths use the ORIGINAL (pre-affix) method name so emitted includes don't change with affixes.
        includes.Add(new(_naming.ResultIncludePath(operation.OriginalMethodName)));
        includes.Add(new(_naming.ParamsIncludePath(operation.OriginalMethodName)));
        includes.Add(new(headerFileName.Replace(".h", _options.GeneratedHeaderSuffix)));

        return includes;
    }

    /// <summary>
    /// UE-макрос TEXT("...") — явная обёртка вызовом макроса: литерал String рендерится
    /// как обычная кавыченная строка, без платформенных обёрток рендера.
    /// </summary>
    private static ExpressionNode UeText(string value) =>
        new CallExpressionNode(
            new IdentifierExpressionNode(new IdentifierNode("TEXT")),
            [new LiteralExpressionNode(value, LiteralKind.String)]);

    private List<CppForwardDeclarationNode> BuildForwardDeclarations()
    {
        return new List<CppForwardDeclarationNode>
        {
            new(CppForwardDeclarationKind.Class, new IdentifierNode("FValIOClient")),
            new(CppForwardDeclarationKind.Struct, new IdentifierNode("FValIOResponse")),
            new(CppForwardDeclarationKind.Class, new IdentifierNode("UValIORequestBuilder"))
        };
    }

    private List<CppUsingAliasNode> BuildUsingAliases()
    {
        return new List<CppUsingAliasNode>
        {
            new(
                new IdentifierNode("FValIOClientRef"),
                new GenericTypeNode(
                    new IdentifierNode("TSharedRef"),
                    new TypeNode[] { new SimpleTypeNode(new IdentifierNode("FValIOClient")) }))
        };
    }

    /// <summary>
    /// Строит DECLARE_DYNAMIC_MULTICAST_DELEGATE_OneParam для операции
    /// </summary>
    private UDelegateDeclarationNode BuildDelegateDeclaration(IrOperation operation)
    {
        var delegateName = _naming.DelegateName(operation.MethodName);
        var resultTypeName = _naming.ResultStructName(operation.MethodName);

        return new UDelegateDeclarationNode(
            delegateName: new IdentifierNode(delegateName),
            parameters: new[]
            {
                new UDelegateParameter(
                    new SimpleTypeNode(new IdentifierNode(resultTypeName)),
                    new IdentifierNode("Result"))
            });
    }

    /// <summary>
    /// Строит OperationLibrary класс (UBlueprintFunctionLibrary) с Path, Parse, Build, Send
    /// </summary>
    private UClassDeclarationNode BuildOperationLibraryClass(string clientName, IrOperation operation)
    {
        var className = _naming.OperationLibraryClassName(clientName, operation.MethodName);
        var resultTypeName = _naming.ResultStructName(operation.MethodName);
        var paramsTypeName = _naming.ParamsStructName(operation.MethodName);
        var pathMethodName = _naming.PathMethodName(operation.MethodName);
        var category = _naming.OperationTypeCategory(_options.BlueprintCategory, clientName, operation.MethodName);

        var publicMembers = new List<AstNode>();
        var hasPathParams = operation.PathParameters.Count > 0;
        var (pathParamTypeName, pathParamName) = GetBuildPathUrlParamInfo(operation);

        // BuildPathUrl(Params | RouteParams)? -> FString (без параметра, если нет path params)
        var pathBuilderParams = hasPathParams
            ? new[] { new CppParameterNode(new IdentifierNode(pathParamName!), new SimpleTypeNode(new IdentifierNode(pathParamTypeName!)), isConst: true, isReference: true) }
            : Array.Empty<CppParameterNode>();
        publicMembers.Add(new UFunctionDeclarationNode(
            name: new IdentifierNode(pathMethodName),
            returnType: new SimpleTypeNode(new IdentifierNode("FString")),
            parameters: pathBuilderParams,
            specifiers: UFunctionSpecifiers.PathBuilder($"{category}|Path", FormatDisplayName(pathMethodName)),
            isStatic: true,
            documentation: new CommentNode(DoxygenCommentBuilder.BuildBuildPathUrlDocumentation(operation, pathParamTypeName), CommentKind.MultiLine)));

        // Parse(Response) -> FResult
        publicMembers.Add(new UFunctionDeclarationNode(
            name: new IdentifierNode("Parse"),
            returnType: new SimpleTypeNode(new IdentifierNode(resultTypeName)),
            parameters: new[]
            {
                new CppParameterNode(
                    new IdentifierNode("Response"),
                    new SimpleTypeNode(new IdentifierNode("FValIOResponse")),
                    isConst: true, isReference: true)
            },
            specifiers: new UFunctionSpecifiers { BlueprintPure = true, Category = $"{category}|Parse" },
            isStatic: true,
            documentation: new CommentNode(DoxygenCommentBuilder.BuildParseDocumentation(operation, resultTypeName), CommentKind.MultiLine)));

        // Build(Params) -> FValIORequestBuilder — без UFUNCTION (временно)
        publicMembers.Add(new UFunctionDeclarationNode(
            name: new IdentifierNode("Build"),
            returnType: new SimpleTypeNode(new IdentifierNode("FValIORequestBuilder")),
            parameters: new[]
            {
                new CppParameterNode(new IdentifierNode("Params"), new SimpleTypeNode(new IdentifierNode(paramsTypeName)), isConst: true, isReference: true)
            },
            specifiers: new UFunctionSpecifiers(),
            isStatic: true,
            documentation: new CommentNode(DoxygenCommentBuilder.BuildBuildDocumentation(operation, paramsTypeName), CommentKind.MultiLine)));

        // BuildRequestBuilder(Params) -> UValIORequestBuilder* (Blueprint)
        publicMembers.Add(new UFunctionDeclarationNode(
            name: new IdentifierNode("BuildRequestBuilder"),
            returnType: new SimpleTypeNode(new IdentifierNode("UValIORequestBuilder*")),
            parameters: new[]
            {
                new CppParameterNode(new IdentifierNode("Params"), new SimpleTypeNode(new IdentifierNode(paramsTypeName)), isConst: true, isReference: true)
            },
            specifiers: UFunctionSpecifiers.BuildRequestBuilder($"{category}|Build", "Build Request Builder"),
            isStatic: true,
            documentation: new CommentNode(DoxygenCommentBuilder.BuildBuildRequestBuilderDocumentation(operation, paramsTypeName), CommentKind.MultiLine)));

        // Send(Client, Params) -> TFuture<FResult> — без UFUNCTION
        publicMembers.Add(new UFunctionDeclarationNode(
            name: new IdentifierNode("Send"),
            returnType: new GenericTypeNode(new IdentifierNode("TFuture"), new TypeNode[] { new SimpleTypeNode(new IdentifierNode(resultTypeName)) }),
            parameters: new[]
            {
                new CppParameterNode(new IdentifierNode("Client"), new SimpleTypeNode(new IdentifierNode("FValIOClientRef"))),
                new CppParameterNode(new IdentifierNode("Params"), new SimpleTypeNode(new IdentifierNode(paramsTypeName)), isConst: true, isReference: true)
            },
            specifiers: new UFunctionSpecifiers(),
            isStatic: true,
            documentation: new CommentNode(DoxygenCommentBuilder.BuildSendDocumentation(operation, resultTypeName, paramsTypeName, className), CommentKind.MultiLine)));

        return new UClassDeclarationNode(
            name: new IdentifierNode(className),
            specifiers: UClassSpecifiers.PathsLibrary(category),
            apiMacro: _options.ModuleApiMacro,
            baseTypes: new TypeNode[] { new SimpleTypeNode(new IdentifierNode("UBlueprintFunctionLibrary")) },
            publicMembers: publicMembers,
            documentation: new CommentNode(DoxygenCommentBuilder.BuildLibraryClassDocumentation(operation, clientName), CommentKind.MultiLine));
    }

    /// <summary>
    /// Строит Blueprint Async Action класс
    /// </summary>
    private UClassDeclarationNode BuildAsyncActionClass(string clientName, IrOperation operation)
    {
        var className = _naming.AsyncActionClassName(clientName, operation.MethodName);
        var delegateName = _naming.DelegateName(operation.MethodName);
        var resultTypeName = _naming.ResultStructName(operation.MethodName);
        var paramsTypeName = _naming.ParamsStructName(operation.MethodName);
        var category = _naming.OperationTypeCategory(_options.BlueprintCategory, clientName, operation.MethodName);

        var publicMembers = new List<AstNode>();
        var protectedMembers = new List<AstNode>();

        // UPROPERTY(BlueprintAssignable) delegate OnCompleted
        publicMembers.Add(new UPropertyFieldNode(
            name: new IdentifierNode("OnCompleted"),
            type: new SimpleTypeNode(new IdentifierNode(delegateName)),
            specifiers: UPropertySpecifiers.AssignableDelegate("On Completed")));

        // UPROPERTY(BlueprintReadOnly) LastResult
        publicMembers.Add(new UPropertyFieldNode(
            name: new IdentifierNode("LastResult"),
            type: new SimpleTypeNode(new IdentifierNode(resultTypeName)),
            specifiers: UPropertySpecifiers.ResultField(category)));

        // UFUNCTION(BlueprintCallable) static factory method
        var factoryParams = new List<CppParameterNode>
        {
            new(new IdentifierNode("WorldContextObject"),
                new SimpleTypeNode(new IdentifierNode("UObject")),
                isPointer: true),
            new(new IdentifierNode("ClientName"),
                new SimpleTypeNode(new IdentifierNode("FString")),
                isConst: true, isReference: true),
            new(new IdentifierNode("Params"),
                new SimpleTypeNode(new IdentifierNode(paramsTypeName)),
                isConst: true, isReference: true)
        };

        var displayName = FormatDisplayName(operation.MethodName);
        publicMembers.Add(new UFunctionDeclarationNode(
            name: new IdentifierNode(operation.MethodName),
            returnType: new SimpleTypeNode(new IdentifierNode($"{className}*")),
            parameters: factoryParams,
            specifiers: UFunctionSpecifiers.AsyncFactory(category, displayName),
            isStatic: true,
            documentation: new CommentNode(DoxygenCommentBuilder.BuildAsyncFactoryDocumentation(operation, paramsTypeName), CommentKind.MultiLine)));

        // virtual void Activate() override;
        publicMembers.Add(new UFunctionDeclarationNode(
            name: new IdentifierNode("Activate"),
            returnType: new SimpleTypeNode(new IdentifierNode("void"), isPrimitive: true),
            isVirtual: true, isOverride: true));

        // void Complete(const Result& Result);
        publicMembers.Add(new UFunctionDeclarationNode(
            name: new IdentifierNode("Complete"),
            returnType: new SimpleTypeNode(new IdentifierNode("void"), isPrimitive: true),
            parameters: new[]
            {
                new CppParameterNode(
                    new IdentifierNode("Result"),
                    new SimpleTypeNode(new IdentifierNode(resultTypeName)),
                    isConst: true, isReference: true)
            }));

        // protected: GetWorldContext, GetClientName (override from UValIOAsyncActionBase)
        var stateIdent = new IdentifierExpressionNode(new IdentifierNode("State"));
        var worldContextGet = new CallExpressionNode(
            new MemberAccessExpressionNode(
                new MemberAccessExpressionNode(stateIdent, new IdentifierNode("WorldContext")),
                new IdentifierNode("Get")),
            ImmutableList<ExpressionNode>.Empty);
        var worldContextVar = new IdentifierNode("WorldContextObject");
        var getWorldContextBody = new BlockNode(new StatementNode[]
        {
            new VariableDeclarationStatementNode(
                worldContextVar,
                new SimpleTypeNode(new IdentifierNode("UObject*")),
                worldContextGet),
            new ExpressionStatementNode(new CallExpressionNode(
                new IdentifierExpressionNode(new IdentifierNode("checkf")),
                new ExpressionNode[]
                {
                    new IdentifierExpressionNode(worldContextVar),
                    UeText("Invalid WorldContext")
                }.ToImmutableList())),
            new ReturnStatementNode(new IdentifierExpressionNode(worldContextVar))
        });
        protectedMembers.Add(new UFunctionDeclarationNode(
            name: new IdentifierNode("GetWorldContext"),
            returnType: new SimpleTypeNode(new IdentifierNode("UObject*")),
            isVirtual: true, isOverride: true, isConst: true,
            body: getWorldContextBody));

        var clientNameAccess = new MemberAccessExpressionNode(stateIdent, new IdentifierNode("ClientName"));
        protectedMembers.Add(new UFunctionDeclarationNode(
            name: new IdentifierNode("GetClientName"),
            returnType: new SimpleTypeNode(new IdentifierNode("FString")),
            isVirtual: true, isOverride: true, isConst: true,
            body: new BlockNode(new StatementNode[] { new ReturnStatementNode(clientNameAccess) })));

        // public: State field
        var stateType = new GenericTypeNode(
            new IdentifierNode("FValIOAsyncState"),
            new TypeNode[] { new SimpleTypeNode(new IdentifierNode(paramsTypeName)) });

        publicMembers.Add(new UPropertyFieldNode(
            name: new IdentifierNode("State"),
            type: stateType,
            documentation: new CommentNode("@brief Internal state for NewAsyncObject.\n@details Used by UValIOAsyncActionBase::NewAsyncObject.", CommentKind.MultiLine),
            skipUProperty: true));

        return new UClassDeclarationNode(
            name: new IdentifierNode(className),
            specifiers: UClassSpecifiers.AsyncAction(category),
            apiMacro: _options.ModuleApiMacro,
            baseTypes: new TypeNode[] { new SimpleTypeNode(new IdentifierNode(_options.BaseAsyncActionClassName)) },
            publicMembers: publicMembers,
            protectedMembers: protectedMembers,
            documentation: new CommentNode(DoxygenCommentBuilder.BuildAsyncClassDocumentation(operation, clientName), CommentKind.MultiLine));
    }

    // ===== Source (.cpp) building methods =====

    /// <summary>
    /// Строит реализации методов OperationLibrary: Path, Parse, Build, Send
    /// </summary>
    private List<AstNode> BuildOperationLibraryImplementations(
        string clientName,
        IrOperation operation,
        IReadOnlyDictionary<string, IrModel> modelLookup)
    {
        var libraryClassName = _naming.OperationLibraryClassName(clientName, operation.MethodName);
        var pathMethodName = _naming.PathMethodName(operation.MethodName);
        var resultTypeName = _naming.ResultStructName(operation.MethodName);
        var paramsTypeName = _naming.ParamsStructName(operation.MethodName);
        var implementations = new List<AstNode>();

        // Path implementation
        implementations.Add(BuildPathMethodImplementationForLibrary(libraryClassName, operation));

        // Parse implementation
        implementations.Add(BuildParseMethodImplementation(libraryClassName, operation));

        // Build implementation
        implementations.Add(BuildBuildMethodImplementation(libraryClassName, pathMethodName, operation, modelLookup));

        // BuildRequestBuilder implementation
        implementations.Add(BuildBuildRequestBuilderMethodImplementation(libraryClassName, paramsTypeName));

        // Send implementation
        implementations.Add(BuildSendMethodImplementation(libraryClassName, resultTypeName, paramsTypeName));

        return implementations;
    }

    /// <summary>
    /// Строит реализацию метода Path для OperationLibrary
    /// </summary>
    private CppPathBuilderImplementationNode BuildPathMethodImplementationForLibrary(string libraryClassName, IrOperation operation)
    {
        var pathMethodName = _naming.PathMethodName(operation.MethodName);
        var (parameters, body) = BuildPathMethodParametersAndBody(operation);
        return new CppPathBuilderImplementationNode(
            className: new IdentifierNode(libraryClassName),
            methodName: new IdentifierNode(pathMethodName),
            parameters: parameters,
            body: body);
    }

    /// <summary>
    /// Строит параметры и тело метода BuildPathUrl (Params вместо отдельных аргументов)
    /// </summary>
    private (List<CppParameterNode> Parameters, BlockNode Body) BuildPathMethodParametersAndBody(IrOperation operation)
    {
        var (pathParamTypeName, pathParamName) = GetBuildPathUrlParamInfo(operation);
        var parameters = new List<CppParameterNode>();
        if (pathParamTypeName != null && pathParamName != null)
            parameters.Add(new(new IdentifierNode(pathParamName), new SimpleTypeNode(new IdentifierNode(pathParamTypeName)), isConst: true, isReference: true));

        var pathPrefix = pathParamName != null ? pathParamName + "." : "";

        var pathParamSpecs = operation.PathParameters.Select(p =>
        {
            var propName = _naming.PropertyName(p.Name);
            var pathValueAccess = pathPrefix + propName;
            var isGuid = _typeMapper.IsGuidType(p.Type);
            var intermediateVarName = isGuid ? propName + "Str" : null;
            var printfArgExpr = _typeMapper.GetPrintfArgumentExpression(p.Type, pathValueAccess, intermediateVarName);
            return (OriginalName: p.Name, PathValueAccess: pathValueAccess, PropertyName: propName, PrintfSpecifier: _typeMapper.GetPrintfSpecifier(p.Type),
                PrintfArgExpression: printfArgExpr, IsGuid: isGuid, IntermediateVarName: intermediateVarName);
        }).ToList();

        var statements = new List<StatementNode>();
        foreach (var param in pathParamSpecs)
        {
            if (param.IsGuid && param.IntermediateVarName != null)
            {
                var pathExpr = BuildExpressionFromPath(param.PathValueAccess);
                var toStrCall = new CallExpressionNode(
                    new MemberAccessExpressionNode(pathExpr, new IdentifierNode("ToString")),
                    new ExpressionNode[] { new MemberAccessExpressionNode(
                        new IdentifierExpressionNode(new IdentifierNode("EGuidFormats")),
                        new IdentifierNode("DigitsWithHyphens"),
                        useScopeOperator: true) }.ToImmutableList());
                statements.Add(new VariableDeclarationStatementNode(
                    new IdentifierNode(param.IntermediateVarName),
                    new SimpleTypeNode(new IdentifierNode("const FString")),
                    toStrCall));
            }
        }
        var formatStr = pathParamSpecs.Aggregate(operation.Path, (acc, p) =>
            acc.Replace($"{{{p.OriginalName}}}", p.PrintfSpecifier));
        var printfArgs = new List<ExpressionNode> { UeText(formatStr) };
        printfArgs.AddRange(pathParamSpecs.Select(p =>
        {
            var needsDeref = p.PrintfArgExpression.StartsWith("*");
            var pathOrIdent = needsDeref ? p.PrintfArgExpression.TrimStart('*') : p.PrintfArgExpression;
            var expr = BuildExpressionFromPath(pathOrIdent);
            return needsDeref ? new UnaryExpressionNode(UnaryOperator.Dereference, expr) : expr;
        }));
        var printfCall = new CallExpressionNode(
            new MemberAccessExpressionNode(
                new IdentifierExpressionNode(new IdentifierNode("FString")),
                new IdentifierNode("Printf"),
                useScopeOperator: true),
            printfArgs.ToImmutableList());
        statements.Add(new VariableDeclarationStatementNode(
            new IdentifierNode("Url"),
            new SimpleTypeNode(new IdentifierNode("const FString")),
            printfCall));
        statements.Add(new ReturnStatementNode(new IdentifierExpressionNode(new IdentifierNode("Url"))));
        return (parameters, new BlockNode(statements.ToImmutableList()));
    }

    /// <summary>
    /// Строит реализацию метода Parse для OperationLibrary
    /// </summary>
    private CppAsyncFactoryImplementationNode BuildParseMethodImplementation(string libraryClassName, IrOperation operation)
    {
        var parseBody = BuildParseMethodBody(operation);
        var parameters = new List<CppParameterNode>
        {
            new(new IdentifierNode("Response"),
                new SimpleTypeNode(new IdentifierNode("FValIOResponse")),
                isConst: true, isReference: true)
        };
        return new CppAsyncFactoryImplementationNode(
            className: new IdentifierNode(libraryClassName),
            methodName: new IdentifierNode("Parse"),
            returnType: new SimpleTypeNode(new IdentifierNode(_naming.ResultStructName(operation.MethodName))),
            parameters: parameters,
            body: parseBody);
    }

    /// <summary>
    /// Строит реализацию метода Build для OperationLibrary
    /// </summary>
    private CppAsyncFactoryImplementationNode BuildBuildMethodImplementation(
        string libraryClassName,
        string pathMethodName,
        IrOperation operation,
        IReadOnlyDictionary<string, IrModel> modelLookup)
    {
        var paramsTypeName = _naming.ParamsStructName(operation.MethodName);
        var hasMultipleParamGroups = HasMultipleParamGroups(operation);
        var queryPrefix = hasMultipleParamGroups ? "Params.Query." : "Params.";
        var rb = operation.RequestBody;
        var hasBody = rb != null && rb.IsJson;
        var hasForm = rb != null && rb.IsForm;
        var isMultipart = rb != null && rb.IsMultipartFormData;

        var httpVerb = operation.HttpMethod.ToUpperInvariant() switch
        {
            "GET" => "Get", "POST" => "Post", "PUT" => "Put", "DELETE" => "Delete",
            "PATCH" => "Patch", "HEAD" => "Head", "OPTIONS" => "Options",
            _ => "Get"
        };

        var pathCallArg = GetBuildPathUrlCallArgument(operation);
        var pathCallArgs = pathCallArg != null
            ? new ExpressionNode[] { pathCallArg }.ToImmutableList()
            : ImmutableList<ExpressionNode>.Empty;
        var pathCall = new CallExpressionNode(
            new IdentifierExpressionNode(new IdentifierNode(pathMethodName)),
            pathCallArgs);

        var statements = new List<StatementNode>();
        statements.Add(new VariableDeclarationStatementNode(
            new IdentifierNode("Path"),
            new SimpleTypeNode(new IdentifierNode("const FString")),
            pathCall));

        var constructorArgs = new List<ExpressionNode>
        {
            new MemberAccessExpressionNode(
                new IdentifierExpressionNode(new IdentifierNode("EValIOHttpVerb")),
                new IdentifierNode(httpVerb),
                useScopeOperator: true),
            new IdentifierExpressionNode(new IdentifierNode("Path"))
        };
        var requestBuilderInit = new CallExpressionNode(
            new IdentifierExpressionNode(new IdentifierNode("FValIORequestBuilder")),
            constructorArgs.ToImmutableList());
        statements.Add(new VariableDeclarationStatementNode(
            new IdentifierNode("RequestBuilder"),
            new SimpleTypeNode(new IdentifierNode("FValIORequestBuilder")),
            requestBuilderInit));

        foreach (var qp in operation.QueryParameters)
        {
            var propName = queryPrefix + _naming.PropertyName(qp.Name);
            var valueExpr = (ExpressionNode)new IdentifierExpressionNode(new IdentifierNode(propName));
            var methodName = IsQueryParamEnum(qp) ? "AddQueryParamEnumAsString" : "AddQueryParam";
            var addQueryCall = new CallExpressionNode(
                new MemberAccessExpressionNode(
                    new IdentifierExpressionNode(new IdentifierNode("RequestBuilder")),
                    new IdentifierNode(methodName)),
                new ExpressionNode[]
                {
                    UeText(qp.Name),
                    valueExpr
                }.ToImmutableList());
            statements.Add(new ExpressionStatementNode(addQueryCall));
        }

        // Body: JSON, multipart/form-data, application/x-www-form-urlencoded
        var bodyStatements = BuildRequestBodyStatements(operation, hasMultipleParamGroups, hasBody, hasForm, isMultipart, modelLookup);
        statements.AddRange(bodyStatements);

        var setRequestNameCall = new CallExpressionNode(
            new MemberAccessExpressionNode(
                new IdentifierExpressionNode(new IdentifierNode("RequestBuilder")),
                new IdentifierNode("SetRequestName")),
            new ExpressionNode[] { UeText(operation.MethodName) }.ToImmutableList());
        statements.Add(new ExpressionStatementNode(setRequestNameCall));

        statements.Add(new ReturnStatementNode(new IdentifierExpressionNode(new IdentifierNode("RequestBuilder"))));

        return new CppAsyncFactoryImplementationNode(
            className: new IdentifierNode(libraryClassName),
            methodName: new IdentifierNode("Build"),
            returnType: new SimpleTypeNode(new IdentifierNode("FValIORequestBuilder")),
            parameters: new[]
            {
                new CppParameterNode(new IdentifierNode("Params"), new SimpleTypeNode(new IdentifierNode(paramsTypeName)), isConst: true, isReference: true)
            },
            body: new BlockNode(statements.ToImmutableList()));
    }

    /// <summary>
    /// Строит операторы для установки body: JSON, multipart/form-data, application/x-www-form-urlencoded.
    /// </summary>
    private List<StatementNode> BuildRequestBodyStatements(
        IrOperation operation,
        bool hasMultipleParamGroups,
        bool hasBody,
        bool hasForm,
        bool isMultipart,
        IReadOnlyDictionary<string, IrModel> modelLookup)
    {
        var statements = new List<StatementNode>();
        if (operation.RequestBody == null) return statements;

        var formDataPath = hasMultipleParamGroups ? "Params.Form" : "Params.Body";
        var bodyDataPath = "Params.Body";

        if (hasBody)
        {
            var bodyTypeName = GetRequestBodyTypeName(operation.RequestBody.Type);
            var setJsonCall = new CallExpressionNode(
                new MemberAccessExpressionNode(
                    new IdentifierExpressionNode(new IdentifierNode("RequestBuilder")),
                    new IdentifierNode("SetJsonBodyFromStruct")),
                new ExpressionNode[] { new IdentifierExpressionNode(new IdentifierNode(bodyDataPath)) }.ToImmutableList(),
                genericTypeName: bodyTypeName);
            statements.Add(new ExpressionStatementNode(setJsonCall));
            return statements;
        }

        if (hasForm && isMultipart && operation.RequestBody.Type is IrModelReference modelRef &&
            modelLookup.TryGetValue(modelRef.ModelName, out var formModel))
        {
            statements.Add(new VariableDeclarationStatementNode(
                new IdentifierNode("FormData"),
                new SimpleTypeNode(new IdentifierNode("FValIOMultipartFormData")),
                new CallExpressionNode(
                    new IdentifierExpressionNode(new IdentifierNode("FValIOMultipartFormData")),
                    ImmutableList<ExpressionNode>.Empty)));

            foreach (var prop in formModel.Properties)
            {
                var fieldName = prop.OriginalName;
                var propName = _naming.PropertyName(prop.Name);
                var dataAccessPath = $"{formDataPath}.{propName}";

                if (UETypeMapper.IsFileOrBinaryType(prop.Type))
                {
                    var bytesCopyVar = $"BytesCopy_{propName}";
                    statements.Add(new VariableDeclarationStatementNode(
                        new IdentifierNode(bytesCopyVar),
                        new SimpleTypeNode(new IdentifierNode("TArray<uint8>")),
                        BuildExpressionFromPath(dataAccessPath)));
                    var addFileCall = new CallExpressionNode(
                        new MemberAccessExpressionNode(
                            new IdentifierExpressionNode(new IdentifierNode("FormData")),
                            new IdentifierNode("AddFile")),
                        new ExpressionNode[]
                        {
                            UeText(fieldName),
                            GetFormFileFileNameExpression(formModel, prop, formDataPath),
                            new CallExpressionNode(
                                new IdentifierExpressionNode(new IdentifierNode("MoveTemp")),
                                new ExpressionNode[] { new IdentifierExpressionNode(new IdentifierNode(bytesCopyVar)) }.ToImmutableList()),
                            GetFormFileMimeTypeExpression(formModel, prop, formDataPath)
                        }.ToImmutableList());
                    statements.Add(new ExpressionStatementNode(addFileCall));
                }
                else
                {
                    var valueExpr = GetFormFieldValueExpression(prop, formDataPath);
                    var addFieldCall = new CallExpressionNode(
                        new MemberAccessExpressionNode(
                            new IdentifierExpressionNode(new IdentifierNode("FormData")),
                            new IdentifierNode("AddField")),
                        new ExpressionNode[]
                        {
                            UeText(fieldName),
                            valueExpr
                        }.ToImmutableList());
                    statements.Add(new ExpressionStatementNode(addFieldCall));
                }
            }

            var setMultipartCall = new CallExpressionNode(
                new MemberAccessExpressionNode(
                    new IdentifierExpressionNode(new IdentifierNode("RequestBuilder")),
                    new IdentifierNode("SetMultipartBody")),
                new ExpressionNode[] { new IdentifierExpressionNode(new IdentifierNode("FormData")) }.ToImmutableList());
            statements.Add(new ExpressionStatementNode(setMultipartCall));
        }
        else if (hasForm && !isMultipart && operation.RequestBody.Type is IrModelReference urlEncModelRef &&
                 modelLookup.TryGetValue(urlEncModelRef.ModelName, out var urlEncFormModel))
        {
            statements.Add(new VariableDeclarationStatementNode(
                new IdentifierNode("FormMap"),
                new GenericTypeNode(new IdentifierNode("TMap"), new TypeNode[] { new SimpleTypeNode(new IdentifierNode("FString")), new SimpleTypeNode(new IdentifierNode("FString")) })));

            foreach (var prop in urlEncFormModel.Properties)
            {
                var fieldName = prop.OriginalName;
                var propName = _naming.PropertyName(prop.Name);
                var valueExpr = GetFormFieldValueExpression(prop, formDataPath);
                var addCall = new CallExpressionNode(
                    new MemberAccessExpressionNode(
                        new IdentifierExpressionNode(new IdentifierNode("FormMap")),
                        new IdentifierNode("Add")),
                    new ExpressionNode[]
                    {
                        UeText(fieldName),
                        valueExpr
                    }.ToImmutableList());
                statements.Add(new ExpressionStatementNode(addCall));
            }

            var setFormBodyCall = new CallExpressionNode(
                new MemberAccessExpressionNode(
                    new IdentifierExpressionNode(new IdentifierNode("RequestBuilder")),
                    new IdentifierNode("SetFormBody")),
                new ExpressionNode[] { new IdentifierExpressionNode(new IdentifierNode("FormMap")) }.ToImmutableList());
            statements.Add(new ExpressionStatementNode(setFormBodyCall));
        }

        return statements;
    }

    /// <summary>
    /// Возвращает выражение для FileName при AddFile. Ищет {Prop}FileName, затем общий FileName, иначе — литерал.
    /// </summary>
    private ExpressionNode GetFormFileFileNameExpression(IrModel formModel, IrProperty fileProp, string formDataPath)
    {
        var propPascal = _naming.PropertyName(fileProp.Name);
        var fileNamePropName = propPascal + "FileName";
        var hasPropFileName = formModel.Properties.Any(p => _naming.PropertyName(p.Name).Equals(fileNamePropName, StringComparison.Ordinal));
        if (hasPropFileName)
            return BuildExpressionFromPath($"{formDataPath}.{fileNamePropName}");
        var hasFileName = formModel.Properties.Any(p => _naming.PropertyName(p.Name).Equals("FileName", StringComparison.Ordinal));
        if (hasFileName)
            return BuildExpressionFromPath($"{formDataPath}.FileName");
        return UeText("upload.dat");
    }

    /// <summary>
    /// Возвращает выражение для MimeType при AddFile. Ищет {Prop}MimeType, затем общий MimeType, иначе — "application/octet-stream".
    /// </summary>
    private ExpressionNode GetFormFileMimeTypeExpression(IrModel formModel, IrProperty fileProp, string formDataPath)
    {
        var propPascal = _naming.PropertyName(fileProp.Name);
        var mimePropName = propPascal + "MimeType";
        var hasPropMimeType = formModel.Properties.Any(p => _naming.PropertyName(p.Name).Equals(mimePropName, StringComparison.Ordinal));
        if (hasPropMimeType)
            return BuildExpressionFromPath($"{formDataPath}.{mimePropName}");
        var hasMimeType = formModel.Properties.Any(p => _naming.PropertyName(p.Name).Equals("MimeType", StringComparison.Ordinal));
        if (hasMimeType)
            return BuildExpressionFromPath($"{formDataPath}.MimeType");
        return UeText("application/octet-stream");
    }

    /// <summary>
    /// Возвращает выражение значения для AddField (конвертирует числа в строку через LexToString при необходимости).
    /// </summary>
    private ExpressionNode GetFormFieldValueExpression(IrProperty prop, string formDataPath)
    {
        var propName = _naming.PropertyName(prop.Name);
        var accessExpr = BuildExpressionFromPath($"{formDataPath}.{propName}");
        var needsLexToString = prop.Type is IrPrimitiveType p && p.PrimitiveKind is IrPrimitiveKind.Integer or IrPrimitiveKind.Long or IrPrimitiveKind.Number or IrPrimitiveKind.Boolean;
        if (needsLexToString)
        {
            return new CallExpressionNode(
                new IdentifierExpressionNode(new IdentifierNode("LexToString")),
                new ExpressionNode[] { accessExpr }.ToImmutableList());
        }
        return accessExpr;
    }

    private string GetRequestBodyTypeName(IrType type)
    {
        return type switch
        {
            IrModelReference modelRef => _typeMapper.IsEnumReference(modelRef.ModelName)
                ? _naming.EnumName(modelRef.ModelName)
                : _naming.StructName(modelRef.ModelName),
            _ => "FString"
        };
    }

    /// <summary>
    /// Строит реализацию метода BuildRequestBuilder для OperationLibrary
    /// </summary>
    private CppAsyncFactoryImplementationNode BuildBuildRequestBuilderMethodImplementation(
        string libraryClassName, string paramsTypeName)
    {
        var buildCall = new CallExpressionNode(
            new IdentifierExpressionNode(new IdentifierNode("Build")),
            new ExpressionNode[] { new IdentifierExpressionNode(new IdentifierNode("Params")) }.ToImmutableList());
        var fromCall = new CallExpressionNode(
            new MemberAccessExpressionNode(
                new IdentifierExpressionNode(new IdentifierNode("UValIORequestBuilder")),
                new IdentifierNode("From"),
                useScopeOperator: true),
            new ExpressionNode[] { new IdentifierExpressionNode(new IdentifierNode("RequestBuilder")) }.ToImmutableList());

        var statements = new List<StatementNode>
        {
            new VariableDeclarationStatementNode(
                new IdentifierNode("RequestBuilder"),
                new SimpleTypeNode(new IdentifierNode("FValIORequestBuilder")),
                buildCall),
            new VariableDeclarationStatementNode(
                new IdentifierNode("BlueprintRequestBuilder"),
                new SimpleTypeNode(new IdentifierNode("UValIORequestBuilder*")),
                fromCall),
            new ReturnStatementNode(new IdentifierExpressionNode(new IdentifierNode("BlueprintRequestBuilder")))
        };

        return new CppAsyncFactoryImplementationNode(
            className: new IdentifierNode(libraryClassName),
            methodName: new IdentifierNode("BuildRequestBuilder"),
            returnType: new SimpleTypeNode(new IdentifierNode("UValIORequestBuilder*")),
            parameters: new[]
            {
                new CppParameterNode(new IdentifierNode("Params"), new SimpleTypeNode(new IdentifierNode(paramsTypeName)), isConst: true, isReference: true)
            },
            body: new BlockNode(statements.ToImmutableList()));
    }

    /// <summary>
    /// Строит реализацию метода Send для OperationLibrary
    /// </summary>
    private CppAsyncFactoryImplementationNode BuildSendMethodImplementation(
        string libraryClassName, string resultTypeName, string paramsTypeName)
    {
        var buildCall = new CallExpressionNode(
            new IdentifierExpressionNode(new IdentifierNode("Build")),
            new ExpressionNode[] { new IdentifierExpressionNode(new IdentifierNode("Params")) }.ToImmutableList());

        var requestBuilderBuildCall = new CallExpressionNode(
            new MemberAccessExpressionNode(
                new IdentifierExpressionNode(new IdentifierNode("RequestBuilder")),
                new IdentifierNode("Build")),
            ImmutableList<ExpressionNode>.Empty);

        var sendCall = new CallExpressionNode(
            new MemberAccessExpressionNode(
                new IdentifierExpressionNode(new IdentifierNode("Client")),
                new IdentifierNode("Send"),
                useArrowOperator: true),
            new ExpressionNode[] { new IdentifierExpressionNode(new IdentifierNode("Request")) }.ToImmutableList());

        // Next(&Parse) - static method as callable (unqualified, same class)
        var parseRef = new UnaryExpressionNode(
            UnaryOperator.AddressOf,
            new IdentifierExpressionNode(new IdentifierNode("Parse")));
        var nextCall = new CallExpressionNode(
            new MemberAccessExpressionNode(
                new IdentifierExpressionNode(new IdentifierNode("ResponseFuture")),
                new IdentifierNode("Next")),
            new ExpressionNode[] { parseRef }.ToImmutableList());

        var statements = new List<StatementNode>
        {
            new VariableDeclarationStatementNode(
                new IdentifierNode("RequestBuilder"),
                new SimpleTypeNode(new IdentifierNode("FValIORequestBuilder")),
                buildCall),
            new VariableDeclarationStatementNode(
                new IdentifierNode("Request"),
                new SimpleTypeNode(new IdentifierNode("FValIORequest")),
                requestBuilderBuildCall),
            new VariableDeclarationStatementNode(
                new IdentifierNode("ResponseFuture"),
                new GenericTypeNode(new IdentifierNode("TFuture"), new TypeNode[] { new SimpleTypeNode(new IdentifierNode("FValIOResponse")) }),
                sendCall),
            new VariableDeclarationStatementNode(
                new IdentifierNode("ResultFuture"),
                new GenericTypeNode(new IdentifierNode("TFuture"), new TypeNode[] { new SimpleTypeNode(new IdentifierNode(resultTypeName)) }),
                nextCall),
            new ReturnStatementNode(new IdentifierExpressionNode(new IdentifierNode("ResultFuture")))
        };

        return new CppAsyncFactoryImplementationNode(
            className: new IdentifierNode(libraryClassName),
            methodName: new IdentifierNode("Send"),
            returnType: new GenericTypeNode(new IdentifierNode("TFuture"), new TypeNode[] { new SimpleTypeNode(new IdentifierNode(resultTypeName)) }),
            parameters: new[]
            {
                new CppParameterNode(new IdentifierNode("Client"), new SimpleTypeNode(new IdentifierNode("FValIOClientRef"))),
                new CppParameterNode(new IdentifierNode("Params"), new SimpleTypeNode(new IdentifierNode(paramsTypeName)), isConst: true, isReference: true)
            },
            body: new BlockNode(statements.ToImmutableList()));
    }

    /// <summary>
    /// Строит тело метода Parse (Response -> Result)
    /// </summary>
    private BlockNode BuildParseMethodBody(IrOperation operation)
    {
        var parseNode = BuildParseFunction(operation);
        return parseNode.Body;
    }

    /// <summary>
    /// Строит Parse-функцию (используется для извлечения тела)
    /// </summary>
    private CppParseFunctionNode BuildParseFunction(IrOperation operation)
    {
        var parseName = _naming.ParseFunctionName(operation.MethodName);
        var resultTypeName = _naming.ResultStructName(operation.MethodName);
        var hasSuccessBody = _resultBuilder.HasSuccessBody(operation);
        var successBodyTypeName = GetSuccessBodyTypeName(operation);

        const string ResponseParamName = "Response";
        var parameters = new List<CppParameterNode>
        {
            new(new IdentifierNode(ResponseParamName),
                new SimpleTypeNode(new IdentifierNode("FValIOResponse")),
                isConst: true, isReference: true)
        };

        var statements = new List<StatementNode>();

        statements.Add(new VariableDeclarationStatementNode(
            new IdentifierNode("Out"),
            new SimpleTypeNode(new IdentifierNode(resultTypeName))));

        statements.Add(new ExpressionStatementNode(
            new BinaryExpressionNode(
                new MemberAccessExpressionNode(new IdentifierExpressionNode(new IdentifierNode("Out")), new IdentifierNode("StatusCode")),
                BinaryOperator.Assign,
                new MemberAccessExpressionNode(new IdentifierExpressionNode(new IdentifierNode(ResponseParamName)), new IdentifierNode("StatusCode")),
                isAssignment: true)));

        statements.Add(new ExpressionStatementNode(
            new BinaryExpressionNode(
                new MemberAccessExpressionNode(new IdentifierExpressionNode(new IdentifierNode("Out")), new IdentifierNode("Response")),
                BinaryOperator.Assign,
                new IdentifierExpressionNode(new IdentifierNode(ResponseParamName)),
                isAssignment: true)));

        var nonSuccessResponses = UEResultAstBuilder.GetNonSuccessResponses(operation);
        BlockNode? nonSuccessChain = BuildNonSuccessResponseChain(operation, nonSuccessResponses, ResponseParamName);

        if (hasSuccessBody)
        {
            var successThenStatements = IsSuccessBodyBinaryOrBytes(operation)
                ? new List<StatementNode>
                {
                    new ExpressionStatementNode(
                        new BinaryExpressionNode(
                            new MemberAccessExpressionNode(new IdentifierExpressionNode(new IdentifierNode("Out")), new IdentifierNode("SuccessBody")),
                            BinaryOperator.Assign,
                            new MemberAccessExpressionNode(new IdentifierExpressionNode(new IdentifierNode(ResponseParamName)), new IdentifierNode("BodyBytes")),
                            isAssignment: true))
                }
                : new List<StatementNode>
                {
                    new VariableDeclarationStatementNode(
                        new IdentifierNode("Dto"),
                        new SimpleTypeNode(new IdentifierNode($"const TOptional<{successBodyTypeName}>")),
                        new CallExpressionNode(
                            new MemberAccessExpressionNode(new IdentifierExpressionNode(new IdentifierNode(ResponseParamName)), new IdentifierNode("GetBodyAs")),
                            Array.Empty<ExpressionNode>().ToImmutableList(),
                            genericTypeName: successBodyTypeName)),
                    new VariableDeclarationStatementNode(
                        new IdentifierNode("bDtoIsSet"),
                        new SimpleTypeNode(new IdentifierNode("const bool"), isPrimitive: true),
                        new CallExpressionNode(
                            new MemberAccessExpressionNode(new IdentifierExpressionNode(new IdentifierNode("Dto")), new IdentifierNode("IsSet")),
                            Array.Empty<ExpressionNode>().ToImmutableList())),
                    new IfStatementNode(
                        new IdentifierExpressionNode(new IdentifierNode("bDtoIsSet")),
                        new BlockNode(new StatementNode[]
                        {
                            new ExpressionStatementNode(
                                new BinaryExpressionNode(
                                    new MemberAccessExpressionNode(new IdentifierExpressionNode(new IdentifierNode("Out")), new IdentifierNode("SuccessBody")),
                                    BinaryOperator.Assign,
                                    new CallExpressionNode(
                                        new MemberAccessExpressionNode(new IdentifierExpressionNode(new IdentifierNode("Dto")), new IdentifierNode("GetValue")),
                                        Array.Empty<ExpressionNode>().ToImmutableList()),
                                    isAssignment: true))
                        }.ToImmutableList()))
                };
            var bIsSuccessDecl = new VariableDeclarationStatementNode(
                new IdentifierNode("bIsSuccess"),
                new SimpleTypeNode(new IdentifierNode("const bool"), isPrimitive: true),
                new CallExpressionNode(
                    new MemberAccessExpressionNode(new IdentifierExpressionNode(new IdentifierNode(ResponseParamName)), new IdentifierNode("IsSuccess")),
                    Array.Empty<ExpressionNode>().ToImmutableList()));
            statements.Add(bIsSuccessDecl);

            var successCond = new IdentifierExpressionNode(new IdentifierNode("bIsSuccess"));

            if (nonSuccessChain != null)
            {
                statements.Add(new IfStatementNode(
                    successCond,
                    new BlockNode(successThenStatements.ToImmutableList()),
                    nonSuccessChain));
            }
            else
            {
                statements.Add(new IfStatementNode(
                    successCond,
                    new BlockNode(successThenStatements.ToImmutableList())));
            }
        }
        else if (nonSuccessChain != null)
        {
            foreach (var s in nonSuccessChain.Statements)
                statements.Add(s);
        }

        statements.Add(new ReturnStatementNode(new IdentifierExpressionNode(new IdentifierNode("Out"))));

        var body = new BlockNode(statements.ToImmutableList());
        return new CppParseFunctionNode(
            name: new IdentifierNode(parseName),
            returnType: new SimpleTypeNode(new IdentifierNode(resultTypeName)),
            parameters: parameters,
            body: body);
    }

    /// <summary>
    /// Строит реализации методов Async Action класса
    /// </summary>
    private List<AstNode> BuildAsyncActionImplementations(string clientName, IrOperation operation)
    {
        var className = _naming.AsyncActionClassName(clientName, operation.MethodName);
        var resultTypeName = _naming.ResultStructName(operation.MethodName);
        var paramsTypeName = _naming.ParamsStructName(operation.MethodName);

        var implementations = new List<AstNode>();

        var factoryReturnExpr = new CallExpressionNode(
            new IdentifierExpressionNode(new IdentifierNode("NewAsyncObject")),
            new ExpressionNode[]
            {
                new IdentifierExpressionNode(new IdentifierNode("WorldContextObject")),
                new IdentifierExpressionNode(new IdentifierNode("ClientNameArg")),
                new IdentifierExpressionNode(new IdentifierNode("Params"))
            }.ToImmutableList(),
            genericTypeName: className);
        implementations.Add(new CppAsyncFactoryImplementationNode(
            className: new IdentifierNode(className),
            methodName: new IdentifierNode(operation.MethodName),
            returnType: new SimpleTypeNode(new IdentifierNode($"{className}*")),
            parameters: new[]
            {
                new CppParameterNode(new IdentifierNode("WorldContextObject"), new SimpleTypeNode(new IdentifierNode("UObject")), isPointer: true),
                new CppParameterNode(new IdentifierNode("ClientNameArg"), new SimpleTypeNode(new IdentifierNode("FString")), isConst: true, isReference: true),
                new CppParameterNode(new IdentifierNode("Params"), new SimpleTypeNode(new IdentifierNode(paramsTypeName)), isConst: true, isReference: true)
            },
            body: new BlockNode(new StatementNode[]
            {
                new ReturnStatementNode(factoryReturnExpr)
            }.ToImmutableList())));

        var completeStatements = new List<StatementNode>
        {
            new ExpressionStatementNode(
                new BinaryExpressionNode(
                    new IdentifierExpressionNode(new IdentifierNode("LastResult")),
                    BinaryOperator.Assign,
                    new IdentifierExpressionNode(new IdentifierNode("Result")),
                    isAssignment: true)),
            new ExpressionStatementNode(
                new CallExpressionNode(
                    new MemberAccessExpressionNode(new IdentifierExpressionNode(new IdentifierNode("OnCompleted")), new IdentifierNode("Broadcast")),
                    new ExpressionNode[] { new IdentifierExpressionNode(new IdentifierNode("Result")) }.ToImmutableList())),
            new ExpressionStatementNode(
                new CallExpressionNode(
                    new MemberAccessExpressionNode(new IdentifierExpressionNode(new IdentifierNode("this")), new IdentifierNode("SetReadyToDestroy"), useArrowOperator: true),
                    ImmutableList<ExpressionNode>.Empty))
        };
        implementations.Add(new CppAsyncCompleteImplementationNode(
            className: new IdentifierNode(className),
            methodName: new IdentifierNode("Complete"),
            returnType: new SimpleTypeNode(new IdentifierNode("void"), isPrimitive: true),
            parameters: new[]
            {
                new CppParameterNode(new IdentifierNode("Result"), new SimpleTypeNode(new IdentifierNode(resultTypeName)), isConst: true, isReference: true)
            },
            body: new BlockNode(completeStatements.ToImmutableList())));

        var libraryClassName = _naming.OperationLibraryClassName(clientName, operation.MethodName);
        var rawCall = new CallExpressionNode(
            new MemberAccessExpressionNode(
                new IdentifierExpressionNode(new IdentifierNode(libraryClassName)),
                new IdentifierNode("Send"),
                useScopeOperator: true),
            new ExpressionNode[]
            {
                new IdentifierExpressionNode(new IdentifierNode("Client")),
                new MemberAccessExpressionNode(new IdentifierExpressionNode(new IdentifierNode("State")), new IdentifierNode("Params"))
            }.ToImmutableList());
        var lambdaBody = new BlockNode(new StatementNode[]
        {
            new ReturnStatementNode(rawCall)
        }.ToImmutableList());
        var activateLambda = new CppLambdaExpressionNode(
            new[] { "this" },
            new[] { new CppParameterNode(new IdentifierNode("Client"), new SimpleTypeNode(new IdentifierNode("FValIOClientRef"))) },
            lambdaBody);
        var runWithCall = new CallExpressionNode(
            new MemberAccessExpressionNode(
                new IdentifierExpressionNode(new IdentifierNode("this")),
                new IdentifierNode("RunWithClientAndComplete"),
                useArrowOperator: true),
            new ExpressionNode[] { activateLambda }.ToImmutableList(),
            genericTypeName: $"{className}, {resultTypeName}");
        implementations.Add(new CppAsyncActivateImplementationNode(
            className: new IdentifierNode(className),
            body: new BlockNode(new StatementNode[]
            {
                new ExpressionStatementNode(runWithCall)
            }.ToImmutableList())));

        return implementations;
    }

    // ===== Helper methods =====

    /// <summary>
    /// Строит цепочку if (Response.StatusCode == X) / else if / else для не-успешных ответов.
    /// Возвращает BlockNode с одним StatementNode (IfStatement цепочка).
    /// </summary>
    private BlockNode? BuildNonSuccessResponseChain(IrOperation operation, IReadOnlyList<IrResponse> nonSuccessResponses, string responseParamName = "Response")
    {
        if (nonSuccessResponses.Count == 0) return null;

        var responsesOrdered = nonSuccessResponses
            .OrderBy(r => r.StatusCode == "default" ? 1 : 0)
            .ThenBy(r => r.StatusCode)
            .ToList();

        AstNode? chain = null;
        for (var i = responsesOrdered.Count - 1; i >= 0; i--)
        {
            var r = responsesOrdered[i];
            var bodyBlock = BuildBodyBlockForResponse(r, responseParamName);
            if (r.StatusCode == "default")
            {
                chain = bodyBlock;
            }
            else
            {
                if (!int.TryParse(r.StatusCode, out var statusCode))
                    continue;
                var cond = new BinaryExpressionNode(
                    new MemberAccessExpressionNode(new IdentifierExpressionNode(new IdentifierNode(responseParamName)), new IdentifierNode("StatusCode")),
                    BinaryOperator.Equal,
                    new LiteralExpressionNode(statusCode, LiteralKind.Number));
                var elseBlock = chain != null
                    ? (chain is BlockNode bn ? bn : new BlockNode(new StatementNode[] { (StatementNode)chain }.ToImmutableList()))
                    : null;
                chain = new IfStatementNode(cond, bodyBlock, elseBlock);
            }
        }

        if (chain == null) return null;
        return chain is BlockNode blk ? blk : new BlockNode(new StatementNode[] { (StatementNode)chain }.ToImmutableList());
    }

    /// <summary>
    /// Строит блок для одного не-успешного ответа: Response.GetBodyAs&lt;Type&gt;() и присваивание в Out.Body{StatusCode}.
    /// </summary>
    private BlockNode BuildBodyBlockForResponse(IrResponse response, string responseParamName = "Response")
    {
        var fieldName = _naming.ResultBodyFieldName(response.StatusCode);
        var typeName = GetResponseBodyTypeName(response);

        if (response.Type != null)
        {
            var varName = $"{_naming.ResultBodyFieldName(response.StatusCode)}Opt";
            var statements = new List<StatementNode>
            {
                new VariableDeclarationStatementNode(
                    new IdentifierNode(varName),
                    new SimpleTypeNode(new IdentifierNode($"const TOptional<{typeName}>")),
                    new CallExpressionNode(
                        new MemberAccessExpressionNode(new IdentifierExpressionNode(new IdentifierNode(responseParamName)), new IdentifierNode("GetBodyAs")),
                        Array.Empty<ExpressionNode>().ToImmutableList(),
                        genericTypeName: typeName)),
                new IfStatementNode(
                    new CallExpressionNode(
                        new MemberAccessExpressionNode(new IdentifierExpressionNode(new IdentifierNode(varName)), new IdentifierNode("IsSet")),
                        Array.Empty<ExpressionNode>().ToImmutableList()),
                    new BlockNode(new StatementNode[]
                    {
                        new ExpressionStatementNode(
                            new BinaryExpressionNode(
                                new MemberAccessExpressionNode(new IdentifierExpressionNode(new IdentifierNode("Out")), new IdentifierNode(fieldName)),
                                BinaryOperator.Assign,
                                new CallExpressionNode(
                                    new MemberAccessExpressionNode(new IdentifierExpressionNode(new IdentifierNode(varName)), new IdentifierNode("GetValue")),
                                    Array.Empty<ExpressionNode>().ToImmutableList()),
                                isAssignment: true))
                    }.ToImmutableList()))
            };
            return new BlockNode(statements.ToImmutableList());
        }
        else
        {
            var assign = new ExpressionStatementNode(
                new BinaryExpressionNode(
                    new MemberAccessExpressionNode(new IdentifierExpressionNode(new IdentifierNode("Out")), new IdentifierNode(fieldName)),
                    BinaryOperator.Assign,
                    new CallExpressionNode(
                        new MemberAccessExpressionNode(new IdentifierExpressionNode(new IdentifierNode(responseParamName)), new IdentifierNode("GetBodyAsString")),
                        Array.Empty<ExpressionNode>().ToImmutableList()),
                    isAssignment: true));
            return new BlockNode(new StatementNode[] { assign }.ToImmutableList());
        }
    }

    private string GetResponseBodyTypeName(IrResponse response)
    {
        if (response.Type is IrModelReference modelRef)
            return _typeMapper.IsEnumReference(modelRef.ModelName)
                ? _naming.EnumName(modelRef.ModelName)
                : _naming.StructName(modelRef.ModelName);
        if (response.Type != null)
        {
            var mapped = _typeMapper.MapType(response.Type);
            return mapped.ToString() ?? "FString";
        }
        return "FString";
    }

    /// <summary>
    /// Проверяет, является ли параметр query enum-типом (для AddQueryParamEnumAsString).
    /// </summary>
    private bool IsQueryParamEnum(IrParameter param)
    {
        return param.Type is IrModelReference modelRef && _typeMapper.IsEnumReference(modelRef.ModelName);
    }

    /// <summary>
    /// Проверяет, является ли SuccessBody бинарным/файловым (TArray&lt;uint8&gt;).
    /// В этом случае используем Response.BodyBytes вместо GetBodyAs.
    /// </summary>
    private static bool IsSuccessBodyBinaryOrBytes(IrOperation operation)
    {
        var successResponse = operation.Responses.FirstOrDefault(r =>
            r.StatusCode.StartsWith('2') && r.Type != null);
        if (successResponse?.Type is not IrPrimitiveType prim)
            return false;
        return prim.PrimitiveKind is IrPrimitiveKind.Binary or IrPrimitiveKind.ByteArray or IrPrimitiveKind.File;
    }

    private string GetSuccessBodyTypeName(IrOperation operation)
    {
        var successResponse = operation.Responses.FirstOrDefault(r =>
            r.StatusCode.StartsWith('2') && r.Type != null);

        if (successResponse?.Type is IrModelReference modelRef)
            return _typeMapper.IsEnumReference(modelRef.ModelName)
                ? _naming.EnumName(modelRef.ModelName)
                : _naming.StructName(modelRef.ModelName);

        if (successResponse?.Type != null)
        {
            var mapped = _typeMapper.MapType(successResponse.Type);
            return mapped.ToString() ?? "void";
        }

        return "void";
    }

    private bool HasMultipleParamGroups(IrOperation operation)
    {
        var count = 0;
        if (operation.PathParameters.Count > 0) count++;
        if (operation.QueryParameters.Count > 0) count++;
        if (operation.HeaderParameters.Count > 0) count++;
        if (operation.RequestBody != null) count++;
        return count > 1;
    }

    /// <summary>
    /// Возвращает тип и имя параметра для BuildPathUrl.
    /// При отсутствии path params — (null, null).
    /// Flat: (FxxxParams, "Params"); nested: (FxxxRouteParams, "RouteParams").
    /// </summary>
    private (string? TypeName, string? ParamName) GetBuildPathUrlParamInfo(IrOperation operation)
    {
        if (operation.PathParameters.Count == 0)
            return (null, null);
        if (HasMultipleParamGroups(operation))
            return (_naming.RouteParamsStructName(operation.MethodName), "RouteParams");
        return (_naming.ParamsStructName(operation.MethodName), "Params");
    }

    /// <summary>
    /// Возвращает выражение для вызова BuildPathUrl из Build.
    /// При отсутствии path params — null (вызов без аргументов).
    /// </summary>
    private ExpressionNode? GetBuildPathUrlCallArgument(IrOperation operation)
    {
        if (operation.PathParameters.Count == 0)
            return null;
        if (HasMultipleParamGroups(operation))
            return new MemberAccessExpressionNode(
                new IdentifierExpressionNode(new IdentifierNode("Params")),
                new IdentifierNode("Route"));
        return new IdentifierExpressionNode(new IdentifierNode("Params"));
    }

    private static bool IsReferenceType(IrType type)
    {
        return type switch
        {
            IrPrimitiveType p => p.PrimitiveKind is IrPrimitiveKind.String or IrPrimitiveKind.Uuid,
            IrModelReference => true,
            IrArrayType => true,
            IrDictionaryType => true,
            _ => false
        };
    }

    private static string FormatDisplayName(string methodName)
    {
        // Convert PascalCase to "Pascal Case"
        return System.Text.RegularExpressions.Regex.Replace(
            methodName, "(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " ");
    }

    /// <summary>
    /// Строит ExpressionNode из пути вида "Params.BuildingId" или "Params.Route.BuildingId".
    /// </summary>
    private static ExpressionNode BuildExpressionFromPath(string path)
    {
        var parts = path.Split('.');
        ExpressionNode expr = new IdentifierExpressionNode(new IdentifierNode(parts[0]));
        for (var i = 1; i < parts.Length; i++)
            expr = new MemberAccessExpressionNode(expr, new IdentifierNode(parts[i]));
        return expr;
    }
}
