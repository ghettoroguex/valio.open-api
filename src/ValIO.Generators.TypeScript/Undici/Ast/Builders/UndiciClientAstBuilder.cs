using ValIO.Core.Ast.Builders;
using System.Collections.Immutable;
using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Expressions;
using ValIO.Core.Ast.Nodes.Statements;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Core.Interfaces;
using ValIO.Core.IR;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Core.Models;
using ValIO.Generators.TypeScript.Ast.Builders;
using ValIO.Generators.TypeScript.Ast.JsDoc;
using ValIO.Generators.TypeScript.Ast.Nodes;
using ValIO.Generators.TypeScript.Ast.Nodes.Expressions;
using ValIO.Generators.TypeScript.Options;

namespace ValIO.Generators.TypeScript.Undici.Ast.Builders;

/// <summary>
/// Builds Undici operation clients as TypeScript AST.
/// Generates one <c>{op}.undici.ts</c> per IrOperation.
/// </summary>
public sealed class UndiciClientAstBuilder : AstBuilderBase
{

    private readonly INamingConverter _namingConverter;
    private readonly TypeScriptParamsAstBuilder _paramsBuilder;
    private readonly TypeScriptResultsAstBuilder _resultsBuilder;
    private readonly Validation.IClientValidationStrategy _validation;


    /// <summary>Creates a UndiciClientAstBuilder for one finalized IR document.</summary>
    /// <param name="documentContext">The shared immutable indexes of the finalized IR document.</param>
    /// <param name="namingConverter">The generated identifier naming policy.</param>
    /// <param name="options">The TypeScript generation settings.</param>
    /// <exception cref="ArgumentNullException">A required context or collaborator is null.</exception>
    public UndiciClientAstBuilder(IrDocumentContext documentContext,
        INamingConverter namingConverter, TypeScriptGenerationOptions? options)
        : base(documentContext)
    {
        _namingConverter = namingConverter ?? throw new ArgumentNullException(nameof(namingConverter));
        _validation = Validation.ClientValidationStrategyFactory.Create(options);

        var typeBuilder = new TypeScriptAstBuilder(documentContext, namingConverter, options);
        _paramsBuilder = new TypeScriptParamsAstBuilder(documentContext, namingConverter, typeBuilder.BuildType);
        _resultsBuilder = new TypeScriptResultsAstBuilder(documentContext, namingConverter, typeBuilder.BuildType, options?.ResultTypeSuffix, options?.ResultTypePrefix);
    }

