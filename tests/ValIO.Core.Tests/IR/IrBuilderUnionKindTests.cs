using FluentAssertions;
using NUnit.Framework;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Core.Tests.TestHelpers;
using ValIO.Tests.Fixtures;

namespace ValIO.Core.Tests.IR;

/// <summary>Protects JSON Schema match cardinality while translating oneOf and anyOf into the IR.</summary>
[TestFixture]
public sealed class IrBuilderUnionKindTests
{
    /// <summary>Ensures duplicate oneOf branches remain visible because two matches must reject the input.</summary>
    /// <returns>The asynchronous assertion operation.</returns>
    [Test]
    public async Task OneOf_DuplicateStrings_PreservesBothBranches()
    {
        var type = await BuildPropertyTypeAsync("oneof-duplicate-strings.json");

        var union = type.Should().BeOfType<IrUnionType>().Which;
        union.Kind.Should().Be(IrUnionKind.OneOf);
        union.Types.Should().HaveCount(2);
        union.Types.Should().AllBeOfType<IrPrimitiveType>();
        union.Types.Cast<IrPrimitiveType>().Should().OnlyContain(member => member.PrimitiveKind == IrPrimitiveKind.String);
    }

    /// <summary>Ensures duplicate anyOf alternatives collapse because their match count is irrelevant.</summary>
    /// <returns>The asynchronous assertion operation.</returns>
    [Test]
    public async Task AnyOf_DuplicateStrings_CollapsesToSinglePrimitive()
    {
        var type = await BuildPropertyTypeAsync("anyof-duplicate-strings.json");

        type.Should().BeOfType<IrPrimitiveType>().Which.PrimitiveKind.Should().Be(IrPrimitiveKind.String);
    }

    /// <summary>Ensures an explicit null branch remains distinct from a nullable oneOf branch that already matches null.</summary>
    /// <returns>The asynchronous assertion operation.</returns>
    [Test]
    public async Task OneOf_NullableStringAndNull_PreservesOverlappingNullMatches()
    {
        var type = await BuildPropertyTypeAsync("oneof-nullable-string-and-null.json");

        var union = type.Should().BeOfType<IrUnionType>().Which;
        union.Kind.Should().Be(IrUnionKind.OneOf);
        union.Types.Should().HaveCount(2);
        union.Types[0].Should().BeOfType<IrNullableType>().Which.InnerType
            .Should().BeOfType<IrPrimitiveType>().Which.PrimitiveKind.Should().Be(IrPrimitiveKind.String);
        union.Types[1].Should().BeOfType<IrPrimitiveType>().Which.PrimitiveKind.Should().Be(IrPrimitiveKind.Null);
    }

    /// <summary>Ensures duplicate pure-null oneOf alternatives are retained as an impossible exact-one match.</summary>
    /// <returns>The asynchronous assertion operation.</returns>
    [Test]
    public async Task OneOf_DuplicateNullBranches_PreservesBothBranches()
    {
        var type = await BuildPropertyTypeAsync("oneof-duplicate-nulls.json");

        var union = type.Should().BeOfType<IrUnionType>().Which;
        union.Kind.Should().Be(IrUnionKind.OneOf);
        union.Types.Should().HaveCount(2);
        union.Types.Should().AllBeOfType<IrPrimitiveType>();
        union.Types.Cast<IrPrimitiveType>().Should().OnlyContain(member => member.PrimitiveKind == IrPrimitiveKind.Null);
    }

    /// <summary>Ensures an anyOf parent cannot erase an exact-one validation boundary in a nested oneOf.</summary>
    /// <returns>The asynchronous assertion operation.</returns>
    [Test]
    public async Task AnyOf_NestedOneOf_PreservesTheNestedExactOneBoundary()
    {
        var type = await BuildPropertyTypeAsync("anyof-nested-oneof.json");

        var outer = type.Should().BeOfType<IrUnionType>().Which;
        outer.Kind.Should().Be(IrUnionKind.AnyOf);
        outer.Types.Should().HaveCount(2);
        var inner = outer.Types[0].Should().BeOfType<IrUnionType>().Which;
        inner.Kind.Should().Be(IrUnionKind.OneOf);
        inner.Types.Should().HaveCount(2);
        outer.Types[1].Should().BeOfType<IrPrimitiveType>().Which.PrimitiveKind.Should().Be(IrPrimitiveKind.Integer);
    }

    /// <summary>Builds a property from a standalone OpenAPI JSON fixture through the production IR translation.</summary>
    /// <param name="fixtureName">The JSON fixture filename under Fixtures/OpenApi/CoreIR.</param>
    /// <returns>The translated property type.</returns>
    private static async Task<IrType> BuildPropertyTypeAsync(string fixtureName)
    {
        var ir = await IrTestHarness.BuildIrAsync(OpenApiFixture.Read("CoreIR/" + fixtureName));
        return ir.Models.Single(model => model.Name == "UnionContainer").Properties.Single().Type;
    }
}
