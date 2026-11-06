using FluentAssertions;
using NUnit.Framework;
using ValIO.Generators.TypeScript.Options;
using ValIO.Generators.TypeScript.Tests.TestHelpers;

namespace ValIO.Generators.TypeScript.Tests.Steps;

/// <summary>
/// Unit tests for <c>KyClientStep</c> — emits <c>{op}.ky.ts</c> with
/// Ky-based operation clients built fully through AST.
/// </summary>
[TestFixture]
public class KyClientStepTests
{
    private PipelineTestHarness _harness = null!;

    [OneTimeSetUp]
    public void Setup() => _harness = new PipelineTestHarness();

    [OneTimeTearDown]
    public void Teardown() => _harness.Dispose();

    [Test]
    public async Task NoKyTransport_NoKyFiles()
    {
        var doc = await PipelineTestHarness.LoadDocumentAsync("simple-schema.json");
        var ctx = await _harness.RunAsync(doc, new TypeScriptGenerationOptions { Transports = ["fetch"] });

        ctx.GeneratedFiles.Should().NotContain(f =>
            f.RelativePath.EndsWith(".ky.ts", StringComparison.Ordinal));
    }

    /// <summary>Verifies KyClient_GetByPathParam_EmitsKyGetCall with native transport injection enabled.</summary>
    /// <returns>The asynchronous generation check.</returns>
    [Test]
    public async Task KyClient_GetByPathParam_EmitsKyGetCall()
    {
        var doc = await PipelineTestHarness.LoadDocumentAsync("simple-schema.json");
        var ctx = await _harness.RunAsync(doc, new TypeScriptGenerationOptions { Transports = ["ky"] });

        var ky = ctx.GeneratedFiles.First(f =>
            f.RelativePath.EndsWith("getUserById.ky.ts", StringComparison.Ordinal));
        ky.Content.Should().Contain("import ky");
        ky.Content.Should().Contain("httpClient.get");
        ky.Content.Should().Contain("requestUrl");
    }

    [Test]
    public async Task KyClient_SchemaImportPath_IsCamelCase()
    {
        var doc = await PipelineTestHarness.LoadDocumentAsync("petstore.json");
        var ctx = await _harness.RunAsync(doc, new TypeScriptGenerationOptions
        {
            Transports = ["ky"],
            ValidationLibrary = "zod3",
        });

        // Regression: ky originally generated `../../models/Pet/Pet.schema`
        // (PascalCase), causing TS1261 on case-sensitive filesystems.
        var withSchema = ctx.GeneratedFiles.First(f =>
            f.RelativePath.EndsWith(".ky.ts", StringComparison.Ordinal)
            && f.Content.Contains("Schema", StringComparison.Ordinal));
        withSchema.Content.Should().NotMatchRegex(@"models/[A-Z]\w*/[A-Z]\w*\.schema");
        withSchema.Content.Should().MatchRegex(@"models/[a-z]\w*/[a-z]\w*\.schema");
    }

    [Test]
    public async Task KyClient_HeadersAreCast_ToOptionsHeaders()
    {
        var doc = await PipelineTestHarness.LoadDocumentAsync("petstore.json");
        var ctx = await _harness.RunAsync(doc, new TypeScriptGenerationOptions { Transports = ["ky"] });

        // Petstore's deletePet has an `api_key` header. Ky's typed headers
        // can't accept narrow header interfaces directly — needs `as Options["headers"]`.
        var deletePet = ctx.GeneratedFiles.First(f =>
            f.RelativePath.EndsWith("deletePet.ky.ts", StringComparison.Ordinal));
        deletePet.Content.Should().Contain("Options[\"headers\"]");
    }

    [Test]
    public async Task KyClient_ArrayQueryParams_CastToOptionsSearchParams()
    {
        var doc = await PipelineTestHarness.LoadDocumentAsync("petstore.json");
        var ctx = await _harness.RunAsync(doc, new TypeScriptGenerationOptions { Transports = ["ky"] });

        // findPetsByTags accepts tags as a string[]. Ky's SearchParamsObject
        // only accepts scalar values, so the searchParams literal is cast
        // through unknown to Options["searchParams"].
        var findByTags = ctx.GeneratedFiles.First(f =>
            f.RelativePath.EndsWith("findPetsByTags.ky.ts", StringComparison.Ordinal));
        findByTags.Content.Should().Contain("Options[\"searchParams\"]");
    }

    [Test]
    public async Task KyClient_BinaryBody_UsesBodyOptionAndAccessesParamsBody()
    {
        var doc = await PipelineTestHarness.LoadDocumentAsync("petstore.json");
        var ctx = await _harness.RunAsync(doc, new TypeScriptGenerationOptions { Transports = ["ky"] });

        var upload = ctx.GeneratedFiles.First(f =>
            f.RelativePath.EndsWith("uploadFile.ky.ts", StringComparison.Ordinal));
        // Binary body → access `params.body` (not params.form, not params.json)
        // and pass through ky's `body:` option.
        upload.Content.Should().Contain("params.body");
        upload.Content.Should().NotContain("params.form");
    }

    [Test]
    public async Task KyClient_JsonBody_UsesJsonOption()
    {
        var doc = await PipelineTestHarness.LoadDocumentAsync("petstore.json");
        var ctx = await _harness.RunAsync(doc, new TypeScriptGenerationOptions { Transports = ["ky"] });

        var addPet = ctx.GeneratedFiles.First(f =>
            f.RelativePath.EndsWith("addPet.ky.ts", StringComparison.Ordinal));
        addPet.Content.Should().Contain("json:");
    }
}
