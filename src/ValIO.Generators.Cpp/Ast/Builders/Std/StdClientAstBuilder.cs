using ValIO.Core.Ast.Builders;
using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Expressions;
using ValIO.Core.Ast.Nodes.Statements;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Core.IR;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Core.IR.Utilities;
using ValIO.Generators.Cpp.Ast.Nodes;
using ValIO.Generators.Cpp.Models;
using ValIO.Generators.Cpp.Services;

namespace ValIO.Generators.Cpp.Ast.Builders.Std;

/// <summary>
/// Builds standalone client contracts and implementations with owned, reusable HTTP transport.
/// </summary>
public sealed class StdClientAstBuilder : AstBuilderBase
{
    /// <summary>Identifies constructor declarations and definitions to the C++ renderer.</summary>
    private const string ConstructorMetadataKey = "cpp.constructor";

    /// <summary>Maps document values into C++ types and wire literals.</summary>
    private readonly StdCppTypeMapper _typeMapper;
    /// <summary>Names generated client declarations and files.</summary>
    private readonly StdNamingConvention _naming;

    /// <summary>Creates a client builder using consistent type and identifier services.</summary>
    /// <param name="typeMapper">The document-aware type mapper.</param>
    /// <param name="naming">The output naming convention.</param>
    /// <param name="documentContext">The finalized declaration indexes shared by this generation.</param>
    /// <exception cref="ArgumentNullException">A required service is missing.</exception>
    /// <exception cref="ArgumentException">The mapper uses different declaration indexes.</exception>
    public StdClientAstBuilder(StdCppTypeMapper typeMapper, StdNamingConvention naming, IrDocumentContext documentContext)
        : base(documentContext)
    {
        _typeMapper = typeMapper ?? throw new ArgumentNullException(nameof(typeMapper));
        _naming = naming ?? throw new ArgumentNullException(nameof(naming));
        if (!ReferenceEquals(typeMapper.DocumentContext, documentContext))
            throw new ArgumentException("The type mapper and builder must use the same document context.", nameof(documentContext));
    }

    /// <summary>Contains the separate contract, concrete header, and implementation ASTs.</summary>
    public sealed class BuildResult
    {
        /// <summary>Gets the optional transport-independent interface.</summary>
        /// <value>The interface header when interface generation is enabled.</value>
        public CppHeaderFileNode? InterfaceHeader { get; init; }
        /// <summary>Gets the concrete client declaration header.</summary>
        /// <value>The reusable HTTP client declaration.</value>
        public required CppHeaderFileNode ClientHeader { get; init; }
        /// <summary>Gets the concrete implementation source.</summary>
        /// <value>The operation and constructor definitions.</value>
        public required CppSourceFileNode ClientSource { get; init; }
    }

    /// <summary>Builds a client and its optional interface for the selected execution style.</summary>
    /// <param name="client">The client contract.</param>
    /// <param name="rootNamespace">The generated namespace.</param>
    /// <param name="options">The client output options.</param>
    /// <param name="warnings">The optional sink for document type-mapping diagnostics.</param>
    /// <returns>The client declarations and implementation.</returns>
    public BuildResult Build(
        IrClient client,
        string rootNamespace,
        CppGenerationOptions options,
        ICollection<string>? warnings = null)
    {
        var clientClassName = _naming.ClientClassName(client.Name);
        var interfaceName = _naming.InterfaceName(client.Name);
        var useInterface = options.ClientShape == CppClientShape.InterfaceImpl;
        var emitSync = options.ApiStyle is CppApiStyle.Sync or CppApiStyle.Both;
        var emitAsync = options.ApiStyle is CppApiStyle.Async or CppApiStyle.Both;
        var enumNames = EnumNames;
        var modelLookup = ModelsByName;
        var routeNames = AllocateRouteNames(client);

        var referencedModels = new HashSet<string>();
        var referencedEnums = new HashSet<string>();
        var originalNames = new Dictionary<string, string>(StringComparer.Ordinal);
        var standardIncludes = new SortedSet<string>(StringComparer.Ordinal);
        var providerIncludes = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var operation in client.Operations)
        {
            CollectOperationReferences(operation, referencedModels, referencedEnums, enumNames);
            foreach (var type in OperationTypes(operation))
            {
                CppTypeReferenceCollector.CollectOriginalNames(type, originalNames);
                foreach (var include in StdCppTypeMapper.StdIncludesFor(type))
                    standardIncludes.Add(include);
                foreach (var include in _typeMapper.ProviderIncludesFor(type))
                    providerIncludes.Add(include);
            }
            if (operation.PathParameters.Concat(operation.QueryParameters).Concat(operation.HeaderParameters).Any(p => !p.IsRequired))
                standardIncludes.Add("<optional>");
        }

        // ===== Интерфейс (только модели + Expected/Error; без runtime-pimpl) =====
        CppHeaderFileNode? interfaceHeader = null;
        if (useInterface)
        {
            var interfaceMethods = new List<DeclarationNode>();
            foreach (var operation in client.Operations)
            {
                if (emitSync)
                    interfaceMethods.Add(BuildMethodDeclaration(operation, isAsync: false, isInterface: true));
                if (emitAsync)
                    interfaceMethods.Add(BuildMethodDeclaration(operation, isAsync: true, isInterface: true));
            }

            interfaceHeader = new CppHeaderFileNode(
                fileName: _naming.InterfaceHeaderPath(client.Name),
                includes: BuildInterfaceIncludes(referencedModels, referencedEnums, originalNames, standardIncludes, providerIncludes, emitAsync),
                declarations:
                [
                    new NamespaceDeclarationNode(new IdentifierNode(rootNamespace),
                    [
                        new InterfaceDeclarationNode(
                            new IdentifierNode(interfaceName),
                            members: interfaceMethods,
                            documentation: StdDocumentation.Create($"API contract for {client.Name}.")),
                    ]),
                ]);
        }

        // ===== Класс клиента =====
        var classMembers = new List<DeclarationNode>
        {
            new MethodDeclarationNode(
                new IdentifierNode(clientClassName),
                parameters: [new ParameterDeclarationNode(new IdentifierNode("config"), new RawTypeNode("const ValIO::HttpConfig&"))],
                accessModifier: AccessModifier.Public,
                documentation: StdDocumentation.Create("Creates an owned HTTP transport.", null,
                    "@param config Transport configuration copied into the owned client.",
                    "@details The transport is shared by synchronous calls and outstanding asynchronous work."),
                metadata: new Dictionary<string, object> { [ConstructorMetadataKey] = true, ["cpp.explicit"] = true }),
            new MethodDeclarationNode(
                new IdentifierNode(clientClassName),
                parameters: [new ParameterDeclarationNode(new IdentifierNode("http"), SharedHttpType())],
                accessModifier: AccessModifier.Public,
                documentation: StdDocumentation.Create("Reuses an existing HTTP transport with shared ownership.", null,
                    "@param http A non-null shared HTTP client. Ownership is retained by this client and its asynchronous calls.",
                    "@throws std::invalid_argument When http is null."),
                metadata: new Dictionary<string, object> { [ConstructorMetadataKey] = true, ["cpp.explicit"] = true }),
        };

        foreach (var operation in client.Operations)
        {
            classMembers.Add(new FieldDeclarationNode(new IdentifierNode(routeNames[operation.MethodName]),
                new SimpleTypeNode(new IdentifierNode("std::string_view")), AccessModifier.Public,
                isReadOnly: true, isStatic: true,
                defaultValue: new LiteralExpressionNode(operation.Path, LiteralKind.String),
                documentation: StdDocumentation.Create($"Route template for {operation.HttpMethod.ToUpperInvariant()} {operation.Path}."),
                metadata: new Dictionary<string, object> { ["cpp.constexpr"] = true }));
            if (emitSync || emitAsync)
                classMembers.Add(BuildMethodDeclaration(operation, isAsync: false, isInterface: false,
                    accessModifier: emitSync ? AccessModifier.Public : AccessModifier.Private));
            if (emitAsync)
                classMembers.Add(BuildMethodDeclaration(operation, isAsync: true, isInterface: false));
        }

