using FluentAssertions;
using NUnit.Framework;
using ValIO.Generators.TypeScript.Options;
using ValIO.Generators.TypeScript.Tests.TestHelpers;

namespace ValIO.Generators.TypeScript.Tests.Steps;

/// <summary>
/// Unit tests for <c>UndiciClientStep</c> — emits <c>{op}.undici.ts</c> with
/// Undici-based operation clients built fully through AST.
/// </summary>
[TestFixture]
public class UndiciClientStepTests
{
    private PipelineTestHarness _harness = null!;

    [OneTimeSetUp]
    public void Setup() => _harness = new PipelineTestHarness();

    [OneTimeTearDown]
    public void Teardown() => _harness.Dispose();

    [Test]
    public async Task NoUndiciTransport_NoUndiciFiles()
    {
        var doc = await PipelineTestHarness.LoadDocumentAsync("simple-schema.json");
        var ctx = await _harness.RunAsync(doc, new TypeScriptGenerationOptions { Transports = ["fetch"] });

        ctx.GeneratedFiles.Should().NotContain(f =>
            f.RelativePath.EndsWith(".undici.ts", StringComparison.Ordinal));
    }

    /// <summary>Verifies UndiciClient_EmitsRequestImportAndCall with native transport injection enabled.</summary>
    /// <returns>The asynchronous generation check.</returns>
    [Test]
    public async Task UndiciClient_EmitsRequestImportAndCall()
    {
        var doc = await PipelineTestHarness.LoadDocumentAsync("simple-schema.json");
        var ctx = await _harness.RunAsync(doc, new TypeScriptGenerationOptions { Transports = ["undici"] });

        var undici = ctx.GeneratedFiles.First(f =>
            f.RelativePath.EndsWith("getUserById.undici.ts", StringComparison.Ordinal));
        undici.Content.Should().Contain("import { request } from \"undici\"");
        undici.Content.Should().Contain("await httpClient(");
        undici.Content.Should().Contain("response.statusCode");
    }

    [Test]
    public async Task UndiciClient_JsonBody_UsesJsonStringify()
    {
        var doc = await PipelineTestHarness.LoadDocumentAsync("petstore.json");
        var ctx = await _harness.RunAsync(doc, new TypeScriptGenerationOptions { Transports = ["undici"] });

        var addPet = ctx.GeneratedFiles.First(f =>
            f.RelativePath.EndsWith("addPet.undici.ts", StringComparison.Ordinal));
        addPet.Content.Should().Contain("JSON.stringify");
    }

    [Test]
    public async Task UndiciClient_BodyJsonAwaited()
    {
        var doc = await PipelineTestHarness.LoadDocumentAsync("simple-schema.json");
        var ctx = await _harness.RunAsync(doc, new TypeScriptGenerationOptions { Transports = ["undici"] });

        var get = ctx.GeneratedFiles.First(f =>
            f.RelativePath.EndsWith("getUserById.undici.ts", StringComparison.Ordinal));
        get.Content.Should().Contain("response.body.json()");
    }
}
