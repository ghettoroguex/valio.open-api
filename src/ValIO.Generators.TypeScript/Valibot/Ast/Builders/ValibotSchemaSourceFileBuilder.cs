using System.Collections.Immutable;
using ValIO.Core.Ast.Builders;
using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Expressions;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Core.Interfaces;
using ValIO.Core.IR;
using ValIO.Core.IR.Types;
using ValIO.Generators.TypeScript.Ast.Builders;
using ValIO.Generators.TypeScript.Ast.JsDoc;
using ValIO.Generators.TypeScript.Ast.Nodes;
using ValIO.Generators.TypeScript.Ast.Nodes.Expressions;
using ValIO.Generators.TypeScript.Options;

namespace ValIO.Generators.TypeScript.Valibot.Ast.Builders;

/// <summary>
/// Builds Valibot schema source files as TypeScript AST.
/// </summary>
public sealed class ValibotSchemaSourceFileBuilder : AstBuilderBase
{
    private readonly INamingConverter _namingConverter;
    private readonly ValibotAstBuilder _builder;
    private readonly string _modelsPath;
    private readonly string _uuidFormat;

    /// <summary>Creates a schema source builder bound to one finalized document context.</summary>
    /// <param name="documentContext">The shared immutable declaration indexes.</param>
    /// <param name="namingConverter">The naming rules for declaration file stems.</param>
    /// <param name="modelsPath">The configured model output directory.</param>
    /// <param name="uuidFormat">The selected UUID validation mode.</param>
    /// <exception cref="ArgumentNullException">A required dependency is null.</exception>
    public ValibotSchemaSourceFileBuilder(IrDocumentContext documentContext, INamingConverter namingConverter,
        string modelsPath = "models", string? uuidFormat = null)
        : base(documentContext)
    {
        _namingConverter = namingConverter ?? throw new ArgumentNullException(nameof(namingConverter));
        _modelsPath = string.IsNullOrWhiteSpace(modelsPath) ? "models" : modelsPath;
        _uuidFormat = TypeScriptOptionIds.UuidFormat.Resolve(uuidFormat);
        _builder = new ValibotAstBuilder(uuidFormat);
    }

    /// <summary>Builds a model schema with dependencies resolved from the complete input document.</summary>
    /// <param name="model">The model being validated.</param>
    /// <param name="separateModels">Whether model files occupy individual directories.</param>
    /// <returns>The complete schema source AST.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is null.</exception>
    public SourceFileNode BuildModelSchemaSourceFile(IrModel model, bool separateModels)
    {
        ArgumentNullException.ThrowIfNull(model);

        var schemaName = GetSchemaName(model.Name);
        var isRecursive = IsSelfRecursive(model);
        // Для рекурсивных схем TS не может вывести тип из v.lazy(...) без явной аннотации.
        // Декларируем `: v.GenericSchema<{ModelName}>` и подтягиваем тип модели.
        var schemaType = isRecursive
            ? (TypeNode)new RawTypeNode($"v.GenericSchema<{model.Name}>")
            : null;

        return new SourceFileNode(
            imports: BuildImports(model, separateModels, includeOwnModelType: isRecursive),
            declarations: ImmutableList<DeclarationNode>.Empty
                .Add(new VariableDeclarationNode(
                    name: new IdentifierNode(schemaName),
                    declarationKind: VariableDeclarationKind.Const,
                    type: schemaType,
                    initializer: _builder.BuildSchemaFromIrModel(model),
                    documentation: BuildDocumentation(model.Description),
                    isExported: true))
                .Add(BuildInferOutputTypeAlias(model.Name, schemaName)),
            fileDocumentation: SchemaFileHeader(model.OriginalName, model.Name, "Valibot"));
    }

    private static bool IsSelfRecursive(IrModel model)
    {
        var seen = new HashSet<IrType>();
        foreach (var property in model.Properties)
        {
            if (ReferencesSelf(property.Type, model.Name, seen))
                return true;
        }
        if (model.Composition != null)
        {
            foreach (var type in model.Composition.Types)
            {
                if (ReferencesSelf(type, model.Name, seen))
                    return true;
            }
        }
        return false;
    }

