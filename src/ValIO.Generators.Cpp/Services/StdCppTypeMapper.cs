using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Core.IR;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Core.IR.Utilities;
using ValIO.Generators.Cpp.Models;
using ValIO.Generators.Cpp.Runtime;

namespace ValIO.Generators.Cpp.Services;

/// <summary>
/// Maps IR types to standalone C++ types using the finalized document's declaration indexes.
/// </summary>
public sealed class StdCppTypeMapper
{
    /// <summary>Converts generated declaration names to standalone C++ identifiers.</summary>
    private readonly StdNamingConvention _naming;
    /// <summary>The explicitly selected application scalar representations for this generation.</summary>
    private readonly CppGenerationOptions _options;

    /// <summary>Gets diagnostics produced by this mapper's generation step.</summary>
    /// <value>The step-owned mutable diagnostic collection.</value>
    public ICollection<string> Warnings { get; } = new List<string>();

    /// <summary>Requires naming rules and document indexes before any type can be mapped.</summary>
    /// <param name="naming">The standalone C++ naming rules.</param>
    /// <param name="documentContext">The indexes used to classify declaration references.</param>
    /// <param name="options">The selected scalar providers, or default string representations.</param>
    /// <exception cref="ArgumentNullException">Either dependency is null.</exception>
    public StdCppTypeMapper(StdNamingConvention naming, IrDocumentContext documentContext, CppGenerationOptions? options = null)
    {
        _naming = naming ?? throw new ArgumentNullException(nameof(naming));
        DocumentContext = documentContext ?? throw new ArgumentNullException(nameof(documentContext));
        _options = options ?? new CppGenerationOptions();
    }

    /// <summary>Gets the document context used throughout this mapper's lifetime.</summary>
    /// <value>The mandatory immutable declaration indexes.</value>
    public IrDocumentContext DocumentContext { get; }

    /// <summary>
    /// Маппит IR тип в std-C++ TypeNode.
    /// </summary>
    public TypeNode MapType(IrType irType)
    {
        return irType switch
        {
            IrPrimitiveType primitive => MapPrimitiveType(primitive),
            IrModelReference modelRef => MapModelReference(modelRef),
            IrArrayType array => MapArrayType(array),
            IrDictionaryType dict => MapDictionaryType(dict),
            IrNullableType nullable => MapWrapperType("std::optional", nullable.InnerType),
            IrOptionalType optional => MapWrapperType("std::optional", optional.InnerType),
            IrUnionType union => MapUnionType(union),
            IrIntersectionType intersection => MapIntersectionType(intersection),
            _ => SimpleType("std::string"),
        };
    }

    /// <summary>Maps scalar contracts to standard-library types, including explicit JSON null.</summary>
    /// <param name="primitive">The primitive schema contract.</param>
    /// <returns>The standalone C++ type preserving its scalar representation.</returns>
    public TypeNode MapPrimitiveType(IrPrimitiveType primitive)
    {
        if (_options.UuidBackend != CppUuidBackend.String && primitive.PrimitiveKind == IrPrimitiveKind.Uuid)
            return SimpleType("ValIO::Uuid");
        if (_options.TemporalBackend != CppTemporalBackend.String && TemporalTypeName(primitive) is { } temporal)
            return SimpleType(temporal);
        return primitive.PrimitiveKind switch
        {
            IrPrimitiveKind.String => SimpleType("std::string"),
            IrPrimitiveKind.Number => SimpleType("double", isPrimitive: true),
            IrPrimitiveKind.Integer => SimpleType("std::int32_t", isPrimitive: true),
            IrPrimitiveKind.Long => SimpleType("std::int64_t", isPrimitive: true),
            IrPrimitiveKind.Boolean => SimpleType("bool", isPrimitive: true),
            IrPrimitiveKind.DateTime => SimpleType("std::string"),
            IrPrimitiveKind.Date => SimpleType("std::string"),
            IrPrimitiveKind.Time => SimpleType("std::string"),
            IrPrimitiveKind.Uuid => SimpleType("std::string"),
            IrPrimitiveKind.Binary or IrPrimitiveKind.ByteArray or IrPrimitiveKind.File
                => GenericType("std::vector", SimpleType("std::uint8_t", isPrimitive: true)),
            IrPrimitiveKind.Any => WarnedString(primitive, "Any"),
            IrPrimitiveKind.Void => SimpleType("void", isPrimitive: true),
            IrPrimitiveKind.Null => SimpleType("std::nullptr_t", isPrimitive: true),
            _ => SimpleType("std::string"),
        };
    }

