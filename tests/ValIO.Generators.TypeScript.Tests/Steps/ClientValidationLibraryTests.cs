using FluentAssertions;
using Microsoft.OpenApi;
using NUnit.Framework;
using ValIO.Generators.TypeScript.Options;
using ValIO.Generators.TypeScript.Tests.TestHelpers;

namespace ValIO.Generators.TypeScript.Tests.Steps;

/// <summary>
/// Axios/fetch клиенты должны валидировать тело/ответ выбранной библиотекой
/// (а не хардкодить Zod): zod → <c>Schema.parse(x)</c>, valibot → <c>v.parse(Schema, x)</c>,
/// typebox → <c>Value.Decode(Schema, x)</c>, с корректным runtime-импортом.
/// </summary>
[TestFixture]
public class ClientValidationLibraryTests
{
    private PipelineTestHarness _harness = null!;
    private OpenApiDocument _document = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetup()
    {
        _harness = new PipelineTestHarness();
        _document = await PipelineTestHarness.LoadDocumentAsync("status-union-response.json");
    }

    [OneTimeTearDown]
    public void OneTimeTeardown() => _harness.Dispose();

    private async Task<string> ClientAsync(string transport, string validation, string suffix)
    {
        var ctx = await _harness.RunAsync(_document, new TypeScriptGenerationOptions
        {
            Transports = [transport],
            ValidationLibrary = validation,
        });

        return ctx.GeneratedFiles
            .First(f => f.RelativePath.EndsWith($"getThing{suffix}", StringComparison.Ordinal))
            .Content;
    }

    [TestCase("axios", ".axios.ts")]
    [TestCase("fetch", ".fetch.ts")]
    public async Task Valibot_UsesVParseAndImportsValibotRuntime(string transport, string suffix)
    {
        var content = await ClientAsync(transport, "valibot", suffix);
        content.Should().Contain("import * as v from \"valibot\"");
        content.Should().MatchRegex(@"v\.parse\(\s*ProblemDetailsSchema");
        content.Should().NotContain("ProblemDetailsSchema.parse(");
    }

    /// <summary>Verifies TypeBox validates response values without coercion or instance-destroying cloning.</summary>
    /// <param name="transport">The generated transport.</param>
    /// <param name="suffix">The transport file suffix.</param>
    [TestCase("axios", ".axios.ts")]
    [TestCase("fetch", ".fetch.ts")]
    public async Task TypeBox_UsesValueDecodeAndImportsTypeBoxRuntime(string transport, string suffix)
    {
        var content = await ClientAsync(transport, "typebox", suffix);
        content.Should().Contain("@sinclair/typebox/value");
        content.Should().MatchRegex(@"Value\.Decode\(\s*ProblemDetailsSchema");
        content.Should().NotContain("ProblemDetailsSchema.parse(");
    }

    /// <summary>Verifies Zod uses its named schema's parse method without importing other validator runtimes.</summary>
    /// <param name="transport">The generated transport.</param>
    /// <param name="suffix">The transport file suffix.</param>
    /// <param name="validation">The selected Zod major.</param>
    [TestCase("axios", ".axios.ts", "zod3")]
    [TestCase("axios", ".axios.ts", "zod4")]
    [TestCase("fetch", ".fetch.ts", "zod3")]
    public async Task Zod_UsesSchemaDotParse(string transport, string suffix, string validation)
    {
        var content = await ClientAsync(transport, validation, suffix);
        content.Should().Contain("ProblemDetailsSchema.parse(");
        content.Should().NotContain("v.parse(");
        content.Should().NotContain("Value.Decode(");
    }
}