    private static bool ReferencesSelf(IrType type, string ownName, HashSet<IrType> seen)
    {
        if (!seen.Add(type)) return false;
        switch (type)
        {
            case IrModelReference m:
                return string.Equals(m.ModelName, ownName, StringComparison.OrdinalIgnoreCase);
            case IrArrayType a:
                return ReferencesSelf(a.ElementType, ownName, seen);
            case IrDictionaryType d:
                return ReferencesSelf(d.KeyType, ownName, seen) || ReferencesSelf(d.ValueType, ownName, seen);
            case IrNullableType n:
                return ReferencesSelf(n.InnerType, ownName, seen);
            case IrOptionalType o:
                return ReferencesSelf(o.InnerType, ownName, seen);
            case IrUnionType u:
                return u.Types.Any(t => ReferencesSelf(t, ownName, seen));
            case IrIntersectionType i:
                return i.Types.Any(t => ReferencesSelf(t, ownName, seen));
            default:
                return false;
        }
    }

    /// <summary>Builds a schema referencing the enum's existing values export.</summary>
    /// <param name="enumModel">The enum being validated.</param>
    /// <param name="separateModels">Whether enum files occupy individual directories.</param>
    /// <returns>The complete enum schema source AST.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="enumModel"/> is null.</exception>
    public SourceFileNode BuildEnumSchemaSourceFile(IrEnum enumModel, bool separateModels)
    {
        ArgumentNullException.ThrowIfNull(enumModel);

        var schemaName = GetSchemaName(enumModel.Name);

        // Переиспользуем {Name}Values из модели enum (v.picklist принимает этот массив),
        // не дублируя значения в файле схемы.
        return new SourceFileNode(
            imports: BuildEnumImports(enumModel, separateModels),
            declarations: ImmutableList<DeclarationNode>.Empty
                .Add(new VariableDeclarationNode(
                    name: new IdentifierNode(schemaName),
                    declarationKind: VariableDeclarationKind.Const,
                    initializer: _builder.BuildEnumSchema(enumModel),
                    documentation: BuildDocumentation(enumModel.Description),
                    isExported: true))
                .Add(BuildInferOutputTypeAlias(enumModel.Name, schemaName)),
            fileDocumentation: SchemaFileHeader(enumModel.OriginalName, enumModel.Name, "Valibot"));
    }

    private CommentNode SchemaFileHeader(string originalName, string typeName, string library)
    {
        // Имя файла/модуля — от исходного (до affix) имени, чтобы путь не менялся.
        // Текст описания ссылается на идентификатор и сохраняет affix-имя.
        var fileBase = _namingConverter.ToCamelCase(originalName);
        return TypeScriptDoc.FileHeader(
            $"{fileBase}.schema.ts",
            $"{_modelsPath}/{fileBase}",
            $"{library} validation schema for {typeName}.");
    }

    private IReadOnlyList<ImportNode> BuildEnumImports(IrEnum enumModel, bool separateModels)
    {
        var imports = ImmutableList.CreateBuilder<ImportNode>();
        imports.AddRange(BuildValibotImport());

        var enumFileName = _namingConverter.ToCamelCase(enumModel.OriginalName);
        // Co-located: the enum interface + {Name}Values live next to the schema in the same directory.
        var modelPath = $"./{enumFileName}";

        imports.Add(new ImportNode(
            items: ImmutableList.Create(new IdentifierNode($"{enumModel.Name}Values")),
            from: modelPath,
            kind: ImportKind.Named));

        return imports.ToImmutable();
    }

    private static string GetSchemaName(string name) => $"{name}Schema";

    private static IReadOnlyList<ImportNode> BuildValibotImport()
    {
        return ImmutableList<ImportNode>.Empty.Add(new ImportNode(
            items: ImmutableList.Create(new IdentifierNode("v")),
            from: "valibot",
            kind: ImportKind.Namespace));
    }