    /// <summary>Maps a reference using the document's enum classification and C++ naming rules.</summary>
    /// <param name="modelRef">The generated declaration reference.</param>
    /// <returns>The model or enum type node.</returns>
    public TypeNode MapModelReference(IrModelReference modelRef)
    {
        var isEnum = DocumentContext.EnumNames.Contains(modelRef.ModelName);
        var typeName = isEnum ? _naming.EnumName(modelRef.ModelName) : _naming.StructName(modelRef.ModelName);
        return SimpleType(typeName);
    }

    /// <summary>Determines whether a generated declaration name identifies an enum.</summary>
    /// <param name="modelName">The exact generated declaration name.</param>
    /// <returns>True when the document declares an enum with this ordinal name.</returns>
    public bool IsEnumReference(string modelName)
        => DocumentContext.EnumNames.Contains(modelName);

    public TypeNode MapArrayType(IrArrayType array)
        => GenericType("std::vector", MapType(array.ElementType));

    public TypeNode MapDictionaryType(IrDictionaryType dict)
        => GenericType("std::unordered_map", MapType(dict.KeyType), MapType(dict.ValueType));

    /// <summary>
    /// Nullable/Optional → std::optional&lt;Inner&gt; — в отличие от UE, std-C++ моделирует
    /// опциональность явно (модель знает, что ключ можно опустить).
    /// </summary>
    public TypeNode MapWrapperType(string wrapper, IrType inner)
        => GenericType(wrapper, MapType(inner));

    /// <summary>Maps an unsupported typed union to text and records the missing structured-value semantics.</summary>
    /// <param name="union">The union schema contract.</param>
    /// <returns>The fallback text type.</returns>
    public TypeNode MapUnionType(IrUnionType union)
    {
        AddWarning($"union type maps to text ({union.Types.Count} variants); structured JSON preservation and variant validation require a dedicated codec");
        return SimpleType("std::string");
    }

    /// <summary>Maps an unsupported typed intersection to text and reports its semantic limitation.</summary>
    /// <param name="intersection">The intersection schema contract.</param>
    /// <returns>The fallback text type.</returns>
    public TypeNode MapIntersectionType(IrIntersectionType intersection)
    {
        AddWarning($"intersection type maps to text ({intersection.Types.Count} members); structured JSON preservation requires a dedicated codec");
        return SimpleType("std::string");
    }

    /// <summary>Provides explicit scalar initializers while leaving value initialization to the model builder.</summary>
    /// <param name="irType">The field's scalar or wrapped type.</param>
    /// <returns>The initializer expression, or null when the builder should use value initialization.</returns>
    public string? GetDefaultValue(IrType irType)
    {
        if (IsNativeStringValue(irType)) return "{}";
        return irType switch
        {
            IrPrimitiveType p => p.PrimitiveKind switch
            {
                IrPrimitiveKind.Number => "0.0",
                IrPrimitiveKind.Integer or IrPrimitiveKind.Long => "0",
                IrPrimitiveKind.Boolean => "false",
                IrPrimitiveKind.Null => "nullptr",
                _ => null,
            },
            IrNullableType n => GetDefaultValue(n.InnerType),
            IrOptionalType o => GetDefaultValue(o.InnerType),
            _ => null,
        };
    }

