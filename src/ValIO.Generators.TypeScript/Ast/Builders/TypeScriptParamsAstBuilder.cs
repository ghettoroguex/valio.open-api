using ValIO.Core.Ast.Builders;
using System.Collections.Immutable;
using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Core.Interfaces;
using ValIO.Core.IR;
using ValIO.Core.IR.Types;
using ValIO.Core.Models;
using ValIO.Generators.TypeScript.Ast.JsDoc;
using ValIO.Generators.TypeScript.Ast.Nodes;

namespace ValIO.Generators.TypeScript.Ast.Builders;

/// <summary>
/// Строитель AST для Params-интерфейсов из IrOperation.
/// Создаёт интерфейсы параметров с под-интерфейсами Route/Query/Body/Form (по аналогии с UE Cpp).
/// При одной группе параметров — плоская структура без вложенности.
/// </summary>
public sealed class TypeScriptParamsAstBuilder : AstBuilderBase
{
    private readonly INamingConverter _namingConverter;
    private readonly Func<IrType, TypeNode> _typeBuilder;


    /// <summary>Creates a TypeScriptParamsAstBuilder for one finalized IR document.</summary>
    /// <param name="documentContext">The shared immutable indexes of the finalized IR document.</param>
    /// <param name="namingConverter">The generated identifier naming policy.</param>
    /// <param name="typeBuilder">The language type mapping function.</param>
    /// <exception cref="ArgumentNullException">A required context or collaborator is null.</exception>
    public TypeScriptParamsAstBuilder(IrDocumentContext documentContext,
        INamingConverter namingConverter,
        Func<IrType, TypeNode> typeBuilder)
        : base(documentContext)
    {
        _namingConverter = namingConverter ?? throw new ArgumentNullException(nameof(namingConverter));
        _typeBuilder = typeBuilder ?? throw new ArgumentNullException(nameof(typeBuilder));
    }

    /// <summary>
    /// Имя params интерфейса: {OperationName}Params
    /// </summary>
    public string ParamsInterfaceName(string operationName)
    {
        return $"{_namingConverter.ToPascalCase(operationName)}Params";
    }

    /// <summary>
    /// Имя Route params интерфейса: {OperationName}RouteParams
    /// </summary>
    public string RouteParamsInterfaceName(string operationName)
    {
        return $"{_namingConverter.ToPascalCase(operationName)}RouteParams";
    }

    /// <summary>
    /// Имя Query params интерфейса: {OperationName}QueryParams
    /// </summary>
    public string QueryParamsInterfaceName(string operationName)
    {
        return $"{_namingConverter.ToPascalCase(operationName)}QueryParams";
    }

    /// <summary>Name of the exported route-params constraints const: <c>{Operation}RouteParamsConstraints</c>.</summary>
    /// <param name="operationName">The operation (method) name.</param>
    /// <returns>The constant name.</returns>
    public string RouteParamsConstraintsName(string operationName) =>
        $"{_namingConverter.ToPascalCase(operationName)}RouteParamsConstraints";

    /// <summary>Name of the exported query-params constraints const: <c>{Operation}QueryParamsConstraints</c>.</summary>
    /// <param name="operationName">The operation (method) name.</param>
    /// <returns>The constant name.</returns>
    public string QueryParamsConstraintsName(string operationName) =>
        $"{_namingConverter.ToPascalCase(operationName)}QueryParamsConstraints";

    /// <summary>
    /// Имя Body params типа (используется существующий тип из RequestBody)
    /// </summary>
    public string BodyParamsTypeName(IrRequestBody body)
    {
        return body.Type switch
        {
            IrModelReference modelRef => modelRef.ModelName,
            _ => "unknown"
        };
    }

