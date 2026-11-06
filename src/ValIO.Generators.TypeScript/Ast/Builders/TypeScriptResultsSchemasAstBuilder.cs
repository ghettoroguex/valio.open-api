using ValIO.Core.Ast.Builders;
using System.Collections.Immutable;
using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Interfaces;
using ValIO.Core.IR;
using ValIO.Core.Models;
using ValIO.Generators.TypeScript.Ast.JsDoc;
using ValIO.Generators.TypeScript.Ast.Nodes;
using ValIO.Generators.TypeScript.Validation;

namespace ValIO.Generators.TypeScript.Ast.Builders;

/// <summary>
/// Строит <c>{operation}.results.schemas.ts</c> — именованные схемы валидации для тел ответов,
/// которые иначе пришлось бы строить inline в клиенте на каждый вызов (union/array и т.п.).
/// Каждая constructed-схема объявляется один раз как <c>export const {Op}Result{Status}Schema = ...;</c>.
/// </summary>
public sealed class TypeScriptResultsSchemasAstBuilder : AstBuilderBase
{
    private readonly INamingConverter _namingConverter;
    private readonly IClientValidationStrategy _validation;


    /// <summary>Creates a TypeScriptResultsSchemasAstBuilder for one finalized IR document.</summary>
    /// <param name="documentContext">The shared immutable indexes of the finalized IR document.</param>
    /// <param name="namingConverter">The generated identifier naming policy.</param>
    /// <param name="validation">The configured runtime validation strategy.</param>
    /// <exception cref="ArgumentNullException">A required context or collaborator is null.</exception>
    internal TypeScriptResultsSchemasAstBuilder(IrDocumentContext documentContext,
        INamingConverter namingConverter, IClientValidationStrategy validation)
        : base(documentContext)
    {
        _namingConverter = namingConverter ?? throw new ArgumentNullException(nameof(namingConverter));
        _validation = validation ?? throw new ArgumentNullException(nameof(validation));
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

    private IReadOnlyList<ImportNode> BuildImports(IReadOnlyList<ValIO.Core.IR.Types.IrType> definedTypes, FileOrganization? fileOrganization)
    {
        var imports = new List<ImportNode>();
        imports.AddRange(_validation.SchemaDefinitionImports(definedTypes));

        // Model schemas are co-located under modelsPath; this results-schema file sits at
        // {clientsPath}/{op}/ → two levels up to the output root, then into modelsPath.
        var modelsPath = fileOrganization?.ModelsPath ?? "models";
        var modelNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in definedTypes)
            ValidationTypes.CollectModelSchemaNames(type, modelNames);

        foreach (var modelName in modelNames.OrderBy(n => n, StringComparer.Ordinal))
        {
            // Имя файла модели — от исходного (до affix) имени; спецификатор импорта affix.
            var modelFile = _namingConverter.ToCamelCase(OriginalNameMap.GetTypeOriginalName(DocumentContext, modelName));
            imports.Add(new ImportNode(
                items: ImmutableList.Create(new IdentifierNode($"{modelName}Schema")),
                from: $"../../{modelsPath}/{modelFile}/{modelFile}.schema",
                kind: ImportKind.Named));
        }

        return imports;
    }
}
