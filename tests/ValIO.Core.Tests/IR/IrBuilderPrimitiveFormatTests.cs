using FluentAssertions;
using NUnit.Framework;
using ValIO.Core.IR;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Core.IR.Utilities;
using ValIO.Core.Tests.TestHelpers;
using ValIO.Tests.Fixtures;

namespace ValIO.Core.Tests.IR;

/// <summary>Protects temporal, custom, and numeric format annotations through scalar IR construction.</summary>
[TestFixture]
public sealed class IrBuilderPrimitiveFormatTests
{
    /// <summary>Retains formats on model fields, containers, operation parameters, requests, and responses.</summary>
    /// <param name="version">The OpenAPI dialect whose explicit format fixture is translated.</param>
    /// <returns>The asynchronous assertion operation.</returns>
    [TestCase("2.0")]
    [TestCase("3.0")]
    [TestCase("3.1")]
    [TestCase("3.2")]
    public async Task PrimitiveFormats_NormalizeKnownNamesAndPreserveCustomContracts(string version)
    {
        var ir = await BuildAsync(version);
        var properties = ir.Models.Single(model => model.Name == "Formats").Properties
            .ToDictionary(property => property.OriginalName);
        var expected = new Dictionary<string, string?>
        {
            ["plain"] = null, ["date"] = "date", ["dateTime"] = "date-time", ["time"] = "time",
            ["duration"] = "duration", ["timeSpan"] = "time-span", ["timespan"] = "timespan",
            ["uuid"] = "uuid", ["wideInteger"] = "int64", ["customUpper"] = "Acme-Token", ["customLower"] = "acme-token",
        };
        foreach (var (name, format) in expected)
            properties[name].Type.Should().BeOfType<IrPrimitiveType>().Which.Format.Should().Be(format);

        properties["duration"].Type.Should().BeOfType<IrPrimitiveType>().Which.PrimitiveKind.Should().Be(IrPrimitiveKind.String);
        properties["time"].Type.Should().BeOfType<IrPrimitiveType>().Which.PrimitiveKind.Should().Be(IrPrimitiveKind.Time);
        properties["wideInteger"].Type.Should().BeOfType<IrPrimitiveType>().Which.PrimitiveKind.Should().Be(IrPrimitiveKind.Long);
        IrTypeComparer.AreSemanticallyEqual(properties["time"].Type, properties["timeEquivalent"].Type, false).Should().BeTrue();
        IrTypeComparer.AreSemanticallyEqual(properties["customUpper"].Type, properties["customLower"].Type, false).Should().BeFalse();

        properties["nullableDuration"].Type.Should().BeOfType<IrNullableType>().Which.InnerType
            .Should().BeOfType<IrPrimitiveType>().Which.Format.Should().Be("duration");
        properties["durationArray"].Type.Should().BeOfType<IrArrayType>().Which.ElementType
            .Should().BeOfType<IrPrimitiveType>().Which.Format.Should().Be("duration");
        properties["nullableTimeValues"].Type.Should().BeOfType<IrDictionaryType>().Which.ValueType
            .Should().BeOfType<IrNullableType>().Which.InnerType.Should().BeOfType<IrPrimitiveType>().Which.Format.Should().Be("time");

        var operation = ir.Clients.SelectMany(client => client.Operations).Single();
        operation.RequestBody!.IsRequired.Should().BeFalse();
        operation.RequestBody.Type.Should().BeOfType<IrPrimitiveType>().Which.Format.Should().Be("duration");
        operation.QueryParameters.Single().Type.Should().BeOfType<IrPrimitiveType>().Which.Format.Should().Be("time");
        operation.Responses.Single().Type.Should().BeOfType<IrPrimitiveType>().Which.Format.Should().Be("time");
    }

    /// <summary>Prevents inclusive normalization from merging distinct temporal or custom format contracts.</summary>
    /// <param name="version">The OpenAPI dialect supporting explicit alternative schemas.</param>
    /// <returns>The asynchronous assertion operation.</returns>
    [TestCase("3.0")]
    [TestCase("3.1")]
    [TestCase("3.2")]
    public async Task CompositionNormalization_DistinguishesFormatContracts(string version)
    {
        var ir = await BuildAsync(version);
        var properties = ir.Models.Single(model => model.Name == "Formats").Properties
            .ToDictionary(property => property.OriginalName);

        var alternatives = properties["choice"].Type.Should().BeOfType<IrUnionType>().Which;
        alternatives.Types.Cast<IrPrimitiveType>().Select(type => type.Format).Should().Equal("duration", "time", null);
        properties["sameKnown"].Type.Should().BeOfType<IrPrimitiveType>().Which.Format.Should().Be("time");
        var custom = properties["customChoice"].Type.Should().BeOfType<IrUnionType>().Which;
        custom.Types.Cast<IrPrimitiveType>().Select(type => type.Format).Should().Equal("Acme-Token", "acme-token");
    }

    /// <summary>Retains formats in nullable type-array branches and the existing numeric string-coercion representation.</summary>
    /// <param name="version">The OpenAPI dialect supporting JSON Schema type arrays.</param>
    /// <returns>The asynchronous assertion operation.</returns>
    [TestCase("3.1")]
    [TestCase("3.2")]
    public async Task NullableTypeArrays_RetainMemberAndCoercionFormats(string version)
    {
        var ir = await BuildAsync(version);
        var properties = ir.Models.Single(model => model.Name == "Formats").Properties
            .ToDictionary(property => property.OriginalName);

        var numeric = properties["numericStringCoercion"].Type.Should().BeOfType<IrNullableType>().Which.InnerType
            .Should().BeOfType<IrPrimitiveType>().Which;
        numeric.PrimitiveKind.Should().Be(IrPrimitiveKind.Long);
        numeric.Format.Should().Be("int64");
        var union = properties["typedUnion"].Type.Should().BeOfType<IrNullableType>().Which.InnerType
            .Should().BeOfType<IrUnionType>().Which;
        union.Types.Cast<IrPrimitiveType>().Select(type => type.Format).Should().Equal("AcmeScalar", "AcmeScalar");
        union.Types.Cast<IrPrimitiveType>().Select(type => type.PrimitiveKind).Should().Equal(IrPrimitiveKind.Integer, IrPrimitiveKind.Boolean);
    }

    /// <summary>Builds a real parser-to-IR document from the requested disk fixture.</summary>
    /// <param name="version">The OpenAPI dialect encoded in the fixture filename.</param>
    /// <returns>The translated document containing model and operation format scenarios.</returns>
    private static Task<IrDocument> BuildAsync(string version) =>
        IrTestHarness.BuildIrAsync(OpenApiFixture.Read($"CoreIR/primitive-formats-{version}.json"));
}
