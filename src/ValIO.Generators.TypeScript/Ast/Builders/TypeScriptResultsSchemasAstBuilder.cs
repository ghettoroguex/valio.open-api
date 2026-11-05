using System.Collections.Immutable;
using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Interfaces;
using ValIO.Core.IR;
using ValIO.Core.Models;
using ValIO.Generators.TypeScript.Ast.JsDoc;
using ValIO.Generators.TypeScript.Ast.Nodes;
using ValIO.Generators.TypeScript.Validation;
using ValIO.Generators.TypeScript.Options;

namespace ValIO.Generators.TypeScript.Ast.Builders;

/// <summary>
/// Строит <c>{operation}.results.schemas.ts</c> — именованные схемы валидации для тел ответов,
/// которые иначе пришлось бы строить inline в клиенте на каждый вызов (union/array и т.п.).
/// Каждая constructed-схема объявляется один раз как <c>export const {Op}Result{Status}Schema = ...;</c>.
/// </summary>
public sealed class TypeScriptResultsSchemasAstBuilder
{
    private readonly INamingConverter _namingConverter;
    private readonly IClientValidationStrategy _validation;
    /// <summary>Whether generated schemas use the shared UUID regular expression.</summary>
    private readonly bool _usesUuidRegex;

    /// <summary>Карта «affix-имя → исходное имя» для путей импорта (тождество без affix).</summary>
    private readonly Dictionary<string, string> _orig = new(StringComparer.Ordinal);

    /// <summary>Creates a builder for named response schemas.</summary>
    /// <param name="namingConverter">The naming converter for generated symbols.</param>
    /// <param name="validation">The configured validation strategy.</param>
    /// <param name="uuidFormat">The configured UUID validation mode.</param>
    /// <exception cref="ArgumentNullException">A required dependency is null.</exception>
    internal TypeScriptResultsSchemasAstBuilder(INamingConverter namingConverter, IClientValidationStrategy validation, string? uuidFormat)
    {
        _namingConverter = namingConverter ?? throw new ArgumentNullException(nameof(namingConverter));
        _validation = validation ?? throw new ArgumentNullException(nameof(validation));
        _usesUuidRegex = TypeScriptOptionIds.UuidFormat.Resolve(uuidFormat) == TypeScriptOptionIds.UuidFormat.Regex;
    }

    /// <summary>
    /// Имя константы схемы для status-варианта результата: <c>{Op}Result{Status}Schema</c>
    /// (совпадает с интерфейсом результата + "Schema").
    /// </summary>
    public string ResultSchemaName(string operationName, string statusCode)
        => TypeScriptResultsAstBuilder.ResultVariantName(_namingConverter, operationName, statusCode) + "Schema";

    /// <summary>
    /// Строит файл со схемами result-вариантов. Возвращает <c>null</c>, если ни один ответ не
    /// требует вынесенной (constructed) схемы.
    /// </summary>
    public SourceFileNode? BuildResultsSchemasSourceFile(IrOperation operation, FileOrganization? fileOrganization)
    {
        if (!_validation.IsEnabled)
            return null;

        OriginalNameMap.AddOperation(_orig, operation);

        var constructed = operation.Responses
            .Where(r => r.Type != null
                && int.TryParse(r.StatusCode, out _)
                && ValidationTypes.NeedsConstructedSchema(r.Type!))
            .ToList();

        if (constructed.Count == 0)
            return null;

        var declarations = constructed
            .Select(response => (DeclarationNode)new VariableDeclarationNode(
                name: new IdentifierNode(ResultSchemaName(operation.MethodName, response.StatusCode)),
                declarationKind: VariableDeclarationKind.Const,
                initializer: _validation.BuildSchema(response.Type!),
                documentation: new CommentNode($"Response body schema for HTTP {response.StatusCode}.", CommentKind.JSDoc),
                isExported: true))
            .ToList();

        var imports = BuildImports(constructed.Select(r => r.Type!).ToList(), fileOrganization);

        // Имя файла/модуля — от исходного (до affix) имени метода, чтобы путь не менялся.
        // Текст описания ссылается на идентификатор метода и сохраняет affix-имя.
        var fileBase = _namingConverter.ToCamelCase(operation.OriginalMethodName);
        return new SourceFileNode(
            imports: imports,
            declarations: declarations,
            fileDocumentation: TypeScriptDoc.FileHeader(
                $"{fileBase}.results.schemas.ts",
                $"{fileBase}.results.schemas",
                $"Response body validation schemas for {operation.MethodName}."));
    }

    /// <summary>Builds runtime imports for the schemas declared in a response file.</summary>
    /// <param name="definedTypes">The materialized response types requiring schemas.</param>
    /// <param name="fileOrganization">The generated output layout.</param>
    /// <returns>Imports for validation, UUID constants and referenced model schemas.</returns>
    private IReadOnlyList<ImportNode> BuildImports(IReadOnlyList<ValIO.Core.IR.Types.IrType> definedTypes, FileOrganization? fileOrganization)
    {
        var imports = new List<ImportNode>();
        imports.AddRange(_validation.SchemaDefinitionImports(definedTypes));

        // Model schemas are co-located under modelsPath; this results-schema file sits at
        // {clientsPath}/{op}/ → two levels up to the output root, then into modelsPath.
        var modelsPath = fileOrganization?.ModelsPath ?? "models";
        if (_usesUuidRegex && _validation.UuidConstName is { } uuidConst && definedTypes.Any(UuidFormatSupport.ContainsUuid))
        {
            imports.Add(new ImportNode(
                items: ImmutableList.Create(new IdentifierNode(uuidConst)),
                from: $"../../{modelsPath}/{TypeScriptOptionIds.UuidFormat.ConstFileName}",
                kind: ImportKind.Named));
        }
        var modelNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in definedTypes)
            ValidationTypes.CollectModelSchemaNames(type, modelNames);

        foreach (var modelName in modelNames.OrderBy(n => n, StringComparer.Ordinal))
        {
            // Имя файла модели — от исходного (до affix) имени; спецификатор импорта affix.
            var modelFile = _namingConverter.ToCamelCase(_orig.GetValueOrDefault(modelName, modelName));
            imports.Add(new ImportNode(
                items: ImmutableList.Create(new IdentifierNode($"{modelName}Schema")),
                from: $"../../{modelsPath}/{modelFile}/{modelFile}.schema",
                kind: ImportKind.Named));
        }

        return imports;
    }
}
