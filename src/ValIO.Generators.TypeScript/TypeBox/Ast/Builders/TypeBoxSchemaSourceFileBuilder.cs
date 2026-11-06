using System.Collections.Immutable;
using ValIO.Core.Ast.Builders;
using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Core.Interfaces;
using ValIO.Core.IR;
using ValIO.Core.IR.Types;
using ValIO.Generators.TypeScript.Ast.Builders;
using ValIO.Generators.TypeScript.Ast.JsDoc;
using ValIO.Generators.TypeScript.Ast.Nodes;
using ValIO.Generators.TypeScript.Options;

namespace ValIO.Generators.TypeScript.TypeBox.Ast.Builders;

/// <summary>
/// Builds TypeBox schema source files as TypeScript AST.
/// </summary>
public sealed class TypeBoxSchemaSourceFileBuilder : AstBuilderBase
{
    private readonly INamingConverter _namingConverter;
    private readonly TypeBoxAstBuilder _builder;
    private readonly string _modelsPath;
    private readonly string _uuidFormat;

    /// <summary>Creates a schema source builder bound to one finalized document context.</summary>
    /// <param name="documentContext">The shared immutable declaration indexes.</param>
    /// <param name="namingConverter">The naming rules for declaration file stems.</param>
    /// <param name="modelsPath">The configured model output directory.</param>
    /// <param name="uuidFormat">The selected UUID validation mode.</param>
    /// <exception cref="ArgumentNullException">A required dependency is null.</exception>
    public TypeBoxSchemaSourceFileBuilder(IrDocumentContext documentContext, INamingConverter namingConverter,
        string modelsPath = "models", string? uuidFormat = null)
        : base(documentContext)
    {
        _namingConverter = namingConverter ?? throw new ArgumentNullException(nameof(namingConverter));
        _modelsPath = string.IsNullOrWhiteSpace(modelsPath) ? "models" : modelsPath;
        _uuidFormat = TypeScriptOptionIds.UuidFormat.Resolve(uuidFormat);
        _builder = new TypeBoxAstBuilder(uuidFormat);
    }

    /// <summary>Builds a model schema with dependencies resolved from the complete input document.</summary>
    /// <param name="model">The model being validated.</param>
    /// <param name="currentFileName">The current schema file stem.</param>
    /// <param name="schemasPath">The schema output directory.</param>
    /// <returns>The complete schema source AST.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is null.</exception>
    public SourceFileNode BuildModelSchemaSourceFile(IrModel model, string? currentFileName = null, string? schemasPath = null)
    {
        ArgumentNullException.ThrowIfNull(model);

        var schemaName = GetSchemaName(model.Name);
        return new SourceFileNode(
            imports: BuildImports(model, currentFileName, schemasPath),
            declarations: ImmutableList<DeclarationNode>.Empty
                .Add(new VariableDeclarationNode(
                    name: new IdentifierNode(schemaName),
                    declarationKind: VariableDeclarationKind.Const,
                    initializer: _builder.BuildSchemaFromIrModel(model),
                    documentation: BuildDocumentation(model.Description),
                    isExported: true))
                .Add(BuildStaticTypeAlias(model.Name, schemaName)),
            fileDocumentation: SchemaFileHeader(model.OriginalName, model.Name));
    }

    /// <summary>Builds a schema referencing the enum's existing values export.</summary>
    /// <param name="irEnum">The enum being validated.</param>
    /// <param name="separateModels">Whether enum files occupy individual directories.</param>
    /// <returns>The complete enum schema source AST.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="irEnum"/> is null.</exception>
    public SourceFileNode BuildEnumSchemaSourceFile(IrEnum irEnum, bool separateModels)
    {
        ArgumentNullException.ThrowIfNull(irEnum);

        var schemaName = GetSchemaName(irEnum.Name);

        // Переиспользуем {Name}Values из модели enum: Type.Union(...map(Type.Literal)).
        return new SourceFileNode(
            imports: BuildEnumImports(irEnum, separateModels),
            declarations: ImmutableList<DeclarationNode>.Empty
                .Add(new VariableDeclarationNode(
                    name: new IdentifierNode(schemaName),
                    declarationKind: VariableDeclarationKind.Const,
                    initializer: _builder.BuildEnumSchema(irEnum),
                    documentation: BuildDocumentation(irEnum.Description),
                    isExported: true))
                .Add(BuildStaticTypeAlias(irEnum.Name, schemaName)),
            fileDocumentation: SchemaFileHeader(irEnum.OriginalName, irEnum.Name));
    }

