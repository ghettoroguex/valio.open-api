using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Core.IR;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Generators.Cpp.Ast.Builders;
using ValIO.Generators.Cpp.Ast.Renderers;
using ValIO.Generators.Cpp.Models;
using ValIO.Generators.Cpp.Services;
using ValIO.Generators.Cpp.Tests.TestHelpers;

namespace ValIO.Generators.Cpp.Tests.Services;

/// <summary>Verifies document indexes remain consistent across C++ mappers, builders, and service lifetimes.</summary>
[TestFixture]
public sealed class CppDocumentContextIntegrationTests
{
    /// <summary>Ensures two documents cannot leak enum classification into one another's mappers.</summary>
    [Test]
    public void Mappers_ClassifyTheSameNameUsingTheirOwnDocument()
    {
        var modelContext = CppDocumentContextFactory.Create(models: [new IrModel { Name = "Shared", Properties = [] }]);
        var enumContext = CppDocumentContextFactory.Create(enums:
            [new IrEnum { Name = "Shared", BaseType = IrEnumBaseType.String, Values = [] }]);
        var reference = new IrModelReference { ModelName = "Shared" };
        var ueModelMapper = new UETypeMapper(new UENamingConvention(), modelContext);
        var ueEnumMapper = new UETypeMapper(new UENamingConvention(), enumContext);
        var stdModelMapper = new StdCppTypeMapper(new StdNamingConvention(), modelContext);
        var stdEnumMapper = new StdCppTypeMapper(new StdNamingConvention(), enumContext);

        ((SimpleTypeNode)ueModelMapper.MapType(reference)).Name.Name.Should().Be("FShared");
        ((SimpleTypeNode)ueEnumMapper.MapType(reference)).Name.Name.Should().Be("EShared");
        stdModelMapper.FormatQueryLiteral(reference, "value").Should().Be("ValIO::Dump(value)");
        stdEnumMapper.FormatQueryLiteral(reference, "value").Should().Be("ToWireText(value)");
        stdModelMapper.DocumentContext.Should().BeSameAs(modelContext);
        ueEnumMapper.DocumentContext.Should().BeSameAs(enumContext);
    }

    /// <summary>Ensures a builder rejects mapper indexes that belong to a different document context.</summary>
    [Test]
    public void UnrealBuilder_WithAnotherMapperContext_ThrowsBeforeGeneration()
    {
        var first = CppDocumentContextFactory.Create();
        var second = CppDocumentContextFactory.Create();
        var naming = new UENamingConvention();
        var mapper = new UETypeMapper(naming, first);
        Action create = () => _ = new UEModelAstBuilder(mapper, naming, new CppGenerationOptions(), second);

        create.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("typeMapper");
    }

    /// <summary>Ensures full facade generation cannot use indexes from another document, even for equal declarations.</summary>
    [Test]
    public void Facade_WithAnotherDocument_ThrowsBeforeGeneration()
    {
        var context = CppDocumentContextFactory.Create();
        var otherDocument = context.Document with { Metadata = new IrDocumentMetadata { Title = "Another" } };
        var builder = new CppAstBuilder(new CppGenerationOptions(), context);
        Action build = () => _ = builder.Build(otherDocument);

        build.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("document");
    }

    /// <summary>Ensures independently created pipelines can resolve independent mutable renderer instances.</summary>
    [Test]
    public void Plugin_RegistersIndependentRendererInstances()
    {
        var services = new ServiceCollection();
        new CppGeneratorPlugin().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<ICppAstRenderer>();
        var second = provider.GetRequiredService<ICppAstRenderer>();

        first.Should().NotBeSameAs(second);
        services.Single(descriptor => descriptor.ServiceType == typeof(ICppAstRenderer)).Lifetime
            .Should().Be(ServiceLifetime.Transient);
    }

    /// <summary>Ensures plugin registration respects an explicitly supplied renderer implementation.</summary>
    [Test]
    public void Plugin_PreservesAnExplicitRendererRegistration()
    {
        var customRenderer = new CppAstRenderer();
        var services = new ServiceCollection().AddSingleton<ICppAstRenderer>(customRenderer);
        new CppGeneratorPlugin().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ICppAstRenderer>().Should().BeSameAs(customRenderer);
        services.Count(descriptor => descriptor.ServiceType == typeof(ICppAstRenderer)).Should().Be(1);
    }

    /// <summary>Ensures Unreal models, properties, and enums remain documented without schema prose.</summary>
    [Test]
    public void UnrealDefinitions_AlwaysEmitUsefulDoxygenBriefs()
    {
        var model = new IrModel
        {
            Name = "Probe",
            Properties =
            [
                new IrProperty
                {
                    Name = "Value", OriginalName = "value", IsRequired = true,
                    Type = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.String },
                },
            ],
        };
        var enumModel = new IrEnum { Name = "State", BaseType = IrEnumBaseType.String, Values = [] };
        var context = CppDocumentContextFactory.Create([model], [enumModel]);
        var naming = new UENamingConvention();
        var options = new CppGenerationOptions();
        var renderer = new CppAstRenderer();
        var modelBuilder = new UEModelAstBuilder(new UETypeMapper(naming, context), naming, options, context);

        var modelText = renderer.Render(modelBuilder.Build(model));
        var enumText = renderer.Render(new UEEnumAstBuilder(naming, options).Build(enumModel));

        modelText.Should().Contain("@brief Probe model.").And.Contain("@brief Value field.");
        enumText.Should().Contain("@brief State enum.");
    }
}