    /// <summary>
    /// Formats a value for a query, header, path, or form field using its typed IR contract.
    /// </summary>
    /// <param name="irType">The value's schema type.</param>
    /// <param name="expression">The generated value expression.</param>
    /// <returns>A C++ expression yielding the wire text.</returns>
    public string FormatQueryLiteral(IrType irType, string expression)
    {
        if (IsNativeStringValue(irType))
            return $"ValIO::ToWireText({expression}, ValIO::JsonStringFormat::{StdJsonWireFormat.Name(irType)})";
        return irType switch
        {
            IrPrimitiveType p => p.PrimitiveKind switch
            {
                IrPrimitiveKind.Boolean => $"({expression} ? \"true\" : \"false\")",
                IrPrimitiveKind.Integer or IrPrimitiveKind.Long or IrPrimitiveKind.Number => $"std::to_string({expression})",
                _ => expression,
            },
            IrModelReference modelRef when IsEnumReference(modelRef.ModelName)
                => $"ToWireText({expression})",
            IrModelReference
                => $"ValIO::Dump({expression})", // модель в query-параметре — JSON-строка (сериализатор включён в клиентском .cpp)
            IrNullableType n => FormatQueryLiteral(n.InnerType, expression),
            IrOptionalType o => FormatQueryLiteral(o.InnerType, expression),
            _ => expression,
        };
    }

    /// <summary>Checks whether a wire string has an explicitly selected native application representation.</summary>
    /// <param name="type">The schema type, optionally wrapped in presence metadata.</param>
    /// <returns>Whether textual boundaries require the selected provider's checked formatter.</returns>
    public bool IsNativeStringValue(IrType type) => type switch
    {
        IrNullableType nullable => IsNativeStringValue(nullable.InnerType),
        IrOptionalType optional => IsNativeStringValue(optional.InnerType),
        IrPrimitiveType primitive => (_options.UuidBackend != CppUuidBackend.String && primitive.PrimitiveKind == IrPrimitiveKind.Uuid)
            || (_options.TemporalBackend != CppTemporalBackend.String && TemporalTypeName(primitive) != null),
        _ => false,
    };

    /// <summary>Collects only pure provider headers needed by a type and its nested container values.</summary>
    /// <param name="type">The schema type whose application representation is being declared.</param>
    /// <returns>Deduplicated provider header paths without JSON or HTTP imports.</returns>
    public IReadOnlyList<string> ProviderIncludesFor(IrType type)
    {
        var includes = new SortedSet<string>(StringComparer.Ordinal);
        CollectProviderIncludes(type, includes);
        return [.. includes];
    }

    /// <summary>Visits nested schema wrappers and collects their explicitly selected application provider declarations.</summary>
    /// <param name="type">The schema type to inspect.</param>
    /// <param name="includes">The canonical output header paths.</param>
    private void CollectProviderIncludes(IrType type, ISet<string> includes)
    {
        switch (type)
        {
            case IrPrimitiveType primitive:
                if (_options.UuidBackend != CppUuidBackend.String && primitive.PrimitiveKind == IrPrimitiveKind.Uuid)
                    includes.Add(UuidRuntimeTemplates.TypesHeaderFileName);
                if (_options.TemporalBackend != CppTemporalBackend.String && TemporalTypeName(primitive) != null)
                    includes.Add(TemporalRuntimeTemplates.TypesHeaderFileName);
                break;
            case IrNullableType nullable: CollectProviderIncludes(nullable.InnerType, includes); break;
            case IrOptionalType optional: CollectProviderIncludes(optional.InnerType, includes); break;
            case IrArrayType array: CollectProviderIncludes(array.ElementType, includes); break;
            case IrDictionaryType dictionary: CollectProviderIncludes(dictionary.ValueType, includes); break;
        }
    }

