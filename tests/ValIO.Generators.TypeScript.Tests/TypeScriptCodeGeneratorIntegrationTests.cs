using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using NUnit.Framework;
using ValIO.Core.Interfaces;
using ValIO.Core.IR.Builders;
using ValIO.Core.Models;
using ValIO.Core.Plugins;
using ValIO.Core.Services;
using ValIO.Generators.TypeScript;
using ValIO.Generators.TypeScript.Options;

namespace ValIO.Generators.TypeScript.Tests;

[TestFixture]
public class TypeScriptCodeGeneratorIntegrationTests
{
    private IServiceProvider _services = null!;
    private OpenApiDocument _document = null!;

    [SetUp]
    public async Task Setup()
    {
        var services = new ServiceCollection();
        services.AddSingleton<INamingConverter, NamingConverter>();
        services.AddSingleton<IGeneratorPlugin, TypeScriptGeneratorPlugin>();
        services.AddSingleton<IGeneratorRegistry>(sp =>
            new GeneratorRegistry(sp.GetServices<IGeneratorPlugin>()));
        // Mirror ServiceCollectionExtensions.AddGeneratorPlugin: the plugin owns its
        // service registrations (including the ITypeScriptNodeRenderer chain).
        new TypeScriptGeneratorPlugin().ConfigureServices(services);
        _services = services.BuildServiceProvider();

        var assembly = typeof(TypeScriptCodeGeneratorIntegrationTests).Assembly;
        await using var stream = assembly.GetManifestResourceStream(
            "ValIO.Generators.TypeScript.Tests.TestData.simple-schema.json")!;
        var result = await OpenApiDocument.LoadAsync(stream, "json");
        _document = result.Document ?? throw new InvalidOperationException("Failed to parse OpenAPI document");
    }

    private async Task<GenerationContext> RunPipeline(TypeScriptGenerationOptions opts)
    {
        var namingConverter = _services.GetRequiredService<INamingConverter>();
        var schemaResolver = new SchemaResolver(_document);
        var irBuilder = new IrBuilder(schemaResolver, namingConverter);
        var irDocument = irBuilder.BuildFromOpenApi(_document);

        var config = new GenerationConfiguration
        {
            LanguageId = "typescript",
            GenerateModels = true,
            GenerateClients = true,
            GenerateEnums = true,
            PluginOptions = opts,
        };

        var context = new GenerationContext
        {
            IrDocument = irDocument,
            Configuration = config,
            PluginOptions = opts,
        };

        var registry = _services.GetRequiredService<IGeneratorRegistry>();
        var plugin = registry.FindPlugin("typescript")!;
        var pipeline = plugin.CreatePipeline(_services);
        await pipeline.ExecuteAsync(context);
        return context;
    }

    [Test]
    public async Task GenerateModels_WithFetch_GeneratesInterfaces()
    {
        var opts = new TypeScriptGenerationOptions { Transports = ["fetch"] };
        var context = await RunPipeline(opts);

        var modelFiles = context.GeneratedFiles
            .Where(f => f.RelativePath.EndsWith(".ts") && f.RelativePath.Contains("models"))
            .ToList();

        modelFiles.Should().NotBeEmpty("should generate model files");

        var userFile = modelFiles.FirstOrDefault(f => f.RelativePath.Contains("user", StringComparison.OrdinalIgnoreCase));
        userFile.Should().NotBeNull("should generate User model");
        userFile!.Content.Should().Contain("export interface User");
        userFile.Content.Should().Contain("id:");
        userFile.Content.Should().Contain("name:");
        userFile.Content.Should().Contain("email:");
    }

    /// <summary>Verifies GenerateClients_WithFetch_GeneratesFetchClient with native transport injection enabled.</summary>
    /// <returns>The asynchronous generation check.</returns>
    [Test]
    public async Task GenerateClients_WithFetch_GeneratesFetchClient()
    {
        var opts = new TypeScriptGenerationOptions { Transports = ["fetch"] };
        var context = await RunPipeline(opts);

        var fetchFiles = context.GeneratedFiles
            .Where(f => f.RelativePath.EndsWith(".fetch.ts"))
            .ToList();

        fetchFiles.Should().NotBeEmpty("should generate fetch client files");
        fetchFiles[0].Content.Should().Contain("httpClient(");
    }

