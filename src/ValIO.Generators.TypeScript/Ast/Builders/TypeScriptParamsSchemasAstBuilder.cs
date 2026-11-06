using ValIO.Core.Ast.Builders;
using System.Collections.Immutable;
using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Interfaces;
using ValIO.Core.IR;
using ValIO.Core.IR.Types;
using ValIO.Core.Models;
using ValIO.Generators.TypeScript.Ast.JsDoc;
using ValIO.Generators.TypeScript.Ast.Nodes;
using ValIO.Generators.TypeScript.Options;
using ValIO.Generators.TypeScript.Validation;

namespace ValIO.Generators.TypeScript.Ast.Builders;

/// <summary>
/// Строит <c>{operation}.params.schema.ts</c> — именованные схемы валидации route/query параметров,
/// которые иначе пришлось бы строить inline в каждом клиенте. Каждая объявляется один раз как
/// <c>export const {Op}RouteParamsSchema = ...;</c> / <c>{Op}QueryParamsSchema = ...;</c> и импортируется
/// всеми транспортами. Библиотека (zod/valibot/typebox) выбирается стратегией — файл от неё не зависит.
/// </summary>
public sealed class TypeScriptParamsSchemasAstBuilder : AstBuilderBase
{
    private readonly INamingConverter _namingConverter;
    private readonly IClientValidationStrategy _validation;
    private readonly string _uuidFormat;


    /// <summary>Creates a TypeScriptParamsSchemasAstBuilder for one finalized IR document.</summary>
    /// <param name="documentContext">The shared immutable indexes of the finalized IR document.</param>
    /// <param name="namingConverter">The generated identifier naming policy.</param>
    /// <param name="validation">The configured runtime validation strategy.</param>
    /// <param name="uuidFormat">The configured UUID format policy.</param>
    /// <exception cref="ArgumentNullException">A required context or collaborator is null.</exception>
    internal TypeScriptParamsSchemasAstBuilder(IrDocumentContext documentContext,
        INamingConverter namingConverter, IClientValidationStrategy validation, string? uuidFormat = null)
        : base(documentContext)
    {
        _namingConverter = namingConverter ?? throw new ArgumentNullException(nameof(namingConverter));
        _validation = validation ?? throw new ArgumentNullException(nameof(validation));
        _uuidFormat = TypeScriptOptionIds.UuidFormat.Resolve(uuidFormat);
    }

    /// <summary>Имя константы схемы route-параметров: <c>{Op}RouteParamsSchema</c>.</summary>
    public string RouteParamsSchemaName(string operationName)
        => $"{_namingConverter.ToPascalCase(operationName)}RouteParamsSchema";

    /// <summary>Имя константы схемы query-параметров: <c>{Op}QueryParamsSchema</c>.</summary>
    public string QueryParamsSchemaName(string operationName)
        => $"{_namingConverter.ToPascalCase(operationName)}QueryParamsSchema";

    /// <summary>
    /// Строит файл со схемами параметров. Возвращает <c>null</c>, если валидация выключена или у
    /// операции нет route/query параметров.
    /// </summary>
    public SourceFileNode? BuildParamsSchemasSourceFile(IrOperation operation, FileOrganization? fileOrganization)
    {
        if (!_validation.IsEnabled)
            return null;

        var hasRoute = operation.PathParameters.Count > 0;
        var hasQuery = operation.QueryParameters.Count > 0;
        if (!hasRoute && !hasQuery)
            return null;


        // Each param-schema const is annotated with the corresponding params interface (e.g.
        // `: ZodSchema<{Op}QueryParams>`) so it reads like a model schema. The Route/Query sub-interfaces
        // are only emitted when the params model is split into more than one group (route/query/body/
        // form/headers); with a single group only the flat {Op}Params interface exists. Re-derive that
        // group decision (mirrors TypeScriptParamsAstBuilder) so we never annotate against a missing type.
        var rb = operation.RequestBody;
        var groupCount = (hasRoute ? 1 : 0) + (hasQuery ? 1 : 0)
            + (rb != null && !rb.IsForm ? 1 : 0) + (rb != null && rb.IsForm ? 1 : 0)
            + (operation.HeaderParameters.Count > 0 ? 1 : 0);
        var useFlatOnly = groupCount <= 1;
        var opPascal = _namingConverter.ToPascalCase(operation.MethodName);

        var annotatedInterfaces = new SortedSet<string>(StringComparer.Ordinal);

        // Resolves the annotation for a schema validating interface `interfaceName`; records the
        // interface for a type-only import only when the validator actually annotates the const.
        TypeNode? Annotate(string interfaceName)
        {
            var annotation = _validation.SchemaTypeAnnotation(interfaceName);
            if (annotation != null)
                annotatedInterfaces.Add(interfaceName);
            return annotation;
        }

        // Records constraint-constant names to import from ./{op}.params, and returns the ref prefix to pass to
        // the validator — only when this validator references constants (Zod) and the group has constrained params.
        var constraintConstNames = new List<string>();
        string? ConstraintsPrefix(IReadOnlyList<IrParameter> parameters, string constName)
        {
            if (!_validation.UsesConstraintConstants
                || !parameters.Any(p => p.Constraints != null && TypeScriptConstraints.HasAnyConstraint(p.Constraints)))
                return null;
            constraintConstNames.Add(constName);
            return constName;
        }

        var declarations = new List<DeclarationNode>();
        if (hasRoute)
        {
            var routeInterface = useFlatOnly ? $"{opPascal}Params" : $"{opPascal}RouteParams";
            declarations.Add(BuildParamsSchemaDeclaration(
                RouteParamsSchemaName(operation.MethodName), operation.PathParameters,
                "Route (path) parameter validation schema.", Annotate(routeInterface),
                ConstraintsPrefix(operation.PathParameters, $"{opPascal}RouteParamsConstraints")));
        }
        if (hasQuery)
        {
            var queryInterface = useFlatOnly ? $"{opPascal}Params" : $"{opPascal}QueryParams";
            declarations.Add(BuildParamsSchemaDeclaration(
                QueryParamsSchemaName(operation.MethodName), operation.QueryParameters,
                "Query parameter validation schema.", Annotate(queryInterface),
                ConstraintsPrefix(operation.QueryParameters, $"{opPascal}QueryParamsConstraints")));
        }

        var paramTypes = operation.PathParameters.Concat(operation.QueryParameters).Select(p => p.Type).ToList();
        var fileBase = _namingConverter.ToCamelCase(operation.OriginalMethodName);
        var imports = BuildImports(operation, paramTypes, fileOrganization, annotatedInterfaces, fileBase, constraintConstNames);
        return new SourceFileNode(
            imports: imports,
            declarations: declarations,
            fileDocumentation: TypeScriptDoc.FileHeader(
                $"{fileBase}.params.schema.ts",
                $"{fileBase}.params.schema",
                $"Route/query parameter validation schemas for {operation.MethodName}."));
    }

