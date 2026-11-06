using ValIO.Core.IR;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;

namespace ValIO.Generators.Cpp.CompileTests;

/// <summary>Provides one temporal contract shared by every actual native provider acceptance configuration.</summary>
internal static class TemporalProviderFixture
{
    /// <summary>Creates temporal leaves, intervals, and nested optional/nullable collections.</summary>
    /// <returns>The scalar provider regression model.</returns>
    public static IrModel CreateModel() => new()
    {
        Name = "TemporalProbe",
        Properties =
        [
            Property("DateValue", "date", Primitive(IrPrimitiveKind.Date, "date")),
            Property("TimeValue", "time", Primitive(IrPrimitiveKind.Time, "time")),
            Property("Timestamp", "timestamp", Primitive(IrPrimitiveKind.DateTime, "date-time")),
            Property("Interval", "interval", Primitive(IrPrimitiveKind.String, "duration")),
            Property("TimeSpan", "time_span", Primitive(IrPrimitiveKind.String, "time-span")),
            Property("OptionalDate", "optional_date", new IrNullableType { InnerType = Primitive(IrPrimitiveKind.Date, "date") }, false),
            Property("Dates", "dates", new IrArrayType { ElementType = new IrNullableType { InnerType = Primitive(IrPrimitiveKind.Date, "date") } }),
            Property("Times", "times", new IrDictionaryType { KeyType = Primitive(IrPrimitiveKind.String), ValueType = Primitive(IrPrimitiveKind.Time, "time") }),
            Property("OptionalTime", "optional_time", Primitive(IrPrimitiveKind.Time, "time"), false),
        ],
    };

    /// <summary>Creates a scalar leaf retaining its normalized schema format.</summary>
    /// <param name="kind">The semantic primitive kind.</param>
    /// <param name="format">The explicit wire format.</param>
    /// <returns>The formatted IR primitive.</returns>
    private static IrPrimitiveType Primitive(IrPrimitiveKind kind, string? format = null) => new() { PrimitiveKind = kind, Format = format };

    /// <summary>Creates a property with distinct wire naming and required-presence semantics.</summary>
    /// <param name="name">The generated C++ field name.</param>
    /// <param name="wire">The JSON object key.</param>
    /// <param name="type">The formatted property contract.</param>
    /// <param name="required">Whether omission is forbidden.</param>
    /// <returns>The fixture property.</returns>
    private static IrProperty Property(string name, string wire, IrType type, bool required = true)
        => new() { Name = name, OriginalName = wire, Type = type, IsRequired = required };
}
