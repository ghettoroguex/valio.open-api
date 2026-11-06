using ValIO.Core.Ast.Builders;
using System.Collections.Immutable;
using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Expressions;
using ValIO.Core.IR;
using ValIO.Core.IR.Types;
using ValIO.Core.IR.Utilities;
using ValIO.Generators.Cpp.Ast.Nodes;
using ValIO.Generators.Cpp.Ast.Nodes.UE;
using ValIO.Generators.Cpp.Ast.Nodes.UE.Specifiers;
using ValIO.Generators.Cpp.Models;
using ValIO.Generators.Cpp.Services;

namespace ValIO.Generators.Cpp.Ast.Builders;

/// <summary>Builds Unreal model headers with shared document declaration indexes.</summary>
public sealed class UEModelAstBuilder : AstBuilderBase
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
    public UEModelAstBuilder(UETypeMapper typeMapper, UENamingConvention naming, CppGenerationOptions options, IrDocumentContext documentContext) : base(documentContext)
    {
        _typeMapper = typeMapper ?? throw new ArgumentNullException(nameof(typeMapper));
        if (!ReferenceEquals(typeMapper.DocumentContext, documentContext))
            throw new ArgumentException("The mapper and builder must share a document context.", nameof(typeMapper));
        _naming = naming ?? throw new ArgumentNullException(nameof(naming));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Builds an Unreal Engine model header without discarding unsupported recursive properties.
    /// </summary>
    /// <param name="model">The model contract.</param>
    /// <param name="warnings">The optional generation diagnostics.</param>
    /// <returns>The generated Unreal Engine header.</returns>
    /// <exception cref="NotSupportedException">The model requires unsupported recursive ownership or composition codecs.</exception>
    public CppHeaderFileNode Build(IrModel model, ICollection<string>? warnings = null)
    {
        var enumNames = EnumNames;
        var modelLookup = ModelsByName;
        var structName = _naming.StructName(model.Name);
        // File name uses the ORIGINAL (pre-affix) name so the emitted file/include doesn't change with affixes.
        var fileName = _naming.ModelFileName(model.OriginalName);

        var category = _naming.ModelTypeCategory(_options.BlueprintCategory);
        CppTypeReferenceCollector.EnsureSupportedComposition(model, modelLookup);
        var (baseStruct, baseModelName, propertiesToUse) = GetPropertiesAndBase(model, modelLookup);
        CppTypeReferenceCollector.EnsureNonRecursive(model, propertiesToUse, enumNames);
        var fields = new List<UPropertyFieldNode>();
        foreach (var property in propertiesToUse)
        {
            fields.Add(BuildField(property, category));
        }
        var structNode = new UStructDeclarationNode(
            name: new IdentifierNode(structName),
            specifiers: new UStructSpecifiers { BlueprintType = true, Category = category },
            fields: fields,
            baseStruct: baseStruct,
            documentation: StdDocumentation.Create($"{model.Name} model.", model.Description));

        var includes = CollectIncludes(model, fileName, enumNames, propertiesToUse, baseModelName);

        return new CppHeaderFileNode(
            fileName: fileName,
            headerComment: BuildCopyrightComment(),
            includes: includes,
            declarations: new AstNode[] { structNode });
    }

    /// <summary>
    /// Определяет, использовать ли наследование C++ для allOf, и возвращает свойства для полей.
    /// Делегирует общей реализации Core (IrInheritancePlan): одиночный $ref-базис → наследование
    /// (только собственные свойства), иначе — flatten.
    /// </summary>
    private (IdentifierNode? BaseStruct, string? BaseModelName, IReadOnlyList<IrProperty> Properties) GetPropertiesAndBase(IrModel model, IReadOnlyDictionary<string, IrModel> modelLookup)
    {
        var plan = IrInheritancePlan.Resolve(model, modelLookup);
        if (plan.UseInheritance)
            return (new IdentifierNode(_naming.StructName(plan.BaseModelName!)), plan.BaseModelName, plan.EffectiveProperties);
        return (null, null, plan.EffectiveProperties);
    }

    /// <summary>Builds a documented Unreal field from the property's IR contract.</summary>
    /// <param name="property">The field's property contract.</param>
    /// <param name="category">The Unreal Blueprint category.</param>
    /// <returns>The typed field with a useful Doxygen brief.</returns>
    private UPropertyFieldNode BuildField(IrProperty property, string category)
    {
        var type = _typeMapper.MapType(property.Type);
        var defaultValueStr = _typeMapper.GetDefaultValue(property.Type);
        ExpressionNode? defaultValue = defaultValueStr != null
            ? new IdentifierExpressionNode(new IdentifierNode(defaultValueStr))
            : null;

        return new UPropertyFieldNode(
            name: new IdentifierNode(_naming.PropertyName(property.Name)),
            type: type,
            specifiers: UPropertySpecifiers.ModelField(category),
            defaultValue: defaultValue,
            documentation: StdDocumentation.Create($"{property.Name} field.", property.Description));
    }

    /// <summary>
    /// Собирает необходимые #include директивы для модели
    /// </summary>
    private List<CppIncludeDirectiveNode> CollectIncludes(IrModel model, string fileName, IReadOnlySet<string> enumNames, IReadOnlyList<IrProperty> propertiesToUse, string? baseModelName)
    {
        var includes = new List<CppIncludeDirectiveNode>();
        if (_options.AddUnrealEngineIncludesInHeaders)
            includes.Add(new("CoreMinimal.h"));

        var referencedModels = new HashSet<string>();
        var referencedEnums = new HashSet<string>();
        // Map affixed reference name → ORIGINAL name so #include paths don't change with affixes.
        var orig = BuildOriginalNameMap(model, propertiesToUse);
        if (!string.IsNullOrEmpty(baseModelName))
            referencedModels.Add(baseModelName);
        foreach (var property in propertiesToUse)
        {
            if (TypeReferencesModel(property.Type, model.Name, enumNames))
                continue; // Skip recursive — already excluded from fields
            CollectTypeReferences(property.Type, referencedModels, referencedEnums, enumNames);
        }

        foreach (var refModel in referencedModels.Where(m => m != model.Name).OrderBy(x => x))
            includes.Add(new CppIncludeDirectiveNode(_naming.ModelFileName(orig.GetValueOrDefault(refModel, refModel))));

        foreach (var refEnum in referencedEnums.OrderBy(x => x))
            includes.Add(new CppIncludeDirectiveNode($"../{_naming.EnumIncludePath(orig.GetValueOrDefault(refEnum, refEnum))}"));

        // .generated.h должен быть всегда последним (требование UHT)
        includes.Add(new CppIncludeDirectiveNode(fileName.Replace(".h", _options.GeneratedHeaderSuffix)));

        return includes;
    }

    /// <summary>
    /// Проверяет, содержит ли тип ссылку на указанную модель (прямо или через TArray, TMap и т.д.).
    /// UE не поддерживает рекурсивные типы.
    /// </summary>
    private static bool TypeReferencesModel(IrType type, string modelName, IReadOnlySet<string> enumNames)
        => CppTypeReferenceCollector.ReferencesModel(type, modelName, enumNames);

    /// <summary>
    /// Строит карту «affix-имя ссылки → исходное имя» по типам модели (свойства + composition).
    /// Используется для вычисления путей #include, которые не должны меняться при affix.
    /// При отсутствии affix исходное имя совпадает с affix-именем (карта работает как тождество).
    /// </summary>
    private static Dictionary<string, string> BuildOriginalNameMap(IrModel model, IReadOnlyList<IrProperty> propertiesToUse)
        => CppTypeReferenceCollector.BuildOriginalNameMap(model, propertiesToUse);

    /// <summary>
    /// Рекурсивно собирает сопоставления «affix-имя ссылки → исходное имя» из IrType.
    /// </summary>
    private static void CollectOriginalNames(IrType type, Dictionary<string, string> orig)
        => CppTypeReferenceCollector.CollectOriginalNames(type, orig);

    /// <summary>
    /// Рекурсивно собирает ссылки на модели и enum'ы из типа
    /// </summary>
    private static void CollectTypeReferences(IrType type, HashSet<string> models, HashSet<string> enums, IReadOnlySet<string> enumNames)
        => CppTypeReferenceCollector.Collect(type, models, enums, enumNames);

    private CommentNode? BuildCopyrightComment()
    {
        return _options.Copyright != null
            ? new CommentNode(_options.Copyright, CommentKind.MultiLine)
            : null;
    }
}