    [Test]
    public async Task GenerateClients_WithAxios_GeneratesAxiosClient()
    {
        var opts = new TypeScriptGenerationOptions { Transports = ["axios"] };
        var context = await RunPipeline(opts);

        var axiosFiles = context.GeneratedFiles
            .Where(f => f.RelativePath.EndsWith(".axios.ts"))
            .ToList();

        axiosFiles.Should().NotBeEmpty("should generate axios client files");
        axiosFiles[0].Content.Should().Contain("axios");
    }

    [Test]
    public async Task GenerateSchemas_WithZod3_GeneratesZodSchemas()
    {
        var opts = new TypeScriptGenerationOptions
        {
            Transports = ["fetch"],
            ValidationLibrary = "zod3"
        };
        var context = await RunPipeline(opts);

        var schemaFiles = context.GeneratedFiles
            .Where(f => f.RelativePath.EndsWith(".schema.ts"))
            .ToList();

        schemaFiles.Should().NotBeEmpty("should generate schema files with zod3");

        var userSchema = schemaFiles.FirstOrDefault(f =>
            f.RelativePath.Replace('\\', '/').EndsWith("/user.schema.ts", StringComparison.OrdinalIgnoreCase));
        userSchema.Should().NotBeNull("should generate User schema");
        userSchema!.Content.Should().Contain("z.object");
        userSchema.Content.Should().Contain("UserSchema");
    }

    [Test]
    public async Task GenerateIndex_AlwaysCreatesIndexTs()
    {
        var opts = new TypeScriptGenerationOptions { Transports = ["fetch"] };
        var context = await RunPipeline(opts);

        var indexFile = context.GeneratedFiles.FirstOrDefault(f => f.RelativePath.EndsWith("index.ts"));
        indexFile.Should().NotBeNull("should generate index.ts");
        indexFile!.Content.Should().Contain("export *");
    }

    [Test]
    public async Task GenerateEnums_GeneratesUnionTypes()
    {
        var opts = new TypeScriptGenerationOptions
        {
            Transports = ["fetch"],
            EnumFormat = "union"
        };
        var context = await RunPipeline(opts);

        var enumFiles = context.GeneratedFiles
            .Where(f => f.RelativePath.Contains("UserRole", StringComparison.OrdinalIgnoreCase) && f.RelativePath.EndsWith(".ts"))
            .ToList();

        enumFiles.Should().NotBeEmpty("should generate UserRole enum file");
    }

    [Test]
    public async Task GenerateSchemas_WithZod4_UsesZod4Import()
    {
        var opts = new TypeScriptGenerationOptions
        {
            Transports = ["fetch"],
            ValidationLibrary = "zod4"
        };
        var context = await RunPipeline(opts);

        var schemaFiles = context.GeneratedFiles
            .Where(f => f.RelativePath.EndsWith(".schema.ts"))
            .ToList();

        schemaFiles.Should().NotBeEmpty("should generate schema files with zod4");
        schemaFiles[0].Content.Should().Contain("zod/v4", "zod4 should use the zod/v4 import path");
    }

    [Test]
    public async Task GenerateSchemas_WithTypeBox_GeneratesTypeBoxSchemas()
    {
        var opts = new TypeScriptGenerationOptions
        {
            Transports = ["fetch"],
            ValidationLibrary = "typebox"
        };
        var context = await RunPipeline(opts);

        var schemaFiles = context.GeneratedFiles
            .Where(f => f.RelativePath.EndsWith(".schema.ts"))
            .ToList();

        schemaFiles.Should().NotBeEmpty("should generate schema files with TypeBox");
        var userSchema = schemaFiles.FirstOrDefault(f =>
            f.RelativePath.Replace('\\', '/').EndsWith("/user.schema.ts", StringComparison.OrdinalIgnoreCase));
        userSchema.Should().NotBeNull("should generate User TypeBox schema");
        userSchema!.Content.Should().Contain("@sinclair/typebox");
        userSchema.Content.Should().Contain("export const UserSchema");
        userSchema.Content.Should().Contain("Type.Object");
        userSchema.Content.Should().Contain("Static<typeof UserSchema>");
    }