    /// <summary>
    /// Строит SourceFileNode с Params-интерфейсами для операции
    /// </summary>
    public SourceFileNode BuildParamsSourceFile(
        IrOperation operation,
        string? importPath,
        FileOrganization? fileOrganization,
        string currentOperationName)
    {


        var declarations = new List<DeclarationNode>();
        var imports = new List<ImportNode>();

        var rb = operation.RequestBody;
        // The flat-vs-aggregate decision and the body/form/route/query/header grouping are owned by
        // TypeScriptParameterModel so the emitted params interface can never drift from how the transport
        // clients and the TanStack query keys reach into it (params.route.id / params.query / …).
        var model = TypeScriptParameterModel.Create(operation);
        var hasRoute = model.HasRoute;
        var hasQuery = model.HasQuery;
        // body — для JSON и бинарных контентов (octet-stream и т.п.).
        // form — отдельно для multipart/form-data и url-encoded (становится FormData/URLSearchParams).
        var hasBody = model.HasBody;
        var hasForm = model.HasForm;
        var hasHeaders = model.HasHeaders;

        var useFlatOnly = model.IsFlat;

        // Под-интерфейсы Route/Query/Headers — только при агрегатной структуре (groupCount > 1)
        if (!useFlatOnly)
        {
            if (hasRoute)
            {
                var routeInterface = BuildRouteParamsInterface(operation);
                declarations.Add(routeInterface);
            }

            if (hasQuery)
            {
                var queryInterface = BuildQueryParamsInterface(operation);
                declarations.Add(queryInterface);
            }

            if (hasHeaders)
            {
                var headersInterface = BuildHeadersParamsInterface(operation);
                declarations.Add(headersInterface);
            }
        }

        // Params: при groupCount <= 1 — плоская модель с properties и комментариями; иначе — агрегат
        var paramsInterface = useFlatOnly
            ? BuildFlatParamsInterface(operation, hasBody, hasForm)
            : BuildAggregateParamsInterface(operation, hasRoute, hasQuery, hasBody, hasForm, hasHeaders);

        declarations.Add(paramsInterface);

        // Exported validation-constraint constants (lengths/ranges/patterns) for route/query params — imported and
        // referenced by {op}.params.schema.ts, mirroring model-property {Model}Constraints. Emitted only when a
        // group actually has constrained parameters.
        var opName = _namingConverter.ToPascalCase(operation.MethodName);
        var routeConstraints = TypeScriptConstraints.BuildConstraintsObject(
            RouteParamsConstraintsName(operation.MethodName),
            operation.PathParameters.Where(p => p.Constraints != null).Select(p => (p.Name, p.Constraints!)),
            TypeScriptDoc.Block($"Validation constraints for {operation.MethodName} route parameters (lengths, ranges, patterns).", ("constant", "")));
        if (routeConstraints != null)
            declarations.Add(routeConstraints);

        var queryConstraints = TypeScriptConstraints.BuildConstraintsObject(
            QueryParamsConstraintsName(operation.MethodName),
            operation.QueryParameters.Where(p => p.Constraints != null).Select(p => (p.Name, p.Constraints!)),
            TypeScriptDoc.Block($"Validation constraints for {operation.MethodName} query parameters (lengths, ranges, patterns).", ("constant", "")));
        if (queryConstraints != null)
            declarations.Add(queryConstraints);

        // Собираем все модели-зависимости: body/form (включая массивы и т.п.) +
        // route/query/header параметры. Дедупим по имени.
        var allDependencies = new HashSet<string>(StringComparer.Ordinal);
        if ((hasBody || hasForm) && rb != null)
            ExtractDependenciesFromIrType(rb.Type, allDependencies);

        foreach (var dep in ExtractParamTypeDependencies(operation))
            allDependencies.Add(dep);

        foreach (var dep in allDependencies.OrderBy(n => n, StringComparer.Ordinal))
        {
            imports.Add(CreateModelImport(dep, fileOrganization));
        }

        var exports = declarations.Select(d => new ExportNode(d, ExportKind.Named)).ToImmutableList();

        var fileBase = _namingConverter.ToCamelCase(operation.OriginalMethodName);
        return new SourceFileNode(
            imports: imports.ToImmutableList(),
            exports: exports,
            declarations: declarations.ToImmutableList(),
            fileDocumentation: TypeScriptDoc.FileHeader(
                $"{fileBase}.params.ts",
                $"endpoints/{fileBase}",
                $"Request parameter types for the {operation.MethodName} operation."));
    }

