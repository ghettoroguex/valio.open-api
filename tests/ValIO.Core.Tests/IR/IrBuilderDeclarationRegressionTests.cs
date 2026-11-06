using FluentAssertions;
using NUnit.Framework;
using ValIO.Core.IR.Builders;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Core.Services;
using ValIO.Core.Tests.TestHelpers;
using ValIO.Tests.Fixtures;

namespace ValIO.Core.Tests.IR;

/// <summary>Protects shared declaration allocation, graph extraction, and numeric constraint fidelity.</summary>
[TestFixture]
public sealed class IrBuilderDeclarationRegressionTests
{
    /// <summary>Ensures nested alternatives inside model allOf remain semantic type members and dependency edges.</summary>
    /// <returns>The asynchronous assertion operation.</returns>
    [Test]
    public async Task ModelAllOf_WithNestedAlternatives_PreservesCompositionAndOwnedProperties()
    {
        var ir = await IrTestHarness.BuildIrAsync(OpenApiFixture.Read("CoreIR/mixed-model-compositions.json"));

        var mixed = ir.Models.Single(model => model.Name == "Mixed");
        mixed.Properties.Should().ContainSingle().Which.OriginalName.Should().Be("own");
        mixed.Composition!.Types.Should().HaveCount(2);
        var alternative = mixed.Composition.Types[0].Should().BeOfType<IrUnionType>().Which;
        alternative.Kind.Should().Be(IrUnionKind.AnyOf);
        alternative.Types.Cast<IrModelReference>().Select(reference => reference.ModelName).Should().Equal("Base", "Alternative");
        mixed.Composition.Types[1].Should().BeOfType<IrModelReference>().Which.ModelName.Should().Be("Extra");
        ir.TypeDependencies.Dependencies["Mixed"].Should().BeEquivalentTo(new[] { "Base", "Alternative", "Extra" });

        var choice = ir.Models.Single(model => model.Name == "Choice");
        choice.Composition!.Types[0].Should().BeOfType<IrIntersectionType>().Which.Types
            .Cast<IrModelReference>().Select(reference => reference.ModelName).Should().Equal("Base", "Extra");
    }

    /// <summary>Ensures ambiguous component identifiers fail before any schema body is translated.</summary>
    /// <returns>The asynchronous assertion operation.</returns>
    [Test]
    public async Task ComponentNames_WithTheSameSanitizedIdentifier_FailBeforeIrConstruction()
    {
        var build = async () => await IrTestHarness.BuildIrAsync(OpenApiFixture.Read("CoreIR/component-name-collision.json"));

        var exception = (await build.Should().ThrowAsync<InvalidOperationException>()).Which;
        exception.Message.Should().Contain("A.B").And.Contain("A-B").And.Contain("A_B")
            .And.Contain("same sanitized IR type name");
    }

    /// <summary>Ensures two inline properties with the same name never overwrite each other's schemas.</summary>
    /// <returns>The asynchronous assertion operation.</returns>
    [Test]
    public async Task InlineModels_WithMatchingPropertyNames_KeepDistinctShapes()
    {
        var ir = await IrTestHarness.BuildIrAsync(OpenApiFixture.Read("CoreIR/inline-matching-property-names.json"));

        var firstReference = (IrModelReference)ir.Models.Single(model => model.Name == "First").Properties.Single().Type;
        var secondReference = (IrModelReference)ir.Models.Single(model => model.Name == "Second").Properties.Single().Type;
        firstReference.ModelName.Should().Be("DetailsInlineModel");
        secondReference.ModelName.Should().NotBe(firstReference.ModelName);
        ir.Models.Single(model => model.Name == firstReference.ModelName).Properties.Single().OriginalName.Should().Be("first");
        ir.Models.Single(model => model.Name == secondReference.ModelName).Properties.Single().OriginalName.Should().Be("second");
        ir.Models.Select(model => model.Name).Should().OnlyHaveUniqueItems();
    }

    /// <summary>Ensures component names are reserved before inline schemas are translated.</summary>
    /// <returns>The asynchronous assertion operation.</returns>
    [Test]
    public async Task InlineModels_DoNotCollideWithLaterDeclaredModelsOrEnums()
    {
        var ir = await IrTestHarness.BuildIrAsync(OpenApiFixture.Read("CoreIR/inline-declared-model-and-enum-collisions.json"));

        ir.Models.Select(model => model.Name).Concat(ir.Enums.Select(enumModel => enumModel.Name)).Should().OnlyHaveUniqueItems();
        ir.Models.Single(model => model.Name == "DetailsInlineModel").Properties.Single().OriginalName.Should().Be("declared");
        var container = ir.Models.Single(model => model.Name == "Container");
        foreach (var property in container.Properties)
        {
            var reference = property.Type.Should().BeOfType<IrModelReference>().Which;
            reference.ModelName.Should().NotBe("DetailsInlineModel").And.NotBe("ModeInlineModel");
            ir.Models.Should().Contain(model => model.Name == reference.ModelName);
        }
    }

