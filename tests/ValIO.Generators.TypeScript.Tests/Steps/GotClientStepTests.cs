using FluentAssertions;
using NUnit.Framework;
using ValIO.Generators.TypeScript.Options;
using ValIO.Generators.TypeScript.Tests.TestHelpers;

namespace ValIO.Generators.TypeScript.Tests.Steps;

/// <summary>
/// Unit tests for <c>GotClientStep</c> — emits <c>{op}.got.ts</c>.
/// </summary>
[TestFixture]
public class GotClientStepTests
{
    private PipelineTestHarness _harness = null!;

    [OneTimeSetUp]
    public void Setup() => _harness = new PipelineTestHarness();

    [OneTimeTearDown]
    public void Teardown() => _harness.Dispose();

    [Test]
    public async Task NoGotTransport_NoGotFiles()
    {
        var doc = await PipelineTestHarness.LoadDocumentAsync("simple-schema.json");
        var ctx = await _harness.RunAsync(doc, new TypeScriptGenerationOptions { Transports = ["fetch"] });

        ctx.GeneratedFiles.Should().NotContain(f =>
            f.RelativePath.EndsWith(".got.ts", StringComparison.Ordinal));
    }

    /// <summary>Verifies GotClient_EmitsGotCallWithResponseTypeJson with native transport injection enabled.</summary>
    /// <returns>The asynchronous generation check.</returns>
    [Test]
    public async Task GotClient_EmitsGotCallWithResponseTypeJson()
    {
        var doc = await PipelineTestHarness.LoadDocumentAsync("simple-schema.json");
        var ctx = await _harness.RunAsync(doc, new TypeScriptGenerationOptions { Transports = ["got"] });

        var got = ctx.GeneratedFiles.First(f =>
            f.RelativePath.EndsWith("getUserById.got.ts", StringComparison.Ordinal));
        got.Content.Should().Contain("import got from \"got\"");
        got.Content.Should().Contain("await httpClient(");
        got.Content.Should().Contain("responseType");
        got.Content.Should().Contain("throwHttpErrors");
        got.Content.Should().Contain("response.statusCode");
    }

    [Test]
    public async Task GotClient_JsonBody_UsesJsonOption()
    {
        var doc = await PipelineTestHarness.LoadDocumentAsync("petstore.json");
        var ctx = await _harness.RunAsync(doc, new TypeScriptGenerationOptions { Transports = ["got"] });

        var addPet = ctx.GeneratedFiles.First(f =>
            f.RelativePath.EndsWith("addPet.got.ts", StringComparison.Ordinal));
        addPet.Content.Should().Contain("json:");
    }
}
