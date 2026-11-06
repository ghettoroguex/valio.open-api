using FluentAssertions;
using NUnit.Framework;
using ValIO.Core.IR;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Generators.Cpp.Ast.Builders.Std;
using ValIO.Generators.Cpp.Ast.Renderers;
using ValIO.Generators.Cpp.Models;
using ValIO.Generators.Cpp.Services;
using ValIO.Generators.Cpp.Tests.TestHelpers;

namespace ValIO.Generators.Cpp.Tests.Builders.Std;

/// <summary>Checks scalar-only client contracts select native provider declarations without relying on model transitive includes.</summary>
[TestFixture]
public sealed class StdProviderClientTests
{
    /// <summary>Checks model headers own their selected provider declarations even for nested container values.</summary>
    [Test]
    public void NativeModel_DeclaresPureProvidersWithoutJsonOrHttpImports()
    {
        var model = new IrModel
        {
            Name = "NativeValues", Properties =
            [
                new IrProperty
                {
                    Name = "Ids", OriginalName = "ids", IsRequired = true,
                    Type = new IrArrayType { ElementType = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.Uuid } },
                },
                new IrProperty
                {
                    Name = "Dates", OriginalName = "dates", IsRequired = false,
                    Type = new IrDictionaryType
                    {
                        KeyType = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.String },
                        ValueType = new IrNullableType { InnerType = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.Date } },
                    },
                },
            ],
        };
        var context = CppDocumentContextFactory.Create([model]);
        var naming = new StdNamingConvention();
        var options = new CppGenerationOptions { TemporalBackend = CppTemporalBackend.Valio, UuidBackend = CppUuidBackend.BoostUuid };
        var builder = new StdModelAstBuilder(new StdCppTypeMapper(naming, context, options), naming, context);
        var header = new CppAstRenderer().Render(builder.Build(model, "Api"));
        header.Should().Contain("#include \"Runtime/ValIOTemporalTypes.h\"");
        header.Should().Contain("#include \"Runtime/ValIOUuidTypes.h\"");
        header.Should().Contain("std::vector<ValIO::Uuid>");
        header.Should().Contain("std::optional<ValIO::Date>");
        header.Should().NotContain("ValIOJson").And.NotContain("nlohmann/").And.NotContain("ValIOHttp").And.NotContain("httplib");
    }

    /// <summary>Checks native path, query, and header values use shared checked codecs for each supported provider family.</summary>
    /// <param name="temporalBackend">The application temporal provider.</param>
    /// <param name="uuidBackend">The application UUID provider.</param>
    [TestCase(CppTemporalBackend.Chrono, CppUuidBackend.BoostUuid)]
    [TestCase(CppTemporalBackend.HowardDate, CppUuidBackend.StdUuid)]
    [TestCase(CppTemporalBackend.BoostDateTime, CppUuidBackend.ModernUuid)]
    [TestCase(CppTemporalBackend.Valio, CppUuidBackend.BoostUuid)]
    public void NativeScalars_DeclareProvidersAndUseCheckedWireFormatters(CppTemporalBackend temporalBackend, CppUuidBackend uuidBackend)
    {
        var options = new CppGenerationOptions
        {
            TemporalBackend = temporalBackend, UuidBackend = uuidBackend, CxxStandard = 23,
            ClientShape = CppClientShape.InterfaceImpl,
        };
        var (header, contract, source) = Render(options);
        foreach (var declaration in new[] { header, contract })
        {
            declaration.Should().Contain("#include \"Runtime/ValIOTemporalTypes.h\"");
            declaration.Should().Contain("#include \"Runtime/ValIOUuidTypes.h\"");
            declaration.Should().NotContain("ValIOJson.h").And.NotContain("nlohmann/");
            declaration.Should().Contain("ValIO::Uuid Id");
            declaration.Should().Contain("ValIO::Date When");
            declaration.Should().Contain("std::optional<ValIO::Time> At = std::nullopt");
        }
        source.Should().Contain("ValIO::UrlEncode(ValIO::ToWireText(Id, ValIO::JsonStringFormat::Uuid))");
        source.Should().Contain("httpRequest.AddQuery(\"date\", When, [](const auto& value)");
        source.Should().Contain("return ValIO::ToWireText(value, ValIO::JsonStringFormat::Date);");
        source.Should().Contain("httpRequest.AddQuery(\"time\", At, [](const auto& value)");
        source.Should().Contain("return ValIO::ToWireText(value, ValIO::JsonStringFormat::Time);");
        source.Should().Contain("httpRequest.AddHeader(\"X-Interval\", Interval, [](const auto& value)");
        source.Should().Contain("return ValIO::ToWireText(value, ValIO::JsonStringFormat::Duration);");
        source.Should().Contain("httpRequest.AddHeader(\"X-Time-Span\", TimeSpan, [](const auto& value)");
        source.Should().Contain("return ValIO::ToWireText(value, ValIO::JsonStringFormat::TimeSpan);");
        source.Should().NotContain("if (At)");
    }

    /// <summary>Checks default string contracts retain their dependency-free public representation.</summary>
    [Test]
    public void StringScalars_OmitNativeProviderHeadersAndArguments()
    {
        var (header, contract, source) = Render(new CppGenerationOptions { ClientShape = CppClientShape.InterfaceImpl });
        foreach (var declaration in new[] { header, contract })
        {
            declaration.Should().NotContain("ValIOTemporalTypes.h").And.NotContain("ValIOUuidTypes.h");
            declaration.Should().Contain("std::string Id");
            declaration.Should().Contain("std::string When");
        }
        source.Should().Contain("ValIO::UrlEncode(Id)");
        source.Should().Contain("httpRequest.AddQuery(\"date\", When);");
    }

    /// <summary>Checks form collections use explicit native and object leaf formatters rather than unavailable external ADL codecs.</summary>
    [Test]
    public void FormCollections_UseNativeAndModelLeafFormatters()
    {
        var uuid = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.Uuid };
        var dateArray = new IrArrayType { ElementType = new IrNullableType { InnerType = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.Date } } };
        var form = new IrModel
        {
            Name = "FormValues", Properties =
            [
                new IrProperty { Name = "Ids", OriginalName = "ids", IsRequired = true, Type = new IrArrayType { ElementType = uuid } },
                new IrProperty { Name = "Dates", OriginalName = "dates", IsRequired = true, Type = dateArray },
                new IrProperty { Name = "Items", OriginalName = "items", IsRequired = true, Type = new IrArrayType { ElementType = new IrModelReference { ModelName = "Item" } } },
                new IrProperty { Name = "ByName", OriginalName = "by_name", IsRequired = true, Type = new IrDictionaryType { KeyType = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.String }, ValueType = uuid } },
                new IrProperty { Name = "NullableDates", OriginalName = "nullable_dates", IsRequired = true, Type = new IrNullableType { InnerType = dateArray } },
            ],
        };
        var context = CppDocumentContextFactory.Create([form, new IrModel { Name = "Item", Properties = [] }]);
        var naming = new StdNamingConvention();
        var options = new CppGenerationOptions { UuidBackend = CppUuidBackend.BoostUuid, TemporalBackend = CppTemporalBackend.Valio };
        var builder = new StdClientAstBuilder(new StdCppTypeMapper(naming, context, options), naming, context);
        var operation = new IrOperation
        {
            MethodName = "Post", HttpMethod = "POST", Path = "/form", PathParameters = [], QueryParameters = [], HeaderParameters = [],
            RequestBody = new IrRequestBody { Type = new IrModelReference { ModelName = "FormValues" }, IsRequired = true, ContentType = "application/x-www-form-urlencoded" },
            Responses = [new IrResponse { StatusCode = "204" }],
        };
        var source = new CppAstRenderer().Render(builder.Build(new IrClient { Name = "Forms", Operations = [operation] }, "Api", options).ClientSource);
        source.Should().Contain("#include \"Serializers/ItemJson.h\"");
        source.Should().Contain("httpRequest.AddFormField(\"ids\", request.Ids, [](const auto& value)");
        source.Should().Contain("return ValIO::ToWireText(value, ValIO::JsonStringFormat::Uuid);");
        source.Should().Contain("httpRequest.AddFormField(\"dates\", request.Dates, [](const auto& value)");
        source.Should().Contain("return ValIO::ToWireText(value, ValIO::JsonStringFormat::Date);");
        source.Should().Contain("httpRequest.AddFormField(\"items\", request.Items, [](const auto& value)");
        source.Should().Contain("httpRequest.AddFormField(\"by_name\", request.ByName, [](const auto& value)");
        source.Should().Contain("return ValIO::Dump(value, ValIO::JsonStringFormat::Plain);");
        source.Should().Contain("if (request.NullableDates)");
        source.Should().Contain("httpRequest.AddFormField(\"nullable_dates\", \"null\");");
    }

    /// <summary>Renders primitive-only operation declarations and their real implementation.</summary>
    /// <param name="options">The explicitly selected providers and client shape.</param>
    /// <returns>The concrete header, interface header, and implementation.</returns>
    private static (string Header, string Contract, string Source) Render(CppGenerationOptions options)
    {
        var operation = new IrOperation
        {
            MethodName = "Read", HttpMethod = "GET", Path = "/probe/{id}",
            PathParameters = [Parameter("Id", "id", new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.Uuid })],
            QueryParameters =
            [
                Parameter("When", "date", new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.Date }),
                Parameter("At", "time", new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.Time }) with { IsRequired = false },
            ],
            HeaderParameters =
            [
                Parameter("Interval", "X-Interval", new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.String, Format = "duration" }),
                Parameter("TimeSpan", "X-Time-Span", new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.String, Format = "time-span" }),
            ],
            Responses = [new IrResponse { StatusCode = "204" }],
        };
        var context = CppDocumentContextFactory.Create();
        var naming = new StdNamingConvention();
        var builder = new StdClientAstBuilder(new StdCppTypeMapper(naming, context, options), naming, context);
        var result = builder.Build(new IrClient { Name = "Probe", Operations = [operation] }, "Api", options);
        var renderer = new CppAstRenderer();
        return (renderer.Render(result.ClientHeader), renderer.Render(result.InterfaceHeader!), renderer.Render(result.ClientSource));
    }

    /// <summary>Creates a required scalar HTTP field.</summary>
    /// <param name="name">The generated argument identifier.</param>
    /// <param name="wireName">The original HTTP name.</param>
    /// <param name="type">The scalar representation.</param>
    /// <returns>The declared operation argument.</returns>
    private static IrParameter Parameter(string name, string wireName, IrType type) => new()
    {
        Name = name, OriginalName = wireName, Type = type, IsRequired = true,
    };
}