    private CommentNode SchemaFileHeader(string originalName, string typeName)
    {
        // Имя файла/модуля — от исходного (до affix) имени, чтобы путь не менялся.
        // Текст описания ссылается на идентификатор и сохраняет affix-имя.
        var fileBase = _namingConverter.ToCamelCase(originalName);
        return TypeScriptDoc.FileHeader(
            $"{fileBase}.schema.ts",
            $"{_modelsPath}/{fileBase}",
            $"TypeBox validation schema for {typeName}.");
    }

    private IReadOnlyList<ImportNode> BuildEnumImports(IrEnum irEnum, bool separateModels)
    {
        var imports = ImmutableList.CreateBuilder<ImportNode>();
        imports.AddRange(BuildTypeBoxImports());

        var enumFileName = _namingConverter.ToCamelCase(irEnum.OriginalName);
        // Co-located: the enum interface + {Name}Values live next to the schema in the same directory.
        var modelPath = $"./{enumFileName}";

        imports.Add(new ImportNode(
            items: ImmutableList.Create(new IdentifierNode($"{irEnum.Name}Values")),
            from: modelPath,
            kind: ImportKind.Named));

        return imports.ToImmutable();
    }

    private static string GetSchemaName(string name) => $"{name}Schema";

    /// <summary>Builds runtime imports and dependencies for the model's complete schema.</summary>
    /// <param name="model">The model being rendered.</param>
    /// <param name="currentFileName">The current schema file stem.</param>
    /// <param name="schemasPath">The schema output directory.</param>
    /// <returns>The imports needed by the generated model schema.</returns>
    private IReadOnlyList<ImportNode> BuildImports(IrModel model, string? currentFileName, string? schemasPath)
    {
        var imports = ImmutableList.CreateBuilder<ImportNode>();
        imports.AddRange(BuildTypeBoxImports());
        if (model.Properties.Any(property => TypeBoxInstanceAst.RequiresRegistry(property.Type))
            || model.Composition?.Types.Any(TypeBoxInstanceAst.RequiresRegistry) == true)
            imports.Add(new ImportNode([new IdentifierNode("Kind"), new IdentifierNode("TypeRegistry")],
                "@sinclair/typebox", ImportKind.Named));
        if (model.Properties.Any(property => _builder.RequiresFormatRegistry(property.Type))
            || model.Composition?.Types.Any(_builder.RequiresFormatRegistry) == true)
            imports.Add(new ImportNode([new IdentifierNode("FormatRegistry")], "@sinclair/typebox", ImportKind.Named));
        var dependencies = ExtractDependencies(model);

        foreach (var dependency in dependencies.OrderBy(name => name, StringComparer.Ordinal))
        {
            var depFileName = _namingConverter.ToCamelCase(OriginalNameMap.GetTypeOriginalName(DocumentContext, dependency));
            imports.Add(new ImportNode(
                items: ImmutableList.Create(new IdentifierNode(GetSchemaName(dependency))),
                from: CalculateImportPath(currentFileName, depFileName, schemasPath),
                kind: ImportKind.Named));
        }

        // Shared UUID pattern constant (regex mode): schema sits in {schemas}/{name}/ → ../formats.
        if (_uuidFormat == TypeScriptOptionIds.UuidFormat.Regex && UuidFormatSupport.ModelUsesUuid(model))
        {
            imports.Add(new ImportNode(
                items: ImmutableList.Create(new IdentifierNode(TypeScriptOptionIds.UuidFormat.PatternConstName)),
                from: $"../{TypeScriptOptionIds.UuidFormat.ConstFileName}",
                kind: ImportKind.Named));
        }

        return imports.ToImmutable();
    }

    private static IReadOnlyList<ImportNode> BuildTypeBoxImports()
    {
        return ImmutableList<ImportNode>.Empty
            .Add(new ImportNode(
                items: ImmutableList.Create(new IdentifierNode("Type")),
                from: "@sinclair/typebox",
                kind: ImportKind.Named))
            .Add(new ImportNode(
                items: ImmutableList.Create(new IdentifierNode("Static")),
                from: "@sinclair/typebox",
                kind: ImportKind.Named,
                isTypeOnly: true));
    }

