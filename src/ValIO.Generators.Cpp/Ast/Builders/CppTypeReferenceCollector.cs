using ValIO.Core.IR;
using ValIO.Core.IR.Types;

namespace ValIO.Generators.Cpp.Ast.Builders;

/// <summary>
/// Сбор ссылок на модели/enum'ы из IrType + карта «affix-имя → OriginalName»
/// (include-пути не должны меняться при affix-преобразованиях).
/// Единая реализация для UE- и std-билдеров (UEModelAstBuilder делегирует).
/// </summary>
public static class CppTypeReferenceCollector
{
    /// <summary>Rejects model compositions that cannot be represented by the current C++ object-codec contract.</summary>
    /// <param name="model">The model being generated.</param>
    /// <param name="modelLookup">The available base model contracts.</param>
    /// <exception cref="NotSupportedException">The reachable composition requires union validation or unresolved inheritance.</exception>
    public static void EnsureSupportedComposition(IrModel model, IReadOnlyDictionary<string, IrModel> modelLookup)
    {
        var pending = new Stack<IrModel>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        pending.Push(model);
        while (pending.TryPop(out var current))
        {
            if (!visited.Add(current.Name) || current.Composition == null)
                continue;
            if (current.Composition.Kind != IrCompositionKind.AllOf)
                throw new NotSupportedException($"C++ model '{current.OriginalName}' uses {current.Composition.Kind}; a typed variant codec is required.");
            foreach (var member in current.Composition.Types)
            {
                if (member is not IrModelReference reference)
                    throw new NotSupportedException($"C++ model '{current.OriginalName}' contains a nested non-reference allOf member; a composition-aware codec is required.");
                if (!modelLookup.TryGetValue(reference.ModelName, out var baseModel))
                    throw new NotSupportedException($"C++ model '{current.OriginalName}' references unresolved allOf base '{reference.OriginalModelName}'.");
                pending.Push(baseModel);
            }
        }
    }

    /// <summary>Rejects recursive value contracts rather than silently dropping their JSON properties.</summary>
    /// <param name="model">The model being generated.</param>
    /// <param name="properties">The effective fields after inheritance resolution.</param>
    /// <param name="enumNames">The document's enum names.</param>
    /// <exception cref="NotSupportedException">A property requires a recursive model representation not provided by this profile.</exception>
    public static void EnsureNonRecursive(IrModel model, IReadOnlyList<IrProperty> properties, IReadOnlySet<string> enumNames)
    {
        foreach (var property in properties)
            if (ReferencesModel(property.Type, model.Name, enumNames))
                throw new NotSupportedException($"C++ model '{model.OriginalName}' has recursive property '{property.OriginalName}'; a recursive ownership representation is required.");
    }

    /// <summary>
    /// Рекурсивно раскладывает ссылки типа по множествам models/enums.
    /// </summary>
    public static void Collect(IrType type, ISet<string> models, ISet<string> enums, IReadOnlySet<string> enumNames)
    {
        switch (type)
        {
            case IrModelReference modelRef:
                if (enumNames.Contains(modelRef.ModelName))
                    enums.Add(modelRef.ModelName);
                else
                    models.Add(modelRef.ModelName);
                break;
            case IrArrayType array:
                Collect(array.ElementType, models, enums, enumNames);
                break;
            case IrDictionaryType dict:
                Collect(dict.KeyType, models, enums, enumNames);
                Collect(dict.ValueType, models, enums, enumNames);
                break;
            case IrNullableType nullable:
                Collect(nullable.InnerType, models, enums, enumNames);
                break;
            case IrOptionalType optional:
                Collect(optional.InnerType, models, enums, enumNames);
                break;
            case IrUnionType union:
                foreach (var t in union.Types) Collect(t, models, enums, enumNames);
                break;
            case IrIntersectionType intersection:
                foreach (var t in intersection.Types) Collect(t, models, enums, enumNames);
                break;
        }
    }

    /// <summary>
    /// Карта «affix-имя ссылки → исходное имя» по свойствам и composition модели.
    /// </summary>
    public static Dictionary<string, string> BuildOriginalNameMap(IrModel model, IReadOnlyList<IrProperty> properties)
    {
        var orig = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [model.Name] = model.OriginalName,
        };
        foreach (var property in properties)
            CollectOriginalNames(property.Type, orig);
        if (model.Composition != null)
            foreach (var t in model.Composition.Types)
                CollectOriginalNames(t, orig);
        return orig;
    }

    /// <summary>
    /// Ссылается ли тип (напрямую или через контейнеры) на указанную модель —
    /// проверка рекурсивных свойств.
    /// </summary>
    public static bool ReferencesModel(IrType type, string modelName, IReadOnlySet<string> enumNames)
    {
        switch (type)
        {
            case IrModelReference modelRef:
                return !enumNames.Contains(modelRef.ModelName) && modelRef.ModelName == modelName;
            case IrArrayType array:
                return ReferencesModel(array.ElementType, modelName, enumNames);
            case IrDictionaryType dict:
                return ReferencesModel(dict.KeyType, modelName, enumNames)
                    || ReferencesModel(dict.ValueType, modelName, enumNames);
            case IrNullableType nullable:
                return ReferencesModel(nullable.InnerType, modelName, enumNames);
            case IrOptionalType optional:
                return ReferencesModel(optional.InnerType, modelName, enumNames);
            case IrUnionType union:
                return union.Types.Any(t => ReferencesModel(t, modelName, enumNames));
            case IrIntersectionType intersection:
                return intersection.Types.Any(t => ReferencesModel(t, modelName, enumNames));
            default:
                return false;
        }
    }

    public static void CollectOriginalNames(IrType type, Dictionary<string, string> orig)
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
}