    /// <summary>Builds schema imports using original declaration names from the shared context.</summary>
    /// <param name="model">The model whose dependencies are imported.</param>
    /// <param name="separateModels">Whether model files occupy individual directories.</param>
    /// <param name="includeOwnModelType">Whether a recursive schema needs its model type annotation.</param>
    /// <returns>The required runtime and type imports.</returns>
    private IReadOnlyList<ImportNode> BuildImports(IrModel model, bool separateModels, bool includeOwnModelType = false)
    {
        var imports = ImmutableList.CreateBuilder<ImportNode>();
        imports.AddRange(BuildValibotImport());

        // Для рекурсивных схем — нужен тип самой модели (для аннотации v.GenericSchema<T>).
        if (includeOwnModelType)
        {
            var ownFileName = _namingConverter.ToCamelCase(model.OriginalName);
            // Co-located: the model interface lives next to its schema in the same directory.
            var ownPath = $"./{ownFileName}";
            imports.Add(new ImportNode(
                items: ImmutableList.Create(new IdentifierNode(model.Name)),
                from: ownPath,
                kind: ImportKind.Named,
                isTypeOnly: true));
        }

        var dependencies = ExtractDependencies(model)
            .Where(dep => !dep.Equals(model.Name, StringComparison.OrdinalIgnoreCase))
            .OrderBy(dep => dep, StringComparer.Ordinal)
            .ToList();

        foreach (var dependency in dependencies)
        {
            var depFileName = _namingConverter.ToCamelCase(OriginalNameMap.GetTypeOriginalName(DocumentContext, dependency));
            var importPath = separateModels
                ? $"../{depFileName}/{depFileName}.schema"
                : $"./{depFileName}.schema";

            imports.Add(new ImportNode(
                items: ImmutableList.Create(new IdentifierNode(GetSchemaName(dependency))),
                from: importPath,
                kind: ImportKind.Named));
        }

        // Shared UUID regex constant (regex mode): schema sits in {schemas}/{name}/ → ../formats.
        if (_uuidFormat == TypeScriptOptionIds.UuidFormat.Regex && UuidFormatSupport.ModelUsesUuid(model))
        {
            imports.Add(new ImportNode(
                items: ImmutableList.Create(new IdentifierNode(TypeScriptOptionIds.UuidFormat.RegexConstName)),
                from: separateModels
                    ? $"../{TypeScriptOptionIds.UuidFormat.ConstFileName}"
                    : $"./{TypeScriptOptionIds.UuidFormat.ConstFileName}",
                kind: ImportKind.Named));
        }

        return imports.ToImmutable();
    }

    private static TypeAliasDeclarationNode BuildInferOutputTypeAlias(string name, string schemaName)
    {
        return new TypeAliasDeclarationNode(
            name: new IdentifierNode($"{name}SchemaOutput"),
            type: new RawTypeNode($"v.InferOutput<typeof {schemaName}>"),
            documentation: TypeScriptDoc.Block(
                $"Inferred output type of {schemaName}.",
                ("typedef", $"{{v.InferOutput<typeof {schemaName}>}} {name}SchemaOutput")));
    }

    private static CommentNode? BuildDocumentation(string? description)
    {
        return string.IsNullOrWhiteSpace(description)
            ? null
            : new CommentNode(description, CommentKind.JSDoc);
    }

    private IReadOnlyList<string> ExtractDependencies(IrModel model)
    {
        var dependencies = new HashSet<string>(StringComparer.Ordinal);

        foreach (var property in model.Properties)
            ExtractDependenciesFromIrType(property.Type, dependencies);

        if (model.Composition != null)
        {
            foreach (var type in model.Composition.Types)
                ExtractDependenciesFromIrType(type, dependencies);
        }

        return dependencies.ToList();
    }

    private void ExtractDependenciesFromIrType(IrType irType, HashSet<string> dependencies)
    {
        switch (irType)
        {
            case IrModelReference modelReference:
                dependencies.Add(modelReference.ModelName);
                break;
            case IrArrayType arrayType:
                ExtractDependenciesFromIrType(arrayType.ElementType, dependencies);
                break;
            case IrDictionaryType dictionaryType:
                ExtractDependenciesFromIrType(dictionaryType.KeyType, dependencies);
                ExtractDependenciesFromIrType(dictionaryType.ValueType, dependencies);
                break;
            case IrUnionType unionType:
                foreach (var type in unionType.Types)
                    ExtractDependenciesFromIrType(type, dependencies);
                break;
            case IrIntersectionType intersectionType:
                foreach (var type in intersectionType.Types)
                    ExtractDependenciesFromIrType(type, dependencies);
                break;
            case IrNullableType nullableType:
                ExtractDependenciesFromIrType(nullableType.InnerType, dependencies);
                break;
            case IrOptionalType optionalType:
                ExtractDependenciesFromIrType(optionalType.InnerType, dependencies);
                break;
        }
    }
}
