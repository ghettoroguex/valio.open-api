using ValIO.Core.Ast.Builders;
using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Expressions;
using ValIO.Core.IR;
using ValIO.Core.IR.Types;
using ValIO.Generators.Cpp.Ast.Nodes;
using ValIO.Generators.Cpp.Ast.Nodes.UE;
using ValIO.Generators.Cpp.Ast.Nodes.UE.Specifiers;
using ValIO.Generators.Cpp.Models;
using ValIO.Generators.Cpp.Services;

namespace ValIO.Generators.Cpp.Ast.Builders;

/// <summary>Builds Unreal parameter headers with shared document declaration indexes.</summary>
public sealed class UEParamsAstBuilder : AstBuilderBase
{
    /// <summary>Maps types using the shared document context.</summary>
    private readonly UETypeMapper _typeMapper;
    /// <summary>Supplies Unreal identifier and path naming.</summary>
    private readonly UENamingConvention _naming;
    /// <summary>Supplies the selected Unreal generation options.</summary>
    private readonly CppGenerationOptions _options;

    /// <summary>Requires one document context shared with the type mapper.</summary>
    /// <param name="typeMapper">The document-aware Unreal type mapper.</param>
    /// <param name="naming">The Unreal identifier and file naming rules.</param>
    /// <param name="options">The Unreal generation options.</param>
    /// <param name="documentContext">The mandatory finalized document indexes.</param>
    /// <exception cref="ArgumentNullException">A dependency is null.</exception>
    /// <exception cref="ArgumentException">The mapper belongs to a different document context.</exception>
    public UEParamsAstBuilder(UETypeMapper typeMapper, UENamingConvention naming, CppGenerationOptions options, IrDocumentContext documentContext) : base(documentContext)
    {
        _typeMapper = typeMapper ?? throw new ArgumentNullException(nameof(typeMapper));
        if (!ReferenceEquals(typeMapper.DocumentContext, documentContext))
            throw new ArgumentException("The mapper and builder must share a document context.", nameof(typeMapper));
        _naming = naming ?? throw new ArgumentNullException(nameof(naming));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>Builds the operation header using this document's enum references.</summary>
    /// <param name="operation">The operation contract to emit.</param>
    /// <param name="clientName">The owning client's generated name.</param>
    /// <returns>The generated Unreal header AST.</returns>
    public CppHeaderFileNode Build(IrOperation operation, string clientName)
    {
        var enumNames = EnumNames;
        var paramsName = _naming.ParamsStructName(operation.MethodName);
        // File name uses the ORIGINAL (pre-affix) method name so the emitted file/include doesn't change with affixes.
        var fileName = _naming.ParamsFileName(operation.OriginalMethodName);
        var category = _naming.OperationTypeCategory(_options.BlueprintCategory, clientName, operation.MethodName);

        var declarations = new List<AstNode>();
        var aggregateFields = new List<UPropertyFieldNode>();

        var hasRoute = operation.PathParameters.Count > 0;
        var hasQuery = operation.QueryParameters.Count > 0;
        var rb = operation.RequestBody;
        var hasBody = rb != null && rb.IsJson;
        var hasForm = rb != null && rb.IsForm;
        var hasHeaders = operation.HeaderParameters.Count > 0;

        var groupCount = (hasRoute ? 1 : 0) + (hasQuery ? 1 : 0) + (hasBody ? 1 : 0) + (hasForm ? 1 : 0) + (hasHeaders ? 1 : 0);

        var methodNamePascal = UENamingConvention.ToPascalCase(operation.MethodName);

        // Если одна группа или менее — упрощённая плоская структура
        if (groupCount <= 1)
        {
            var flatFields = BuildFlatFields(operation, category);
            var flatDoc = new CommentNode(DoxygenCommentBuilder.BuildParamsStructDocumentation(operation, paramsName, methodNamePascal, hasBody: hasBody || hasForm), CommentKind.MultiLine);
            var flatStruct = new UStructDeclarationNode(
                name: new IdentifierNode(paramsName),
                specifiers: new UStructSpecifiers { BlueprintType = true, Category = category },
                fields: flatFields,
                documentation: flatDoc);

            declarations.Add(flatStruct);
        }
        else
        {
            // Route под-структура
            if (hasRoute)
            {
                var routeName = _naming.RouteParamsStructName(operation.MethodName);
                var routeFields = BuildParameterFields(operation.PathParameters, category);
                var routeDoc = new CommentNode(DoxygenCommentBuilder.BuildRouteParamsDocumentation(operation, methodNamePascal), CommentKind.MultiLine);
                var routeStruct = new UStructDeclarationNode(
                    name: new IdentifierNode(routeName),
                    specifiers: new UStructSpecifiers { BlueprintType = true, Category = category },
                    fields: routeFields,
                    documentation: routeDoc);
                declarations.Add(routeStruct);

                aggregateFields.Add(new UPropertyFieldNode(
                    name: new IdentifierNode("Route"),
                    type: new Core.Ast.Nodes.Types.SimpleTypeNode(new IdentifierNode(routeName)),
                    specifiers: UPropertySpecifiers.ParamField(category),
                    documentation: new CommentNode("@brief Path/route parameters.\n@details Values are substituted into the URL path (e.g. /api/projects/{projectId}).\n@see Params.Route", CommentKind.MultiLine)));
            }

            // Query под-структура
            if (hasQuery)
            {
                var queryName = _naming.QueryParamsStructName(operation.MethodName);
                var queryFields = BuildParameterFields(operation.QueryParameters, category);
                var queryDoc = new CommentNode(DoxygenCommentBuilder.BuildQueryParamsDocumentation(operation, methodNamePascal), CommentKind.MultiLine);
                var queryStruct = new UStructDeclarationNode(
                    name: new IdentifierNode(queryName),
                    specifiers: new UStructSpecifiers { BlueprintType = true, Category = category },
                    fields: queryFields,
                    documentation: queryDoc);
                declarations.Add(queryStruct);

                aggregateFields.Add(new UPropertyFieldNode(
                    name: new IdentifierNode("Query"),
                    type: new Core.Ast.Nodes.Types.SimpleTypeNode(new IdentifierNode(queryName)),
                    specifiers: UPropertySpecifiers.ParamField(category),
                    documentation: new CommentNode("@brief Query string parameters.\n@details Appended to the URL as ?key=value pairs.\n@see Params.Query", CommentKind.MultiLine)));
            }

            // Headers под-структура
            if (hasHeaders)
            {
                var headerName = $"F{methodNamePascal}HeaderParams";
                var headerFields = BuildParameterFields(operation.HeaderParameters, category);
                var headerDoc = new CommentNode($"@brief HTTP header parameters for {methodNamePascal}.\n@see Params.Headers", CommentKind.MultiLine);
                var headerStruct = new UStructDeclarationNode(
                    name: new IdentifierNode(headerName),
                    specifiers: new UStructSpecifiers { BlueprintType = true, Category = category },
                    fields: headerFields,
                    documentation: headerDoc);
                declarations.Add(headerStruct);

                aggregateFields.Add(new UPropertyFieldNode(
                    name: new IdentifierNode("Headers"),
                    type: new Core.Ast.Nodes.Types.SimpleTypeNode(new IdentifierNode(headerName)),
                    specifiers: UPropertySpecifiers.ParamField(category),
                    documentation: new CommentNode("@brief HTTP header parameters.\n@see Params.Headers", CommentKind.MultiLine)));
            }

            // Body (JSON) — напрямую в Params, без обёртки BodyParams
            if (hasBody && operation.RequestBody != null)
            {
                var bodyType = _typeMapper.MapType(operation.RequestBody.Type);
                var bodyDoc = new CommentNode(
                    string.IsNullOrWhiteSpace(operation.RequestBody.Description)
                        ? "@brief Request body (JSON).\n@details Sent in the HTTP request body."
                        : $"@brief Request body (JSON).\n@details Sent in the HTTP request body.\n\n{operation.RequestBody.Description}",
                    CommentKind.MultiLine);
                aggregateFields.Add(new UPropertyFieldNode(
                    name: new IdentifierNode("Body"),
                    type: bodyType,
                    specifiers: UPropertySpecifiers.ParamField(category),
                    documentation: bodyDoc));
            }

            // Form (multipart/form-data) — напрямую в Params, без обёртки FormParams
            if (hasForm && operation.RequestBody != null)
            {
                var formType = _typeMapper.MapType(operation.RequestBody.Type);
                var formDoc = new CommentNode(
                    string.IsNullOrWhiteSpace(operation.RequestBody.Description)
                        ? "@brief Form data (multipart/form-data).\n@details For file uploads and form submissions."
                        : $"@brief Form data (multipart/form-data).\n@details For file uploads and form submissions.\n\n{operation.RequestBody.Description}",
                    CommentKind.MultiLine);
                aggregateFields.Add(new UPropertyFieldNode(
                    name: new IdentifierNode("Form"),
                    type: formType,
                    specifiers: UPropertySpecifiers.ParamField(category),
                    documentation: formDoc));
            }

            // Агрегирующая структура
            var aggregateDoc = new CommentNode(DoxygenCommentBuilder.BuildParamsStructDocumentation(operation, paramsName, methodNamePascal, hasBody: hasBody || hasForm), CommentKind.MultiLine);
            var aggregateStruct = new UStructDeclarationNode(
                name: new IdentifierNode(paramsName),
                specifiers: new UStructSpecifiers { BlueprintType = true, Category = category },
                fields: aggregateFields,
                documentation: aggregateDoc);
            declarations.Add(aggregateStruct);
        }

        var includes = new List<CppIncludeDirectiveNode>();
        if (_options.AddUnrealEngineIncludesInHeaders)
            includes.Add(new("CoreMinimal.h"));

        var referencedModels = new HashSet<string>();
        var referencedEnums = new HashSet<string>();
        // Map affixed reference name → ORIGINAL name so #include paths don't change with affixes.
        var orig = new Dictionary<string, string>(StringComparer.Ordinal);
        if (operation.RequestBody?.Type != null)
        {
            CollectTypeReferences(operation.RequestBody.Type, referencedModels, referencedEnums, enumNames);
            CollectOriginalNames(operation.RequestBody.Type, orig);
        }
        foreach (var p in operation.PathParameters)
        {
            CollectTypeReferences(p.Type, referencedModels, referencedEnums, enumNames);
            CollectOriginalNames(p.Type, orig);
        }
        foreach (var p in operation.QueryParameters)
        {
            CollectTypeReferences(p.Type, referencedModels, referencedEnums, enumNames);
            CollectOriginalNames(p.Type, orig);
        }
        foreach (var p in operation.HeaderParameters)
        {
            CollectTypeReferences(p.Type, referencedModels, referencedEnums, enumNames);
            CollectOriginalNames(p.Type, orig);
        }

        // Params в Public/Endpoints/{OpId}/, Models/Enums в Public/Models, Public/Enums
        foreach (var modelName in referencedModels.OrderBy(x => x))
            includes.Add(new CppIncludeDirectiveNode(_naming.ModelIncludePathFromEndpoint(orig.GetValueOrDefault(modelName, modelName))));
        foreach (var enumName in referencedEnums.OrderBy(x => x))
            includes.Add(new CppIncludeDirectiveNode(_naming.EnumIncludePathFromEndpoint(orig.GetValueOrDefault(enumName, enumName))));

        // .generated.h должен быть всегда последним (требование UHT)
        includes.Add(new CppIncludeDirectiveNode(fileName.Replace(".h", _options.GeneratedHeaderSuffix)));

        return new CppHeaderFileNode(
            fileName: fileName,
            headerComment: _options.Copyright != null ? new CommentNode(_options.Copyright, CommentKind.MultiLine) : null,
            includes: includes,
            declarations: declarations);
    }

    /// <summary>
    /// Строит плоский список полей для упрощённой структуры (одна группа параметров)
    /// </summary>
    private List<UPropertyFieldNode> BuildFlatFields(IrOperation operation, string category)
    {
        var fields = new List<UPropertyFieldNode>();

        foreach (var param in operation.PathParameters)
            fields.Add(BuildParamField(param, category));
        foreach (var param in operation.QueryParameters)
            fields.Add(BuildParamField(param, category));
        foreach (var param in operation.HeaderParameters)
            fields.Add(BuildParamField(param, category));

        if (operation.RequestBody != null)
        {
            var bodyDoc = new CommentNode(
                string.IsNullOrWhiteSpace(operation.RequestBody.Description)
                    ? "@brief Request body (JSON).\n@details Sent in the HTTP request body."
                    : $"@brief Request body (JSON).\n@details Sent in the HTTP request body.\n\n{operation.RequestBody.Description}",
                CommentKind.MultiLine);
            fields.Add(new UPropertyFieldNode(
                name: new IdentifierNode("Body"),
                type: _typeMapper.MapType(operation.RequestBody.Type),
                specifiers: UPropertySpecifiers.ParamField(category),
                documentation: bodyDoc));
        }

        return fields;
    }

    /// <summary>
    /// Строит список полей из списка IrParameter
    /// </summary>
    private List<UPropertyFieldNode> BuildParameterFields(IReadOnlyList<IrParameter> parameters, string category)
    {
        return parameters
            .Select(p => BuildParamField(p, category))
            .ToList();
    }

    /// <summary>
    /// Строит одно поле из IrParameter
    /// </summary>
    private UPropertyFieldNode BuildParamField(IrParameter param, string category)
    {
        var type = _typeMapper.MapType(param.Type);
        var defaultValueStr = _typeMapper.GetDefaultValue(param.Type);
        ExpressionNode? defaultValue = defaultValueStr != null
            ? new IdentifierExpressionNode(new IdentifierNode(defaultValueStr))
            : null;

        return new UPropertyFieldNode(
            name: new IdentifierNode(_naming.PropertyName(param.Name)),
            type: type,
            specifiers: UPropertySpecifiers.ParamField(category),
            defaultValue: defaultValue,
            documentation: !string.IsNullOrWhiteSpace(param.Description)
                ? new CommentNode($"@brief {param.Description}", CommentKind.MultiLine) : null);
    }

    /// <summary>
    /// Рекурсивно собирает сопоставления «affix-имя ссылки → исходное имя» из IrType.
    /// Используется для путей #include, которые не должны меняться при affix.
    /// </summary>
    private static void CollectOriginalNames(IrType type, Dictionary<string, string> orig)
    {
        switch (type)
        {
            case IrModelReference modelRef:
                orig[modelRef.ModelName] = modelRef.OriginalModelName;
                break;
            case IrArrayType array:
                CollectOriginalNames(array.ElementType, orig);
                break;
            case IrDictionaryType dict:
                CollectOriginalNames(dict.KeyType, orig);
                CollectOriginalNames(dict.ValueType, orig);
                break;
            case IrNullableType nullable:
                CollectOriginalNames(nullable.InnerType, orig);
                break;
            case IrOptionalType optional:
                CollectOriginalNames(optional.InnerType, orig);
                break;
            case IrUnionType union:
                foreach (var t in union.Types) CollectOriginalNames(t, orig);
                break;
            case IrIntersectionType intersection:
                foreach (var t in intersection.Types) CollectOriginalNames(t, orig);
                break;
        }
    }

    /// <summary>Uses the shared C++ reference traversal for nested type wrappers.</summary>
    /// <param name="type">The type whose declaration references are collected.</param>
    /// <param name="models">The destination for model reference names.</param>
    /// <param name="enums">The destination for enum reference names.</param>
    /// <param name="enumNames">The shared document enum names used for classification.</param>
    private static void CollectTypeReferences(IrType type, HashSet<string> models, HashSet<string> enums, IReadOnlySet<string> enumNames)
        => CppTypeReferenceCollector.Collect(type, models, enums, enumNames);
}
