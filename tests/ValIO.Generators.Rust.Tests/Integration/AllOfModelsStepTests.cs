using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.OpenApi;
using NUnit.Framework;
using ValIO.Core.IR;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Core.Plugins;
using ValIO.Core.Services;
using ValIO.Tests.Fixtures;
using ValIO.Generators.Rust.Tests.TestHelpers;

namespace ValIO.Generators.Rust.Tests.Integration;

/// <summary>Verifies that the complete Rust pipeline preserves composed model fields across OpenAPI dialects.</summary>
[TestFixture]
public sealed class AllOfModelsStepTests
{
    /// <summary>Checks direct and transitive inherited fields while keeping the source IR own-only.</summary>
    /// <param name="version">The OpenAPI or Swagger dialect parsed by the actual reader.</param>
    /// <param name="modelName">The generated model selected from the inheritance chain.</param>
    /// <param name="ownProperty">The property declared directly by the selected model.</param>
    /// <returns>A task that completes after generation and all assertions.</returns>
    [TestCase("2.0", "Child", "name")]
    [TestCase("2.0", "GrandChild", "label")]
    [TestCase("3.0.4", "Child", "name")]
    [TestCase("3.0.4", "GrandChild", "label")]
    [TestCase("3.1.2", "Child", "name")]
    [TestCase("3.1.2", "GrandChild", "label")]
    [TestCase("3.2.0", "Child", "name")]
    [TestCase("3.2.0", "GrandChild", "label")]
    public async Task AllOf_EmitsInheritedPropertiesWithoutChangingOwnProperties(string version, string modelName, string ownProperty)
    {
        var document = await ParseDocumentAsync($"Inheritance/chain-{version}.json");
        using var harness = new RustPipelineTestHarness();
        var context = await harness.RunAsync(document);

        var file = ModelFile(context, modelName);
        var expectedProperties = modelName == "Child" ? new[] { "id", "name" } : new[] { "id", "name", "label" };
        foreach (var propertyName in expectedProperties)
        {
            var targetName = propertyName;
            var declaration = $"pub {targetName}: String";
            Regex.Matches(file.Content, Regex.Escape(declaration)).Count.Should().Be(1,
                $"{modelName} must declare {propertyName} exactly once for OpenAPI {version}");
        }

        var sourceModel = context.IrDocument.Models.Single(model => model.Name == modelName);
        sourceModel.Properties.Select(property => property.OriginalName).Should().Equal(ownProperty);
        sourceModel.Composition.Should().NotBeNull();
        sourceModel.Composition!.Kind.Should().Be(IrCompositionKind.AllOf);
        sourceModel.OriginalSchema.Should().BeSameAs(document.Components!.Schemas![modelName]);
    }

    /// <summary>Checks that inherited nested unions survive parsing, IR conversion and model generation.</summary>
    /// <remarks>Property unions use serde_json::Value; this verifies field retention rather than strict branch validation.</remarks>
    /// <param name="version">The OpenAPI 3 dialect supporting oneOf and anyOf.</param>
    /// <param name="compositionKind">The property composition keyword, oneOf or anyOf.</param>
    /// <returns>A task that completes after the pipeline and preservation assertions.</returns>
    [TestCase("3.0.4", "oneOf")]
    [TestCase("3.0.4", "anyOf")]
    [TestCase("3.1.2", "oneOf")]
    [TestCase("3.1.2", "anyOf")]
    [TestCase("3.2.0", "oneOf")]
    [TestCase("3.2.0", "anyOf")]
    public async Task AllOf_InheritedNestedUnionRetainsFieldAndDocumentReferences(string version, string compositionKind)
    {
        var document = await ParseDocumentAsync($"Inheritance/nested-{compositionKind}-{version}.json");
        using var harness = new RustPipelineTestHarness();
        var context = await harness.RunAsync(document);

        var baseModel = context.IrDocument.Models.Single(model => model.Name == "Base");
        var property = baseModel.Properties.Single(property => property.OriginalName == "payload");
        property.IsRequired.Should().BeTrue();
        var array = property.Type.Should().BeOfType<IrArrayType>().Subject;
        var dictionary = array.ElementType.Should().BeOfType<IrDictionaryType>().Subject;
        var union = dictionary.ValueType.Should().BeOfType<IrUnionType>().Subject;
        union.Kind.Should().Be(compositionKind == "oneOf" ? IrUnionKind.OneOf : IrUnionKind.AnyOf);
        union.Types.Should().AllBeOfType<IrModelReference>();
        union.Types.Cast<IrModelReference>().Select(reference => reference.ModelName).Should().Equal("First", "Second");

        var file = ModelFile(context, "Child");
        file.Content.Should().Contain("pub payload: Vec<std::collections::HashMap<String, serde_json::Value>>");
        ModelFile(context, "First").Content.Should().NotBeNullOrWhiteSpace();
        ModelFile(context, "Second").Content.Should().NotBeNullOrWhiteSpace();
        var child = context.IrDocument.Models.Single(model => model.Name == "Child");
        child.Properties.Select(property => property.OriginalName).Should().Equal("name");
    }