        classMembers.Add(new FieldDeclarationNode(
            new IdentifierNode("_http"),
            SharedHttpType(),
            AccessModifier.Private));

        var clientHeader = new CppHeaderFileNode(
            fileName: _naming.ClientHeaderPath(client.Name),
            includes: BuildClientIncludes(client.Name, referencedModels, referencedEnums, originalNames, standardIncludes, providerIncludes, useInterface, emitAsync),
            declarations:
            [
                new NamespaceDeclarationNode(new IdentifierNode(rootNamespace),
                [
                    new ClassDeclarationNode(
                        new IdentifierNode(clientClassName),
                        isSealed: true,
                        baseTypes: useInterface ? [new SimpleTypeNode(new IdentifierNode(interfaceName))] : null,
                        members: classMembers,
                        documentation: StdDocumentation.Create($"HTTP implementation for {client.Name}.", null,
                            "@details Retains shared ownership of its HTTP transport. Outstanding futures remain valid after the client is destroyed.")),
                ]),
            ]);

        // ===== Реализация (.cpp) =====
        var sourceMembers = new List<AstNode>();
        sourceMembers.Add(BuildConstructorDefinition(clientClassName, injectTransport: false));
        sourceMembers.Add(BuildConstructorDefinition(clientClassName, injectTransport: true));
        foreach (var operation in client.Operations)
        {
            if (emitSync || emitAsync)
                sourceMembers.Add(BuildMethodDefinition(clientClassName, operation, enumNames, modelLookup, routeNames[operation.MethodName], isAsync: false));
            if (emitAsync)
                sourceMembers.Add(BuildMethodDefinition(clientClassName, operation, enumNames, modelLookup, routeNames[operation.MethodName], isAsync: true));
        }

        var clientSource = new CppSourceFileNode(
            fileName: _naming.ClientSourcePath(client.Name),
            includes: BuildSourceIncludes(client, referencedModels, referencedEnums, originalNames),
            sections:
            [
                new CppSourceSection(members:
                [
                    new NamespaceDeclarationNode(new IdentifierNode(rootNamespace), sourceMembers.Cast<DeclarationNode>().ToList()),
                ]),
            ]);

