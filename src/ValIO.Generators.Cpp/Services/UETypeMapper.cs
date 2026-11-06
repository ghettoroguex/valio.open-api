using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Core.IR;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Core.IR.Utilities;

namespace ValIO.Generators.Cpp.Services;

/// <summary>
/// Маппер IR типов в UE C++ TypeNode.
/// Отвечает за преобразование language-agnostic IR типов в конкретные UE C++ типы
/// (FString, int32, TArray, TMap и т.д.).
/// </summary>
public sealed class UETypeMapper
{
    /// <summary>Converts generated declaration names to Unreal identifiers.</summary>
    private readonly UENamingConvention _naming;

    /// <summary>Requires naming rules and finalized document indexes before mapping types.</summary>
    /// <param name="naming">The Unreal naming rules.</param>
    /// <param name="documentContext">The indexes used to classify enum references.</param>
    /// <exception cref="ArgumentNullException">Either dependency is null.</exception>
    public UETypeMapper(UENamingConvention naming, IrDocumentContext documentContext)
    {
        _naming = naming ?? throw new ArgumentNullException(nameof(naming));
        DocumentContext = documentContext ?? throw new ArgumentNullException(nameof(documentContext));
    }

    /// <summary>Gets the document indexes retained for this mapper's lifetime.</summary>
    /// <value>The mandatory immutable declaration context.</value>
    public IrDocumentContext DocumentContext { get; }

    /// <summary>
    /// Маппит IR тип в UE C++ TypeNode
    /// </summary>
    public TypeNode MapType(IrType irType)
    {
        return irType switch
        {
            IrPrimitiveType primitive => MapPrimitiveType(primitive),
            IrModelReference modelRef => MapModelReference(modelRef),
            IrArrayType array => MapArrayType(array),
            IrDictionaryType dict => MapDictionaryType(dict),
            IrNullableType nullable => MapNullableType(nullable),
            IrOptionalType optional => MapOptionalType(optional),
            IrUnionType union => MapUnionType(union),
            IrIntersectionType intersection => MapIntersectionType(intersection),
            _ => SimpleType("FString") // fallback
        };
    }

    /// <summary>
    /// Маппит примитивный IR тип в UE C++ тип
    /// </summary>
    public TypeNode MapPrimitiveType(IrPrimitiveType primitive)
    {
        return primitive.PrimitiveKind switch
        {
            IrPrimitiveKind.String => SimpleType("FString"),
            IrPrimitiveKind.Number => SimpleType("double", isPrimitive: true),
            IrPrimitiveKind.Integer => SimpleType("int32", isPrimitive: true),
            IrPrimitiveKind.Long => SimpleType("int64", isPrimitive: true),
            IrPrimitiveKind.Boolean => SimpleType("bool", isPrimitive: true),
            IrPrimitiveKind.DateTime => SimpleType("FString"), // ISO 8601 строка в UE
            IrPrimitiveKind.Date => SimpleType("FString"),
            IrPrimitiveKind.Time => SimpleType("FString"),
            IrPrimitiveKind.Binary => GenericType("TArray", SimpleType("uint8", isPrimitive: true)),
            IrPrimitiveKind.ByteArray => GenericType("TArray", SimpleType("uint8", isPrimitive: true)),
            IrPrimitiveKind.Uuid => SimpleType("FGuid"),
            IrPrimitiveKind.File => GenericType("TArray", SimpleType("uint8", isPrimitive: true)),
            IrPrimitiveKind.Any => SimpleType("FString"),
            IrPrimitiveKind.Void => SimpleType("void", isPrimitive: true),
            _ => SimpleType("FString")
        };
    }

    /// <summary>Maps a declaration reference to the document's model or enum type.</summary>
    /// <param name="modelRef">The exact generated declaration reference.</param>
    /// <returns>An F-prefixed struct type or E-prefixed enum type.</returns>
    public TypeNode MapModelReference(IrModelReference modelRef)
    {
        var isEnum = DocumentContext.EnumNames.Contains(modelRef.ModelName);
        var typeName = isEnum ? _naming.EnumName(modelRef.ModelName) : _naming.StructName(modelRef.ModelName);
        return SimpleType(typeName);
    }

    /// <summary>Determines whether the document declares an enum with the supplied name.</summary>
    /// <param name="modelName">The exact generated declaration name.</param>
    /// <returns>True when the ordinal name identifies an enum.</returns>
    public bool IsEnumReference(string modelName)
    {
        return DocumentContext.EnumNames.Contains(modelName);
    }

    /// <summary>
    /// Маппит массив: IrArrayType → TArray&lt;ElementType&gt;
    /// </summary>
    public TypeNode MapArrayType(IrArrayType array)
    {
        var elementType = MapType(array.ElementType);
        return GenericType("TArray", elementType);
    }

    /// <summary>
    /// Маппит словарь: IrDictionaryType → TMap&lt;KeyType, ValueType&gt;
    /// </summary>
    public TypeNode MapDictionaryType(IrDictionaryType dict)
    {
        var keyType = MapType(dict.KeyType);
        var valueType = MapType(dict.ValueType);
        return new GenericTypeNode(
            new IdentifierNode("TMap"),
            new TypeNode[] { keyType, valueType });
    }

