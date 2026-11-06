using FluentAssertions;
using NUnit.Framework;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Core.Tests.TestHelpers;
using ValIO.Tests.Fixtures;

namespace ValIO.Core.Tests.IR;

/// <summary>Exercises disjoint nullable compositions through the production OpenAPI reader and shared IR.</summary>
[TestFixture]
public sealed class IrBuilderNullableUnionTests
{
    /// <summary>Verifies nullable model and enum identity in properties, collections, bodies and responses.</summary>
    /// <returns>The asynchronous assertion operation.</returns>
    [Test]
    public async Task ReferencesRetainIdentityAndPresence()
    {
        var ir = await IrTestHarness.BuildIrAsync(OpenApiFixture.Read("CoreIR/nullable-reference-unions-3.1.json"));
        var model = ir.Models.Single(model => model.Name == "NullableReferences");
        foreach (var name in new[] { "country", "state", "optionalCountry", "optionalState" })
        {
            var property = model.Properties.Single(property => property.Name == name);
            var nullable = property.Type.Should().BeOfType<IrNullableType>().Which;
            nullable.InnerType.Should().BeOfType<IrModelReference>().Which.ModelName
                .Should().Be(name.EndsWith("Country", StringComparison.OrdinalIgnoreCase) ? "NullableCountry" : "NullableState");
            property.IsRequired.Should().Be(!name.StartsWith("optional", StringComparison.Ordinal));
        }
        model.Properties.Single(property => property.Name == "countries").Type.Should().BeOfType<IrArrayType>()
            .Which.ElementType.Should().BeOfType<IrNullableType>().Which.InnerType.Should().BeOfType<IrModelReference>();
        model.Properties.Single(property => property.Name == "states").Type.Should().BeOfType<IrDictionaryType>()
            .Which.ValueType.Should().BeOfType<IrNullableType>().Which.InnerType.Should().BeOfType<IrModelReference>();
        var operation = ir.Clients.Single().Operations.Single();
        operation.RequestBody!.Type.Should().BeOfType<IrNullableType>().Which.InnerType.Should().BeOfType<IrModelReference>();
        operation.Responses.Single().Type.Should().BeOfType<IrNullableType>().Which.InnerType.Should().BeOfType<IrModelReference>();
    }

    /// <summary>Checks order independence and support for OpenAPI 3.0, 3.1 and 3.2 composition readers.</summary>
    /// <param name="version">The source document dialect.</param>
    /// <param name="kind">The source union keyword.</param>
    /// <returns>The asynchronous assertion operation.</returns>
    [TestCase("3.0.4", "oneOf")]
    [TestCase("3.1.2", "oneOf")]
    [TestCase("3.2.0", "oneOf")]
    [TestCase("3.1.2", "anyOf")]
    public async Task DisjointReferencesBecomeNullable(string version, string kind)
    {
        var model = await IrTestHarness.BuildSingleModelAsync("Container",
            """{"type":"object","properties":{"value":{"KIND":[{"type":"null"},{"$ref":"#/components/schemas/Value"}]}}}""".Replace("KIND", kind, StringComparison.Ordinal),
            version, ("Value", """{"type":"object","properties":{"id":{"type":"integer"}}}"""));
        model.Properties.Single().Type.Should().BeOfType<IrNullableType>().Which.InnerType
            .Should().BeOfType<IrModelReference>().Which.ModelName.Should().Be("Value");
    }

    /// <summary>Preserves exclusive null match counts when referenced schemas permit null or cannot prove its rejection.</summary>
    /// <param name="target">The referenced component body.</param>
    /// <returns>The asynchronous assertion operation.</returns>
    [TestCase("{\"type\":[\"object\",\"null\"],\"properties\":{\"id\":{\"type\":\"integer\"}}}")]
    [TestCase("{\"properties\":{\"id\":{\"type\":\"integer\"}}}")]
    [TestCase("{\"anyOf\":[{\"type\":\"string\"},{\"type\":\"null\"}]}")]
    [TestCase("{\"allOf\":[{\"$ref\":\"#/components/schemas/Value\"}]}")]
    public async Task NullableOrUnknownReferencesKeepExclusiveBranches(string target)
    {
        var model = await IrTestHarness.BuildSingleModelAsync("Container",
            """{"type":"object","properties":{"value":{"oneOf":[{"type":"null"},{"$ref":"#/components/schemas/Value"}]}}}""",
            "3.1.2", ("Value", target));
        var union = model.Properties.Single().Type.Should().BeOfType<IrUnionType>().Which;
        union.Kind.Should().Be(IrUnionKind.OneOf);
        union.Types.Should().HaveCount(2);
    }

    /// <summary>Retains duplicate non-null branches and their predicates after extracting a disjoint null branch.</summary>
    /// <returns>The asynchronous assertion operation.</returns>
    [Test]
    public async Task NonNullMatchCountsAndConstraintsAreRetained()
    {
        var model = await IrTestHarness.BuildSingleModelAsync("Container",
            """{"type":"object","properties":{"value":{"oneOf":[{"type":"null"},{"type":"string","pattern":"^a"},{"type":"string","pattern":"^a"}]}}}""", "3.1.2");
        var union = model.Properties.Single().Type.Should().BeOfType<IrNullableType>().Which.InnerType
            .Should().BeOfType<IrUnionType>().Which;
        union.Kind.Should().Be(IrUnionKind.OneOf);
        union.Types.Should().HaveCount(2);
        union.Types.Should().OnlyContain(type => type.Constraints!.Pattern == "^a");
    }

    /// <summary>Prevents a null-only union from widening to an unconstrained nullable value.</summary>
    /// <param name="composition">The null-only composition.</param>
    /// <returns>The asynchronous assertion operation.</returns>
    [TestCase("\"oneOf\":[{\"type\":\"null\"}]")]
    [TestCase("\"anyOf\":[{\"type\":\"null\"},{\"type\":\"null\"}]")]
    public async Task NullOnlyCompositionsStayNullOnly(string composition)
    {
        var model = await IrTestHarness.BuildSingleModelAsync("Container",
            """{"type":"object","properties":{"value":{ COMPOSITION }}}""".Replace("COMPOSITION", composition, StringComparison.Ordinal), "3.1.2");
        model.Properties.Single().Type.Should().BeOfType<IrPrimitiveType>().Which.PrimitiveKind.Should().Be(IrPrimitiveKind.Null);
    }
}
