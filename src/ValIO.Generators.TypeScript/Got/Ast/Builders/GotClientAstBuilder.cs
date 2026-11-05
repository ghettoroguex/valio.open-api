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

namespace ValIO.Generators.TypeScript.Got.Ast.Builders;

/// <summary>
/// Builds Got (sindresorhus/got) operation clients as TypeScript AST.
/// <c>got(url, { method, headers, json, searchParams, responseType:"json", throwHttpErrors:false })</c>
/// returns a <c>Response&lt;unknown&gt;</c>; the result is branched on <c>response.statusCode</c>.
/// </summary>
public sealed class GotClientAstBuilder
{
    private readonly INamingConverter _namingConverter;
    private readonly TypeScriptParamsAstBuilder _paramsBuilder;
    private readonly TypeScriptResultsAstBuilder _resultsBuilder;
    private readonly Validation.IClientValidationStrategy _validation;

    /// <summary>Карта «affix-имя → исходное имя» для путей импорта (тождество без affix).</summary>
    private readonly Dictionary<string, string> _orig = new(StringComparer.Ordinal);

    public GotClientAstBuilder(INamingConverter namingConverter, TypeScriptGenerationOptions? options)
    {
        _namingConverter = namingConverter ?? throw new ArgumentNullException(nameof(namingConverter));
        _validation = Validation.ClientValidationStrategyFactory.Create(options);

        var typeBuilder = new TypeScriptAstBuilder(namingConverter, options);
        _paramsBuilder = new TypeScriptParamsAstBuilder(namingConverter, typeBuilder.BuildType);
        _resultsBuilder = new TypeScriptResultsAstBuilder(namingConverter, typeBuilder.BuildType, options?.ResultTypeSuffix, options?.ResultTypePrefix);
    }

    public SourceFileNode BuildOperationClientSourceFile(IrOperation operation, FileOrganization fileOrganization)
    {
        ArgumentNullException.ThrowIfNull(operation);
        OriginalNameMap.AddOperation(_orig, operation);

        var operationFileName = _namingConverter.ToCamelCase(operation.OriginalMethodName);
        var hasParameters = _paramsBuilder.HasParameters(operation);
        var hasResultType = _resultsBuilder.HasResponsesWithBody(operation);
        var declarations = ImmutableList.CreateBuilder<DeclarationNode>();

        declarations.Add(BuildOperationFunction(operation, hasParameters, hasResultType));

        return new SourceFileNode(
            imports: BuildImports(operation, operationFileName, fileOrganization, hasParameters, hasResultType),
            declarations: declarations.ToImmutable(),
            fileDocumentation: TypeScriptDoc.FileHeader(
                $"{operationFileName}.got.ts",
                $"endpoints/{operationFileName}",
                $"Got HTTP client for the {operation.MethodName} operation."));
    }

    private IReadOnlyList<ImportNode> BuildImports(
        IrOperation operation,
        string operationFileName,
        FileOrganization fileOrganization,
        bool hasParameters,
        bool hasResultType)
    {
        var imports = ImmutableList.CreateBuilder<ImportNode>();
        imports.Add(new ImportNode(
            items: ImmutableList.Create(new IdentifierNode("got")),
            from: "got",
            kind: ImportKind.Default));

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

        return new FunctionDeclarationNode(
            name: new IdentifierNode(operation.MethodName),
            returnType: new GenericTypeNode(new IdentifierNode("Promise"), ImmutableList.Create(returnType)),
            parameters: parameters.ToImmutable(),
            body: BuildOperationFunctionBody(operation, hasResultType),
            isAsync: true,
            documentation: BuildDocumentation(operation),
            isExported: true);
    }

