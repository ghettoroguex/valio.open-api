using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Core.IR;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Core.Models;
using ValIO.Core.Plugins;
using ValIO.Generators.Cpp.Ast.Renderers;
using ValIO.Generators.Cpp.Models;
using ValIO.Generators.Cpp.Options;
using ValIO.Generators.Cpp.Services;

namespace ValIO.Generators.Cpp.Tests.Steps;

/// <summary>Guards explicit temporal selection, preserved interval metadata, and pure application header boundaries.</summary>
[TestFixture]
public sealed class CppTemporalBackendTests
{
    /// <summary>Checks each public configuration choice selects exactly the requested temporal provider.</summary>
    /// <param name="configured">The canonical configuration value.</param>
    /// <param name="expected">The selected provider.</param>
    [TestCase("string", CppTemporalBackend.String)]
    [TestCase("chrono", CppTemporalBackend.Chrono)]
    [TestCase("howard-date", CppTemporalBackend.HowardDate)]
    [TestCase("boost-date-time", CppTemporalBackend.BoostDateTime)]
    [TestCase("valio", CppTemporalBackend.Valio)]
    public void Descriptor_ParsesTemporalProviderIndependently(string configured, CppTemporalBackend expected)
    {
        var descriptor = new CppOptionsDescriptor();
        var parsed = (CppGenerationOptions)descriptor.ParseOptions(new Dictionary<string, JsonElement>
        {
            ["runtime"] = JsonSerializer.SerializeToElement("std"),
            ["cxxStandard"] = JsonSerializer.SerializeToElement(20),
            ["temporalBackend"] = JsonSerializer.SerializeToElement(configured),
        }, out var errors)!;
        errors.Should().BeEmpty();
        parsed.TemporalBackend.Should().Be(expected);
        parsed.UuidBackend.Should().Be(CppUuidBackend.String);
        descriptor.ValidateOptions(parsed).Should().BeEmpty();
        descriptor.GetOptionDescriptions().Should().Contain(option => option.Name == "temporalBackend");
    }

    /// <summary>Checks unsupported provider selections and C++ language mismatches fail before generation.</summary>
    [Test]
    public void Descriptor_RejectsInvalidSelectionAndChronoCpp17()
    {
        var descriptor = new CppOptionsDescriptor();
        descriptor.ParseOptions(new Dictionary<string, JsonElement> { ["temporalBackend"] = JsonSerializer.SerializeToElement("automatic") }, out var unknown);
        unknown.Should().ContainSingle();
        descriptor.ParseOptions(new Dictionary<string, JsonElement> { ["temporalBackend"] = JsonSerializer.SerializeToElement(false) }, out var wrongType);
        wrongType.Should().ContainSingle();
        descriptor.ValidateOptions(new CppGenerationOptions { Runtime = CppRuntimeProfile.Std, TemporalBackend = CppTemporalBackend.Chrono, CxxStandard = 17 })
            .Should().ContainSingle(error => error.Contains("temporalBackend", StringComparison.Ordinal));
        descriptor.ValidateOptions(new CppGenerationOptions { Runtime = CppRuntimeProfile.Ue, TemporalBackend = CppTemporalBackend.Valio })
            .Should().ContainSingle(error => error.Contains("runtime=std", StringComparison.Ordinal));
    }

