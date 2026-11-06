using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Interfaces;
using ValIO.Core.IR;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Core.Models;
using ValIO.Core.Plugins;
using ValIO.Generators.CSharp.Ast.Builders;
using ValIO.Generators.CSharp.Ast.Renderers;
using ValIO.Generators.CSharp.Options;
using ValIO.Generators.CSharp.Services;

namespace ValIO.Generators.CSharp.Tests;

/// <summary>Verifies immutable document ownership and renderer isolation across C# pipelines.</summary>
[TestFixture]
public sealed class CSharpDocumentContextTests
{
    /// <summary>Uses the constructor context's indexes without rebuilding or replacing them.</summary>
    [Test]
    public void Builder_UsesTheProvidedIndexesAndRejectsAnotherDocument()
    {
        var context = CSharpDocumentFixture.Context([Model("Item")], []);
        var builder = new CSharpAstBuilder(new CSharpNamingConverter(), context);

        builder.ModelsByName.Should().BeSameAs(context.ModelsByName);
        builder.EnumsByName.Should().BeSameAs(context.EnumsByName);
        builder.EnumNames.Should().BeSameAs(context.EnumNames);
        var build = () => builder.BuildCompilationUnits(context.Document with { });

        build.Should().Throw<ArgumentException>().WithParameterName("document");
    }

    /// <summary>Generates every model once according to the order already resolved by the context.</summary>
    [Test]
    public void DocumentEntryPoint_UsesCanonicalContextOrder()
    {
        var document = CSharpDocumentFixture.Context([Model("First"), Model("Second"), Model("Unordered")], []).Document;
        document = document with
        {
            TypeDependencies = new IrTypeDependencyGraph
            {
                Dependencies = new Dictionary<string, IReadOnlyList<string>>(),
                TopologicalOrder = ["Second", "unknown", "Second", "First"]
            }
        };
        var context = new IrDocumentContext(document);
        var builder = new CSharpAstBuilder(new CSharpNamingConverter(), context);

        var units = builder.BuildCompilationUnits(document);

        units.Should().OnlyContain(unit => unit.Namespaces.Count == 0);
        units.SelectMany(unit => unit.Declarations)
            .OfType<ClassDeclarationNode>().Select(model => model.Name.Name)
            .Should().Equal("Second", "First", "Unordered");
    }

    /// <summary>Places every kind of generated declaration directly in an unspecified global namespace.</summary>
    /// <param name="namespace">The absent or empty namespace supplied by a direct builder consumer.</param>
    [TestCase(null)]
    [TestCase("")]
    public void CompilationUnits_WithoutNamespace_EmitGlobalDeclarations(string? @namespace)
    {
        var model = Model("Item");
        var enumModel = new IrEnum
        {
            Name = "Kind", BaseType = IrEnumBaseType.String,
            Values = [new IrEnumValue { Name = "Active", Value = "active" }]
        };
        var client = new IrClient { Name = "ItemApi", Operations = [] };
        var builder = new CSharpAstBuilder(new CSharpNamingConverter(), CSharpDocumentFixture.Context([model], [enumModel]),
            new CSharpGenerationOptions { GenerateInterfaces = true, GenerateTraces = true, GenerateMetrics = true });
        var units = new[]
        {
            builder.BuildCompilationUnit(model, @namespace), builder.BuildCompilationUnit(enumModel, @namespace),
            builder.BuildCompilationUnit(client, @namespace), builder.BuildCompilationUnitForInterface(client, @namespace),
            builder.BuildCompilationUnitForIHttpClientMetrics(@namespace), builder.BuildCompilationUnitForHttpClientMetrics(@namespace),
            builder.BuildCompilationUnitForDecorator(client, @namespace), builder.BuildCompilationUnitApiException(@namespace)
        };
        var renderer = new CSharpAstRenderer();

        foreach (var unit in units)
        {
            unit.Namespaces.Should().BeEmpty();
            unit.Declarations.Should().NotBeEmpty();
            renderer.Render(unit).Should().NotContain("namespace ");
        }
    }

    /// <summary>Runs independent pipelines with the same declaration names and different enum/model meanings.</summary>
    [Test]
    public async Task ConcurrentPipelines_KeepDocumentIndexesAndRendererBuffersIsolated()
    {
        var services = new ServiceCollection();
        services.AddSingleton<INamingConverter, CSharpNamingConverter>();
        var plugin = new CSharpGeneratorPlugin();
        plugin.ConfigureServices(services);
        plugin.ConfigureServices(services);
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ICSharpAstRenderer>().Should().NotBeSameAs(provider.GetRequiredService<ICSharpAstRenderer>());
        var enumDocument = CreateGenerationContext("EnumDocument", isEnum: true);
        var modelDocument = CreateGenerationContext("ModelDocument", isEnum: false);
        var enumPipeline = plugin.CreatePipeline(provider);
        var modelPipeline = plugin.CreatePipeline(provider);
        using var start = new Barrier(2);

        await Task.WhenAll(
            Task.Run(async () => { start.SignalAndWait(); await enumPipeline.ExecuteAsync(enumDocument); }),
            Task.Run(async () => { start.SignalAndWait(); await modelPipeline.ExecuteAsync(modelDocument); }));

        var enumCode = enumDocument.GeneratedFiles.Single(file => file.RelativePath == "Models/Container.cs").Content;
        var modelCode = modelDocument.GeneratedFiles.Single(file => file.RelativePath == "Models/Container.cs").Content;
        enumCode.Should().Contain("namespace EnumDocument").And.Contain("public Kind? Status");
        enumCode.Should().NotContain("namespace ModelDocument");
        modelCode.Should().Contain("namespace ModelDocument").And.Contain("public Kind Status");
        modelCode.Should().NotContain("public Kind? Status").And.NotContain("namespace EnumDocument");
    }

    /// <summary>Creates a complete request with one reference resolving differently in each document.</summary>
    /// <param name="namespace">The output namespace identifying the request.</param>
    /// <param name="isEnum">Whether Kind is an enum or a reference model.</param>
    /// <returns>The independently owned request and its finalized document.</returns>
    private static GenerationContext CreateGenerationContext(string @namespace, bool isEnum)
    {
        var container = new IrModel
        {
            Name = "Container",
            Properties = [new IrProperty
            {
                Name = "Status", OriginalName = "status", IsRequired = false,
                Type = new IrModelReference { ModelName = "Kind" }
            }]
        };
        var context = CSharpDocumentFixture.Context(
            isEnum ? [container] : [container, Model("Kind")],
            isEnum ? [new IrEnum
            {
                Name = "Kind", BaseType = IrEnumBaseType.String,
                Values = [new IrEnumValue { Name = "Active", Value = "active" }]
            }] : []);
        return new GenerationContext
        {
            IrDocument = context.Document,
            Configuration = new GenerationConfiguration { Namespace = @namespace }
        };
    }

    /// <summary>Creates a bounded model declaration for document-context scenarios.</summary>
    /// <param name="name">The declaration identity.</param>
    /// <returns>An empty model with a valid generated name.</returns>
    private static IrModel Model(string name) => new() { Name = name, Properties = [] };
}