    [Test]
    public async Task GenerateSchemas_WithValibot_GeneratesValibotSchemas()
    {
        var opts = new TypeScriptGenerationOptions
        {
            Transports = ["fetch"],
            ValidationLibrary = "valibot"
        };
        var context = await RunPipeline(opts);

        var schemaFiles = context.GeneratedFiles
            .Where(f => f.RelativePath.EndsWith(".schema.ts"))
            .ToList();

        schemaFiles.Should().NotBeEmpty("should generate schema files with Valibot");
        var userSchema = schemaFiles.FirstOrDefault(f =>
            f.RelativePath.Replace('\\', '/').EndsWith("/user.schema.ts", StringComparison.OrdinalIgnoreCase));
        userSchema.Should().NotBeNull("should generate User Valibot schema");
        userSchema!.Content.Should().Contain("import * as v from \"valibot\"");
        userSchema.Content.Should().Contain("export const UserSchema");
        userSchema.Content.Should().Contain("v.object");
        userSchema.Content.Should().Contain("v.InferOutput<typeof UserSchema>");
    }

    /// <summary>Verifies GenerateClients_WithKy_GeneratesKyClient with native transport injection enabled.</summary>
    /// <returns>The asynchronous generation check.</returns>
    [Test]
    public async Task GenerateClients_WithKy_GeneratesKyClient()
    {
        var opts = new TypeScriptGenerationOptions { Transports = ["ky"] };
        var context = await RunPipeline(opts);

        var kyFiles = context.GeneratedFiles
            .Where(f => f.RelativePath.EndsWith(".ky.ts"))
            .ToList();

        kyFiles.Should().NotBeEmpty("should generate ky client files");
        kyFiles[0].Content.Should().Contain("import ky");
        kyFiles[0].Content.Should().Contain("httpClient.get");
    }

    [Test]
    public async Task GenerateClients_WithTanStackQuery_GeneratesQueryHooks()
    {
        var opts = new TypeScriptGenerationOptions
        {
            Transports = ["fetch"],
            QueryWrapper = "tanstack-react"
        };
        var context = await RunPipeline(opts);

        var queryFiles = context.GeneratedFiles
            .Where(f => f.RelativePath.EndsWith(".query.ts"))
            .ToList();

        queryFiles.Should().NotBeEmpty("should generate TanStack Query hook files");
        var getUsersQuery = queryFiles.FirstOrDefault(f => f.Content.Contains("getGetUsersQueryKey"));
        getUsersQuery.Should().NotBeNull("should generate query helper for getUsers");
        getUsersQuery!.Content.Should().Contain("@tanstack/react-query");
        getUsersQuery.Content.Should().Contain("useQuery");
        getUsersQuery.Content.Should().Contain("./getUsers.fetch");
    }

    [Test]
    public async Task GenerateClients_WithTanStackReactAndKy_ImportsKyClient()
    {
        var opts = new TypeScriptGenerationOptions
        {
            Transports = ["ky"],
            QueryWrapper = "tanstack-react"
        };
        var context = await RunPipeline(opts);

        var getUsersQuery = context.GeneratedFiles
            .Where(f => f.RelativePath.EndsWith(".query.ts"))
            .FirstOrDefault(f => f.Content.Contains("getGetUsersQueryKey"));

        getUsersQuery.Should().NotBeNull("should generate React TanStack query helper");
        getUsersQuery!.Content.Should().Contain("./getUsers.ky");
        getUsersQuery.Content.Should().NotContain("./getUsers.fetch");
    }