    /// <summary>Builds the parameter interface with value nullability independent from required presence.</summary>
    /// <param name="operation">The operation whose request is generated.</param>
    /// <returns>The generated parameter interface.</returns>
    private InterfaceDeclarationNode BuildRouteParamsInterface(IrOperation operation)
    {
        var routeName = RouteParamsInterfaceName(operation.MethodName);
        var properties = operation.PathParameters
            .Select(p => new PropertySignatureNode(
                name: new IdentifierNode(p.Name),
                type: _typeBuilder(p.Type),
                isRequired: p.IsRequired,
                documentation: !string.IsNullOrWhiteSpace(p.Description)
                    ? new CommentNode(p.Description, CommentKind.JSDoc)
                    : null) as DeclarationNode)
            .ToImmutableList();

        return new InterfaceDeclarationNode(
            name: new IdentifierNode(routeName),
            accessModifier: AccessModifier.Public,
            baseTypes: ImmutableList<TypeNode>.Empty,
            members: properties,
            documentation: new CommentNode($"Route/path parameters for {operation.MethodName}", CommentKind.JSDoc));
    }

    /// <summary>Builds the parameter interface with value nullability independent from required presence.</summary>
    /// <param name="operation">The operation whose request is generated.</param>
    /// <returns>The generated parameter interface.</returns>
    private InterfaceDeclarationNode BuildQueryParamsInterface(IrOperation operation)
    {
        var queryName = QueryParamsInterfaceName(operation.MethodName);
        var properties = operation.QueryParameters
            .Select(p => new PropertySignatureNode(
                name: new IdentifierNode(p.Name),
                type: _typeBuilder(p.Type),
                isRequired: p.IsRequired,
                documentation: !string.IsNullOrWhiteSpace(p.Description)
                    ? new CommentNode(p.Description, CommentKind.JSDoc)
                    : null) as DeclarationNode)
            .ToImmutableList();

        return new InterfaceDeclarationNode(
            name: new IdentifierNode(queryName),
            accessModifier: AccessModifier.Public,
            baseTypes: ImmutableList<TypeNode>.Empty,
            members: properties,
            documentation: new CommentNode($"Query string parameters for {operation.MethodName}", CommentKind.JSDoc));
    }

    /// <summary>Builds the parameter interface with value nullability independent from required presence.</summary>
    /// <param name="operation">The operation whose request is generated.</param>
    /// <returns>The generated parameter interface.</returns>
    private InterfaceDeclarationNode BuildHeadersParamsInterface(IrOperation operation)
    {
        var headersName = $"{_namingConverter.ToPascalCase(operation.MethodName)}HeaderParams";
        var properties = operation.HeaderParameters
            .Select(p => new PropertySignatureNode(
                name: new IdentifierNode(p.Name),
                type: _typeBuilder(p.Type),
                isRequired: p.IsRequired,
                documentation: !string.IsNullOrWhiteSpace(p.Description)
                    ? new CommentNode(p.Description, CommentKind.JSDoc)
                    : null) as DeclarationNode)
            .ToImmutableList();

        return new InterfaceDeclarationNode(
            name: new IdentifierNode(headersName),
            accessModifier: AccessModifier.Public,
            baseTypes: ImmutableList<TypeNode>.Empty,
            members: properties,
            documentation: new CommentNode($"Header parameters for {operation.MethodName}", CommentKind.JSDoc));
    }

