using FluentAssertions;
using NUnit.Framework;
using ValIO.Core.IR;
using ValIO.Core.IR.Types;
using ValIO.Core.IR.Utilities;
using ValIO.Core.Tests.TestHelpers;
using ValIO.Tests.Fixtures;

namespace ValIO.Core.Tests.IR;

/// <summary>Checks model inheritance, discriminator metadata and request/response composition normalization.</summary>
[TestFixture]
public class CompositionVariantsTests
{
    /// <summary>Keeps referenced bases in composition metadata and inline child properties in the own-property list.</summary>
    /// <returns>The asynchronous assertion operation.</returns>
    [Test]
    public async Task Inheritance_CompositionHoldsOnlyBaseRef_NoDuplicateProps()
    {
        var ir = await BuildFixtureAsync("inheritance-basic");
        var model = ir.Models.Single(model => model.Name == "Dog");

        model.Composition.Should().NotBeNull();
        model.Composition!.Kind.Should().Be(IrCompositionKind.AllOf);
        model.Composition.Types.Should().ContainSingle()
            .Which.Should().BeOfType<IrModelReference>()
            .Which.ModelName.Should().Be("AnimalBase");
        model.Properties.Select(property => property.OriginalName).Should().Contain("bark");
        model.Properties.Select(property => property.OriginalName).Should().NotContain("name");
    }

    /// <summary>Excludes child redeclarations of inherited wire properties from generated inheritance members.</summary>
    /// <returns>The asynchronous assertion operation.</returns>
    [Test]
    public async Task Inheritance_ChildRedeclaresBaseProperty_ResolverExcludesIt()
    {
        var ir = await BuildFixtureAsync("inheritance-redeclared");
        var byName = ir.Models.ToDictionary(model => model.Name);
        var textBlock = byName["TextBlock"];

        textBlock.Properties.Select(property => property.OriginalName).Should().Contain("kind").And.Contain("format");
        IrInheritanceResolver.GetInheritedPropertyNames(textBlock, byName).Should().Contain("kind");
        var own = IrInheritanceResolver.ExcludeInheritedProperties(textBlock, textBlock.Properties, byName);
        own.Select(property => property.OriginalName).Should().NotContain("kind").And.Contain("format");
    }

    /// <summary>Retains oneOf discriminator identity and explicit source-to-model mappings.</summary>
    /// <returns>The asynchronous assertion operation.</returns>
    [Test]
    public async Task OneOf_WithDiscriminator_PopulatesMapping()
    {
        var ir = await BuildFixtureAsync("discriminated-oneOf");
        var model = ir.Models.Single(model => model.Name == "Pet");

        model.Composition.Should().NotBeNull();
        model.Composition!.Kind.Should().Be(IrCompositionKind.OneOf);
        model.Composition.Types.Should().HaveCount(2);
        model.Composition.DiscriminatorPropertyName.Should().Be("kind");
        model.Composition.DiscriminatorMapping.Should().ContainKey("dog");
        model.Composition.DiscriminatorMapping!["dog"].Should().Be("Dog");
    }

    /// <summary>Terminates when an allOf component wraps a reference to itself.</summary>
    /// <returns>The asynchronous assertion operation.</returns>
    [Test]
    public async Task SelfWrappingAllOf_DoesNotStackOverflow()
    {
        var action = async () => await BuildFixtureAsync("self-wrapping-allOf");
        var ir = await action.Should().NotThrowAsync();
        ir.Subject.Models.Select(model => model.Name).Should().Contain("Node");
    }

    /// <summary>Normalizes a one-reference allOf in response content to the referenced result model.</summary>
    /// <returns>The asynchronous assertion operation.</returns>
    [Test]
    public async Task SingleElementAllOf_InResponseBody_NormalizesToReference()
    {
        var ir = await BuildFixtureAsync("response-single-allOf");
        var operation = ir.Clients.SelectMany(client => client.Operations).Single(operation => operation.Path == "/x");
        var success = operation.Responses.First(response => response.StatusCode == "200");
        success.Type.Should().BeOfType<IrModelReference>().Which.ModelName.Should().Be("Result");
    }

    /// <summary>Normalizes a one-reference allOf in request content to the referenced form model.</summary>
    /// <returns>The asynchronous assertion operation.</returns>
    [Test]
    public async Task SingleElementAllOf_InRequestBody_NormalizesToReference()
    {
        var ir = await BuildFixtureAsync("request-single-allOf");
        var operation = ir.Clients.SelectMany(client => client.Operations).Single(operation => operation.RequestBody != null);
        operation.RequestBody!.Type.Should().BeOfType<IrModelReference>().Which.ModelName.Should().Be("CreateForm");
    }

    /// <summary>Builds the production IR from a complete shared JSON document.</summary>
    /// <param name="fixtureName">The shared legacy composition document name without its extension.</param>
    /// <returns>The translated OpenAPI document.</returns>
    private static Task<IrDocument> BuildFixtureAsync(string fixtureName)
        => IrTestHarness.BuildIrAsync(OpenApiFixture.Read($"Inheritance/Legacy/{fixtureName}.json"));
}