    /// <summary>Classifies normalized schema temporal metadata without inspecting rendered C++ type names.</summary>
    /// <param name="primitive">The primitive schema contract.</param>
    /// <returns>The selected application type name, or no temporal representation.</returns>
    private static string? TemporalTypeName(IrPrimitiveType primitive) => primitive.PrimitiveKind switch
    {
        IrPrimitiveKind.Date => "ValIO::Date",
        IrPrimitiveKind.Time => "ValIO::Time",
        IrPrimitiveKind.DateTime => "ValIO::DateTime",
        IrPrimitiveKind.String when primitive.Format is "duration" or "time-span" or "timespan" => "ValIO::Duration",
        _ => null,
    };

    /// <summary>
    /// Стандартные include, необходимые модели с данным типом поля (без учёта ссылок
    /// на другие модели — их добавляет CppTypeReferenceCollector).
    /// </summary>
    public static IReadOnlyList<string> StdIncludesFor(IrType irType)
    {
        var includes = new SortedSet<string>(StringComparer.Ordinal);
        CollectStdIncludes(irType, includes);
        return [.. includes];
    }

    /// <summary>Collects standard-library headers required by the complete nested type.</summary>
    /// <param name="irType">The type whose headers are collected.</param>
    /// <param name="includes">The destination set preserving deterministic header order.</param>
    private static void CollectStdIncludes(IrType irType, ISet<string> includes)
    {
        switch (irType)
        {
            case IrPrimitiveType p:
                switch (p.PrimitiveKind)
                {
                    case IrPrimitiveKind.String or IrPrimitiveKind.DateTime or IrPrimitiveKind.Date
                        or IrPrimitiveKind.Time or IrPrimitiveKind.Uuid or IrPrimitiveKind.Any:
                        includes.Add("<string>");
                        break;
                    case IrPrimitiveKind.Integer or IrPrimitiveKind.Long:
                        includes.Add("<cstdint>");
                        break;
                    case IrPrimitiveKind.Null:
                        includes.Add("<cstddef>");
                        break;
                    case IrPrimitiveKind.Binary or IrPrimitiveKind.ByteArray or IrPrimitiveKind.File:
                        includes.Add("<vector>");
                        includes.Add("<cstdint>");
                        break;
                }
                break;
            case IrArrayType:
                includes.Add("<vector>");
                CollectStdIncludes(((IrArrayType)irType).ElementType, includes);
                break;
            case IrDictionaryType dict:
                includes.Add("<unordered_map>");
                CollectStdIncludes(dict.KeyType, includes);
                CollectStdIncludes(dict.ValueType, includes);
                break;
            case IrNullableType nullable:
                includes.Add("<optional>");
                CollectStdIncludes(nullable.InnerType, includes);
                break;
            case IrOptionalType optional:
                includes.Add("<optional>");
                CollectStdIncludes(optional.InnerType, includes);
                break;
            case IrUnionType or IrIntersectionType:
                includes.Add("<string>");
                break;
            // IrModelReference — собственный хедер модели, std-инклуды не нужны.
        }
    }

    /// <summary>Файловый/бинарный тип (multipart-части) — общая реализация Core.</summary>
    public static bool IsFileOrBinaryType(IrType irType) => IrTypeQueries.IsFileOrBinary(irType);

    /// <summary>Records when an unconstrained JSON value is reduced to the current text fallback.</summary>
    /// <param name="irType">The original schema type.</param>
    /// <param name="kind">The schema kind used in the diagnostic.</param>
    /// <returns>The fallback text type.</returns>
    private TypeNode WarnedString(IrType irType, string kind)
    {
        AddWarning($"{kind} fields map to text; arbitrary structured JSON preservation requires a dedicated codec");
        return SimpleType("std::string");
    }

    private void AddWarning(string message) => Warnings.Add(message);

    private static SimpleTypeNode SimpleType(string name, bool isPrimitive = false)
        => new(new IdentifierNode(name), isPrimitive: isPrimitive);

    private static GenericTypeNode GenericType(string name, params TypeNode[] typeArgs)
        => new(new IdentifierNode(name), typeArgs);
}
