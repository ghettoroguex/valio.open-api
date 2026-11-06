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

/// <summary>Guards explicit UUID provider selection and the separation between application types and JSON adapters.</summary>
[TestFixture]
public sealed class CppUuidBackendTests
{
    /// <summary>Checks existing configurations retain their dependency-free string representation.</summary>
    [Test]
    public void DefaultSelection_PreservesStringRepresentation()
    {
        var descriptor = new CppOptionsDescriptor();
        var parsed = (CppGenerationOptions)descriptor.ParseOptions([], out var errors)!;
        errors.Should().BeEmpty();
        parsed.UuidBackend.Should().Be(CppUuidBackend.String);
        descriptor.GetOptionDescriptions().Should().Contain(option => option.Name == "uuidBackend");
    }

    /// <summary>Checks canonical configuration values produce independently selected UUID providers.</summary>
    /// <param name="configured">The public configuration value.</param>
    /// <param name="expected">The selected application representation.</param>
    [TestCase("string", CppUuidBackend.String)]
    [TestCase("boost", CppUuidBackend.BoostUuid)]
    [TestCase("stduuid", CppUuidBackend.StdUuid)]
    [TestCase("modern-uuid", CppUuidBackend.ModernUuid)]
    public void Descriptor_ParsesExplicitProviders(string configured, CppUuidBackend expected)
    {
        var descriptor = new CppOptionsDescriptor();
        var raw = new Dictionary<string, JsonElement>
        {
            ["runtime"] = JsonSerializer.SerializeToElement("std"),
            ["cxxStandard"] = JsonSerializer.SerializeToElement(20),
            ["uuidBackend"] = JsonSerializer.SerializeToElement(configured),
        };
        var parsed = (CppGenerationOptions)descriptor.ParseOptions(raw, out var errors)!;
        errors.Should().BeEmpty();
        parsed.UuidBackend.Should().Be(expected);
        parsed.TemporalBackend.Should().Be(CppTemporalBackend.String);
    }

    /// <summary>Checks invalid provider configuration cannot silently select another representation.</summary>
    [Test]
    public void Descriptor_RejectsUnknownOrNonStringProvider()
    {
        var descriptor = new CppOptionsDescriptor();
        descriptor.ParseOptions(new Dictionary<string, JsonElement> { ["uuidBackend"] = JsonSerializer.SerializeToElement("random-provider") }, out var unknown);
        unknown.Should().ContainSingle(error => error.Contains("uuidBackend", StringComparison.Ordinal));
        descriptor.ParseOptions(new Dictionary<string, JsonElement> { ["uuidBackend"] = JsonSerializer.SerializeToElement(true) }, out var wrongType);
        wrongType.Should().ContainSingle(error => error.Contains("uuidBackend", StringComparison.Ordinal));
    }

    /// <summary>Checks modern-uuid's real C++20 requirement is validated before code generation.</summary>
    [Test]
    public void ModernUuid_RequiresCpp20OrLater()
    {
        var descriptor = new CppOptionsDescriptor();
        descriptor.ValidateOptions(new CppGenerationOptions
        {
            Runtime = CppRuntimeProfile.Std, UuidBackend = CppUuidBackend.ModernUuid, CxxStandard = 17,
        }).Should().ContainSingle(error => error.Contains("uuidBackend", StringComparison.Ordinal));
        descriptor.ValidateOptions(new CppGenerationOptions
        {
            Runtime = CppRuntimeProfile.Std, UuidBackend = CppUuidBackend.ModernUuid, CxxStandard = 20,
        }).Should().BeEmpty();
    }

