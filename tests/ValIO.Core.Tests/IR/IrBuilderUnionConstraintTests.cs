using FluentAssertions;
using NUnit.Framework;
using ValIO.Core.IR;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Core.Tests.TestHelpers;
using ValIO.Tests.Fixtures;

namespace ValIO.Core.Tests.IR;

/// <summary>Ensures alternative schemas retain the predicates required to count matching oneOf branches.</summary>
[TestFixture]
public sealed class IrBuilderUnionConstraintTests
{
    /// <summary>Ensures collapsing a single alternative does not overwrite its pattern with a sibling pattern.</summary>
    /// <returns>The asynchronous assertion operation.</returns>
    [Test]
    public async Task CollapsedAnyOfArm_WithSiblingPattern_PreservesBothPredicates()
    {
        var property = await BuildPropertyAsync("collapsed-anyof-patterns.json");

        var intersection = property.Type.Should().BeOfType<IrIntersectionType>().Which;
        intersection.Types.Should().HaveCount(2);
        intersection.Types.Select(type => type.Constraints!.Pattern).Should().Equal("^a", "z$");
    }

    /// <summary>Ensures a collapsed allOf member and its enclosing numeric bound are both retained.</summary>
    /// <returns>The asynchronous assertion operation.</returns>
    [Test]
    public async Task CollapsedAllOfArm_WithSiblingMaximum_PreservesBothBounds()
    {
        var property = await BuildPropertyAsync("collapsed-allof-bounds.json");

        var intersection = property.Type.Should().BeOfType<IrIntersectionType>().Which;
        intersection.Types.Should().HaveCount(2);
        intersection.Types[0].Constraints!.Min.Should().Be(0m);
        intersection.Types[1].Constraints!.Max.Should().Be(10m);
    }

    /// <summary>Ensures flattening an inclusive parent does not discard constraints on a nested inclusive group.</summary>
    /// <returns>The asynchronous assertion operation.</returns>
    [Test]
    public async Task AnyOf_NestedConstrainedAnyOf_RetainsItsBoundaryAndPredicates()
    {
        var property = await BuildPropertyAsync("anyof-nested-constrained-anyof.json");

        var outer = property.Type.Should().BeOfType<IrUnionType>().Which;
        outer.Types.Should().HaveCount(2);
        var inner = outer.Types[0].Should().BeOfType<IrUnionType>().Which;
        inner.Kind.Should().Be(IrUnionKind.AnyOf);
        inner.Constraints!.Pattern.Should().Be("^a");
        inner.Types.Should().HaveCount(2);
    }

    /// <summary>Ensures structurally identical string alternatives retain distinct pattern and length predicates.</summary>
    /// <returns>The asynchronous assertion operation.</returns>
    [Test]
    public async Task OneOf_StringAlternatives_RetainDistinctValidationPredicates()
    {
        var property = await BuildPropertyAsync("oneof-string-constraints.json");

        var union = property.Type.Should().BeOfType<IrUnionType>().Which;
        union.Kind.Should().Be(IrUnionKind.OneOf);
        union.Types.Should().HaveCount(2);
        union.Types[0].Constraints!.Pattern.Should().Be("^a");
        union.Types[0].Constraints!.MinLength.Should().Be(2);
        union.Types[1].Constraints!.Pattern.Should().Be("^b");
        union.Types[1].Constraints!.MaxLength.Should().Be(8);
        property.Constraints.Should().BeNull();
    }

    /// <summary>Ensures inclusive and exclusive numeric bounds are attached to their respective alternatives.</summary>
    /// <returns>The asynchronous assertion operation.</returns>
    [Test]
    public async Task OneOf_NumericAlternatives_RetainTheirIndividualBounds()
    {
        var property = await BuildPropertyAsync("oneof-number-constraints.json");

        var union = property.Type.Should().BeOfType<IrUnionType>().Which;
        union.Types[0].Constraints!.Min.Should().Be(0m);
        union.Types[0].Constraints!.Max.Should().Be(10m);
        union.Types[1].Constraints!.ExclusiveMin.Should().Be(10m);
        union.Types[1].Constraints!.ExclusiveMax.Should().Be(20m);
    }

    /// <summary>Ensures anyOf normalization does not collapse alternatives with distinct acceptance predicates.</summary>
    /// <returns>The asynchronous assertion operation.</returns>
    [Test]
    public async Task AnyOf_StringAlternativesWithDifferentPatterns_AreNotDeduplicated()
    {
        var property = await BuildPropertyAsync("anyof-string-constraints.json");

        var union = property.Type.Should().BeOfType<IrUnionType>().Which;
        union.Kind.Should().Be(IrUnionKind.AnyOf);
        union.Types.Should().HaveCount(2);
        union.Types.Select(type => type.Constraints!.Pattern).Should().Equal("^a", "^b");
    }

    /// <summary>Ensures nullable alternatives preserve their own predicates without losing their null acceptance.</summary>
    /// <returns>The asynchronous assertion operation.</returns>
    [Test]
    public async Task OneOf_NullableConstrainedArm_RetainsItsMetadataAndNullBranch()
    {
        var property = await BuildPropertyAsync("oneof-nullable-constrained-arm.json");

        var union = property.Type.Should().BeOfType<IrUnionType>().Which;
        var nullable = union.Types[0].Should().BeOfType<IrNullableType>().Which;
        nullable.Constraints!.Pattern.Should().Be("^a");
        nullable.InnerType.Should().BeOfType<IrPrimitiveType>().Which.PrimitiveKind.Should().Be(IrPrimitiveKind.String);
        union.Types[1].Should().BeOfType<IrPrimitiveType>().Which.PrimitiveKind.Should().Be(IrPrimitiveKind.Null);
    }

    /// <summary>Ensures schema defaults remain initialization metadata rather than alternative match transformations.</summary>
    /// <returns>The asynchronous assertion operation.</returns>
    [Test]
    public async Task TypeValidationMetadata_DoesNotDuplicatePropertyOrAlternativeDefaults()
    {
        var property = await BuildPropertyAsync("oneof-defaults.json");

        property.Constraints!.DefaultValue.Should().NotBeNull();
        var union = property.Type.Should().BeOfType<IrUnionType>().Which;
        union.Constraints.Should().BeNull();
        union.Types.Should().OnlyContain(type => type.Constraints != null && type.Constraints.DefaultValue == null);
    }

    /// <summary>Builds a property from a standalone OpenAPI JSON fixture through the production IR translation.</summary>
    /// <param name="fixtureName">The JSON fixture filename under Fixtures/OpenApi/CoreIR.</param>
    /// <returns>The translated property.</returns>
    private static async Task<IrProperty> BuildPropertyAsync(string fixtureName)
    {
        var ir = await IrTestHarness.BuildIrAsync(OpenApiFixture.Read("CoreIR/" + fixtureName));
        return ir.Models.Single(model => model.Name == "ConstraintContainer").Properties.Single();
    }
}
