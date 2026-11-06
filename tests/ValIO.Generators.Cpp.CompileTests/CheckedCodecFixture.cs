using ValIO.Core.IR;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;

namespace ValIO.Generators.Cpp.CompileTests;

/// <summary>Supplies typed native codec fixtures covering leaf formats through nested container wrappers.</summary>
internal static class CheckedCodecFixture
{
    /// <summary>Builds the model whose generated codecs are exercised by the native validation smoke.</summary>
    /// <returns>The standard-library DTO contract for checked scalar and collection decoding.</returns>
    public static IrModel CreateModel() => new()
    {
        Name = "CodecFormats",
        Properties =
        [
            Property("Date", "date", Primitive(IrPrimitiveKind.Date)),
            Property("Time", "time", Primitive(IrPrimitiveKind.Time)),
            Property("Timestamp", "date_time", Primitive(IrPrimitiveKind.DateTime)),
            Property("Uuid", "uuid", Primitive(IrPrimitiveKind.Uuid)),
            Property("Bytes", "bytes", Primitive(IrPrimitiveKind.ByteArray)),
            Property("Count", "count", Primitive(IrPrimitiveKind.Integer)),
            Property("Number", "number", Primitive(IrPrimitiveKind.Number)),
            Property("Enabled", "enabled", Primitive(IrPrimitiveKind.Boolean)),
            Property("Dates", "dates", new IrArrayType { ElementType = new IrNullableType { InnerType = Primitive(IrPrimitiveKind.Date) } }),
            Property("Dictionary", "dictionary", new IrDictionaryType { KeyType = Primitive(IrPrimitiveKind.String), ValueType = Primitive(IrPrimitiveKind.Integer) }),
            Property("Orders", "orders", new IrArrayType { ElementType = new IrModelReference { ModelName = "Order" } }),
            Property("Levels", "levels", new IrArrayType { ElementType = new IrModelReference { ModelName = "Level" } }),
            Property("OptionalNullableDate", "optional_date", new IrNullableType { InnerType = Primitive(IrPrimitiveKind.Date) }, required: false),
            Property("NullValue", "null_value", Primitive(IrPrimitiveKind.Null)),
            Property("OptionalNull", "optional_null", Primitive(IrPrimitiveKind.Null), required: false),
        ],
    };

    /// <summary>Creates a schema leaf without relying on a rendered C++ type string.</summary>
    /// <param name="kind">The scalar schema kind.</param>
    /// <returns>The scalar IR type.</returns>
    private static IrPrimitiveType Primitive(IrPrimitiveKind kind) => new() { PrimitiveKind = kind };

    /// <summary>Creates a named schema property with explicit optional-presence semantics.</summary>
    /// <param name="name">The generated field identifier.</param>
    /// <param name="wire">The original JSON key.</param>
    /// <param name="type">The schema type.</param>
    /// <param name="required">Whether omission is forbidden.</param>
    /// <returns>The property contract.</returns>
    private static IrProperty Property(string name, string wire, IrType type, bool required = true)
        => new() { Name = name, OriginalName = wire, Type = type, IsRequired = required };
}