    public SourceFileNode BuildOperationClientSourceFile(IrOperation operation, FileOrganization fileOrganization)
    {
        ArgumentNullException.ThrowIfNull(operation);


        var operationFileName = _namingConverter.ToCamelCase(operation.OriginalMethodName);
        var hasParameters = _paramsBuilder.HasParameters(operation);
        var hasResultType = _resultsBuilder.HasResponsesWithBody(operation);
        var declarations = ImmutableList.CreateBuilder<DeclarationNode>();

        declarations.Add(BuildOperationFunction(operation, hasParameters, hasResultType));

        return new SourceFileNode(
            imports: BuildImports(operation, operationFileName, fileOrganization, hasParameters, hasResultType),
            declarations: declarations.ToImmutable(),
            fileDocumentation: TypeScriptDoc.FileHeader(
                $"{operationFileName}.undici.ts",
                $"endpoints/{operationFileName}",
                $"Undici HTTP client for the {operation.MethodName} operation."));
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
        bool hasResultType)
    {
        var imports = ImmutableList.CreateBuilder<ImportNode>();
        imports.Add(new ImportNode(
            items: ImmutableList.Create(new IdentifierNode("request")),
            from: "undici",
            kind: ImportKind.Named));

        // Runtime-импорты библиотеки (z / v / Type / Value) — зависят от генерируемых выражений.
        imports.AddRange(_validation.RuntimeImports(
            ClientInlineConstructedTypes(operation),
            hasParams: operation.PathParameters.Count > 0 || operation.QueryParameters.Count > 0,
            hasNamedSchemaParse: Validation.ClientResponseSchemas.ValidatedResponses(operation).Any()));

        if (hasParameters)
        {
            imports.Add(new ImportNode(
                items: ImmutableList.Create(new IdentifierNode(_paramsBuilder.ParamsInterfaceName(operation.MethodName))),
                from: $"./{operationFileName}.params",
                kind: ImportKind.Named,
                isTypeOnly: true));
        }

        if (hasResultType)
        {
            imports.Add(new ImportNode(
                items: ImmutableList.Create(new IdentifierNode(_resultsBuilder.ResultTypeName(operation.MethodName))),
                from: $"./{operationFileName}.results",
                kind: ImportKind.Named,
                isTypeOnly: true));
        }

        if (_validation.IsEnabled)
        {
            // Схемы параметров (route/query) импортируются из {op}.params.schema.ts.
            AddParamsSchemaImports(imports, operation, operationFileName);

            // Model-схемы тела/голых-ответов (рекурсивно). Члены union/array constructed-ответов
            // импортируются файлом {op}.results.schemas.ts.
            foreach (var modelName in Validation.ClientResponseSchemas.ModelSchemaNames(operation))
            {
                imports.Add(new ImportNode(
                    items: ImmutableList.Create(new IdentifierNode($"{modelName}Schema")),
                    from: BuildSchemaImportPath(modelName, fileOrganization),
                    kind: ImportKind.Named));
            }

            // Вынесенные result-схемы (constructed-ответы) из {op}.results.schemas.
            var resultSchemaNames = Validation.ClientResponseSchemas.ConstructedResultSchemaNames(_namingConverter, operation);
            if (resultSchemaNames.Count > 0)
            {
                imports.Add(new ImportNode(
                    items: resultSchemaNames.Select(n => new IdentifierNode(n)).ToImmutableList(),
                    from: $"./{operationFileName}.results.schemas",
                    kind: ImportKind.Named));
            }
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

    /// <summary>Builds a native operation with compatible options, cancellation, and optional configured client injection.</summary>
    /// <param name="operation">The operation whose callable API is generated.</param>
    /// <param name="hasParameters">Whether the operation accepts a parameter object.</param>
    /// <param name="hasResultType">Whether the operation returns a response union.</param>
    /// <returns>The generated operation declaration.</returns>
    private FunctionDeclarationNode BuildOperationFunction(
        IrOperation operation,
        bool hasParameters,
        bool hasResultType)
    {
        var returnType = hasResultType
            ? Simple(_resultsBuilder.ResultTypeName(operation.MethodName))
            : Simple("void", isPrimitive: true);

        var parameters = ImmutableList.CreateBuilder<ParameterDeclarationNode>();
        if (hasParameters)
        {
            parameters.Add(new ParameterDeclarationNode(
                name: new IdentifierNode("params"),
                type: Simple(_paramsBuilder.ParamsInterfaceName(operation.MethodName)),
                isRequired: true));
        }

        parameters.Add(new ParameterDeclarationNode(
            name: new IdentifierNode("signal"),
            type: Simple("AbortSignal"),
            isRequired: false));

        parameters.Add(TypeScriptHttpClientInjection.Parameter(new TypeQueryTypeNode(Id("request"))));

        return new FunctionDeclarationNode(
            name: new IdentifierNode(operation.MethodName),
            returnType: new GenericTypeNode(new IdentifierNode("Promise"), ImmutableList.Create(returnType)),
            parameters: parameters.ToImmutable(),
            body: BuildOperationFunctionBody(operation, hasResultType),
            isAsync: true,
            documentation: BuildDocumentation(operation),
            isExported: true);
    }

    /// <summary>Builds validation, serialized request data, and response dispatch for one operation.</summary>
    /// <param name="operation">The operation whose request is generated.</param>
    /// <param name="hasResultType">Whether the operation returns a generated result union.</param>
    /// <returns>The operation function body.</returns>
    private BlockNode BuildOperationFunctionBody(IrOperation operation, bool hasResultType)
    {
        var statements = new List<StatementNode>();
        statements.Add(TypeScriptHttpClientInjection.Declaration("request"));
        var sources = TypeScriptParameterSources.Create(operation);

        if (_validation.IsEnabled)
            AddValidationParsingStatements(operation, sources, statements);

        if (operation.RequestBody is { IsForm: true } requestBody && sources.BodySource != null)
        {
            sources.BodySource = TypeScriptFormBody.Build(requestBody, sources.BodySource, ModelsByName, statements);
            statements.Add(new BlankLineStatementNode());
        }

        statements.Add(new VariableDeclarationStatementNode(
            new IdentifierNode("requestUrl"),
            type: null,
            initializer: TypeScriptRequestPath.BuildUrl(operation, sources.RouteSource)));
        statements.Add(new BlankLineStatementNode());

        statements.Add(new VariableDeclarationStatementNode(
            new IdentifierNode("requestOptions"),
            type: null,
            initializer: BuildUndiciOptionsExpression(operation, sources)));
        statements.Add(new BlankLineStatementNode());

        var requestCall = new CallExpressionNode(
            Id("httpClient"),
            ImmutableList.Create<ExpressionNode>(Id("requestUrl"), Id("requestOptions")));

        if (!hasResultType)
        {
            statements.Add(new ExpressionStatementNode(new AwaitExpressionNode(requestCall)));
            return new BlockNode(statements.ToImmutableList());
        }

        statements.Add(new VariableDeclarationStatementNode(
            new IdentifierNode("response"),
            type: null,
            initializer: new AwaitExpressionNode(requestCall)));

        // responseData = response.statusCode === 204 ? undefined : await response.body.json() as unknown
        var bodyJsonCall = new CallExpressionNode(
            Member(Member(Id("response"), "body"), "json"),
            ImmutableList<ExpressionNode>.Empty);
        var bodyJsonCast = new CastExpressionNode(
            Simple("unknown", isPrimitive: true),
            new AwaitExpressionNode(bodyJsonCall),
            useAsOperator: true);

        statements.Add(new VariableDeclarationStatementNode(
            new IdentifierNode("responseData"),
            type: null,
            initializer: TypeScriptResponseBody.Read(operation, Member(Id("response"), "statusCode"), bodyJsonCast)));
        statements.Add(new BlankLineStatementNode());

        AddStatusResultBranches(operation, statements);
        statements.Add(BuildFallbackResult(operation));

        return new BlockNode(statements.ToImmutableList());
    }

    /// <summary>Validates present parameter groups while preserving optional request-body absence.</summary>
    /// <param name="operation">The operation whose request is generated.</param>
    /// <param name="sources">The current raw or validated parameter group expressions.</param>
    /// <param name="statements">The endpoint statements receiving validation or request construction.</param>
    private void AddValidationParsingStatements(
        IrOperation operation,
        TypeScriptParameterSources sources,
        List<StatementNode> statements)
    {
        var opName = _namingConverter.ToPascalCase(operation.MethodName);

        if (operation.PathParameters.Count > 0 && sources.RouteSource != null)
        {
            statements.Add(new VariableDeclarationStatementNode(
                new IdentifierNode("parsedRoute"),
                type: null,
                initializer: _validation.ValidateNamed($"{opName}RouteParamsSchema", sources.RouteSource)));
            sources.RouteSource = Id("parsedRoute");
        }

        if (operation.QueryParameters.Count > 0 && sources.QuerySource != null)
        {
            var queryInput = sources.QuerySourceMayBeUndefined
                ? new BinaryExpressionNode(sources.QuerySource, BinaryOperator.NullishCoalescing, EmptyObject())
                : sources.QuerySource;

            statements.Add(new VariableDeclarationStatementNode(
                new IdentifierNode("parsedQuery"),
                type: null,
                initializer: _validation.ValidateNamed($"{opName}QueryParamsSchema", queryInput)));
            sources.QuerySource = Id("parsedQuery");
            sources.QuerySourceMayBeUndefined = false;
        }

        // Тело запроса валидируется схемой выбранной библиотеки (модель/union/array/...) — inline.
        if (operation.RequestBody != null
            && Validation.ValidationTypes.IsValidatable(operation.RequestBody.Type)
            && sources.BodySource != null)
        {
            statements.Add(new VariableDeclarationStatementNode(
                new IdentifierNode("parsedBody"),
                type: null,
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

    /// <summary>Builds transport options from validated values and original wire names.</summary>
    /// <param name="operation">The operation whose request is generated.</param>
    /// <param name="sources">The current raw or validated parameter group expressions.</param>
    /// <returns>The transport request options.</returns>
    private ObjectLiteralExpressionNode BuildUndiciOptionsExpression(
        IrOperation operation,
        TypeScriptParameterSources sources)
    {
        var properties = new List<PropertyAssignmentNode>
        {
            new(new IdentifierNode("method"), String(operation.HttpMethod.ToUpperInvariant()))
        };

        var wireHeaders = TypeScriptWireParameterObject.BuildHeaders(
            operation, sources.HeadersSource, sources.HeadersSourceMayBeUndefined, includeJsonContentType: true);
        if (wireHeaders.Properties.Count > 0)
            properties.Add(new PropertyAssignmentNode(new IdentifierNode("headers"), wireHeaders));

        if (operation.RequestBody != null && sources.BodySource != null)
        {
            // Undici accepts string | Buffer | Readable | FormData for body.
            // For JSON we serialize via JSON.stringify and set content-type header.
            ExpressionNode bodyExpression;
            if (operation.RequestBody.IsJson)
            {
                bodyExpression = new CallExpressionNode(
                    Member(Id("JSON"), "stringify"),
                    ImmutableList.Create<ExpressionNode>(sources.BodySource));
            }
            else
            {
                bodyExpression = new CastExpressionNode(
                    Simple("string", isPrimitive: true),
                    new CastExpressionNode(Simple("unknown", isPrimitive: true), sources.BodySource, useAsOperator: true),
                    useAsOperator: true);
            }

            if (sources.BodySourceMayBeUndefined)
            {
                properties.Add(SpreadProperty(new ConditionalExpressionNode(
                    new BinaryExpressionNode(sources.BodySource, BinaryOperator.NotEqual, Id("undefined")),
                    Object(("body", bodyExpression)),
                    EmptyObject())));
            }
            else
            {
                properties.Add(new PropertyAssignmentNode(new IdentifierNode("body"), bodyExpression));
            }
        }

        if (operation.QueryParameters.Count > 0 && sources.QuerySource != null)
        {
            properties.Add(new PropertyAssignmentNode(
                new IdentifierNode("query"),
                BuildQueryObject(operation, sources)));
        }

        properties.Add(SpreadProperty(new ConditionalExpressionNode(
            Id("signal"),
            Object(("signal", Id("signal"))),
            EmptyObject())));

        return new ObjectLiteralExpressionNode(properties.ToImmutableList(), preferMultilineFormat: true);
    }

    /// <summary>Builds a canonical wire-keyed query parameter object.</summary>
    /// <param name="operation">The operation whose request is generated.</param>
    /// <param name="sources">The current raw or validated parameter group expressions.</param>
    /// <returns>The query parameter expression.</returns>
    private ExpressionNode BuildQueryObject(IrOperation operation, TypeScriptParameterSources sources)
    {
        var literal = TypeScriptWireParameterObject.BuildFromSource(
            operation.QueryParameters,
            sources.QuerySource!,
            sources.QuerySourceMayBeUndefined,
            omitUndefinedValues: true);

        return new CastExpressionNode(
            Simple("Record<string, string | number | boolean | string[]>", isPrimitive: false),
            new CastExpressionNode(Simple("unknown", isPrimitive: true), literal, useAsOperator: true),
            useAsOperator: true);
    }

    /// <summary>Adds a result branch for every documented numeric status, including responses without content.</summary>
    /// <param name="operation">The operation whose response contract is generated.</param>
    /// <param name="statements">The function statements receiving the response branches.</param>
    private void AddStatusResultBranches(IrOperation operation, List<StatementNode> statements)
    {
        var resultTypeName = _resultsBuilder.ResultTypeName(operation.MethodName);
        var responseStatus = Member(Id("response"), "statusCode");
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
        var responseStatus = Member(Id("response"), "statusCode");
        // Undocumented status → catch-all `…ResultOther`, typed as a literal failure
        // (isSuccessful: false, isFailed: true) so the result union stays discriminable.
        return new ReturnStatementNode(new CastExpressionNode(
            Simple(resultTypeName),
            Object(
                ("status", responseStatus),
                ("body", Id("responseData")),
                ("isSuccessful", Bool(false)),
                ("isFailed", Bool(true))),
            useAsOperator: true));
    }

    /// <summary>
    /// Тело ответа: голая ссылка на модель → <c>{Model}Schema</c>; constructed-тип (union/array/...) →
    /// вынесенная <c>{Op}Result{Status}Schema</c>; иначе/без валидации — сырые данные.
    /// </summary>
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
        // Model schemas are co-located with their models under modelsPath.
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

    private static ObjectLiteralExpressionNode EmptyObject() =>
        new(ImmutableList<PropertyAssignmentNode>.Empty);

    private static CallExpressionNode Call(ExpressionNode target, params ExpressionNode[] arguments) =>
        new(target, arguments.ToImmutableList());

    private static IdentifierExpressionNode Id(string name) =>
        new(new IdentifierNode(name));

    private static MemberAccessExpressionNode Member(ExpressionNode source, string name, bool optional = false) =>
        new(source, new IdentifierNode(name), useOptionalChaining: optional);

    private static SimpleTypeNode Simple(string name, bool isPrimitive = false) =>
        new(new IdentifierNode(name), isPrimitive);

    private static LiteralExpressionNode String(string value) =>
        new(value, LiteralKind.String);

    private static LiteralExpressionNode Number(int value) =>
        new(value, LiteralKind.Number);

    private static LiteralExpressionNode Bool(bool value) =>
        new(value, LiteralKind.Boolean);

}