    private BlockNode BuildOperationFunctionBody(IrOperation operation, bool hasResultType)
    {
        var statements = new List<StatementNode>();
        var sources = TypeScriptParameterSources.Create(operation);

        if (_validation.IsEnabled)
            AddValidationParsingStatements(operation, sources, statements);

        statements.Add(new VariableDeclarationStatementNode(
            new IdentifierNode("requestUrl"),
            type: null,
            initializer: TypeScriptRequestPath.BuildUrl(operation, sources.RouteSource)));
        statements.Add(new BlankLineStatementNode());

        var gotCall = new CallExpressionNode(
            Id("got"),
            ImmutableList.Create<ExpressionNode>(
                Id("requestUrl"),
                BuildGotOptionsExpression(operation, sources, hasResultType)));

        if (!hasResultType)
        {
            statements.Add(new ExpressionStatementNode(new AwaitExpressionNode(gotCall)));
            return new BlockNode(statements.ToImmutableList());
        }

        statements.Add(new VariableDeclarationStatementNode(
            new IdentifierNode("response"),
            type: null,
            initializer: new AwaitExpressionNode(gotCall)));

        // got returns Response<unknown> when responseType:"json"; body is parsed.
        statements.Add(new VariableDeclarationStatementNode(
            new IdentifierNode("responseData"),
            type: null,
            initializer: new CastExpressionNode(
                Simple("unknown", isPrimitive: true),
                Member(Id("response"), "body"),
                useAsOperator: true)));
        statements.Add(new BlankLineStatementNode());

        AddStatusResultBranches(operation, statements);
        statements.Add(BuildFallbackResult(operation));

        return new BlockNode(statements.ToImmutableList());
    }

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
                initializer: _validation.Validate(operation.RequestBody.Type, sources.BodySource)));
            sources.BodySource = Id("parsedBody");
            sources.BodySourceMayBeUndefined = false;
        }

        if (statements.Count > 0)
            statements.Add(new BlankLineStatementNode());
    }

    private ObjectLiteralExpressionNode BuildGotOptionsExpression(
        IrOperation operation,
        TypeScriptParameterSources sources,
        bool hasResultType)
    {
        var properties = new List<PropertyAssignmentNode>
        {
            new(new IdentifierNode("method"), String(operation.HttpMethod.ToUpperInvariant())),
            new(new IdentifierNode("responseType"), String("json"))
        };

        if (operation.HeaderParameters.Count > 0 && sources.HeadersSource != null)
        {
            var castedHeaders = new CastExpressionNode(
                Simple("Record<string, string>", isPrimitive: false),
                new CastExpressionNode(Simple("unknown", isPrimitive: true), sources.HeadersSource, useAsOperator: true),
                useAsOperator: true);

            if (sources.HeadersSourceMayBeUndefined)
            {
                properties.Add(SpreadProperty(new ConditionalExpressionNode(
                    sources.HeadersSource,
                    Object(("headers", castedHeaders)),
                    EmptyObject())));
            }
            else
            {
                properties.Add(new PropertyAssignmentNode(new IdentifierNode("headers"), castedHeaders));
            }
        }

        if (operation.QueryParameters.Count > 0 && sources.QuerySource != null)
        {
            // got accepts SearchParameters = Record<string, primitive | undefined>; for arrays use URLSearchParams.
            var literal = BuildQueryObject(operation, sources);
            properties.Add(new PropertyAssignmentNode(
                new IdentifierNode("searchParams"),
                new CastExpressionNode(
                    Simple("Record<string, string | number | boolean>", isPrimitive: false),
                    new CastExpressionNode(Simple("unknown", isPrimitive: true), literal, useAsOperator: true),
                    useAsOperator: true)));
        }

        if (operation.RequestBody != null && sources.BodySource != null)
        {
            var bodyOptionName = operation.RequestBody.IsJson ? "json" : "body";
            ExpressionNode bodyExpression = operation.RequestBody.IsJson
                ? sources.BodySource
                : new CastExpressionNode(
                    Simple("string", isPrimitive: true),
                    new CastExpressionNode(Simple("unknown", isPrimitive: true), sources.BodySource, useAsOperator: true),
                    useAsOperator: true);

            if (sources.BodySourceMayBeUndefined)
            {
                properties.Add(SpreadProperty(new ConditionalExpressionNode(
                    new BinaryExpressionNode(sources.BodySource, BinaryOperator.NotEqual, Id("undefined")),
                    Object((bodyOptionName, bodyExpression)),
                    EmptyObject())));
            }
            else
            {
                properties.Add(new PropertyAssignmentNode(new IdentifierNode(bodyOptionName), bodyExpression));
            }
        }

        if (hasResultType)
            properties.Add(new PropertyAssignmentNode(new IdentifierNode("throwHttpErrors"), Bool(false)));

        properties.Add(SpreadProperty(new ConditionalExpressionNode(
            Id("signal"),
            Object(("signal", Id("signal"))),
            EmptyObject())));

        return new ObjectLiteralExpressionNode(properties.ToImmutableList(), preferMultilineFormat: true);
    }

    /// <summary>Builds query parameters using their original wire names.</summary>
    /// <param name="operation">The source operation.</param>
    /// <param name="sources">The generated parameter sources.</param>
    /// <returns>The generated query object.</returns>
    private ObjectLiteralExpressionNode BuildQueryObject(IrOperation operation, TypeScriptParameterSources sources)
    {
        var properties = new List<PropertyAssignmentNode>();
        foreach (var parameter in operation.QueryParameters)
        {
            var access = TypeScriptRequestPath.BuildParameterAccess(sources.QuerySource!, parameter.Name, sources.QuerySourceMayBeUndefined);
            if (parameter.IsRequired)
            {
                properties.Add(new PropertyAssignmentNode(new IdentifierNode(TypeScriptQueryString.WirePropertyName(parameter)), access));
            }
            else
            {
                properties.Add(SpreadProperty(new ConditionalExpressionNode(
                    new BinaryExpressionNode(access, BinaryOperator.NotEqual, Id("undefined")),
                    Object((TypeScriptQueryString.WirePropertyName(parameter), access)),
                    EmptyObject())));
            }
        }

        return new ObjectLiteralExpressionNode(properties.ToImmutableList(), preferMultilineFormat: true);
    }

    private void AddStatusResultBranches(IrOperation operation, List<StatementNode> statements)
    {
        var resultTypeName = _resultsBuilder.ResultTypeName(operation.MethodName);
        var responseStatus = Member(Id("response"), "statusCode");
        var responseData = Id("responseData");

        foreach (var response in operation.Responses.Where(r => r.Type != null && int.TryParse(r.StatusCode, out _)))
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

    /// <summary>
    /// Тело ответа: голая ссылка на модель → <c>{Model}Schema</c>; constructed-тип (union/array/...) →
    /// вынесенная <c>{Op}Result{Status}Schema</c>; иначе/без валидации — сырые данные.
    /// </summary>
    private ExpressionNode BuildResponseBody(IrOperation operation, IrResponse response, ExpressionNode responseData)
    {
        if (!_validation.IsEnabled || !Validation.ValidationTypes.IsValidatable(response.Type!))
            return responseData;
        if (response.Type is IrModelReference modelRef)
            return _validation.ValidateNamed($"{modelRef.ModelName}Schema", responseData);
        return _validation.ValidateNamed(
            Validation.ClientResponseSchemas.ResultSchemaName(_namingConverter, operation.MethodName, response.StatusCode),
            responseData);
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

    private string BuildSchemaImportPath(string modelName, FileOrganization fileOrganization)
    {
        // Model schemas are co-located with their models under modelsPath.
        var modelsPath = fileOrganization.ModelsPath ?? "models";
        var modelFileName = _namingConverter.ToCamelCase(_orig.GetValueOrDefault(modelName, modelName));
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
