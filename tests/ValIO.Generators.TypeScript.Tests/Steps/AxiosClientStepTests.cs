using FluentAssertions;
using NUnit.Framework;
using ValIO.Generators.TypeScript.Options;
using ValIO.Generators.TypeScript.Tests.TestHelpers;

namespace ValIO.Generators.TypeScript.Tests.Steps;

[TestFixture]
public class AxiosClientStepTests
{
    private PipelineTestHarness _harness = null!;

    [OneTimeSetUp]
    public void Setup() => _harness = new PipelineTestHarness();

    [OneTimeTearDown]
    public void Teardown() => _harness.Dispose();

    [Test]
    public async Task AxiosClient_ImportsAxiosAndConfigType()
    {
        var doc = await PipelineTestHarness.LoadDocumentAsync("simple-schema.json");
        var ctx = await _harness.RunAsync(doc, new TypeScriptGenerationOptions { Transports = ["axios"] });

        var any = ctx.GeneratedFiles.First(f =>
            f.RelativePath.EndsWith(".axios.ts", StringComparison.Ordinal));
        any.Content.Should().Contain("import axios");
        any.Content.Should().Contain("AxiosRequestConfig");
    }

    [Test]
    public async Task AxiosClient_TypeImports_AreSplitOnePerLine()
    {
        var doc = await PipelineTestHarness.LoadDocumentAsync("simple-schema.json");
        var ctx = await _harness.RunAsync(doc, new TypeScriptGenerationOptions { Transports = ["axios"] });

        var any = ctx.GeneratedFiles.First(f =>
            f.RelativePath.EndsWith(".axios.ts", StringComparison.Ordinal));

        // Type-only imports are emitted one specifier per line, not merged into `{ A, B }`.
        any.Content.Should().Contain("import type { AxiosRequestConfig } from \"axios\";");
        any.Content.Should().Contain("import type { AxiosInstance } from \"axios\";");
        any.Content.Should().NotContain("import type { AxiosRequestConfig, AxiosInstance }");
    }

    [Test]
    public async Task AxiosClient_HeadersAreCast_ToAxiosRequestConfigHeaders()
    {
        var doc = await PipelineTestHarness.LoadDocumentAsync("petstore.json");
        var ctx = await _harness.RunAsync(doc, new TypeScriptGenerationOptions { Transports = ["axios"] });

        var deletePet = ctx.GeneratedFiles.First(f =>
            f.RelativePath.EndsWith("deletePet.axios.ts", StringComparison.Ordinal));
        deletePet.Content.Should().Contain("AxiosRequestConfig[\"headers\"]");
    }

    [Test]
    public async Task AxiosClient_WithZod_DeduplicatesModelSchemaImports()
    {
        var doc = await PipelineTestHarness.LoadDocumentAsync("petstore.json");
        var ctx = await _harness.RunAsync(doc, new TypeScriptGenerationOptions
        {
            Transports = ["axios"],
            ValidationLibrary = "zod3",
        });

        // addPet has Pet as both request body and 200 response. Previous code
        // emitted PetSchema import twice → TS2300 duplicate identifier.
        var addPet = ctx.GeneratedFiles.First(f =>
            f.RelativePath.EndsWith("addPet.axios.ts", StringComparison.Ordinal));
        var petSchemaImports = System.Text.RegularExpressions.Regex.Matches(
            addPet.Content, @"import \{ PetSchema \}").Count;
        petSchemaImports.Should().Be(1, "PetSchema must be imported exactly once even when used for body + response");
    }

    /// <summary>Uses the configured Axios export unless a caller supplies a client.</summary>
    [Test]
    public async Task AxiosClient_CustomInstance_UsesNamespaceDefaultWithOptionalOverride()
    {
        var doc = await PipelineTestHarness.LoadDocumentAsync("simple-schema.json");
        var ctx = await _harness.RunAsync(doc, new TypeScriptGenerationOptions
        {
            Transports = ["axios"],
            Axios = new TypeScriptAxiosOptions
            {
                CustomInstancePath = "@shared/api/api-mutator.ts",
                CustomInstanceName = "apiMutator",
            },
        });

        var any = ctx.GeneratedFiles.First(f =>
            f.RelativePath.EndsWith(".axios.ts", StringComparison.Ordinal));

        // A namespace import avoids collisions while stripping the .ts module extension.
        any.Content.Should().Contain("import * as axiosDefaults from \"@shared/api/api-mutator\"");
        // The instance is assigned into the local axiosInstance variable...
        any.Content.Should().Contain("const axiosInstance: AxiosInstance = (client ?? axiosDefaults.apiMutator)");
        // ...and the global axios default import is no longer emitted.
        any.Content.Should().NotContain("import axios from");
    }

    /// <summary>Falls back to the global Axios instance when no custom default is configured.</summary>
    [Test]
    public async Task AxiosClient_NoCustomInstance_FallsBackToGlobalAxios()
    {
        var doc = await PipelineTestHarness.LoadDocumentAsync("simple-schema.json");
        var ctx = await _harness.RunAsync(doc, new TypeScriptGenerationOptions { Transports = ["axios"] });

        var any = ctx.GeneratedFiles.First(f =>
            f.RelativePath.EndsWith(".axios.ts", StringComparison.Ordinal));

        any.Content.Should().Contain("import axios from \"axios\"");
        any.Content.Should().Contain("const axiosInstance: AxiosInstance = (client ?? axios)");
    }

    [Test]
    public async Task AxiosClient_NoZod_NoSchemaImports()
    {
        var doc = await PipelineTestHarness.LoadDocumentAsync("simple-schema.json");
        var ctx = await _harness.RunAsync(doc, new TypeScriptGenerationOptions { Transports = ["axios"] });

        var any = ctx.GeneratedFiles.First(f =>
            f.RelativePath.EndsWith(".axios.ts", StringComparison.Ordinal));
        any.Content.Should().NotContain(".schema");
    }
}