    /// <summary>Builds the parameter interface with value nullability independent from required presence.</summary>
    /// <param name="operation">The operation whose request is generated.</param>
    /// <param name="hasBody">Whether the operation exposes a non-form body.</param>
    /// <param name="hasForm">Whether the operation exposes a form body.</param>
    /// <returns>The generated parameter interface.</returns>
    private InterfaceDeclarationNode BuildFlatParamsInterface(IrOperation operation, bool hasBody, bool hasForm)
    {
        var paramsName = ParamsInterfaceName(operation.MethodName);
        var properties = new List<DeclarationNode>();

        foreach (var param in operation.PathParameters)
        {
            properties.Add(new PropertySignatureNode(
                name: new IdentifierNode(param.Name),
                type: _typeBuilder(param.Type),
                isRequired: param.IsRequired,
                documentation: !string.IsNullOrWhiteSpace(param.Description) ? new CommentNode(param.Description, CommentKind.JSDoc) : null));
        }
        foreach (var param in operation.QueryParameters)
        {
            properties.Add(new PropertySignatureNode(
                name: new IdentifierNode(param.Name),
                type: _typeBuilder(param.Type),
                isRequired: param.IsRequired,
                documentation: !string.IsNullOrWhiteSpace(param.Description) ? new CommentNode(param.Description, CommentKind.JSDoc) : null));
        }
        foreach (var param in operation.HeaderParameters)
        {
            properties.Add(new PropertySignatureNode(
                name: new IdentifierNode(param.Name),
                type: _typeBuilder(param.Type),
                isRequired: param.IsRequired,
                documentation: !string.IsNullOrWhiteSpace(param.Description) ? new CommentNode(param.Description, CommentKind.JSDoc) : null));
        }

        if (hasBody && operation.RequestBody != null)
        {
            var rb = operation.RequestBody;
            properties.Add(new PropertySignatureNode(
                name: new IdentifierNode("body"),
                type: _typeBuilder(rb.Type),
                isRequired: rb.IsRequired,
                documentation: new CommentNode("Request body (JSON)", CommentKind.JSDoc)));
        }

        if (hasForm && operation.RequestBody != null)
        {
            var rb = operation.RequestBody;
            properties.Add(new PropertySignatureNode(
                name: new IdentifierNode("form"),
                type: _typeBuilder(rb.Type),
                isRequired: rb.IsRequired,
                documentation: new CommentNode("Form data (multipart/form-data)", CommentKind.JSDoc)));
        }

        return new InterfaceDeclarationNode(
            name: new IdentifierNode(paramsName),
            accessModifier: AccessModifier.Public,
            baseTypes: ImmutableList<TypeNode>.Empty,
            members: properties.ToImmutableList(),
            documentation: new CommentNode($"Parameters for {operation.MethodName}", CommentKind.JSDoc));
    }

    /// <summary>Builds the parameter interface with value nullability independent from required presence.</summary>
    /// <param name="operation">The operation whose request is generated.</param>
    /// <param name="hasRoute">Whether the operation exposes path parameters.</param>
    /// <param name="hasQuery">Whether the operation exposes query parameters.</param>
    /// <param name="hasBody">Whether the operation exposes a non-form body.</param>
    /// <param name="hasForm">Whether the operation exposes a form body.</param>
    /// <param name="hasHeaders">Whether the operation exposes header parameters.</param>
    /// <returns>The generated parameter interface.</returns>
    private InterfaceDeclarationNode BuildAggregateParamsInterface(
        IrOperation operation,
        bool hasRoute,
        bool hasQuery,
        bool hasBody,
        bool hasForm,
        bool hasHeaders)
    {
        var paramsName = ParamsInterfaceName(operation.MethodName);
        var properties = new List<DeclarationNode>();

        if (hasRoute)
        {
            var routeName = RouteParamsInterfaceName(operation.MethodName);
            properties.Add(new PropertySignatureNode(
                name: new IdentifierNode("route"),
                type: new SimpleTypeNode(new IdentifierNode(routeName), isPrimitive: false, isNullable: false, isOptional: false),
                isRequired: true,
                documentation: new CommentNode("Path/route parameters", CommentKind.JSDoc)));
        }

        if (hasQuery)
        {
            var queryName = QueryParamsInterfaceName(operation.MethodName);
            var allQueryParamsOptional = operation.QueryParameters.All(p => !p.IsRequired);
            properties.Add(new PropertySignatureNode(
                name: new IdentifierNode("query"),
                type: new SimpleTypeNode(new IdentifierNode(queryName), isPrimitive: false, isNullable: false, isOptional: false),
                isRequired: !allQueryParamsOptional,
                documentation: new CommentNode("Query string parameters", CommentKind.JSDoc)));
        }

        if (hasHeaders)
        {
            var headersName = $"{_namingConverter.ToPascalCase(operation.MethodName)}HeaderParams";
            properties.Add(new PropertySignatureNode(
                name: new IdentifierNode("headers"),
                type: new SimpleTypeNode(new IdentifierNode(headersName), isPrimitive: false, isNullable: false, isOptional: false),
                isRequired: operation.HeaderParameters.Any(parameter => parameter.IsRequired),
                documentation: new CommentNode("HTTP header parameters", CommentKind.JSDoc)));
        }

        if (hasBody && operation.RequestBody != null)
        {
            var rb = operation.RequestBody;
            properties.Add(new PropertySignatureNode(
                name: new IdentifierNode("body"),
                type: _typeBuilder(rb.Type),
                isRequired: rb.IsRequired,
                documentation: new CommentNode("Request body (JSON)", CommentKind.JSDoc)));
        }

        if (hasForm && operation.RequestBody != null)
        {
            var rb = operation.RequestBody;
            properties.Add(new PropertySignatureNode(
                name: new IdentifierNode("form"),
                type: _typeBuilder(rb.Type),
                isRequired: rb.IsRequired,
                documentation: new CommentNode("Form data (multipart/form-data)", CommentKind.JSDoc)));
        }

        return new InterfaceDeclarationNode(
            name: new IdentifierNode(paramsName),
            accessModifier: ValIO.Core.Ast.Enums.AccessModifier.Public,
            baseTypes: ImmutableList<TypeNode>.Empty,
            members: properties.ToImmutableList(),
            documentation: new CommentNode($"Parameters for {operation.MethodName}", ValIO.Core.Ast.Enums.CommentKind.JSDoc));
    }