    /// <summary>Ensures a nested schema cannot reuse a name reserved by its unfinished parent declaration.</summary>
    /// <returns>The asynchronous assertion operation.</returns>
    [Test]
    public async Task NestedInlineModels_ReserveNamesBeforeRecursiveConstruction()
    {
        var ir = await IrTestHarness.BuildIrAsync(OpenApiFixture.Read("CoreIR/inline-nested-reservation.json"));

        ir.Models.Should().HaveCount(3);
        ir.Models.Select(model => model.Name).Should().OnlyHaveUniqueItems();
        var outer = (IrModelReference)ir.Models.Single(model => model.Name == "Root").Properties.Single().Type;
        var inner = (IrModelReference)ir.Models.Single(model => model.Name == outer.ModelName).Properties.Single().Type;
        inner.ModelName.Should().NotBe(outer.ModelName);
        ir.Models.Single(model => model.Name == inner.ModelName).Properties.Single().OriginalName.Should().Be("value");
    }

    /// <summary>Ensures reusing an IR builder resets all inline name reservations for the next document.</summary>
    /// <returns>The asynchronous assertion operation.</returns>
    [Test]
    public async Task ReusedBuilder_AllocatesTheSameNamesForTheSameDocument()
    {
        var json = OpenApiFixture.Read("CoreIR/inline-builder-reuse.json");

        var document = await IrTestHarness.LoadAsync(json);
        var builder = new IrBuilder(new SchemaResolver(document), new NamingConverter());
        var first = builder.BuildFromOpenApi(document);
        var second = builder.BuildFromOpenApi(document);

        first.Models.Select(model => model.Name).Should().Equal(second.Models.Select(model => model.Name));
        second.Models.Single(model => model.Name == "DetailsInlineModel").Properties.Single().OriginalName.Should().Be("value");
    }

    /// <summary>Ensures production graph extraction matches the shared builder after affixing and inline allocation.</summary>
    /// <returns>The asynchronous assertion operation.</returns>
    [Test]
    public async Task ProductionDependencyGraph_MatchesSharedBuilder()
    {
        var json = OpenApiFixture.Read("CoreIR/dependency-graph-parity.json");

        var ir = await IrTestHarness.BuildIrAsync(json, new NameAffixOptions { ModelSuffix = "Model", EnumSuffix = "Enum" });
        var extracted = new IrTypeDependencyGraphBuilder().Build(ir.Models, ir.Enums);

        ir.TypeDependencies.Should().BeEquivalentTo(extracted);
        ir.TypeDependencies.TopologicalOrder.Should().OnlyHaveUniqueItems();
        ir.TypeDependencies.Dependencies["BaseModel"].Should().Contain("ModeEnum");
        ir.TypeDependencies.Dependencies["ChildModel"].Should().Contain("BaseModel").And.Contain("ChildModel");
    }

    /// <summary>Ensures exclusive-only ranges survive extraction for properties and operation parameters.</summary>
    /// <returns>The asynchronous assertion operation.</returns>
    [Test]
    [SetCulture("fr-FR")]
    public async Task ExclusiveOnlyConstraints_AreRetainedUnderNonInvariantCulture()
    {
        var json = OpenApiFixture.Read("CoreIR/numeric-constraints.json");

        var ir = await IrTestHarness.BuildIrAsync(json);
        var score = ir.Models.Single().Properties.Single(property => property.OriginalName == "score");
        score.Constraints.Should().NotBeNull();
        score.Constraints!.ExclusiveMin.Should().Be(1.25m);
        score.Constraints.ExclusiveMax.Should().Be(250m);
        ir.Clients.Single().Operations.Single().QueryParameters.Single().Constraints.Should().BeEquivalentTo(score.Constraints);

        var amount = ir.Models.Single().Properties.Single(property => property.OriginalName == "amount");
        amount.Constraints!.Min.Should().Be(-1.5m);
        amount.Constraints.Max.Should().Be(125m);
    }
}
