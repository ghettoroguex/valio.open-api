using FluentAssertions;
using NUnit.Framework;
using ValIO.Core.IR;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Core.IR.Utilities;

namespace ValIO.Core.Tests.IR;

/// <summary>Guards shared inheritance planning against ordinary bases, cycles and wire-name duplication.</summary>
[TestFixture]
public class IrInheritancePlanTests
{
    /// <summary>Verifies multiple ordinary bases are flattened without dereferencing absent composition.</summary>
    [Test]
    public void MultipleOrdinaryBases_PreserveEveryPropertyInBaseFirstOrder()
    {
        var first = Model("First", [Property("id")]);
        var second = Model("Second", [Property("code")]);
        var child = Model("Child", [Property("name")], "First", "Second");

        var plan = IrInheritancePlan.Resolve(child, Lookup(first, second, child));

        plan.UseInheritance.Should().BeFalse();
        plan.EffectiveProperties.Select(p => p.OriginalName).Should().Equal("id", "code", "name");
    }

    /// <summary>Verifies single-base declarations exclude inherited members while flattening includes them.</summary>
    [Test]
    public void SingleBase_ProvidesBothInheritanceAndFlatteningViews()
    {
        var parent = Model("Parent", [Property("id")]);
        var child = Model("Child", [Property("name")], "Parent");
        var lookup = Lookup(parent, child);

        var plan = IrInheritancePlan.Resolve(child, lookup);

        plan.BaseModelName.Should().Be("Parent");
        plan.EffectiveProperties.Select(p => p.OriginalName).Should().Equal("name");
        IrInheritancePlan.FlattenProperties(child, lookup).Select(p => p.OriginalName).Should().Equal("id", "name");
        child.Properties.Should().ContainSingle();
    }

    /// <summary>Verifies cyclic base graphs produce finite properties rather than invalid target inheritance.</summary>
    [Test]
    public void CyclicAllOf_FlattensWithoutRepeatedMembers()
    {
        var first = Model("First", [Property("first")], "Second");
        var second = Model("Second", [Property("second")], "First");

        var plan = IrInheritancePlan.Resolve(first, Lookup(first, second));

        plan.UseInheritance.Should().BeFalse();
        plan.EffectiveProperties.Select(p => p.OriginalName).Should().Equal("second", "first");
    }

    /// <summary>Verifies original wire identity determines overrides even when generated names differ.</summary>
    [Test]
    public void SameWireProperty_LastDeclarationWinsWithoutReordering()
    {
        var inherited = Property("wire-id") with { Name = "inheritedId" };
        var own = Property("wire-id") with { Name = "ownId" };
        var first = Model("First", [inherited]);
        var second = Model("Second", [Property("code")]);
        var child = Model("Child", [own], "First", "Second");

        var properties = IrInheritancePlan.FlattenProperties(child, Lookup(first, second, child));

        properties.Should().HaveCount(2);
        properties[0].Should().BeSameAs(own);
        properties[1].OriginalName.Should().Be("code");
    }

    /// <summary>Verifies equal generated names do not erase different wire properties.</summary>
    [Test]
    public void DistinctWireProperties_AreNotDeduplicatedByGeneratedName()
    {
        var parent = Model("Parent", [Property("first-id") with { Name = "id" }]);
        var child = Model("Child", [Property("second-id") with { Name = "id" }], "Parent");

        IrInheritancePlan.FlattenProperties(child, Lookup(parent, child))
            .Select(p => p.OriginalName).Should().Equal("first-id", "second-id");
    }

    /// <summary>Verifies referenced oneOf alternatives are not treated as inherited object properties.</summary>
    [Test]
    public void NonAllOfComposition_DoesNotFlattenAlternatives()
    {
        var option = Model("Option", [Property("option")]);
        var union = Model("Union", [Property("own")]) with
        {
            Composition = new IrComposition
            {
                Kind = IrCompositionKind.OneOf,
                Types = [new IrModelReference { ModelName = "Option" }]
            }
        };

        IrInheritancePlan.FlattenProperties(union, Lookup(option, union))
            .Select(p => p.OriginalName).Should().Equal("own");
    }

    /// <summary>Creates a required string property for graph tests.</summary>
    /// <param name="wireName">The source property identity.</param>
    /// <returns>A property with matching source and generated names.</returns>
    private static IrProperty Property(string wireName) => new()
    {
        Name = wireName,
        OriginalName = wireName,
        IsRequired = true,
        Type = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.String }
    };

    /// <summary>Creates a model with optional allOf reference bases.</summary>
    /// <param name="name">The generated model identity.</param>
    /// <param name="properties">Own property declarations.</param>
    /// <param name="bases">Referenced allOf model identities.</param>
    /// <returns>The model to plan.</returns>
    private static IrModel Model(string name, IReadOnlyList<IrProperty> properties, params string[] bases) => new()
    {
        Name = name,
        Properties = properties,
        Composition = bases.Length == 0 ? null : new IrComposition
        {
            Kind = IrCompositionKind.AllOf,
            Types = bases.Select(baseName => (IrType)new IrModelReference { ModelName = baseName }).ToArray()
        }
    };

    /// <summary>Indexes test models by generated identity.</summary>
    /// <param name="models">The complete model graph.</param>
    /// <returns>The lookup expected by the shared planner.</returns>
    private static IReadOnlyDictionary<string, IrModel> Lookup(params IrModel[] models)
        => models.ToDictionary(model => model.Name, StringComparer.Ordinal);
}
