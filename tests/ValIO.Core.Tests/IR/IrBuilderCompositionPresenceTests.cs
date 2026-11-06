using FluentAssertions;
using NUnit.Framework;
using ValIO.Core.IR;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Core.IR.Utilities;
using ValIO.Core.Tests.TestHelpers;
using ValIO.Tests.Fixtures;

namespace ValIO.Core.Tests.IR;

/// <summary>Separates property presence from null acceptance across compositions and nested containers.</summary>
[TestFixture]
public sealed class IrBuilderCompositionPresenceTests
{
    /// <summary>Checks required and optional properties independently from anyOf, oneOf, and allOf nullability.</summary>
    /// <param name="version">The OpenAPI dialect whose explicit presence fixture is translated.</param>
    /// <returns>The asynchronous assertion operation.</returns>
    [TestCase("3.0")]
    [TestCase("3.1")]
    [TestCase("3.2")]
    public async Task CompositionProperties_KeepPresenceSeparateFromNullAcceptance(string version)
    {
        var model = await BuildMatrixAsync(version);
        foreach (var composition in new[] { "anyOf", "oneOf", "allOf" })
        {
            foreach (var nullable in new[] { false, true })
            {
                var suffix = nullable ? "Nullable" : "Nonnullable";
                var required = model.Properties.Single(property => property.OriginalName == composition + "Required" + suffix);
                var optional = model.Properties.Single(property => property.OriginalName == composition + "Optional" + suffix);

                required.IsRequired.Should().BeTrue();
                optional.IsRequired.Should().BeFalse();
                AllowsJsonNull(required.Type).Should().Be(nullable, $"{required.OriginalName} has explicit null semantics");
                AllowsJsonNull(optional.Type).Should().Be(nullable, $"{optional.OriginalName} is optional independently of null");
                required.Type.Should().NotBeOfType<IrOptionalType>();
                optional.Type.Should().NotBeOfType<IrOptionalType>();
                IrTypeComparer.AreSemanticallyEqual(required.Type, optional.Type, ignorePresence: false).Should().BeTrue();
            }
        }
    }

    /// <summary>Checks nullable containers and nullable nested values without conflating either with property omission.</summary>
    /// <param name="version">The OpenAPI dialect whose explicit presence fixture is translated.</param>
    /// <returns>The asynchronous assertion operation.</returns>
    [TestCase("3.0")]
    [TestCase("3.1")]
    [TestCase("3.2")]
    public async Task NestedContainers_KeepOuterPresenceAndInnerNullabilityIndependent(string version)
    {
        var ir = await IrTestHarness.BuildIrAsync(OpenApiFixture.Read($"CoreIR/composition-presence-{version}.json"));
        var model = ir.Models.Single(candidate => candidate.Name == "PresenceMatrix");
        var requiredNullableArray = model.Properties.Single(property => property.OriginalName == "requiredNullableArray");
        requiredNullableArray.IsRequired.Should().BeTrue();
        var array = requiredNullableArray.Type.Should().BeOfType<IrNullableType>().Which.InnerType
            .Should().BeOfType<IrArrayType>().Which;
        AllowsJsonNull(array.ElementType).Should().BeTrue();

        var optionalArray = model.Properties.Single(property => property.OriginalName == "optionalArrayWithNullableItems");
        optionalArray.IsRequired.Should().BeFalse();
        AllowsJsonNull(optionalArray.Type).Should().BeFalse();
        AllowsJsonNull(optionalArray.Type.Should().BeOfType<IrArrayType>().Which.ElementType).Should().BeTrue();

        var optionalNullableDictionary = model.Properties.Single(property => property.OriginalName == "optionalNullableDictionary");
        optionalNullableDictionary.IsRequired.Should().BeFalse();
        var dictionary = optionalNullableDictionary.Type.Should().BeOfType<IrNullableType>().Which.InnerType
            .Should().BeOfType<IrDictionaryType>().Which;
        AllowsJsonNull(dictionary.ValueType).Should().BeTrue();

        var requiredDictionary = model.Properties.Single(property => property.OriginalName == "requiredDictionaryWithNullableValues");
        requiredDictionary.IsRequired.Should().BeTrue();
        AllowsJsonNull(requiredDictionary.Type).Should().BeFalse();
        AllowsJsonNull(requiredDictionary.Type.Should().BeOfType<IrDictionaryType>().Which.ValueType).Should().BeTrue();

        var optionalObject = model.Properties.Single(property => property.OriginalName == "optionalNullableObject");
        optionalObject.IsRequired.Should().BeFalse();
        var reference = optionalObject.Type.Should().BeOfType<IrNullableType>().Which.InnerType
            .Should().BeOfType<IrModelReference>().Which;
        var child = ir.Models.Single(candidate => candidate.Name == reference.ModelName).Properties.Single();
        child.IsRequired.Should().BeFalse();
        AllowsJsonNull(child.Type).Should().BeTrue();
    }

    /// <summary>Builds the model under test from a complete shared OpenAPI JSON document.</summary>
    /// <param name="version">The OpenAPI dialect identifying the fixture.</param>
    /// <returns>The presence matrix model.</returns>
    private static async Task<IrModel> BuildMatrixAsync(string version)
    {
        var ir = await IrTestHarness.BuildIrAsync(OpenApiFixture.Read($"CoreIR/composition-presence-{version}.json"));
        return ir.Models.Single(model => model.Name == "PresenceMatrix");
    }

    /// <summary>Evaluates the IR's null-acceptance structure while retaining exact-one alternative cardinality.</summary>
    /// <param name="type">The translated type whose JSON null acceptance is inspected.</param>
    /// <returns>Whether null satisfies the retained composition structure.</returns>
    private static bool AllowsJsonNull(IrType type) => type switch
    {
        IrNullableType => true,
        IrPrimitiveType primitive => primitive.PrimitiveKind is IrPrimitiveKind.Null or IrPrimitiveKind.Any,
        IrUnionType { Kind: IrUnionKind.OneOf } union => union.Types.Count(AllowsJsonNull) == 1,
        IrUnionType union => union.Types.Any(AllowsJsonNull),
        IrIntersectionType intersection => intersection.Types.All(AllowsJsonNull),
        IrOptionalType optional => AllowsJsonNull(optional.InnerType),
        _ => false
    };
}