    /// <summary>Builds an exported <c>export const {schemaName}[: annotation] = {schema};</c> declaration.</summary>
    /// <param name="schemaName">The schema constant name (e.g. <c>{Op}QueryParamsSchema</c>).</param>
    /// <param name="parameters">The parameters whose object schema is the initializer.</param>
    /// <param name="summary">The JSDoc summary for the declaration.</param>
    /// <param name="annotation">The optional typed annotation (e.g. <c>ZodSchema&lt;{Op}QueryParams&gt;</c>), or <see langword="null"/>.</param>
    /// <returns>The variable declaration node.</returns>
    private DeclarationNode BuildParamsSchemaDeclaration(string schemaName, IReadOnlyList<IrParameter> parameters, string summary, TypeNode? annotation, string? constraintsRefPrefix = null)
        => new VariableDeclarationNode(
            name: new IdentifierNode(schemaName),
            declarationKind: VariableDeclarationKind.Const,
            type: annotation,
            initializer: _validation.BuildParamsSchema(parameters, constraintsRefPrefix),
            documentation: new CommentNode(summary, CommentKind.JSDoc),
            isExported: true);

    /// <summary>Builds the imports for the params-schema file: schema runtime, the annotated params
    /// interface(s) (type-only, sibling <c>./{op}.params</c>), the shared UUID constant and any model schemas.</summary>
    private IReadOnlyList<ImportNode> BuildImports(
        IrOperation operation,
        IReadOnlyList<IrType> paramTypes,
        FileOrganization? fileOrganization,
        IReadOnlyCollection<string> annotatedInterfaces,
        string fileBase,
        IReadOnlyCollection<string> constraintConstNames)
    {
        var imports = new List<ImportNode>();
        imports.AddRange(_validation.SchemaDefinitionImports(paramTypes));

        // Runtime import of the constraint constants object(s) referenced by the schema (Zod: e.g.
        // `.min({Op}QueryParamsConstraints.limit.min)`). Value import (not type-only), sibling ./{op}.params.
        if (constraintConstNames.Count > 0)
        {
            imports.Add(new ImportNode(
                items: constraintConstNames.Select(n => new IdentifierNode(n)).ToImmutableList(),
                from: $"./{fileBase}.params",
                kind: ImportKind.Named));
        }

        // Type-only import of the params interface(s) the schema consts are annotated against.
        // The params interface file is a sibling of this schema file: ./{op}.params.ts.
        if (annotatedInterfaces.Count > 0)
        {
            imports.Add(new ImportNode(
                items: annotatedInterfaces.Select(n => new IdentifierNode(n)).ToImmutableList(),
                from: $"./{fileBase}.params",
                kind: ImportKind.Named,
                isTypeOnly: true));
        }

        // Model schemas and the shared formats.ts are co-located under modelsPath; this params-schema
        // file sits at {clientsPath}/{op}/ → two levels up to the output root, then into modelsPath.
        var modelsPath = fileOrganization?.ModelsPath ?? "models";
        var separateModels = fileOrganization?.SeparateModels ?? true;

        // Shared UUID constant (uuidFormat = regex): a parameter schema rendered as `.regex(UUID_REGEX)` /
        // `pattern: UUID_PATTERN` must import the constant from the shared {modelsPath}/formats.ts file.
        if (_validation.UuidConstName != null
            && _uuidFormat == TypeScriptOptionIds.UuidFormat.Regex
            && UuidFormatSupport.OperationParamsUseUuid(operation))
        {
            imports.Add(new ImportNode(
                items: ImmutableList.Create(new IdentifierNode(_validation.UuidConstName)),
                from: $"../../{modelsPath}/{TypeScriptOptionIds.UuidFormat.ConstFileName}",
                kind: ImportKind.Named));
        }
        var modelNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in paramTypes)
            ValidationTypes.CollectModelSchemaNames(type, modelNames);

        foreach (var modelName in modelNames.OrderBy(n => n, StringComparer.Ordinal))
        {
            var modelFile = _namingConverter.ToCamelCase(OriginalNameMap.GetTypeOriginalName(DocumentContext, modelName));
            var from = separateModels
                ? $"../../{modelsPath}/{modelFile}/{modelFile}.schema"
                : $"../../{modelsPath}/{modelFile}.schema";
            imports.Add(new ImportNode(
                items: ImmutableList.Create(new IdentifierNode($"{modelName}Schema")),
                from: from,
                kind: ImportKind.Named));
        }

        return imports;
    }
}
