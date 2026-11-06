using FluentAssertions;
using NUnit.Framework;
using ValIO.Core.IR;
using ValIO.Core.IR.Types;
using ValIO.Core.Tests.TestHelpers;
using ValIO.Tests.Fixtures;

namespace ValIO.Core.Tests.IR;

/// <summary>Guards composition normalization, including nullable references wrapped by single-element allOf.</summary>
[TestFixture]
public class AllOfNormalizationTests
{
    /// <summary>Reads a complete fixture and returns its Holder.value property through the production IR builder.</summary>
    /// <param name="fixtureName">The shared legacy composition document name without its extension.</param>
    /// <returns>The translated value property.</returns>
    private static async Task<IrProperty> PropAsync(string fixtureName)
    {
        var ir = await IrTestHarness.BuildIrAsync(OpenApiFixture.Read($"Inheritance/Legacy/{fixtureName}.json"));
        return ir.Models.Single(model => model.Name == "Holder").Properties
            .Single(property => property.OriginalName == "value");
    }

    /// <summary>Reads the value type from an explicit composition fixture.</summary>
    /// <param name="fixtureName">The shared legacy composition document name without its extension.</param>
    /// <returns>The translated value type.</returns>
    private static async Task<IrType> PropTypeAsync(string fixtureName)
        => (await PropAsync(fixtureName)).Type;

    /// <summary>Collapses a single reference intersection to the referenced model.</summary>
    /// <returns>The asynchronous assertion operation.</returns>
    [Test]
    public async Task SingleElementAllOf_OfRef_CollapsesToModelReference()
    {
        var type = await PropTypeAsync("single-allOf");
        type.Should().BeOfType<IrModelReference>().Which.ModelName.Should().Be("Base");
    }

    /// <summary>Preserves the nullable sibling when collapsing a single-reference intersection.</summary>
    /// <returns>The asynchronous assertion operation.</returns>
    [Test]
    public async Task SingleElementAllOf_WithNullable_BecomesNullableModelReference()
    {
        var type = await PropTypeAsync("single-allOf-nullable");
        type.Should().BeOfType<IrNullableType>()
            .Which.InnerType.Should().BeOfType<IrModelReference>()
            .Which.ModelName.Should().Be("Base");
    }

    /// <summary>Preserves property documentation when eliminating a degenerate intersection wrapper.</summary>
    /// <returns>The asynchronous assertion operation.</returns>
    [Test]
    public async Task SingleElementAllOf_WithDescription_CollapsesAndKeepsDescription()
    {
        var property = await PropAsync("single-allOf-description");
        property.Type.Should().BeOfType<IrModelReference>().Which.ModelName.Should().Be("Base");
        property.Description.Should().Be("Wrapped");
    }

    /// <summary>Handles an empty allOf without creating an invalid empty intersection.</summary>
    /// <returns>The asynchronous assertion operation.</returns>
    [Test]
    public async Task EmptyAllOf_DoesNotThrow_AndIsNotIntersection()
    {
        var action = async () => await PropTypeAsync("empty-allOf");
        await action.Should().NotThrowAsync();
        (await action()).Should().NotBeOfType<IrIntersectionType>();
    }

    /// <summary>Keeps a genuine two-reference allOf as an intersection.</summary>
    /// <returns>The asynchronous assertion operation.</returns>
    [Test]
    public async Task TwoRefAllOf_StaysIntersection_CountTwo()
    {
        var type = await PropTypeAsync("two-ref-allOf");
        type.Should().BeOfType<IrIntersectionType>().Which.Types.Should().HaveCount(2);
    }

    /// <summary>Collapses a single oneOf alternative to its reference.</summary>
    /// <returns>The asynchronous assertion operation.</returns>
    [Test]
    public async Task SingleElementOneOf_CollapsesToInner()
    {
        var type = await PropTypeAsync("single-oneOf");
        type.Should().BeOfType<IrModelReference>().Which.ModelName.Should().Be("A");
    }

    /// <summary>Collapses a single anyOf alternative to its reference.</summary>
    /// <returns>The asynchronous assertion operation.</returns>
    [Test]
    public async Task SingleElementAnyOf_CollapsesToInner()
    {
        var type = await PropTypeAsync("single-anyOf");
        type.Should().BeOfType<IrModelReference>().Which.ModelName.Should().Be("A");
    }

    /// <summary>Preserves a nullable sibling when normalizing a single oneOf alternative.</summary>
    /// <returns>The asynchronous assertion operation.</returns>
    [Test]
    public async Task SingleElementOneOf_WithNullable_BecomesNullable()
    {
        var type = await PropTypeAsync("single-oneOf-nullable");
        type.Should().BeOfType<IrNullableType>()
            .Which.InnerType.Should().BeOfType<IrModelReference>()
            .Which.ModelName.Should().Be("A");
    }

    /// <summary>Keeps multiple oneOf alternatives as a union.</summary>
    /// <returns>The asynchronous assertion operation.</returns>
    [Test]
    public async Task MultiArmOneOf_StaysUnion_CountTwo()
    {
        var type = await PropTypeAsync("multi-oneOf");
        type.Should().BeOfType<IrUnionType>().Which.Types.Should().HaveCount(2);
    }

    /// <summary>Eliminates nested one-element intersection wrappers recursively.</summary>
    /// <returns>The asynchronous assertion operation.</returns>
    [Test]
    public async Task NestedSingleElementAllOf_CollapsesFully()
    {
        var type = await PropTypeAsync("nested-single-allOf");
        type.Should().BeOfType<IrModelReference>().Which.ModelName.Should().Be("Base");
    }

    /// <summary>Applies nullability once even when composition normalization also processes the schema.</summary>
    /// <returns>The asynchronous assertion operation.</returns>
    [Test]
    public async Task SingleElementAllOf_Nullable_IsNotDoubleWrapped()
    {
        var type = await PropTypeAsync("single-allOf-nullable");
        var nullable = type.Should().BeOfType<IrNullableType>().Subject;
        nullable.InnerType.Should().NotBeOfType<IrNullableType>();
        nullable.InnerType.Should().BeOfType<IrModelReference>();
    }
}