        return new BuildResult
        {
            InterfaceHeader = interfaceHeader,
            ClientHeader = clientHeader,
            ClientSource = clientSource,
        };
    }

    // ===== Сигнатуры =====

    /// <summary>Declares an operation or an internal synchronous implementation for async-only clients.</summary>
    /// <param name="operation">The operation contract.</param>
    /// <param name="isAsync">Whether this declaration returns a future.</param>
    /// <param name="isInterface">Whether it declares a pure virtual interface method.</param>
    /// <param name="accessModifier">The visibility of the implementation method.</param>
    /// <returns>The operation declaration.</returns>
    private MethodDeclarationNode BuildMethodDeclaration(
        IrOperation operation, bool isAsync, bool isInterface, AccessModifier accessModifier = AccessModifier.Public)
    {
        var methodName = _naming.MethodName(operation.MethodName);
        return new MethodDeclarationNode(
            new IdentifierNode(isAsync ? $"{methodName}Async" : methodName),
            returnType: BuildExpectedType(StdResponsePlan.Create(operation).ResultType, isAsync),
            parameters: BuildMethodParameters(operation),
            accessModifier: accessModifier,
            isInterfaceMethod: isInterface,
            isConst: true,
            documentation: BuildOperationDocumentation(operation, isAsync));
    }

    /// <summary>Wraps a successful value in the synchronous expected result or asynchronous future.</summary>
    /// <param name="successType">The payload including any bodyless absence state.</param>
    /// <param name="isAsync">Whether a future owns the result.</param>
    /// <returns>The operation result type.</returns>
    private TypeNode BuildExpectedType(IrType? successType, bool isAsync)
    {
        var valueType = successType == null
            ? new SimpleTypeNode(new IdentifierNode("void"), isPrimitive: true)
            : _typeMapper.MapType(successType);
        var expected = new GenericTypeNode(
            new IdentifierNode("ValIO::Expected"),
            [valueType, new SimpleTypeNode(new IdentifierNode("ValIO::Error"))]);
        return isAsync
            ? new GenericTypeNode(new IdentifierNode("std::future"), [expected])
            : expected;
    }

    /// <summary>
    /// Places required arguments before trailing optional arguments and the cancellation token.
    /// </summary>
    /// <param name="operation">The operation being declared.</param>
    /// <returns>The C++ parameter declarations in their invocation order.</returns>
    private List<ParameterDeclarationNode> BuildMethodParameters(IrOperation operation)
    {
        var required = new List<ParameterDeclarationNode>();
        var optional = new List<ParameterDeclarationNode>();

        foreach (var parameter in operation.PathParameters.Concat(operation.QueryParameters).Concat(operation.HeaderParameters))
        {
            var declaration = BuildParameter(parameter);
            (parameter.IsRequired ? required : optional).Add(declaration);
        }

        if (operation.RequestBody != null)
        {
            var body = operation.RequestBody;
            var valueType = _typeMapper.MapType(body.Type);
            var parameter = new ParameterDeclarationNode(
                new IdentifierNode("request"),
                new RawTypeNode(body.IsRequired
                    ? $"const {StdTypeText.Render(valueType)}&"
                    : $"const std::optional<{StdTypeText.Render(valueType)}>&"),
                isRequired: body.IsRequired,
                defaultValue: body.IsRequired ? null : StdExpressions.Ident("std::nullopt"));
            if (body.IsRequired)
                required.Add(parameter);
            else
                optional.Insert(0, parameter);
        }

        required.AddRange(optional);
        required.Add(new ParameterDeclarationNode(new IdentifierNode("cancellationToken"),
            new SimpleTypeNode(new IdentifierNode("ValIO::CancellationToken")), isRequired: false,
            defaultValue: StdExpressions.Ident("{}")));
        return required;
    }

    /// <summary>Preserves required value arguments and optional transport parameter presence.</summary>
    /// <param name="parameter">The wire parameter contract.</param>
    /// <returns>The generated C++ parameter.</returns>
    private ParameterDeclarationNode BuildParameter(IrParameter parameter)
    {
        var name = _naming.PropertyName(parameter.Name);
        var type = _typeMapper.MapType(parameter.Type);
        var isStructReference = parameter.Type is IrModelReference && !_typeMapper.IsEnumReference(((IrModelReference)parameter.Type).ModelName);

        if (parameter.IsRequired)
        {
            return isStructReference
                ? new ParameterDeclarationNode(new IdentifierNode(name), new RawTypeNode($"const {StdTypeText.Render(type)}&"))
                : new ParameterDeclarationNode(new IdentifierNode(name), type);
        }

        // Optional: std::optional + trailing default.
        TypeNode optionalType = isStructReference
            ? new RawTypeNode($"std::optional<{StdTypeText.Render(type)}>")
            : new GenericTypeNode(new IdentifierNode("std::optional"), [type]);
        return new ParameterDeclarationNode(
            new IdentifierNode(name),
            optionalType,
            isRequired: false,
            defaultValue: new IdentifierExpressionNode(new IdentifierNode("std::nullopt")));
    }

    // ===== Тела =====

    /// <summary>Initializes shared transport ownership exactly once through a constructor initializer.</summary>
    /// <param name="clientClassName">The generated client type.</param>
    /// <param name="injectTransport">Whether the caller supplies an existing shared transport.</param>
    /// <returns>The out-of-class constructor definition.</returns>
    private CppFreeFunctionNode BuildConstructorDefinition(string clientClassName, bool injectTransport)
    {
        var metadata = new Dictionary<string, object> { [ConstructorMetadataKey] = true };
        ExpressionNode transport = injectTransport
            ? new CallExpressionNode(StdExpressions.Scope("std", "move"), [StdExpressions.Ident("http")])
            : new CallExpressionNode(StdExpressions.Scope("std", "make_shared"), [StdExpressions.Ident("config")], genericTypeName: "ValIO::HttpClient");
        return new CppFreeFunctionNode(
            new IdentifierNode($"{clientClassName}::{clientClassName}"),
            new RawTypeNode(string.Empty),
            [new CppParameterNode(new IdentifierNode(injectTransport ? "http" : "config"),
                injectTransport ? SharedHttpType() : new RawTypeNode("const ValIO::HttpConfig&"))],
            body: new BlockNode(injectTransport
                ? [new IfStatementNode(new UnaryExpressionNode(UnaryOperator.LogicalNot, StdExpressions.Ident("_http")),
                    new BlockNode([new ThrowStatementNode(new CallExpressionNode(StdExpressions.Scope("std", "invalid_argument"),
                        [new LiteralExpressionNode("HTTP client must not be null", LiteralKind.String)]))]))]
                : []),
            metadata: metadata,
            initializers: [new CallExpressionNode(StdExpressions.Ident("_http"), [transport])]);
    }

    /// <summary>Defines an operation or an async wrapper owning its transport and parameter values.</summary>
    /// <param name="clientClassName">The generated client type.</param>
    /// <param name="operation">The operation contract.</param>
    /// <param name="enumNames">The document's enum names.</param>
    /// <param name="modelLookup">The available request model contracts.</param>
    /// <param name="routeName">The header constant holding the operation's route template.</param>
    /// <param name="isAsync">Whether this definition returns a future.</param>
    /// <returns>The operation definition.</returns>
    private CppFreeFunctionNode BuildMethodDefinition(
        string clientClassName, IrOperation operation, IReadOnlySet<string> enumNames, IReadOnlyDictionary<string, IrModel> modelLookup, string routeName, bool isAsync)
    {
        var methodName = _naming.MethodName(operation.MethodName);
        var name = isAsync ? $"{methodName}Async" : methodName;
        var responsePlan = StdResponsePlan.Create(operation);
        var successType = responsePlan.ResultType;
        var valueTypeName = successType == null
            ? "void"
            : StdTypeText.Render(_typeMapper.MapType(successType));

        if (isAsync)
        {
            var captures = new List<string> { "http = _http" };
            captures.AddRange(operation.PathParameters.Concat(operation.QueryParameters).Concat(operation.HeaderParameters).Select(p => _naming.PropertyName(p.Name)));
            var hasBody = operation.RequestBody != null;
            if (hasBody)
                captures.Add("request");
            captures.Add("cancellationToken");

            // Required parameters and the body precede all trailing default arguments.
            var orderedParameters = operation.PathParameters
                .Concat(operation.QueryParameters).Concat(operation.HeaderParameters)
                .OrderBy(p => p.IsRequired ? 0 : 1).ToList();
            var callArguments = new List<ExpressionNode>();
            var bodyInserted = false;
            foreach (var p in orderedParameters)
            {
                if (!p.IsRequired && hasBody && !bodyInserted)
                {
                    callArguments.Add(StdExpressions.Ident("request"));
                    bodyInserted = true;
                }
                callArguments.Add(StdExpressions.Ident(_naming.PropertyName(p.Name)));
            }
            if (hasBody && !bodyInserted)
                callArguments.Add(StdExpressions.Ident("request"));
            callArguments.Add(StdExpressions.Ident("cancellationToken"));

            return new CppFreeFunctionNode(
                new IdentifierNode($"{clientClassName}::{name}"),
                new GenericTypeNode(new IdentifierNode("std::future"),
                [
                    new GenericTypeNode(new IdentifierNode("ValIO::Expected"),
                        [new RawTypeNode(valueTypeName), new SimpleTypeNode(new IdentifierNode("ValIO::Error"))]),
                ]),
                BuildCppParameters(operation),
                body: new BlockNode(
                [
                    new ReturnStatementNode(new CallExpressionNode(
                        StdExpressions.Scope("std", "async"),
                        [
                            StdExpressions.Scope("std", "launch", "async"),
                            new CppLambdaExpressionNode(
                                captures,
                                [],
                                new BlockNode(
                                [
                                    new VariableDeclarationStatementNode(new IdentifierNode("worker"), new RawTypeNode(clientClassName),
                                        new CallExpressionNode(StdExpressions.Ident(clientClassName), [StdExpressions.Ident("http")])),
                                    new ReturnStatementNode(new CallExpressionNode(
                                        new MemberAccessExpressionNode(StdExpressions.Ident("worker"), new IdentifierNode(methodName)), callArguments)),
                                ])),
                        ])),
                ]),
                isConst: true);
        }

        return new CppFreeFunctionNode(
            new IdentifierNode($"{clientClassName}::{name}"),
            new RawTypeNode($"ValIO::Expected<{valueTypeName}, ValIO::Error>"),
            BuildCppParameters(operation),
            body: BuildCheckedSyncBody(operation, enumNames, modelLookup, valueTypeName, routeName, responsePlan),
            isConst: true);
    }

    /// <summary>Converts checked wire-serialization failures into the generated expected-result contract.</summary>
    /// <param name="operation">The operation whose request is being serialized.</param>
    /// <param name="enumNames">The known enum type names.</param>
    /// <param name="modelLookup">The finalized document's model index.</param>
    /// <param name="valueTypeName">The C++ successful result type.</param>
    /// <param name="routeName">The operation's route constant.</param>
    /// <param name="responsePlan">The operation's successful response policy.</param>
    /// <returns>The checked request, send, and decode body.</returns>
    private BlockNode BuildCheckedSyncBody(IrOperation operation, IReadOnlySet<string> enumNames,
        IReadOnlyDictionary<string, IrModel> modelLookup, string valueTypeName, string routeName, StdResponsePlan responsePlan)
        => new([
            new TryCatchStatementNode(
                new BlockNode(BuildSyncBodyStatements(operation, enumNames, modelLookup, valueTypeName, routeName, responsePlan)),
                [BuildSerializationCatch("const nlohmann::json::exception&", valueTypeName),
                 BuildSerializationCatch("const std::invalid_argument&", valueTypeName)]),
        ]);

    /// <summary>Builds one narrowly typed serialization catch without intercepting allocation or unrelated failures.</summary>
    /// <param name="exceptionType">The known wire-conversion exception type.</param>
    /// <param name="valueTypeName">The operation's successful C++ result type.</param>
    /// <returns>The expected-result conversion for this exception.</returns>
    private static CatchClauseNode BuildSerializationCatch(string exceptionType, string valueTypeName)
        => new(new BlockNode([
            new VariableDeclarationStatementNode(new IdentifierNode("error"), new RawTypeNode("ValIO::Error")),
            new ExpressionStatementNode(new BinaryExpressionNode(
                new MemberAccessExpressionNode(StdExpressions.Ident("error"), new IdentifierNode("What")), BinaryOperator.Assign,
                StdExpressions.Scope("ValIO", "Error", "Code", "Serialization"), isAssignment: true)),
            new ExpressionStatementNode(new BinaryExpressionNode(
                new MemberAccessExpressionNode(StdExpressions.Ident("error"), new IdentifierNode("Message")), BinaryOperator.Assign,
                new CallExpressionNode(new MemberAccessExpressionNode(StdExpressions.Ident("exception"), new IdentifierNode("what"))), isAssignment: true)),
            new ReturnStatementNode(BuildMakeErrorCall(valueTypeName, StdExpressions.Ident("error"))),
        ]), new RawTypeNode(exceptionType), new IdentifierNode("exception"));

    /// <summary>Builds explicit request construction, transport error handling, and successful payload decoding.</summary>
    /// <param name="operation">The operation contract.</param>
    /// <param name="enumNames">The known enum type names.</param>
    /// <param name="modelLookup">The known request models.</param>
    /// <param name="valueTypeName">The complete successful result type.</param>
    /// <param name="routeName">The header route constant.</param>
    /// <param name="responsePlan">The status-specific decoding policy.</param>
    /// <returns>The request construction, transport, and decoding statements.</returns>
    private List<StatementNode> BuildSyncBodyStatements(IrOperation operation, IReadOnlySet<string> enumNames, IReadOnlyDictionary<string, IrModel> modelLookup, string valueTypeName, string routeName, StdResponsePlan responsePlan)
    {
        var statements = new List<StatementNode>();

        // 1. path — литерал + UrlEncode(path-параметры)
        statements.Add(new VariableDeclarationStatementNode(
            new IdentifierNode("path"),
            new SimpleTypeNode(new IdentifierNode("std::string")),
            new CallExpressionNode(StdExpressions.Scope("std", "string"), [StdExpressions.Ident(routeName)])));
        AppendPathParameters(statements, operation);

        // 2. httpRequest
        statements.Add(new VariableDeclarationStatementNode(
            new IdentifierNode("httpRequest"),
            new SimpleTypeNode(new IdentifierNode("ValIO::Request"))));

        // 3. Verb
        statements.Add(new ExpressionStatementNode(new BinaryExpressionNode(
            new MemberAccessExpressionNode(StdExpressions.Ident("httpRequest"), new IdentifierNode("Verb")),
            BinaryOperator.Assign,
            StdExpressions.Scope("ValIO", "HttpVerb", MapHttpVerb(operation.HttpMethod)),
            isAssignment: true)));

        // 4. Path
        statements.Add(new ExpressionStatementNode(new BinaryExpressionNode(
            new MemberAccessExpressionNode(StdExpressions.Ident("httpRequest"), new IdentifierNode("Path")),
            BinaryOperator.Assign,
            StdExpressions.Ident("path"),
            isAssignment: true)));

        // 5. query/header параметры (wire-имена — OriginalName)
        foreach (var parameter in operation.QueryParameters)
            AppendParameterCall(statements, parameter, "AddQuery");
        foreach (var parameter in operation.HeaderParameters)
            AppendParameterCall(statements, parameter, "AddHeader");

        // 6. тело
        AppendRequestBody(statements, operation, modelLookup);

        // 7. sendResult
        statements.Add(new VariableDeclarationStatementNode(
            new IdentifierNode("sendResult"),
            new RawTypeNode("const ValIO::Expected<ValIO::Response, ValIO::Error>"),
            new CallExpressionNode(
                new MemberAccessExpressionNode(StdExpressions.Ident("_http"), new IdentifierNode("Send"), useArrowOperator: true),
                [StdExpressions.Ident("httpRequest"), StdExpressions.Ident("cancellationToken")])));

        // 8. транспортная ошибка
        statements.Add(new IfStatementNode(
            new UnaryExpressionNode(UnaryOperator.LogicalNot, StdExpressions.Ident("sendResult")),
            new BlockNode(
            [
                new ReturnStatementNode(BuildMakeErrorCall(valueTypeName,
                    new CallExpressionNode(new MemberAccessExpressionNode(StdExpressions.Ident("sendResult"), new IdentifierNode("error"))))),
            ])));

        // 9. response
        statements.Add(new VariableDeclarationStatementNode(
            new IdentifierNode("response"),
            new RawTypeNode("const ValIO::Response&"),
            new CallExpressionNode(new MemberAccessExpressionNode(StdExpressions.Ident("sendResult"), new IdentifierNode("value")))));

        // 10. не-2xx
        statements.Add(new IfStatementNode(
            new BinaryExpressionNode(
                new BinaryExpressionNode(
                    new MemberAccessExpressionNode(StdExpressions.Ident("response"), new IdentifierNode("StatusCode")),
                    BinaryOperator.LessThan,
                    new LiteralExpressionNode("200", LiteralKind.Number)),
                BinaryOperator.LogicalOr,
                new BinaryExpressionNode(
                    new MemberAccessExpressionNode(StdExpressions.Ident("response"), new IdentifierNode("StatusCode")),
                    BinaryOperator.GreaterThanOrEqual,
                    new LiteralExpressionNode("300", LiteralKind.Number))),
            new BlockNode(
            [
                new ReturnStatementNode(BuildMakeErrorCall(valueTypeName,
                    new CallExpressionNode(
                        StdExpressions.Scope("ValIO", "ErrorFromStatus"),
                        [
                            new MemberAccessExpressionNode(StdExpressions.Ident("response"), new IdentifierNode("StatusCode")),
                            new MemberAccessExpressionNode(StdExpressions.Ident("response"), new IdentifierNode("Body")),
                        ]))),
            ])));

        if (responsePlan.PayloadType is not null)
        {
            var noContent = BuildNoContentCondition(responsePlan);
            statements.Add(new IfStatementNode(noContent, new BlockNode(
                [
                    responsePlan.AllowsNoContent
                        ? new ReturnStatementNode(new CallExpressionNode(StdExpressions.Ident(valueTypeName)))
                        : new ReturnStatementNode(BuildMakeErrorCall(valueTypeName,
                            new CallExpressionNode(StdExpressions.Scope("ValIO", "ErrorFromStatus"),
                            [new MemberAccessExpressionNode(StdExpressions.Ident("response"), new IdentifierNode("StatusCode")),
                             new MemberAccessExpressionNode(StdExpressions.Ident("response"), new IdentifierNode("Body"))]))),
                ])));
        }

        // Decode only responses whose status permits the declared payload.
        statements.Add(valueTypeName == "void"
            ? new ReturnStatementNode(StdExpressions.Ident("{}"))
            : new ReturnStatementNode(new CallExpressionNode(
                StdExpressions.Scope("ValIO", "Parse"),
                [new MemberAccessExpressionNode(StdExpressions.Ident("response"), new IdentifierNode("Body")),
                 StdJsonWireFormat.BuildArgument(responsePlan.PayloadType!)],
                genericTypeName: valueTypeName)));

        return statements;
    }

    /// <summary>Appends encoded substitutions without repeating route literals inside operation definitions.</summary>
    /// <param name="statements">The operation statements being constructed.</param>
    /// <param name="operation">The operation containing the path template.</param>
    /// <exception cref="InvalidOperationException">A placeholder is malformed or has no declared parameter.</exception>
    private void AppendPathParameters(List<StatementNode> statements, IrOperation operation)
    {
        var template = operation.Path;
        var substituted = new HashSet<string>(StringComparer.Ordinal);

        while (template.Contains('{'))
        {
            var placeholderStart = template.IndexOf('{');
            var placeholderEnd = template.IndexOf('}', placeholderStart + 1);
            if (placeholderEnd < 0)
                throw new InvalidOperationException($"Operation '{operation.MethodName}' has an unterminated path placeholder.");
            var placeholder = template[(placeholderStart + 1)..placeholderEnd];
            var parameter = operation.PathParameters.FirstOrDefault(p => p.OriginalName == placeholder)
                ?? throw new InvalidOperationException($"Operation '{operation.MethodName}' has no path parameter for '{{{placeholder}}}'.");
            template = template[(placeholderEnd + 1)..];
            if (!substituted.Add(placeholder))
                continue;
            var encoded = new CallExpressionNode(
                StdExpressions.Scope("ValIO", "UrlEncode"),
                [StdExpressions.Ident(_typeMapper.FormatQueryLiteral(parameter.Type, _naming.PropertyName(parameter.Name)))]);
            statements.Add(new ExpressionStatementNode(new CallExpressionNode(StdExpressions.Scope("ValIO", "ReplacePathParameter"),
                [StdExpressions.Ident("path"), new LiteralExpressionNode("{" + placeholder + "}", LiteralKind.String), encoded])));
        }
    }

    /// <summary>Builds no-content status dispatch with exact response declarations taking precedence over ranges.</summary>
    /// <param name="plan">The operation's selected successful response contracts.</param>
    /// <returns>The condition that skips payload decoding.</returns>
    private static ExpressionNode BuildNoContentCondition(StdResponsePlan plan)
    {
        ExpressionNode condition = new BinaryExpressionNode(BuildStatusComparison(204), BinaryOperator.LogicalOr, BuildStatusComparison(205));
        foreach (var response in plan.Responses.Where(response => StdResponsePlan.IsBodyless(response)
            && int.TryParse(response.StatusCode, out _)))
            condition = new BinaryExpressionNode(condition, BinaryOperator.LogicalOr, BuildStatusComparison(int.Parse(response.StatusCode, System.Globalization.CultureInfo.InvariantCulture)));
        if (plan.Responses.Any(response => response.StatusCode == "2XX" && StdResponsePlan.IsBodyless(response)))
        {
            ExpressionNode? rangeCondition = null;
            foreach (var response in plan.Responses.Where(response => !StdResponsePlan.IsBodyless(response)
                && int.TryParse(response.StatusCode, out _)))
            {
                var exactExclusion = BuildStatusComparison(int.Parse(response.StatusCode, System.Globalization.CultureInfo.InvariantCulture), BinaryOperator.NotEqual);
                rangeCondition = rangeCondition is null ? exactExclusion : new BinaryExpressionNode(rangeCondition, BinaryOperator.LogicalAnd, exactExclusion);
            }
            condition = rangeCondition is null ? new LiteralExpressionNode(true, LiteralKind.Boolean)
                : new BinaryExpressionNode(condition, BinaryOperator.LogicalOr, rangeCondition);
        }
        return condition;
    }

    /// <summary>Compares the received response status with an exact HTTP status code.</summary>
    /// <param name="code">The exact status code.</param>
    /// <param name="comparison">The comparison used by exact and range dispatch.</param>
    /// <returns>The structured status comparison.</returns>
    private static ExpressionNode BuildStatusComparison(int code, BinaryOperator comparison = BinaryOperator.Equal) => new BinaryExpressionNode(
        new MemberAccessExpressionNode(StdExpressions.Ident("response"), new IdentifierNode("StatusCode")), comparison,
        new LiteralExpressionNode(code, LiteralKind.Number));

    /// <summary>Allocates stable route constant names without colliding with generated methods.</summary>
    /// <param name="client">The client whose operations share a class scope.</param>
    /// <returns>The route name for every operation.</returns>
    private Dictionary<string, string> AllocateRouteNames(IrClient client)
    {
        var used = client.Operations.SelectMany(operation => new[] { _naming.MethodName(operation.MethodName), _naming.MethodName(operation.MethodName) + "Async" })
            .Append(_naming.ClientClassName(client.Name)).ToHashSet(StringComparer.Ordinal);
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var operation in client.Operations)
        {
            var name = _naming.MethodName(operation.MethodName) + "Path";
            while (!used.Add(name))
                name += "_";
            names.Add(operation.MethodName, name);
        }
        return names;
    }

    /// <summary>Builds the standard shared transport ownership type.</summary>
    /// <returns>The HTTP client shared pointer type.</returns>
    private static TypeNode SharedHttpType() => new GenericTypeNode(new IdentifierNode("std::shared_ptr"),
        [new SimpleTypeNode(new IdentifierNode("ValIO::HttpClient"))]);

    /// <summary>Documents operation parameters, HTTP behavior, and async lifetime ownership.</summary>
    /// <param name="operation">The operation being declared.</param>
    /// <param name="isAsync">Whether the declaration returns a future.</param>
    /// <returns>The Doxygen method documentation.</returns>
    private CommentNode BuildOperationDocumentation(IrOperation operation, bool isAsync)
    {
        var lines = new List<string> { "@details " + operation.HttpMethod.ToUpperInvariant() + " " + operation.Path };
        foreach (var parameter in operation.PathParameters.Concat(operation.QueryParameters).Concat(operation.HeaderParameters))
            lines.Add("@param " + _naming.PropertyName(parameter.Name) + " " + (parameter.Description ?? "Value for " + parameter.OriginalName + "."));
        if (operation.QueryParameters.Concat(operation.HeaderParameters).Any(parameter => parameter.Style is null
            && UsesJsonParameterFormatter(IrTypeQueries.UnwrapNullableOptional(parameter.Type) is IrArrayType array
                ? IrTypeQueries.UnwrapNullableOptional(array.ElementType) : IrTypeQueries.UnwrapNullableOptional(parameter.Type))))
            lines.Add("@details Object-valued parameters from legacy IR without style metadata use JSON text encoding; OpenAPI object parameter styles require a supported explicit codec.");
        if (operation.RequestBody is not null)
        {
            lines.Add("@param request " + (operation.RequestBody.Description ?? "The request payload."));
            if (!operation.RequestBody.IsRequired)
                lines.Add("@details An empty outer optional omits the request body and its content type; a present nullable value serializes as JSON null.");
        }
        lines.Add("@param cancellationToken Cooperative cancellation, retained by asynchronous work.");
        lines.Add(isAsync
            ? "@return A future retaining the shared transport and copied arguments; resolves to the successful value or a transport, status, or decoding error."
            : "@return The successful value, or a transport, HTTP status, or decoding error.");
        if (StdResponsePlan.Create(operation).AllowsNoContent)
            lines.Add("@details A bodyless successful response is represented by an empty optional payload.");
        return StdDocumentation.Create(operation.Summary ?? "Executes " + operation.MethodName + ".", operation.Description, lines.ToArray());
    }

    /// <summary>Delegates typed query and header presence, collections, and wire conversion to the shared request runtime.</summary>
    /// <param name="statements">The operation statements being constructed.</param>
    /// <param name="parameter">The wire parameter contract.</param>
    /// <param name="addMethod">The runtime request method receiving the name and value.</param>
    private void AppendParameterCall(List<StatementNode> statements, IrParameter parameter, string addMethod)
    {
        var name = _naming.PropertyName(parameter.Name);
        var valueType = IrTypeQueries.UnwrapNullableOptional(parameter.Type);
        var collection = valueType as IrArrayType;
        var leafType = collection is null ? valueType : IrTypeQueries.UnwrapNullableOptional(collection.ElementType);
        if (leafType is IrArrayType)
            throw new NotSupportedException($"C++ parameter '{parameter.OriginalName}' requires an unsupported nested collection wire encoding.");
        ValidateParameterStyle(parameter, addMethod == "AddQuery", collection is not null, UsesJsonParameterFormatter(leafType));
        var wireName = new LiteralExpressionNode(parameter.OriginalName, LiteralKind.String);
        ExpressionNode value = StdExpressions.Ident(name);
        if (parameter.IsRequired && parameter.Type is IrNullableType or IrOptionalType)
            value = new CallExpressionNode(StdExpressions.Scope("ValIO", "RequireParameterValue"), [wireName, value]);
        var arguments = new List<ExpressionNode>
        {
            wireName,
            value,
        };
        var formatter = BuildParameterFormatter(leafType);
        if (formatter is not null)
            arguments.Add(formatter);
        if (collection is not null)
            arguments.Add(StdExpressions.Scope("ValIO", "CollectionEncoding", ResolveCollectionEncoding(parameter, addMethod == "AddQuery")));
        statements.Add(new ExpressionStatementNode(new CallExpressionNode(
            new MemberAccessExpressionNode(StdExpressions.Ident("httpRequest"), new IdentifierNode(addMethod)), arguments)));
    }

    /// <summary>Retains schema-specific native formats and model JSON at the leaf conversion boundary.</summary>
    /// <param name="type">The present scalar value after runtime presence and collection traversal.</param>
    /// <returns>A typed formatter lambda, or null when the runtime's ordinary scalar or enum codec applies.</returns>
    private CppLambdaExpressionNode? BuildParameterFormatter(IrType type)
    {
        var isNative = _typeMapper.IsNativeStringValue(type);
        var isJson = UsesJsonParameterFormatter(type);
        if (!isNative && !isJson)
            return null;
        return new CppLambdaExpressionNode([], [new CppParameterNode(new IdentifierNode("value"),
            new SimpleTypeNode(new IdentifierNode("auto")), isConst: true, isReference: true)],
            new BlockNode([new ReturnStatementNode(new CallExpressionNode(
                StdExpressions.Scope("ValIO", isNative ? "ToWireText" : "Dump"),
                [StdExpressions.Ident("value"), StdJsonWireFormat.BuildArgument(type)]))]));
    }

    /// <summary>Identifies object leaves whose legacy HTTP representation is checked JSON text.</summary>
    /// <param name="type">The unwrapped present scalar leaf.</param>
    /// <returns>Whether a model or dictionary requires the JSON codec rather than an ordinary scalar formatter.</returns>
    private bool UsesJsonParameterFormatter(IrType type)
        => type is IrModelReference reference && !EnumNames.Contains(reference.ModelName) || type is IrDictionaryType;

    /// <summary>Rejects styles that cannot be represented by the selected HTTP parameter location.</summary>
    /// <param name="parameter">The source serialization contract.</param>
    /// <param name="isQuery">Whether the value belongs to the query string.</param>
    /// <param name="isCollection">Whether the value has a flat array representation.</param>
    /// <param name="isObject">Whether the value requires a schema-aware object codec.</param>
    /// <exception cref="NotSupportedException">The explicit style has no implemented lossless wire representation.</exception>
    private static void ValidateParameterStyle(IrParameter parameter, bool isQuery, bool isCollection, bool isObject)
    {
        var style = parameter.Style ?? (isQuery ? IrParameterStyle.Form : IrParameterStyle.Simple);
        var supported = isQuery
            ? style == IrParameterStyle.Form || isCollection && style is IrParameterStyle.SpaceDelimited or IrParameterStyle.PipeDelimited
            : style == IrParameterStyle.Simple;
        if (!supported)
            throw new NotSupportedException($"C++ parameter '{parameter.OriginalName}' has unsupported {(isQuery ? "query" : "header")} style '{style}'.");
        if (isObject && parameter.Style is not null)
            throw new NotSupportedException($"C++ object parameter '{parameter.OriginalName}' has unsupported {(isQuery ? "query" : "header")} style '{style}': its OpenAPI object wire codec is not implemented.");
    }

    /// <summary>Maps the schema's declared array serialization to a named shared runtime encoding.</summary>
    /// <param name="parameter">The actual array parameter metadata.</param>
    /// <param name="isQuery">Whether the parameter belongs to the query string rather than a header.</param>
    /// <returns>The runtime collection encoding member.</returns>
    /// <exception cref="NotSupportedException">The style has no supported lossless array wire representation.</exception>
    private static string ResolveCollectionEncoding(IrParameter parameter, bool isQuery)
    {
        var style = parameter.Style ?? (isQuery ? IrParameterStyle.Form : IrParameterStyle.Simple);
        var explode = parameter.Explode ?? style == IrParameterStyle.Form;
        return (isQuery, style, explode) switch
        {
            (true, IrParameterStyle.Form, true) => "Repeated",
            (true, IrParameterStyle.Form, false) => "CommaSeparated",
            (true, IrParameterStyle.SpaceDelimited, false) => "SpaceSeparated",
            (true, IrParameterStyle.PipeDelimited, false) => "PipeSeparated",
            (false, IrParameterStyle.Simple, _) => "CommaSeparated",
            _ => throw new NotSupportedException($"C++ array parameter '{parameter.OriginalName}' has unsupported {(isQuery ? "query" : "header")} style '{style}' with explode={explode}."),
        };
    }

    /// <summary>Builds JSON or form request content while preserving optional field presence.</summary>
    /// <param name="statements">The operation statements being constructed.</param>
    /// <param name="operation">The operation contract.</param>
    /// <param name="modelLookup">The available request model contracts.</param>
    private void AppendRequestBody(List<StatementNode> statements, IrOperation operation, IReadOnlyDictionary<string, IrModel> modelLookup)
    {
        var body = operation.RequestBody;
        if (body == null)
        {
            return;
        }

        var bodyStatements = new List<StatementNode>();
        ExpressionNode requestValue = body.IsRequired ? StdExpressions.Ident("request")
            : new UnaryExpressionNode(UnaryOperator.Dereference, StdExpressions.Ident("request"));
        var requestAccess = body.IsRequired ? "request" : "(*request)";

        if (body.IsJson)
        {
            bodyStatements.Add(new VariableDeclarationStatementNode(
                new IdentifierNode("body"),
                new SimpleTypeNode(new IdentifierNode("std::string")),
                new CallExpressionNode(
                    StdExpressions.Scope("ValIO", "Dump"),
                    [requestValue, StdJsonWireFormat.BuildArgument(body.Type)])));
            bodyStatements.Add(new ExpressionStatementNode(new CallExpressionNode(
                new MemberAccessExpressionNode(StdExpressions.Ident("httpRequest"), new IdentifierNode("SetJsonBody")),
                [StdExpressions.Ident("body")])));
        }
        else if (body.IsFormUrlEncoded || body.IsMultipartFormData)
        {
            if (body.IsMultipartFormData)
                bodyStatements.Add(new ExpressionStatementNode(new BinaryExpressionNode(
                    new MemberAccessExpressionNode(StdExpressions.Ident("httpRequest"), new IdentifierNode("ContentType")),
                    BinaryOperator.Assign, new LiteralExpressionNode("multipart/form-data", LiteralKind.String), isAssignment: true)));
            foreach (var property in GetBodyProperties(body, modelLookup))
                bodyStatements.Add(BuildFormProperty(property, body.IsMultipartFormData, requestAccess));
            if (body.IsFormUrlEncoded)
                bodyStatements.Add(new ExpressionStatementNode(new BinaryExpressionNode(
                    new MemberAccessExpressionNode(StdExpressions.Ident("httpRequest"), new IdentifierNode("ContentType")),
                    BinaryOperator.Assign, new LiteralExpressionNode("application/x-www-form-urlencoded", LiteralKind.String), isAssignment: true)));
        }
        else if (body.Type is IrPrimitiveType { PrimitiveKind: ValIO.Core.IR.Enums.IrPrimitiveKind.Binary or ValIO.Core.IR.Enums.IrPrimitiveKind.ByteArray or ValIO.Core.IR.Enums.IrPrimitiveKind.File })
        {
            bodyStatements.Add(new ExpressionStatementNode(new CallExpressionNode(
                new MemberAccessExpressionNode(new MemberAccessExpressionNode(StdExpressions.Ident("httpRequest"), new IdentifierNode("Body")), new IdentifierNode("assign")),
                [new CallExpressionNode(new MemberAccessExpressionNode(requestValue, new IdentifierNode("begin"))),
                 new CallExpressionNode(new MemberAccessExpressionNode(requestValue, new IdentifierNode("end")))])));
            bodyStatements.Add(new ExpressionStatementNode(new BinaryExpressionNode(
                new MemberAccessExpressionNode(StdExpressions.Ident("httpRequest"), new IdentifierNode("ContentType")),
                BinaryOperator.Assign, new LiteralExpressionNode(body.ContentType ?? "application/octet-stream", LiteralKind.String), isAssignment: true)));
        }
        else
            throw new NotSupportedException($"C++ operation '{operation.MethodName}' requires an unsupported request body codec for '{body.ContentType}'.");

        if (body.IsRequired)
            statements.AddRange(bodyStatements);
        else
            statements.Add(new IfStatementNode(StdExpressions.Ident("request"), new BlockNode(bodyStatements)));
    }

    /// <summary>Builds one form field or binary part and omits absent optional fields.</summary>
    /// <param name="property">The request model property.</param>
    /// <param name="isMultipart">Whether binary values are emitted as file parts.</param>
    /// <param name="requestAccess">The present request value, after any outer optional is unwrapped.</param>
    /// <returns>The field conversion and its optional presence guard.</returns>
    private StatementNode BuildFormProperty(IrProperty property, bool isMultipart, string requestAccess)
    {
        var field = $"{requestAccess}.{_naming.PropertyName(property.Name)}";
        var isOptional = StdPropertyTypes.IsOptional(property);
        var value = isOptional ? $"(*{field})" : field;
        var type = StdPropertyTypes.ValueType(property);
        var isBinary = isMultipart && IrTypeQueries.IsFileOrBinary(type);
        var nullable = type as IrNullableType;
        var concreteValue = nullable == null ? value : $"(*{value})";
        var concreteType = nullable?.InnerType ?? type;

        StatementNode statement;
        if (isBinary)
        {
            var fileValue = concreteType is IrArrayType ? "filePart" : concreteValue;
            statement = new ExpressionStatementNode(new CallExpressionNode(
                new MemberAccessExpressionNode(StdExpressions.Ident("httpRequest"), new IdentifierNode("AddFilePart")),
                [
                    new LiteralExpressionNode(property.OriginalName, LiteralKind.String),
                    new LiteralExpressionNode(property.OriginalName, LiteralKind.String),
                    StdExpressions.Ident(fileValue),
                    new LiteralExpressionNode("application/octet-stream", LiteralKind.String),
                ]));
            if (concreteType is IrArrayType)
                statement = new ForEachStatementNode(new IdentifierNode("filePart"), StdExpressions.Ident(concreteValue), new BlockNode([statement]));
            if (nullable != null)
                statement = new IfStatementNode(StdExpressions.Ident(value), new BlockNode([statement]));
        }
        else
        {
            var leafType = concreteType is IrArrayType array ? IrTypeQueries.UnwrapNullableOptional(array.ElementType) : concreteType;
            var formatter = BuildParameterFormatter(leafType);
            if (formatter is not null)
            {
                statement = new ExpressionStatementNode(new CallExpressionNode(
                    new MemberAccessExpressionNode(StdExpressions.Ident("httpRequest"), new IdentifierNode("AddFormField")),
                    [new LiteralExpressionNode(property.OriginalName, LiteralKind.String), StdExpressions.Ident(concreteValue), formatter]));
                if (nullable is not null)
                    statement = new IfStatementNode(StdExpressions.Ident(value), new BlockNode([statement]),
                        new BlockNode([new ExpressionStatementNode(new CallExpressionNode(
                            new MemberAccessExpressionNode(StdExpressions.Ident("httpRequest"), new IdentifierNode("AddFormField")),
                            [new LiteralExpressionNode(property.OriginalName, LiteralKind.String), new LiteralExpressionNode("null", LiteralKind.String)]))]));
            }
            else
            {
                var literal = _typeMapper.FormatQueryLiteral(concreteType, concreteValue);
                var wireValue = nullable == null ? literal : $"({value} ? {literal} : \"null\")";
                statement = new ExpressionStatementNode(new CallExpressionNode(
                    new MemberAccessExpressionNode(StdExpressions.Ident("httpRequest"), new IdentifierNode("AddFormField")),
                    [new LiteralExpressionNode(property.OriginalName, LiteralKind.String), StdExpressions.Ident(wireValue)]));
            }
        }

        return isOptional ? new IfStatementNode(StdExpressions.Ident(field), new BlockNode([statement])) : statement;
    }

    /// <summary>Retrieves all form model properties, including inherited declarations.</summary>
    /// <param name="body">The request body contract.</param>
    /// <param name="modelLookup">The document's model index.</param>
    /// <returns>The complete wire-property set.</returns>
    private static IEnumerable<IrProperty> GetBodyProperties(IrRequestBody body, IReadOnlyDictionary<string, IrModel> modelLookup)
    {
        return body.Type is IrModelReference modelRef && modelLookup.TryGetValue(modelRef.ModelName, out var model)
            ? IrInheritancePlan.FlattenProperties(model, modelLookup)
            : [];
    }

    /// <summary>Builds the typed expected failure conversion.</summary>
    /// <param name="valueTypeName">The successful C++ result type.</param>
    /// <param name="errorExpression">The error value being returned.</param>
    /// <returns>The runtime error-conversion call.</returns>
    private static CallExpressionNode BuildMakeErrorCall(string valueTypeName, ExpressionNode errorExpression)
        => new(
            StdExpressions.Scope("ValIO", "MakeError"),
            [errorExpression],
            genericTypeName: valueTypeName);

    /// <summary>Creates implementation parameters without repeating declaration default arguments.</summary>
    /// <param name="operation">The operation whose arguments are defined.</param>
    /// <returns>The ordered implementation arguments.</returns>
    private List<CppParameterNode> BuildCppParameters(IrOperation operation)
        => BuildMethodParameters(operation)
            .Select(p => new CppParameterNode((IdentifierNode)p.Name.Clone(), p.Type!))
            .ToList();

    /// <summary>Maps supported HTTP methods without silently changing unknown verbs to GET.</summary>
    /// <param name="httpMethod">The schema's HTTP method.</param>
    /// <returns>The runtime HTTP verb member name.</returns>
    /// <exception cref="NotSupportedException">The runtime does not implement the declared HTTP method.</exception>
    private static string MapHttpVerb(string httpMethod) => httpMethod.Trim().ToUpperInvariant() switch
    {
        "GET" => "Get",
        "POST" => "Post",
        "PUT" => "Put",
        "DELETE" => "Delete",
        "PATCH" => "Patch",
        "HEAD" => "Head",
        "OPTIONS" => "Options",
        _ => throw new NotSupportedException($"The C++ HTTP runtime does not support the '{httpMethod}' method."),
    };

    /// <summary>Collects model and enum declarations used anywhere in the operation contract.</summary>
    /// <param name="operation">The operation to inspect.</param>
    /// <param name="models">The model-reference sink.</param>
    /// <param name="enums">The enum-reference sink.</param>
    /// <param name="enumNames">The known enum type names.</param>
    private void CollectOperationReferences(IrOperation operation, ISet<string> models, ISet<string> enums, IReadOnlySet<string> enumNames)
    {
        foreach (var parameter in operation.PathParameters.Concat(operation.QueryParameters).Concat(operation.HeaderParameters))
            CppTypeReferenceCollector.Collect(parameter.Type, models, enums, enumNames);
        if (operation.RequestBody != null && !IrTypeQueries.IsFileOrBinary(operation.RequestBody.Type))
            CppTypeReferenceCollector.Collect(operation.RequestBody.Type, models, enums, enumNames);
        foreach (var response in operation.Responses)
        {
            if (response.Type != null)
                CppTypeReferenceCollector.Collect(response.Type, models, enums, enumNames);
        }
    }

    /// <summary>Enumerates every type used by an operation for include and original-name resolution.</summary>
    /// <param name="operation">The operation contract.</param>
    /// <returns>The parameter, body, and response types.</returns>
    private static IEnumerable<IrType> OperationTypes(IrOperation operation)
    {
        foreach (var parameter in operation.PathParameters.Concat(operation.QueryParameters).Concat(operation.HeaderParameters))
            yield return parameter.Type;
        if (operation.RequestBody != null)
            yield return operation.RequestBody.Type;
        foreach (var response in operation.Responses)
            if (response.Type != null)
                yield return response.Type;
    }

    /// <summary>Builds self-contained interface includes without depending on model transitive includes.</summary>
    /// <param name="referencedModels">The referenced model names.</param>
    /// <param name="referencedEnums">The referenced enum names.</param>
    /// <param name="originalNames">The stable source names used for file paths.</param>
    /// <param name="standardIncludes">The standard headers required by all operation types.</param>
    /// <param name="providerIncludes">Pure native scalar declarations required directly by operations.</param>
    /// <param name="emitAsync">Whether future declarations are generated.</param>
    /// <returns>The interface include directives.</returns>
    private List<CppIncludeDirectiveNode> BuildInterfaceIncludes(
        HashSet<string> referencedModels, HashSet<string> referencedEnums, IReadOnlyDictionary<string, string> originalNames,
        IEnumerable<string> standardIncludes, IEnumerable<string> providerIncludes, bool emitAsync)
    {
        var includes = standardIncludes.Select(i => new CppIncludeDirectiveNode(i.Trim('<', '>'), isSystemInclude: true)).ToList();
        includes.Add(new CppIncludeDirectiveNode("optional", isSystemInclude: true));
        if (emitAsync)
            includes.Add(new CppIncludeDirectiveNode("future", isSystemInclude: true));
        includes.Add(new CppIncludeDirectiveNode("Runtime/ValIOError.h"));
        includes.Add(new CppIncludeDirectiveNode("Runtime/ValIOExpected.h"));
        includes.Add(new CppIncludeDirectiveNode("Runtime/ValIOCancellation.h"));
        includes.AddRange(providerIncludes.Select(path => new CppIncludeDirectiveNode(path)));
        includes.AddRange(BuildModelIncludes(referencedModels, referencedEnums, originalNames));
        return includes;
    }

    /// <summary>Builds client includes with explicit standard dependencies and stable schema file names.</summary>
    /// <param name="clientName">The client contract name.</param>
    /// <param name="referencedModels">The referenced model names.</param>
    /// <param name="referencedEnums">The referenced enum names.</param>
    /// <param name="originalNames">The stable source names used for file paths.</param>
    /// <param name="standardIncludes">The standard headers required by all operation types.</param>
    /// <param name="providerIncludes">Pure native scalar declarations required directly by operations.</param>
    /// <param name="useInterface">Whether the client implements a generated interface.</param>
    /// <param name="emitAsync">Whether future declarations are generated.</param>
    /// <returns>The client include directives.</returns>
    private List<CppIncludeDirectiveNode> BuildClientIncludes(
        string clientName, HashSet<string> referencedModels, HashSet<string> referencedEnums,
        IReadOnlyDictionary<string, string> originalNames, IEnumerable<string> standardIncludes,
        IEnumerable<string> providerIncludes, bool useInterface, bool emitAsync)
    {
        var includes = standardIncludes.Select(i => new CppIncludeDirectiveNode(i.Trim('<', '>'), isSystemInclude: true)).ToList();
        includes.Add(new CppIncludeDirectiveNode("memory", isSystemInclude: true));
        includes.Add(new CppIncludeDirectiveNode("string_view", isSystemInclude: true));
        includes.Add(new CppIncludeDirectiveNode("optional", isSystemInclude: true));
        if (emitAsync)
            includes.Add(new CppIncludeDirectiveNode("future", isSystemInclude: true));
        if (useInterface)
            includes.Add(new CppIncludeDirectiveNode(_naming.InterfaceHeaderPath(clientName)));
        includes.Add(new CppIncludeDirectiveNode("Runtime/ValIOError.h"));
        includes.Add(new CppIncludeDirectiveNode("Runtime/ValIOExpected.h"));
        includes.Add(new CppIncludeDirectiveNode("Runtime/ValIOHttp.h"));
        includes.AddRange(providerIncludes.Select(path => new CppIncludeDirectiveNode(path)));
        includes.AddRange(BuildModelIncludes(referencedModels, referencedEnums, originalNames));
        return includes;
    }

    /// <summary>Collects codec declarations using original schema names even after identifier affixes.</summary>
    /// <param name="client">The client contract whose form fields invoke leaf codecs directly.</param>
    /// <param name="referencedModels">The referenced model names.</param>
    /// <param name="referencedEnums">The referenced enum names.</param>
    /// <param name="originalNames">The stable source names used for file paths.</param>
    /// <returns>The implementation include directives.</returns>
    private List<CppIncludeDirectiveNode> BuildSourceIncludes(
        IrClient client, HashSet<string> referencedModels, HashSet<string> referencedEnums, IReadOnlyDictionary<string, string> originalNames)
    {
        var codecModels = new HashSet<string>(referencedModels, StringComparer.Ordinal);
        var codecEnums = new HashSet<string>(referencedEnums, StringComparer.Ordinal);
        foreach (var operation in client.Operations)
        {
            if (operation.RequestBody is not { IsForm: true } body)
                continue;
            foreach (var property in GetBodyProperties(body, ModelsByName))
                CppTypeReferenceCollector.Collect(property.Type, codecModels, codecEnums, EnumNames);
        }
        var includes = new List<CppIncludeDirectiveNode>
        {
            new(_naming.ClientHeaderPath(client.Name)),
            new("Runtime/ValIOJson.h"),
            new("stdexcept", isSystemInclude: true),
        };
        foreach (var refModel in codecModels.OrderBy(x => x, StringComparer.Ordinal))
            includes.Add(new CppIncludeDirectiveNode(_naming.SerializerHeaderPath(ModelsByName.TryGetValue(refModel, out var model)
                ? model.OriginalName : originalNames.GetValueOrDefault(refModel, refModel))));
        foreach (var refEnum in codecEnums.OrderBy(x => x, StringComparer.Ordinal))
            includes.Add(new CppIncludeDirectiveNode(_naming.SerializerHeaderPath(EnumsByName.TryGetValue(refEnum, out var declaration)
                ? declaration.OriginalName : originalNames.GetValueOrDefault(refEnum, refEnum))));
        return includes;
    }

    /// <summary>Builds model and enum includes from stable schema names.</summary>
    /// <param name="referencedModels">The referenced model names.</param>
    /// <param name="referencedEnums">The referenced enum names.</param>
    /// <param name="originalNames">The stable source names used for file paths.</param>
    /// <returns>The referenced declaration include directives.</returns>
    private IEnumerable<CppIncludeDirectiveNode> BuildModelIncludes(HashSet<string> referencedModels, HashSet<string> referencedEnums,
        IReadOnlyDictionary<string, string> originalNames)
    {
        foreach (var refModel in referencedModels.OrderBy(x => x, StringComparer.Ordinal))
            yield return new CppIncludeDirectiveNode(_naming.ModelHeaderPath(originalNames.GetValueOrDefault(refModel, refModel)));
        foreach (var refEnum in referencedEnums.OrderBy(x => x, StringComparer.Ordinal))
            yield return new CppIncludeDirectiveNode(_naming.EnumHeaderPath(originalNames.GetValueOrDefault(refEnum, refEnum)));
    }
}