    [Test]
    public async Task GenerateClients_WithTanStackVue_GeneratesVueQueryHooks()
    {
        var opts = new TypeScriptGenerationOptions
        {
            Transports = ["ky"],
            QueryWrapper = "tanstack-vue"
        };
        var context = await RunPipeline(opts);

        var getUsersQuery = context.GeneratedFiles
            .Where(f => f.RelativePath.EndsWith(".query.ts"))
            .FirstOrDefault(f => f.Content.Contains("getGetUserByIdQueryKey"));

        getUsersQuery.Should().NotBeNull("should generate Vue TanStack query helper with params");
        getUsersQuery!.Content.Should().Contain("@tanstack/vue-query");
        getUsersQuery.Content.Should().Contain("MaybeRef");
        getUsersQuery.Content.Should().Contain("computed");
        getUsersQuery.Content.Should().Contain("unref");
        getUsersQuery.Content.Should().Contain("./getUserById.ky");
    }

    [Test]
    public void TypeScriptOptionsDescriptor_NormalizesTanStackAlias()
    {
        using var document = JsonDocument.Parse("{\"queryWrapper\":\"tanstack\"}");
        var rawOptions = new Dictionary<string, JsonElement>
        {
            ["queryWrapper"] = document.RootElement.GetProperty("queryWrapper")
        };

        var descriptor = new TypeScriptOptionsDescriptor();
        var options = descriptor.ParseOptions(rawOptions, out var errors);

        errors.Should().BeEmpty();
        options.Should().BeOfType<TypeScriptGenerationOptions>()
            .Which.QueryWrapper.Should().Be("tanstack-react");
    }

    [Test]
    public void TypeScriptOptionsDescriptor_NormalizesNewTransportAndValidationAliases()
    {
        using var document = JsonDocument.Parse("{\"transports\":[\"Ky\"],\"validation\":\"@sinclair/typebox\"}");
        var rawOptions = new Dictionary<string, JsonElement>
        {
            ["transports"] = document.RootElement.GetProperty("transports"),
            ["validation"] = document.RootElement.GetProperty("validation")
        };

        var descriptor = new TypeScriptOptionsDescriptor();
        var options = descriptor.ParseOptions(rawOptions, out var errors);

        errors.Should().BeEmpty();
        var tsOptions = options.Should().BeOfType<TypeScriptGenerationOptions>().Subject;
        tsOptions.Transports.Should().Equal("ky");
        tsOptions.ValidationLibrary.Should().Be("typebox");
    }

    [Test]
    public void TypeScriptOptionsDescriptor_ReportsUnknownTransport()
    {
        using var document = JsonDocument.Parse("{\"transports\":[\"mystery\"]}");
        var rawOptions = new Dictionary<string, JsonElement>
        {
            ["transports"] = document.RootElement.GetProperty("transports")
        };

        var descriptor = new TypeScriptOptionsDescriptor();
        var options = descriptor.ParseOptions(rawOptions, out var errors);

        options.Should().BeOfType<TypeScriptGenerationOptions>()
            .Which.Transports.Should().BeEmpty();
        errors.Should().ContainSingle(e => e.Contains("Unknown transport"));
    }

    [Test]
    public void TypeScriptOptionsDescriptor_ResultTypeSuffix_DefaultsAliasAndCustom()
    {
        var descriptor = new TypeScriptOptionsDescriptor();

        // Absent key → default "Results".
        descriptor.ParseOptions(new Dictionary<string, JsonElement>(), out _)
            .Should().BeOfType<TypeScriptGenerationOptions>()
            .Which.ResultTypeSuffix.Should().Be("Results");

        // Preset, case-insensitive → canonical PascalCase.
        using var preset = JsonDocument.Parse("{\"resultTypeSuffix\":\"RESPONSE\"}");
        descriptor.ParseOptions(
                new Dictionary<string, JsonElement> { ["resultTypeSuffix"] = preset.RootElement.GetProperty("resultTypeSuffix") },
                out var presetErrors)
            .Should().BeOfType<TypeScriptGenerationOptions>()
            .Which.ResultTypeSuffix.Should().Be("Response");
        presetErrors.Should().BeEmpty();

        // Alias key `resultSuffix` is honoured.
        using var alias = JsonDocument.Parse("{\"resultSuffix\":\"result\"}");
        descriptor.ParseOptions(
                new Dictionary<string, JsonElement> { ["resultSuffix"] = alias.RootElement.GetProperty("resultSuffix") },
                out _)
            .Should().BeOfType<TypeScriptGenerationOptions>()
            .Which.ResultTypeSuffix.Should().Be("Result");

        // Custom value passes through (first char capitalised), no error.
        using var custom = JsonDocument.Parse("{\"resultTypeSuffix\":\"outcome\"}");
        descriptor.ParseOptions(
                new Dictionary<string, JsonElement> { ["resultTypeSuffix"] = custom.RootElement.GetProperty("resultTypeSuffix") },
                out var customErrors)
            .Should().BeOfType<TypeScriptGenerationOptions>()
            .Which.ResultTypeSuffix.Should().Be("Outcome");
        customErrors.Should().BeEmpty();
    }

