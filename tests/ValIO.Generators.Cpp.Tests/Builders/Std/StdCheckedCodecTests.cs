using FluentAssertions;
using NUnit.Framework;
using ValIO.Core.IR;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Generators.Cpp.Ast.Builders.Std;
using ValIO.Generators.Cpp.Ast.Renderers;
using ValIO.Generators.Cpp.Models;
using ValIO.Generators.Cpp.Services;

namespace ValIO.Generators.Cpp.Tests.Builders.Std;

/// <summary>Checks that the generated JSON boundary retains schema wire formats and checked scalar decoding.</summary>
[TestFixture]
public sealed class StdCheckedCodecTests
{
    /// <summary>Checks all wrapper kinds preserve the declared leaf format instead of losing it in a rendered type.</summary>
    /// <param name="kind">The schema string or binary kind.</param>
    /// <param name="format">The expected runtime wire-format member.</param>
    [TestCase(IrPrimitiveKind.Date, "Date")]
    [TestCase(IrPrimitiveKind.Time, "Time")]
    [TestCase(IrPrimitiveKind.DateTime, "DateTime")]
    [TestCase(IrPrimitiveKind.Uuid, "Uuid")]
    [TestCase(IrPrimitiveKind.ByteArray, "Base64")]
    [TestCase(IrPrimitiveKind.Binary, "Binary")]
    [TestCase(IrPrimitiveKind.String, "Plain")]
    public void Format_TraversesNestedNullableOptionalDictionaryAndArray(IrPrimitiveKind kind, string format)
    {
        IrType type = new IrOptionalType
        {
            InnerType = new IrNullableType
            {
                InnerType = new IrDictionaryType
                {
                    KeyType = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.String },
                    ValueType = new IrArrayType { ElementType = new IrNullableType { InnerType = new IrPrimitiveType { PrimitiveKind = kind } } },
                },
            },
        };

        var expression = StdJsonWireFormat.BuildArgument(type);

        new CppAstRenderer().Render(expression).Should().Be($"ValIO::JsonStringFormat::{format}");
    }

    /// <summary>Checks model references defer leaf-format selection to each model's own property codecs.</summary>
    [Test]
    public void Format_ModelReferenceUsesItsOwnPropertyCodecs()
    {
        var type = new IrArrayType { ElementType = new IrModelReference { ModelName = "Date" } };
        var expression = StdJsonWireFormat.BuildArgument(type);
        new CppAstRenderer().Render(expression).Should().Be("ValIO::JsonStringFormat::Plain");
    }

    /// <summary>Checks required and optional fields both use the checked codec, including explicit nullable containers.</summary>
    [Test]
    public void Model_CodecChecksNestedTypesAndRetainsDoxygenBoundaryContract()
    {
        var model = new IrModel
        {
            Name = "Probe",
            Properties =
            [
                new IrProperty { Name = "Count", OriginalName = "count", IsRequired = true, Type = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.Integer } },
                new IrProperty
                {
                    Name = "Dates", OriginalName = "dates", IsRequired = false,
                    Type = new IrNullableType { InnerType = new IrArrayType { ElementType = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.Date } } },
                },
            ],
        };
        var context = Context([model]);
        var naming = new StdNamingConvention();
        var builder = new StdModelJsonAstBuilder(new StdCppTypeMapper(naming, context), naming, context);
        var renderer = new CppAstRenderer();
        var (header, source) = builder.Build(model, "Api", new CppGenerationOptions());
        var content = renderer.Render(source);

        content.Should().Contain("value.Count = ValIO::ReadJson<std::int32_t>(j.at(\"count\"), ValIO::JsonStringFormat::Plain)");
        content.Should().Contain("ValIO::ReadJson<std::optional<std::vector<std::string>>>(j.at(\"dates\"), ValIO::JsonStringFormat::Date)");
        content.Should().Contain("ValIO::WriteJson(*value.Dates, ValIO::JsonStringFormat::Date)");
        content.Should().NotContain(".get_to(value.");
        renderer.Render(header).Should().Contain("@brief Parses and validates").And.Contain("@throws nlohmann::json::exception");
    }

    /// <summary>Checks enum parsing cannot silently narrow fractional or out-of-range backing values.</summary>
    [Test]
    public void Enum_UsesCheckedWireParserAndRejectsInvalidSerialization()
    {
        var irEnum = new IrEnum
        {
            Name = "Level", BaseType = IrEnumBaseType.Integer,
            Values = [new IrEnumValue { Name = "High", Value = 2 }],
        };
        var builder = new StdEnumAstBuilder(new StdNamingConvention());
        var renderer = new CppAstRenderer();
        var (header, source) = builder.BuildJson(irEnum, "Api", new CppGenerationOptions());
        var content = renderer.Render(source);

        content.Should().Contain("LevelFromWire(ValIO::ReadJson<std::int32_t>(j))");
        content.Should().Contain("unknown Level enum member");
        content.Should().NotContain("return 0;");
        renderer.Render(header).Should().Contain("@param w").And.Contain("@return The corresponding enum member");
    }

    /// <summary>Creates a shared document context for builder contract tests.</summary>
    /// <param name="models">The models available to resolve.</param>
    /// <returns>The immutable declaration indexes.</returns>
    private static IrDocumentContext Context(IReadOnlyList<IrModel> models) => new(new IrDocument
    {
        Metadata = new IrDocumentMetadata(), Models = models, Enums = [], Clients = [],
        TypeDependencies = new IrTypeDependencyGraph { Dependencies = new Dictionary<string, IReadOnlyList<string>>(), TopologicalOrder = [] },
    });
}