    /// <summary>Checks exact native mappings and wire formats propagate through nested nullable collections.</summary>
    /// <param name="backend">The explicitly selected native representation.</param>
    [TestCase(CppTemporalBackend.Chrono)]
    [TestCase(CppTemporalBackend.HowardDate)]
    [TestCase(CppTemporalBackend.BoostDateTime)]
    [TestCase(CppTemporalBackend.Valio)]
    public void Mapper_PreservesTemporalKindsAndFormats(CppTemporalBackend backend)
    {
        var mapper = new StdCppTypeMapper(new StdNamingConvention(), new IrDocumentContext(CreateDocument()),
            new CppGenerationOptions { TemporalBackend = backend });
        foreach (var (kind, format, name, wire) in new[]
        {
            (IrPrimitiveKind.Date, "date", "Date", "Date"),
            (IrPrimitiveKind.Time, "time", "Time", "Time"),
            (IrPrimitiveKind.DateTime, "date-time", "DateTime", "DateTime"),
            (IrPrimitiveKind.String, "duration", "Duration", "Duration"),
            (IrPrimitiveKind.String, "time-span", "Duration", "TimeSpan"),
            (IrPrimitiveKind.String, "timespan", "Duration", "TimeSpan"),
        })
        {
            var leaf = new IrPrimitiveType { PrimitiveKind = kind, Format = format };
            ((SimpleTypeNode)mapper.MapType(leaf)).Name.Name.Should().Be("ValIO::" + name);
            mapper.FormatQueryLiteral(leaf, "value").Should().Be($"ValIO::ToWireText(value, ValIO::JsonStringFormat::{wire})");
            var nested = new IrArrayType { ElementType = new IrNullableType { InnerType = leaf } };
            mapper.ProviderIncludesFor(nested).Should().BeEquivalentTo(["Runtime/ValIOTemporalTypes.h"]);
            StdJsonWireFormat.Name(nested).Should().Be(wire);
        }
        var custom = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.String, Format = "CustomDate" };
        ((SimpleTypeNode)mapper.MapType(custom)).Name.Name.Should().Be("std::string");
        mapper.ProviderIncludesFor(custom).Should().BeEmpty();
    }

    /// <summary>Checks native application headers remain independent of JSON and HTTP when serializers are disabled.</summary>
    /// <param name="backend">The selected temporal provider.</param>
    /// <param name="providerType">The actual provider type declared by the generated support header.</param>
    /// <returns>The asynchronous boundary assertion.</returns>
    [TestCase(CppTemporalBackend.Chrono, "std::chrono::year_month_day")]
    [TestCase(CppTemporalBackend.HowardDate, "date::year_month_day")]
    [TestCase(CppTemporalBackend.BoostDateTime, "boost::gregorian::date")]
    [TestCase(CppTemporalBackend.Valio, "struct Date")]
    public async Task DefinitionOnlyPipeline_EmitsPureTemporalTypes(CppTemporalBackend backend, string providerType)
    {
        var context = await Generate(backend, false);
        var files = context.GeneratedFiles.ToDictionary(file => file.RelativePath, file => file.Content);
        files.Keys.Should().BeEquivalentTo(["Models/TemporalBoundary.h", "Runtime/ValIOTemporalTypes.h", "CMakeLists.txt"]);
        files["Models/TemporalBoundary.h"].Should().Contain("Runtime/ValIOTemporalTypes.h").And.Contain("ValIO::Date");
        files["Runtime/ValIOTemporalTypes.h"].Should().Contain(providerType).And.NotContain("Json").And.NotContain("httplib");
        if (backend == CppTemporalBackend.HowardDate) files["CMakeLists.txt"].Should().Contain("date::date");
        if (backend == CppTemporalBackend.BoostDateTime) files["CMakeLists.txt"].Should().Contain("Boost REQUIRED");
    }

    /// <summary>Checks selected temporal codecs are integrated with strict interval validation without HTTP.</summary>
    /// <param name="backend">The selected temporal provider.</param>
    /// <returns>The asynchronous codec boundary assertion.</returns>
    [TestCase(CppTemporalBackend.String)]
    [TestCase(CppTemporalBackend.Chrono)]
    [TestCase(CppTemporalBackend.HowardDate)]
    [TestCase(CppTemporalBackend.BoostDateTime)]
    [TestCase(CppTemporalBackend.Valio)]
    public async Task SerializerPipeline_UsesCheckedTemporalWireAdapters(CppTemporalBackend backend)
    {
        var context = await Generate(backend, true);
        var files = context.GeneratedFiles.ToDictionary(file => file.RelativePath, file => file.Content);
        files["Runtime/ValIOJsonValidation.h"].Should().Contain("ParseDurationParts").And.Contain("ParseTimeSpanParts");
        files["Serializers/TemporalBoundaryJson.cpp"].Should().Contain("JsonStringFormat::Duration").And.Contain("JsonStringFormat::TimeSpan");
        if (backend == CppTemporalBackend.String)
            files.Keys.Should().NotContain(path => path.Contains("TemporalTypes", StringComparison.Ordinal) || path.Contains("TemporalJson", StringComparison.Ordinal));
        else
            files["Runtime/ValIOJsonTypes.h"].Should().Contain("ValIOTemporalJson.h");
        files.Keys.Should().NotContain(path => path.Contains("Http", StringComparison.Ordinal));
    }

    /// <summary>Creates a small formatted IR document without test-specific type rendering.</summary>
    /// <returns>The model pipeline input.</returns>
    private static IrDocument CreateDocument() => new()
    {
        Metadata = new IrDocumentMetadata(), Enums = [], Clients = [],
        Models = [new IrModel { Name = "TemporalBoundary", Properties =
        [
            new IrProperty { Name = "Date", OriginalName = "date", IsRequired = true, Type = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.Date } },
            new IrProperty { Name = "Duration", OriginalName = "duration", IsRequired = true, Type = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.String, Format = "duration" } },
            new IrProperty { Name = "Span", OriginalName = "span", IsRequired = true, Type = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.String, Format = "time-span" } },
        ] }],
        TypeDependencies = new IrTypeDependencyGraph { Dependencies = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { ["TemporalBoundary"] = [] }, TopologicalOrder = ["TemporalBoundary"] },
    };

    /// <summary>Executes the production pipeline with explicitly selected model, codec, and provider boundaries.</summary>
    /// <param name="backend">The temporal provider.</param>
    /// <param name="serializers">Whether JSON codec generation is enabled.</param>
    /// <returns>The emitted generation context.</returns>
    private static async Task<GenerationContext> Generate(CppTemporalBackend backend, bool serializers)
    {
        var options = new CppGenerationOptions { Runtime = CppRuntimeProfile.Std, Namespace = "TemporalBoundaryTests", TemporalBackend = backend,
            CxxStandard = backend == CppTemporalBackend.Chrono ? 20 : 17, EmitSerializers = serializers, EmitCmake = true };
        using var services = new ServiceCollection().AddSingleton<ICppAstRenderer, CppAstRenderer>().BuildServiceProvider();
        var context = new GenerationContext { IrDocument = CreateDocument(), PluginOptions = options,
            Configuration = new GenerationConfiguration { LanguageId = "cpp", PluginOptions = options, GenerateClients = false } };
        await new CppGeneratorPlugin().CreatePipeline(services).ExecuteAsync(context);
        return context;
    }
}