    /// <summary>
    /// Проверяет, имеет ли операция параметры (route, query, body, form, headers)
    /// </summary>
    public bool HasParameters(IrOperation operation)
    {
        return operation.PathParameters.Count > 0
            || operation.QueryParameters.Count > 0
            || operation.HeaderParameters.Count > 0
            || operation.RequestBody != null;
    }

    /// <summary>Собирает имена моделей/enums из типов параметров route/query/headers.</summary>
    private static IReadOnlyList<string> ExtractParamTypeDependencies(IrOperation operation)
    {
        var deps = new HashSet<string>();
        foreach (var p in operation.PathParameters)
            ExtractDependenciesFromIrType(p.Type, deps);
        foreach (var p in operation.QueryParameters)
            ExtractDependenciesFromIrType(p.Type, deps);
        foreach (var p in operation.HeaderParameters)
            ExtractDependenciesFromIrType(p.Type, deps);
        return deps.ToList();
    }

    private static void ExtractDependenciesFromIrType(IrType type, HashSet<string> dependencies)
    {
        switch (type)
        {
            case IrModelReference modelRef:
                dependencies.Add(modelRef.ModelName);
                break;
            case IrArrayType arrayType:
                ExtractDependenciesFromIrType(arrayType.ElementType, dependencies);
                break;
            case IrDictionaryType dictType:
                ExtractDependenciesFromIrType(dictType.KeyType, dependencies);
                ExtractDependenciesFromIrType(dictType.ValueType, dependencies);
                break;
            case IrUnionType unionType:
                foreach (var t in unionType.Types)
                    ExtractDependenciesFromIrType(t, dependencies);
                break;
            case IrIntersectionType intersectionType:
                foreach (var t in intersectionType.Types)
                    ExtractDependenciesFromIrType(t, dependencies);
                break;
            case IrNullableType nullableType:
                ExtractDependenciesFromIrType(nullableType.InnerType, dependencies);
                break;
            case IrOptionalType optionalType:
                ExtractDependenciesFromIrType(optionalType.InnerType, dependencies);
                break;
        }
    }

    private ImportNode CreateModelImport(string typeName, FileOrganization? fileOrganization)
    {
        var modelFileName = _namingConverter.ToCamelCase(OriginalNameMap.GetTypeOriginalName(DocumentContext, typeName));
        var relativePath = $"../../{fileOrganization?.ModelsPath ?? "models"}/{modelFileName}/{modelFileName}";
        return new ImportNode(
            items: new[] { new IdentifierNode(typeName) }.ToImmutableList(),
            from: relativePath,
            kind: ImportKind.Named,
            isTypeOnly: true);
    }
}
