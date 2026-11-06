using FluentAssertions;
using NUnit.Framework;
using ValIO.Generators.TypeScript.Options;
using ValIO.Generators.TypeScript.Tests.TestHelpers;

namespace ValIO.Generators.TypeScript.Tests.Steps;

/// <summary>
/// Unit tests for <c>SolidStartClientStep</c> — emits <c>{op}.solid.ts</c> with a
/// <c>"use server";</c> directive prologue and a fetch-based body.
/// </summary>
[TestFixture]
public class SolidStartClientStepTests
{
    private PipelineTestHarness _harness = null!;

    [OneTimeSetUp]
    public void Setup() => _harness = new PipelineTestHarness();

    [OneTimeTearDown]
    public void Teardown() => _harness.Dispose();

    [Test]
    public async Task NoSolidTransport_NoSolidFiles()
    {
        var doc = await PipelineTestHarness.LoadDocumentAsync("simple-schema.json");
        var ctx = await _harness.RunAsync(doc, new TypeScriptGenerationOptions { Transports = ["fetch"] });

        ctx.GeneratedFiles.Should().NotContain(f =>
            f.RelativePath.EndsWith(".solid.ts", StringComparison.Ordinal));
    }

    [Test]
    public async Task SolidTransport_EmitsUseServerDirectiveAsFirstLine()
    {
        var doc = await PipelineTestHarness.LoadDocumentAsync("simple-schema.json");
        var ctx = await _harness.RunAsync(doc, new TypeScriptGenerationOptions { Transports = ["solid"] });

        var solid = ctx.GeneratedFiles.First(f =>
            f.RelativePath.EndsWith("getUserById.solid.ts", StringComparison.Ordinal));

        // Directive prologue must precede everything else: imports, declarations.
        // SolidStart requires "use server" on the very first line so the bundler
        // identifies the file as a server function module.
        solid.Content.Should().StartWith("\"use server\";");
    }

    /// <summary>Verifies SolidTransport_PerOperationFileNamingAndFetchBody with native transport injection enabled.</summary>
    /// <returns>The asynchronous generation check.</returns>
    [Test]
    public async Task SolidTransport_PerOperationFileNamingAndFetchBody()
    {
        var doc = await PipelineTestHarness.LoadDocumentAsync("simple-schema.json");
        var ctx = await _harness.RunAsync(doc, new TypeScriptGenerationOptions { Transports = ["solid"] });

        // File naming: {clientsPath}/{op}/{op}.solid.ts, one per IR operation.
        ctx.GeneratedFiles.Should().Contain(f =>
            f.RelativePath == "api/getUserById/getUserById.solid.ts");
        ctx.GeneratedFiles.Should().Contain(f =>
            f.RelativePath == "api/getUsers/getUsers.solid.ts");

        // Body shape: fetch-based async function (no axios/ofetch/got/$fetch).
        var solid = ctx.GeneratedFiles.First(f =>
            f.RelativePath.EndsWith("getUserById.solid.ts", StringComparison.Ordinal));
        solid.Content.Should().Contain("async function");
        solid.Content.Should().Contain("await httpClient(");
        solid.Content.Should().Contain("RequestInit");
        solid.Content.Should().NotContain("axios");
        solid.Content.Should().NotContain("$fetch");
        solid.Content.Should().NotContain("import ky");
    }
}
