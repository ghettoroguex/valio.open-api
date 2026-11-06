using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using ValIO.Core.IR;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Core.Models;
using ValIO.Core.Plugins;
using ValIO.Generators.Cpp.Ast.Renderers;
using ValIO.Generators.Cpp.Models;

namespace ValIO.Generators.Cpp.Tests.Steps;

/// <summary>Checks DTO definitions, JSON codecs, and HTTP runtime remain independently consumable.</summary>
[TestFixture]
public sealed class CppStdLayerBoundaryTests
{
    /// <summary>Checks definition-only output has no serializer or runtime dependency.</summary>
    /// <returns>The asynchronous test execution.</returns>
    [Test]
    public async Task PureDefinitions_DoNotEmitRuntimeOrCodecFiles()
    {
        var context = await Generate(emitSerializers: false);
        context.GeneratedFiles.Select(f => f.RelativePath).Should().BeEquivalentTo(["Models/Probe.h", "Enums/State.h"]);
        foreach (var file in context.GeneratedFiles)
            file.Content.Should().NotContainAny(["nlohmann", "httplib", "Runtime/", "to_json", "from_json", "ToWire", "FromWire"]);
    }

    /// <summary>Checks serializer-only output does not generate unused HTTP, result, or response parsing layers.</summary>
    /// <returns>The asynchronous test execution.</returns>
    [Test]
    public async Task SerializersOnly_EmitTypeAdaptersWithoutHttpRuntime()
    {
        var context = await Generate(emitSerializers: true);
        var files = context.GeneratedFiles.ToDictionary(f => f.RelativePath, f => f.Content);
        files.Keys.Where(path => path.StartsWith("Runtime/", StringComparison.Ordinal))
            .Should().BeEquivalentTo(["Runtime/ValIOJsonTypes.h", "Runtime/ValIOJsonValidation.h"]);
        files["Models/Probe.h"].Should().NotContainAny(["nlohmann", "httplib", "Runtime/"]);
        files["Serializers/ProbeJson.h"].Should().Contain("nlohmann/json_fwd.hpp").And.NotContain("nlohmann/json.hpp");
        files["Serializers/ProbeJson.cpp"].Should().Contain("nlohmann/json.hpp").And.Contain("Runtime/ValIOJsonTypes.h");
        files["Enums/State.h"].Should().NotContainAny(["ToWire", "FromWire"]);
        files["Serializers/StateJson.h"].Should().Contain("StateFromWire");
        files["Serializers/StateJson.cpp"].Should().Contain("std::string ToWireText(State v)");
    }

    /// <summary>Ensures enum-only codecs include their checked conversion dependency without HTTP layers.</summary>
    /// <returns>The asynchronous test execution.</returns>
    [Test]
    public async Task EnumSerializersOnly_EmitCheckedValidationSupport()
    {
        var context = await Generate(emitSerializers: true, generateModels: false);
        var files = context.GeneratedFiles.ToDictionary(file => file.RelativePath, file => file.Content);

        files.Should().NotContainKey("Models/Probe.h");
        files.Should().ContainKey("Runtime/ValIOJsonValidation.h");
        files["Serializers/StateJson.cpp"].Should().Contain("Runtime/ValIOJsonValidation.h");
        files.Keys.Should().NotContain(path => path.Contains("Http", StringComparison.Ordinal));
    }

    /// <summary>Checks unsupported typed union diagnostics are drained after mapping and deduplicated across layers.</summary>
    /// <returns>The asynchronous test execution.</returns>
    [Test]
    public async Task UnionFallback_ReportsOneActionableDiagnostic()
    {
        var union = new IrUnionType
        {
            Types = [new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.String }, new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.Integer }],
        };
        var context = await Generate(emitSerializers: true, propertyType: union);
        context.Warnings.Should().ContainSingle(warning => warning.Contains("union", StringComparison.Ordinal)
            && warning.Contains("dedicated codec", StringComparison.Ordinal));
    }

    /// <summary>Runs a standalone model generation with or without the independent codec layer.</summary>
    /// <param name="emitSerializers">Whether JSON codec output is requested.</param>
    /// <param name="propertyType">The optional model field type to exercise mapper diagnostics.</param>
    /// <param name="generateModels">Whether model definitions and codecs are requested alongside enum output.</param>
    /// <returns>The completed generation context.</returns>
    private static async Task<GenerationContext> Generate(bool emitSerializers, IrType? propertyType = null, bool generateModels = true)
    {
        var options = new CppGenerationOptions { Runtime = CppRuntimeProfile.Std, EmitSerializers = emitSerializers };
        var document = new IrDocument
        {
            Metadata = new IrDocumentMetadata(),
            Models =
            [
                new IrModel
                {
                    Name = "Probe",
                    Properties = [new IrProperty { Name = "Value", OriginalName = "value", IsRequired = true, Type = propertyType ?? new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.String } }],
                },
            ],
            Enums = [new IrEnum { Name = "State", BaseType = IrEnumBaseType.String, Values = [new IrEnumValue { Name = "ready", Value = "ready" }] }],
            Clients = [],
            TypeDependencies = new IrTypeDependencyGraph { Dependencies = new Dictionary<string, IReadOnlyList<string>>(), TopologicalOrder = ["Probe", "State"] },
        };
        using var services = new ServiceCollection().AddSingleton<ICppAstRenderer, CppAstRenderer>().BuildServiceProvider();
        var context = new GenerationContext
        {
            IrDocument = document, PluginOptions = options,
            Configuration = new GenerationConfiguration { LanguageId = "cpp", PluginOptions = options, GenerateClients = false, GenerateModels = generateModels },
        };
        await new CppGeneratorPlugin().CreatePipeline(services).ExecuteAsync(context);
        return context;
    }
}