    /// <summary>Preserves nested inclusive and exclusive alternatives inside inherited array and dictionary fields.</summary>
    /// <param name="version">The OpenAPI 3 dialect parsed by the actual reader.</param>
    /// <param name="scenario">The explicit nested composition fixture.</param>
    /// <returns>A task that completes after generation and IR boundary assertions.</returns>
    [TestCase("3.0.4", "anyOf-in-allOf")]
    [TestCase("3.1.2", "anyOf-in-allOf")]
    [TestCase("3.2.0", "anyOf-in-allOf")]
    [TestCase("3.0.4", "allOf-in-oneOf")]
    [TestCase("3.1.2", "allOf-in-oneOf")]
    [TestCase("3.2.0", "allOf-in-oneOf")]
    public async Task NestedCompositions_PreserveBoundariesAcrossTheModelPipeline(string version, string scenario)
    {
        var document = await ParseDocumentAsync($"Inheritance/{scenario}-{version}.json");
        using var harness = new RustPipelineTestHarness();
        var context = await harness.RunAsync(document);

        var property = context.IrDocument.Models.Single(model => model.Name == "Base").Properties
            .Single(property => property.OriginalName == "payload");
        var array = property.Type.Should().BeOfType<IrArrayType>().Subject;
        var dictionary = array.ElementType.Should().BeOfType<IrDictionaryType>().Subject;
        if (scenario == "anyOf-in-allOf")
        {
            var intersection = dictionary.ValueType.Should().BeOfType<IrIntersectionType>().Subject;
            intersection.Types.Should().HaveCount(2);
            var union = intersection.Types[0].Should().BeOfType<IrUnionType>().Subject;
            union.Kind.Should().Be(IrUnionKind.AnyOf);
            union.Types.Cast<IrModelReference>().Select(reference => reference.ModelName).Should().Equal("First", "Second");
            intersection.Types[1].Should().BeOfType<IrModelReference>().Which.ModelName.Should().Be("Third");
        }
        else
        {
            var union = dictionary.ValueType.Should().BeOfType<IrUnionType>().Subject;
            union.Kind.Should().Be(IrUnionKind.OneOf);
            union.Types.Should().HaveCount(2);
            var intersection = union.Types[0].Should().BeOfType<IrIntersectionType>().Subject;
            intersection.Types.Cast<IrModelReference>().Select(reference => reference.ModelName).Should().Equal("First", "Second");
            union.Types[1].Should().BeOfType<IrModelReference>().Which.ModelName.Should().Be("Third");
        }

        ModelFile(context, "Child").Content.Should().MatchRegex(@"(?i)\bpayload\b");
        foreach (var modelName in new[] { "First", "Second", "Third" })
        {
            ModelFile(context, modelName).Content.Should().NotBeNullOrWhiteSpace();
            context.IrDocument.Models.Count(model => model.Name == modelName).Should().Be(1);
        }
        context.IrDocument.Models.Single(model => model.Name == "Child").Properties
            .Select(property => property.OriginalName).Should().Equal("name");
    }

    /// <summary>Parses an explicit document copied to the test output directory.</summary>
    /// <param name="fixturePath">The document path relative to the shared OpenAPI fixture directory.</param>
    /// <returns>The document produced by the real OpenAPI reader without parse errors.</returns>
    private static async Task<OpenApiDocument> ParseDocumentAsync(string fixturePath)
    {
        var json = OpenApiFixture.Read(fixturePath);
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var parsed = await OpenApiDocument.LoadAsync(stream, "json");
        (parsed.Diagnostic?.Errors.Select(error => error.Message) ?? []).Should().BeEmpty();
        parsed.Document.Should().NotBeNull();
        return parsed.Document!;
    }

    /// <summary>Selects a generated model file while respecting the target language file naming convention.</summary>
    /// <param name="context">The completed pipeline state.</param>
    /// <param name="modelName">The source model name.</param>
    /// <returns>The unique generated file for the selected model.</returns>
    private static GeneratedFile ModelFile(GenerationContext context, string modelName)
    {
        var fileName = new NamingConverter().ToSnakeCase(modelName);
        return context.GeneratedFiles.Single(file =>
            file.RelativePath.Replace('\\', '/').EndsWith($"/{fileName}.rs", StringComparison.Ordinal));
    }
}
