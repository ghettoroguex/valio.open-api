using FluentAssertions;
using NUnit.Framework;
using ValIO.Generators.TypeScript.Options;
using ValIO.Generators.TypeScript.Tests.TestHelpers;

namespace ValIO.Generators.TypeScript.Tests.Steps;

/// <summary>
/// Unit tests for <c>OFetchClientStep</c> — emits <c>{op}.ofetch.ts</c>.
/// </summary>
[TestFixture]
public class OFetchClientStepTests
{
    private PipelineTestHarness _harness = null!;

    [OneTimeSetUp]
    public void Setup() => _harness = new PipelineTestHarness();

    [OneTimeTearDown]
    public void Teardown() => _harness.Dispose();

    [Test]
    public async Task NoOFetchTransport_NoOFetchFiles()
    {
        var doc = await PipelineTestHarness.LoadDocumentAsync("simple-schema.json");
        var ctx = await _harness.RunAsync(doc, new TypeScriptGenerationOptions { Transports = ["fetch"] });

        ctx.GeneratedFiles.Should().NotContain(f =>
            f.RelativePath.EndsWith(".ofetch.ts", StringComparison.Ordinal));
    }

    /// <summary>Verifies OFetchClient_EmitsRawCallAndIgnoreError with native transport injection enabled.</summary>
    /// <returns>The asynchronous generation check.</returns>
    [Test]
    public async Task OFetchClient_EmitsRawCallAndIgnoreError()
    {
        var doc = await PipelineTestHarness.LoadDocumentAsync("simple-schema.json");
        var ctx = await _harness.RunAsync(doc, new TypeScriptGenerationOptions { Transports = ["ofetch"] });

        var of = ctx.GeneratedFiles.First(f =>
            f.RelativePath.EndsWith("getUserById.ofetch.ts", StringComparison.Ordinal));
        of.Content.Should().Contain("import { $fetch } from \"ofetch\"");
        of.Content.Should().Contain("httpClient.raw");
        of.Content.Should().Contain("ignoreResponseError");
        of.Content.Should().Contain("response.status");
    }

    [Test]
    public async Task OFetchClient_ReadsTypedDataField()
    {
        var doc = await PipelineTestHarness.LoadDocumentAsync("simple-schema.json");
        var ctx = await _harness.RunAsync(doc, new TypeScriptGenerationOptions { Transports = ["ofetch"] });

        var of = ctx.GeneratedFiles.First(f =>
            f.RelativePath.EndsWith("getUserById.ofetch.ts", StringComparison.Ordinal));
        of.Content.Should().Contain("response._data");
    }
}
