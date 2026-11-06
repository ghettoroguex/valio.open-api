using FluentAssertions;
using NUnit.Framework;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Core.IR.Utilities;

namespace ValIO.Core.Tests.IR;

/// <summary>Verifies type deduplication preserves presence and compares nested containers structurally.</summary>
[TestFixture]
public class IrCompositionDeduplicationTests
{
    /// <summary>Verifies a nullable alternative cannot disappear due to a preceding nonnullable arm.</summary>
    [Test]
    public void UnionWithNullableArm_PreservesNullInEitherOrder()
    {
        var text = Primitive(IrPrimitiveKind.String);
        var nullable = new IrNullableType { InnerType = text };
        foreach (var members in new IrType[][] { [text, nullable], [nullable, text] })
        {
            var result = IrCompositionNormalizer.NormalizeUnion(members);
            var union = result.Should().BeOfType<IrUnionType>().Subject;
            union.Types.Should().ContainSingle(type => type is IrNullableType);
            union.Types.Should().ContainSingle(type => type is IrPrimitiveType);
        }
    }

    /// <summary>Verifies intersections retain both presence constraints instead of becoming order-dependent.</summary>
    [Test]
    public void IntersectionWithNullableArm_PreservesBothContracts()
    {
        var text = Primitive(IrPrimitiveKind.String);
        var result = IrCompositionNormalizer.NormalizeIntersection([new IrNullableType { InnerType = text }, text]);
        result.Should().BeOfType<IrIntersectionType>().Which.Types.Should().HaveCount(2);
    }

    /// <summary>Verifies separately allocated dictionaries with reordered unions are deduplicated.</summary>
    [Test]
    public void EquivalentNestedDictionaries_CollapseToOneArm()
    {
        var first = Dictionary(new IrUnionType { Types = [Primitive(IrPrimitiveKind.String), Primitive(IrPrimitiveKind.Integer)] });
        var second = Dictionary(new IrUnionType { Types = [Primitive(IrPrimitiveKind.Integer), Primitive(IrPrimitiveKind.String)] });

        IrCompositionNormalizer.NormalizeUnion([first, second]).Should().BeSameAs(first);
    }

    /// <summary>Verifies strict presence comparison remains recursive inside dictionary values.</summary>
    [Test]
    public void NestedOptionalValue_IsNotTheSameAsRequiredValue()
    {
        var required = Dictionary(Primitive(IrPrimitiveKind.String));
        var optional = Dictionary(new IrOptionalType { InnerType = Primitive(IrPrimitiveKind.String) });

        IrTypeComparer.AreSemanticallyEqual(required, optional, ignorePresence: false).Should().BeFalse();
        IrTypeComparer.AreSemanticallyEqual(required, optional).Should().BeTrue();
    }

    /// <summary>Verifies format-specific primitive kinds are not merged.</summary>
    [Test]
    public void FormattedPrimitives_RemainDistinct()
        => IrCompositionNormalizer.NormalizeUnion([Primitive(IrPrimitiveKind.String), Primitive(IrPrimitiveKind.Uuid)])
            .Should().BeOfType<IrUnionType>();

    /// <summary>Verifies alternating presence wrappers unwrap to the underlying binary type.</summary>
    [Test]
    public void AlternatingPresenceWrappers_AreFullyUnwrapped()
    {
        var binary = Primitive(IrPrimitiveKind.Binary);
        var wrapped = new IrOptionalType { InnerType = new IrNullableType { InnerType = new IrOptionalType { InnerType = binary } } };
        IrTypeQueries.UnwrapNullableOptional(wrapped).Should().BeSameAs(binary);
        IrTypeQueries.IsFileOrBinary(wrapped).Should().BeTrue();
    }

    /// <summary>Creates a primitive type without a target-language spelling.</summary>
    /// <param name="kind">The wire value kind.</param>
    /// <returns>The primitive test type.</returns>
    private static IrPrimitiveType Primitive(IrPrimitiveKind kind) => new() { PrimitiveKind = kind };

    /// <summary>Creates a string-keyed dictionary with the supplied value contract.</summary>
    /// <param name="value">The dictionary value type.</param>
    /// <returns>The dictionary test type.</returns>
    private static IrDictionaryType Dictionary(IrType value) => new()
    {
        KeyType = Primitive(IrPrimitiveKind.String),
        ValueType = value
    };
}