    /// <summary>
    /// UE UPROPERTY не поддерживает TOptional, поэтому для nullable просто используем inner type.
    /// </summary>
    public TypeNode MapNullableType(IrNullableType nullable)
    {
        return MapType(nullable.InnerType);
    }

    /// <summary>
    /// Optional маппится так же, как nullable в UE контексте
    /// </summary>
    public TypeNode MapOptionalType(IrOptionalType optional)
    {
        return MapType(optional.InnerType);
    }

    /// <summary>
    /// Union в UE нет прямого аналога. Берётся первый вариант из IR (остальные комбинации, кроме string+number, сводятся в IrBuilder).
    /// </summary>
    public TypeNode MapUnionType(IrUnionType union)
    {
        if (union.Types.Count > 0)
            return MapType(union.Types[0]);
        return SimpleType("FString");
    }

    /// <summary>
    /// Intersection в UE нет прямого аналога. Маппим первый тип.
    /// </summary>
    public TypeNode MapIntersectionType(IrIntersectionType intersection)
    {
        if (intersection.Types.Count > 0)
            return MapType(intersection.Types[0]);
        return SimpleType("FString");
    }

    /// <summary>
    /// Получает значение по умолчанию для UE C++ типа
    /// </summary>
    public string? GetDefaultValue(IrType irType)
    {
        return irType switch
        {
            IrPrimitiveType p => p.PrimitiveKind switch
            {
                IrPrimitiveKind.Number => "0.0",
                IrPrimitiveKind.Integer => "0",
                IrPrimitiveKind.Long => "0",
                IrPrimitiveKind.Boolean => "false",
                _ => null
            },
            IrNullableType n => GetDefaultValue(n.InnerType),
            IrOptionalType o => GetDefaultValue(o.InnerType),
            _ => null
        };
    }

    /// <summary>
    /// Получает формат printf спецификатора для UE типа (для FString::Printf)
    /// </summary>
    public string GetPrintfSpecifier(IrType irType)
    {
        return irType switch
        {
            IrPrimitiveType p => p.PrimitiveKind switch
            {
                IrPrimitiveKind.String => "%s",
                IrPrimitiveKind.Uuid => "%s",
                IrPrimitiveKind.Integer => "%d",
                IrPrimitiveKind.Long => "%lld",
                IrPrimitiveKind.Number => "%f",
                IrPrimitiveKind.Boolean => "%s",
                _ => "%s"
            },
            IrModelReference => "%s",
            _ => "%s"
        };
    }

    /// <summary>
    /// Определяет нужен ли оператор разыменования (*) при передаче в FString::Printf
    /// </summary>
    public bool NeedsDereference(IrType irType)
    {
        return irType switch
        {
            IrPrimitiveType p => p.PrimitiveKind is IrPrimitiveKind.String,
            IrModelReference => false,
            _ => false
        };
    }

    /// <summary>
    /// Является ли IR тип файловым/бинарным (TArray&lt;uint8&gt; в UE).
    /// Используется для генерации AddFile в multipart/form-data.
    /// Делегирует общей реализации Core (IrTypeQueries.IsFileOrBinary).
    /// </summary>
    public static bool IsFileOrBinaryType(IrType irType) => IrTypeQueries.IsFileOrBinary(irType);

    /// <summary>
    /// Является ли тип FGuid (требует промежуточную переменную с DigitsWithHyphens).
    /// </summary>
    public bool IsGuidType(IrType irType)
    {
        return irType is IrPrimitiveType p && p.PrimitiveKind == IrPrimitiveKind.Uuid;
    }

    /// <summary>
    /// Выражение для FGuid: ToString(EGuidFormats::DigitsWithHyphens).
    /// </summary>
    public string GetGuidToStringExpression(string propertyName)
    {
        return $"{propertyName}.ToString(EGuidFormats::DigitsWithHyphens)";
    }

    /// <summary>
    /// Выражение для передачи в FString::Printf.
    /// Для FGuid — используется промежуточная переменная *VarName.
    /// </summary>
    public string GetPrintfArgumentExpression(IrType irType, string propertyName, string? intermediateVarName = null)
    {
        if (IsGuidType(irType) && intermediateVarName != null)
            return $"*{intermediateVarName}";

        return irType switch
        {
            IrPrimitiveType p => p.PrimitiveKind switch
            {
                IrPrimitiveKind.Uuid => $"*{propertyName}.ToString(EGuidFormats::DigitsWithHyphens)",
                IrPrimitiveKind.String => $"*{propertyName}",
                _ => propertyName
            },
            IrModelReference => $"*{propertyName}",
            _ => propertyName
        };
    }

    /// <summary>
    /// Создаёт SimpleTypeNode
    /// </summary>
    private static SimpleTypeNode SimpleType(string name, bool isPrimitive = false)
    {
        return new SimpleTypeNode(new IdentifierNode(name), isPrimitive: isPrimitive);
    }

    /// <summary>
    /// Создаёт GenericTypeNode с одним аргументом типа
    /// </summary>
    private static GenericTypeNode GenericType(string name, TypeNode typeArg)
    {
        return new GenericTypeNode(new IdentifierNode(name), new TypeNode[] { typeArg });
    }
}