    /// <summary>Checks native UUID mappings remain typed and nested containers request only the pure provider header.</summary>
    /// <param name="backend">The selected native UUID provider.</param>
    [TestCase(CppUuidBackend.BoostUuid)]
    [TestCase(CppUuidBackend.StdUuid)]
    [TestCase(CppUuidBackend.ModernUuid)]
    public void Mapper_UsesNativeUuidAndDeduplicatesNestedIncludes(CppUuidBackend backend)
    {
        var document = CreateDocument();
        var mapper = new StdCppTypeMapper(new StdNamingConvention(), new IrDocumentContext(document), new CppGenerationOptions { UuidBackend = backend });
        var uuid = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.Uuid, Format = "uuid" };
        ((SimpleTypeNode)mapper.MapType(uuid)).Name.Name.Should().Be("ValIO::Uuid");
        mapper.ProviderIncludesFor(new IrOptionalType
        {
            InnerType = new IrDictionaryType { KeyType = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.String }, ValueType = new IrArrayType { ElementType = new IrNullableType { InnerType = uuid } } },
        }).Should().BeEquivalentTo(["Runtime/ValIOUuidTypes.h"]);
        mapper.FormatQueryLiteral(uuid, "identifier").Should().Be("ValIO::ToWireText(identifier, ValIO::JsonStringFormat::Uuid)");
        mapper.ProviderIncludesFor(new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.Date }).Should().BeEmpty();
    }

    /// <summary>Checks native provider definitions remain usable when JSON serializer generation is disabled.</summary>
    /// <param name="backend">The explicitly selected UUID provider.</param>
    /// <param name="providerType">The actual type exported by the selected provider.</param>
    /// <returns>The asynchronous pipeline assertion.</returns>
    [TestCase(CppUuidBackend.BoostUuid, "boost::uuids::uuid")]
    [TestCase(CppUuidBackend.StdUuid, "uuids::uuid")]
    [TestCase(CppUuidBackend.ModernUuid, "muuid::uuid")]
    public async Task DefinitionOnlyPipeline_EmitsPureProviderSupport(CppUuidBackend backend, string providerType)
    {
        var context = await Generate(backend, serializers: false);
        var files = context.GeneratedFiles.ToDictionary(file => file.RelativePath, file => file.Content);
        files.Keys.Should().BeEquivalentTo(["Models/UuidBoundary.h", "Runtime/ValIOUuidTypes.h"]);
        files["Models/UuidBoundary.h"].Should().Contain("Runtime/ValIOUuidTypes.h").And.Contain("ValIO::Uuid");
        files["Runtime/ValIOUuidTypes.h"].Should().Contain($"using Uuid = {providerType};");
        foreach (var contents in files.Values)
            contents.Should().NotContainAny(["#include <nlohmann", "#include <httplib", "#include \"ValIOJson", "#include \"Runtime/ValIOJson", "to_json(", "from_json("]);
    }

    /// <summary>Checks each native provider uses independent JSON implementation support without importing HTTP.</summary>
    /// <param name="backend">The explicitly selected UUID provider.</param>
    /// <returns>The asynchronous pipeline assertion.</returns>
    [TestCase(CppUuidBackend.BoostUuid)]
    [TestCase(CppUuidBackend.StdUuid)]
    [TestCase(CppUuidBackend.ModernUuid)]
    public async Task SerializerPipeline_ConnectsCheckedUuidAdapterWithoutHttp(CppUuidBackend backend)
    {
        var context = await Generate(backend, serializers: true);
        var files = context.GeneratedFiles.ToDictionary(file => file.RelativePath, file => file.Content);
        files.Should().ContainKey("Runtime/ValIOUuidJson.h");
        files.Should().ContainKey("Runtime/ValIOJsonValidation.h");
        files["Runtime/ValIOJsonTypes.h"].Should().Contain("ValIOUuidJson.h");
        files["Models/UuidBoundary.h"].Should().NotContain("ValIOUuidJson.h").And.NotContain("nlohmann");
        files.Keys.Should().NotContain(path => path.Contains("Http", StringComparison.Ordinal));
    }

    /// <summary>Checks the compatibility default introduces no provider dependency or support headers.</summary>
    /// <returns>The asynchronous pipeline assertion.</returns>
    [Test]
    public async Task StringPipeline_RequiresNoProviderFiles()
    {
        var context = await Generate(CppUuidBackend.String, serializers: false);
        context.GeneratedFiles.Should().ContainSingle().Which.RelativePath.Should().Be("Models/UuidBoundary.h");
        context.GeneratedFiles.Single().Content.Should().Contain("std::string").And.NotContain("ValIOUuid");
    }

    /// <summary>Creates a UUID model with nested containers that all provider selections must preserve.</summary>
    /// <returns>The immutable document contract supplied to the complete generator pipeline.</returns>
    private static IrDocument CreateDocument()
    {
        var uuid = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.Uuid, Format = "uuid" };
        return new IrDocument
        {
            Metadata = new IrDocumentMetadata(), Enums = [], Clients = [],
            Models = [new IrModel
            {
                Name = "UuidBoundary",
                Properties = [new IrProperty { Name = "Id", OriginalName = "id", IsRequired = true, Type = uuid },
                    new IrProperty { Name = "Items", OriginalName = "items", IsRequired = false,
                        Type = new IrDictionaryType { KeyType = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.String }, ValueType = new IrArrayType { ElementType = new IrNullableType { InnerType = uuid } } } }],
            }],
            TypeDependencies = new IrTypeDependencyGraph
            {
                Dependencies = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { ["UuidBoundary"] = [] },
                TopologicalOrder = ["UuidBoundary"],
            },
        };
    }

    /// <summary>Executes the production standalone pipeline with independent provider and serializer choices.</summary>
    /// <param name="backend">The explicit UUID provider.</param>
    /// <param name="serializers">Whether the independent JSON layer is requested.</param>
    /// <returns>The generated artifacts and pipeline diagnostics.</returns>
    private static async Task<GenerationContext> Generate(CppUuidBackend backend, bool serializers)
    {
        var options = new CppGenerationOptions
        {
            Runtime = CppRuntimeProfile.Std, Namespace = "UuidBoundaryTests", UuidBackend = backend,
            CxxStandard = backend == CppUuidBackend.ModernUuid ? 20 : 17, EmitSerializers = serializers,
        };
        using var services = new ServiceCollection().AddSingleton<ICppAstRenderer, CppAstRenderer>().BuildServiceProvider();
        var context = new GenerationContext
        {
            IrDocument = CreateDocument(), PluginOptions = options,
            Configuration = new GenerationConfiguration { LanguageId = "cpp", PluginOptions = options, GenerateClients = false },
        };
        await new CppGeneratorPlugin().CreatePipeline(services).ExecuteAsync(context);
        return context;
    }
}
