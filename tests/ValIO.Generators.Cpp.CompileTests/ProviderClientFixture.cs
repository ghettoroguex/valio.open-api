using ValIO.Core.IR;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;

namespace ValIO.Generators.Cpp.CompileTests;

/// <summary>Declares independent scalar operations and separate form collection acceptance contracts.</summary>
internal static class ProviderClientFixture
{
    /// <summary>Creates native UUID and temporal path, query, and header contracts.</summary>
    /// <returns>The independent provider client contract.</returns>
    public static IrClient CreateClient()
    {
        var uuid = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.Uuid };
        var date = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.Date };
        var time = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.Time };
        var timestamp = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.DateTime };
        var duration = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.String, Format = "duration" };
        var timeSpan = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.String, Format = "time-span" };
        return new IrClient
        {
            Name = "ProviderScalars",
            Operations =
            [
                new IrOperation
                {
                    MethodName = "RoundTripValues", HttpMethod = "GET", Path = "/provider/{uuid}/{date}",
                    PathParameters = [Parameter("Uuid", "uuid", uuid), Parameter("Date", "date", date)],
                    QueryParameters =
                    [
                        Parameter("Time", "time", time), Parameter("Timestamp", "timestamp", timestamp),
                        Parameter("Interval", "interval", duration), Parameter("TimeSpan", "time-span", timeSpan),
                    ],
                    HeaderParameters =
                    [
                        Parameter("UuidHeader", "X-UUID", uuid), Parameter("DateHeader", "X-Date", date),
                        Parameter("TimeHeader", "X-Time", time), Parameter("TimestampHeader", "X-Timestamp", timestamp),
                        Parameter("IntervalHeader", "X-Interval", duration), Parameter("TimeSpanHeader", "X-Time-Span", timeSpan),
                    ],
                    Responses = [new IrResponse { StatusCode = "200", Type = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.Boolean } }],
                },
            ],
        };
    }

    /// <summary>Creates flat native, model, and dictionary collection models for the generated form transport.</summary>
    /// <returns>The item declaration followed by its form container.</returns>
    public static IReadOnlyList<IrModel> CreateFormModels()
    {
        var uuid = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.Uuid };
        return
        [
            new IrModel
            {
                Name = "ProviderFormItem",
                Properties = [new IrProperty { Name = "Name", OriginalName = "name", IsRequired = true, Type = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.String } }],
            },
            new IrModel
            {
                Name = "ProviderFormValues", Properties =
                [
                    new IrProperty { Name = "Uuids", OriginalName = "uuids", IsRequired = true, Type = new IrArrayType { ElementType = uuid } },
                    new IrProperty { Name = "Dates", OriginalName = "dates", IsRequired = true, Type = new IrArrayType { ElementType = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.Date } } },
                    new IrProperty { Name = "Items", OriginalName = "items", IsRequired = true, Type = new IrArrayType { ElementType = new IrModelReference { ModelName = "ProviderFormItem" } } },
                    new IrProperty
                    {
                        Name = "Maps", OriginalName = "maps", IsRequired = true,
                        Type = new IrArrayType { ElementType = new IrDictionaryType { KeyType = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.String }, ValueType = uuid } },
                    },
                ],
            },
        ];
    }

    /// <summary>Creates one form POST covering native collection leaf formatting in the body, query string, and headers.</summary>
    /// <returns>The separate client so scalar-only public-header dependency checks remain independent.</returns>
    public static IrClient CreateFormClient()
    {
        var uuids = new IrArrayType { ElementType = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.Uuid } };
        var dates = new IrArrayType { ElementType = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.Date } };
        return new IrClient
        {
            Name = "ProviderForms", Operations =
            [
                new IrOperation
                {
                    MethodName = "PostValues", HttpMethod = "POST", Path = "/provider/form", PathParameters = [],
                    QueryParameters = [Parameter("QueryUuids", "q_uuids", uuids), Parameter("QueryDates", "q_dates", dates)],
                    HeaderParameters = [Parameter("HeaderUuids", "X-Uuids", uuids), Parameter("HeaderDates", "X-Dates", dates)],
                    RequestBody = new IrRequestBody { Type = new IrModelReference { ModelName = "ProviderFormValues" }, IsRequired = true, ContentType = "application/x-www-form-urlencoded" },
                    Responses = [new IrResponse { StatusCode = "200", Type = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.Boolean } }],
                },
            ],
        };
    }

    /// <summary>Creates a required scalar wire argument.</summary>
    /// <param name="name">The generated identifier.</param>
    /// <param name="wireName">The unmodified HTTP field or placeholder name.</param>
    /// <param name="type">The provider-independent scalar contract.</param>
    /// <returns>The operation argument.</returns>
    private static IrParameter Parameter(string name, string wireName, IrType type) => new()
    {
        Name = name, OriginalName = wireName, IsRequired = true, Type = type,
    };
}
