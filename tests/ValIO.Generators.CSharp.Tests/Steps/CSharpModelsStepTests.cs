using FluentAssertions;
using NUnit.Framework;
using ValIO.Core.IR;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Core.Models;
using ValIO.Core.Plugins;
using ValIO.Generators.CSharp.Ast.Renderers;
using ValIO.Generators.CSharp.Services;
using ValIO.Generators.CSharp.Steps;

namespace ValIO.Generators.CSharp.Tests.Steps;

/// <summary>
/// Regression tests for allOf-inheritance wiring in <see cref="CSharpModelsStep"/>: the step must feed
/// the model lookup into <c>CSharpAstBuilder.ModelsByName</c> before building models, otherwise a
/// derived class re-declares its base class's properties (CS0108 "hides inherited member").
/// </summary>
[TestFixture]
public class CSharpModelsStepTests
{
    private CSharpModelsStep _step = null!;

    [SetUp]
    public void Setup()
    {
        _step = new CSharpModelsStep(new CSharpNamingConverter(), new CSharpAstRenderer());
    }

    private static IrProperty Prop(string originalName, string pascalName, bool required = true) => new()
    {
        OriginalName = originalName,
        Name = pascalName,
        Type = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.String },
        IsRequired = required,
    };

    [Test]
    public async Task DerivedModel_DoesNotRedeclareInheritedProperty()
    {
        var baseModel = new IrModel
        {
            Name = "Animal",
            Properties = [Prop("name", "Name")],
        };

        var derivedModel = new IrModel
        {
            Name = "Dog",
            // Как это реально приходит из OpenAPI-flattening: allOf inline-член повторяет базовое свойство.
            Properties = [Prop("name", "Name"), Prop("breed", "Breed")],
            Composition = new IrComposition
            {
                Kind = IrCompositionKind.AllOf,
                Types = [new IrModelReference { ModelName = "Animal" }],
            },
        };

        var document = new IrDocument
        {
            Metadata = new IrDocumentMetadata(),
            Models = [baseModel, derivedModel],
            Enums = [],
            Clients = [],
            TypeDependencies = new IrTypeDependencyGraph
            {
                Dependencies = new Dictionary<string, IReadOnlyList<string>>(),
                TopologicalOrder = ["Animal", "Dog"],
            },
        };

        var context = new GenerationContext
        {
            IrDocument = document,
            Configuration = new GenerationConfiguration { Namespace = "Test.Ns" },
        };

        await _step.ExecuteAsync(context);

        var dogFile = context.GeneratedFiles.Single(f => f.RelativePath.EndsWith("Dog.cs"));

        dogFile.Content.Should().Contain("class Dog : Animal");
        dogFile.Content.Should().NotContain("public string Name"); // унаследовано, не переобъявляется
        dogFile.Content.Should().Contain("public string Breed"); // собственное свойство остаётся
        dogFile.Content.Should().EndWith(Environment.NewLine);
        dogFile.Content.Should().NotEndWith(Environment.NewLine + Environment.NewLine);
    }
}