    [Test]
    public void TypeScriptOptionsDescriptor_UuidFormat_DefaultsAndNormalizes()
    {
        var descriptor = new TypeScriptOptionsDescriptor();

        // Absent → default "guid".
        descriptor.ParseOptions(new Dictionary<string, JsonElement>(), out _)
            .Should().BeOfType<TypeScriptGenerationOptions>()
            .Which.UuidFormat.Should().Be("guid");

        // Aliases / casing normalised to canonical modes; unknown → default.
        foreach (var (input, expected) in new[]
        {
            ("string", "string"), ("None", "string"), ("REGEX", "regex"),
            ("pattern", "regex"), ("uuid", "guid"), ("mystery", "guid"),
        })
        {
            using var d = JsonDocument.Parse($"{{\"uuidFormat\":\"{input}\"}}");
            descriptor.ParseOptions(
                    new Dictionary<string, JsonElement> { ["uuidFormat"] = d.RootElement.GetProperty("uuidFormat") },
                    out _)
                .Should().BeOfType<TypeScriptGenerationOptions>()
                .Which.UuidFormat.Should().Be(expected, $"'{input}' should normalise to '{expected}'");
        }
    }

    [Test]
    public void TypeScriptOptionsDescriptor_AxiosCustomInstance_ParsedAndValidated()
    {
        var descriptor = new TypeScriptOptionsDescriptor();

        // Both path + name → parsed onto opts.Axios, no errors. The .ts extension is kept
        // here (it is stripped later, when the import specifier is emitted).
        using var full = JsonDocument.Parse(
            "{\"axios\":{\"customInstancePath\":\"@shared/api/api-mutator.ts\",\"customInstanceName\":\"apiMutator\"}}");
        var parsed = descriptor.ParseOptions(
            new Dictionary<string, JsonElement> { ["axios"] = full.RootElement.GetProperty("axios") },
            out var errors);
        errors.Should().BeEmpty();
        var axios = parsed.Should().BeOfType<TypeScriptGenerationOptions>().Subject.Axios;
        axios.Should().NotBeNull();
        axios!.CustomInstancePath.Should().Be("@shared/api/api-mutator.ts");
        axios.CustomInstanceName.Should().Be("apiMutator");

        // Absent `axios` → null.
        descriptor.ParseOptions(new Dictionary<string, JsonElement>(), out _)
            .Should().BeOfType<TypeScriptGenerationOptions>()
            .Which.Axios.Should().BeNull();

        // Partial config (path without name) → validation error.
        using var partial = JsonDocument.Parse("{\"axios\":{\"customInstancePath\":\"@shared/api/api-mutator\"}}");
        descriptor.ParseOptions(
            new Dictionary<string, JsonElement> { ["axios"] = partial.RootElement.GetProperty("axios") },
            out var partialErrors);
        partialErrors.Should().ContainSingle(e => e.Contains("customInstanceName"));
    }

    [Test]
    public async Task GeneratorRegistry_FindsTypeScriptPlugin()
    {
        var registry = _services.GetRequiredService<IGeneratorRegistry>();

        registry.FindPlugin("typescript").Should().NotBeNull();
        registry.FindPlugin("ts").Should().NotBeNull();
        registry.FindPlugin("TYPESCRIPT").Should().NotBeNull("should be case-insensitive");
        registry.FindPlugin("php").Should().BeNull("unknown language should return null");
    }
}