    private static TypeAliasDeclarationNode BuildStaticTypeAlias(string name, string schemaName)
    {
        return new TypeAliasDeclarationNode(
            name: new IdentifierNode($"{name}SchemaOutput"),
            type: new RawTypeNode($"Static<typeof {schemaName}>"),
            documentation: TypeScriptDoc.Block(
                $"Inferred static type of {schemaName}.",
                ("typedef", $"{{Static<typeof {schemaName}>}} {name}SchemaOutput")));
    }

    private static CommentNode? BuildDocumentation(string? description)
    {
        return string.IsNullOrWhiteSpace(description)
            ? null
            : new CommentNode(description, CommentKind.JSDoc);
    }

    private static IReadOnlyList<string> ExtractDependencies(IrModel model)
    {
        var dependencies = new HashSet<string>(StringComparer.Ordinal);

        foreach (var property in model.Properties)
            ExtractDependenciesFromType(property.Type, model.Name, dependencies);

        if (model.Composition != null)
        {
            foreach (var type in model.Composition.Types)
                ExtractDependenciesFromType(type, model.Name, dependencies);
        }

        return dependencies.ToList();
    }

    private static void ExtractDependenciesFromType(IrType type, string currentModelName, HashSet<string> dependencies)
    {
        switch (type)
        {
            case IrModelReference modelRef when !modelRef.ModelName.Equals(currentModelName, StringComparison.Ordinal):
                dependencies.Add(modelRef.ModelName);
                break;
            case IrArrayType array:
                ExtractDependenciesFromType(array.ElementType, currentModelName, dependencies);
                break;
            case IrDictionaryType dictionary:
                ExtractDependenciesFromType(dictionary.KeyType, currentModelName, dependencies);
                ExtractDependenciesFromType(dictionary.ValueType, currentModelName, dependencies);
                break;
            case IrNullableType nullable:
                ExtractDependenciesFromType(nullable.InnerType, currentModelName, dependencies);
                break;
            case IrOptionalType optional:
                ExtractDependenciesFromType(optional.InnerType, currentModelName, dependencies);
                break;
            case IrUnionType union:
                foreach (var child in union.Types)
                    ExtractDependenciesFromType(child, currentModelName, dependencies);
                break;
            case IrIntersectionType intersection:
                foreach (var child in intersection.Types)
                    ExtractDependenciesFromType(child, currentModelName, dependencies);
                break;
        }
    }

    private string CalculateImportPath(string? currentFileName, string dependencyFileName, string? schemasPath)
    {
        if (!string.IsNullOrEmpty(schemasPath) && !string.IsNullOrEmpty(currentFileName))
        {
            var normalizedSchemasPath = NormalizePath(schemasPath);
            var currentBasePath = $"{normalizedSchemasPath}/{currentFileName}";
            var dependencyDir = $"{normalizedSchemasPath}/{dependencyFileName}";
            return NormalizePath(CalculateRelativePath(currentBasePath, dependencyDir, $"{dependencyFileName}.schema"));
        }

        return $"./{dependencyFileName}.schema";
    }

    private static string CalculateRelativePath(string fromPath, string toPath, string fileName)
    {
        fromPath = NormalizePath(fromPath);
        toPath = NormalizePath(toPath);

        if (fromPath.Equals(toPath, StringComparison.OrdinalIgnoreCase))
            return $"./{fileName}";

        var fromParts = fromPath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        var toParts = toPath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);

        var commonLength = 0;
        var minLength = Math.Min(fromParts.Length, toParts.Length);
        for (var i = 0; i < minLength; i++)
        {
            if (!fromParts[i].Equals(toParts[i], StringComparison.OrdinalIgnoreCase))
                break;
            commonLength++;
        }

        var upLevels = fromParts.Length - commonLength;
        var downPath = string.Join("/", toParts.Skip(commonLength));
        var relativePath = string.Join("/", Enumerable.Repeat("..", upLevels));

        if (!string.IsNullOrEmpty(downPath))
            relativePath = string.IsNullOrEmpty(relativePath) ? downPath : $"{relativePath}/{downPath}";

        return $"{relativePath}/{fileName}";
    }

    private static string NormalizePath(string path) =>
        path.Replace('\\', '/').Replace("//", "/").TrimEnd('/');
}
