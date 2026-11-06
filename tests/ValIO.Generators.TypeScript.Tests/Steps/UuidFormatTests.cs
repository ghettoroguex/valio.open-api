using FluentAssertions;
using NUnit.Framework;
using ValIO.Core.Plugins;
using ValIO.Generators.TypeScript.Options;
using ValIO.Generators.TypeScript.Tests.TestHelpers;

namespace ValIO.Generators.TypeScript.Tests.Steps;

/// <summary>
/// Tests for the <c>uuidFormat</c> option (guid / string / regex) across validation libraries,
/// using a fixture whose Document model and getDocumentById route param are <c>format: uuid</c>.
/// </summary>
[TestFixture]
public class UuidFormatTests
{
    private PipelineTestHarness _harness = null!;

    [OneTimeSetUp]
    public void Setup() => _harness = new PipelineTestHarness();

    [OneTimeTearDown]
    public void Teardown() => _harness.Dispose();

    private static string ModelSchema(GenerationContext ctx) =>
        ctx.GeneratedFiles.First(f => f.RelativePath.Replace('\\', '/').EndsWith("models/document/document.schema.ts", StringComparison.Ordinal)).Content;

    private static string ParamsSchema(GenerationContext ctx) =>
        ctx.GeneratedFiles.First(f => f.RelativePath.EndsWith("getDocumentById.params.schema.ts", StringComparison.Ordinal)).Content;

    private static bool HasFormatsFile(GenerationContext ctx) =>
        ctx.GeneratedFiles.Any(f => f.RelativePath.Replace('\\', '/').EndsWith("models/formats.ts", StringComparison.Ordinal));

    /// <summary>Verifies the default UUID policy uses the shared canonical text predicate without exporting a regex constant.</summary>
    [Test]
    public async Task DefaultGuid_Zod_EmitsUuidValidator_NoFormatsFile()
    {
        var doc = await PipelineTestHarness.LoadDocumentAsync("uuid-schema.json");
        var ctx = await _harness.RunAsync(doc, new TypeScriptGenerationOptions
        {
            Transports = ["fetch"],
            ValidationLibrary = "zod4",
            // UuidFormat left default ("guid").
        });

        ModelSchema(ctx).Should().Contain("z.string().refine(").And.Contain("[0-9a-fA-F]{8}");
        HasFormatsFile(ctx).Should().BeFalse("guid mode needs no shared regex constant");
    }

    [Test]
    public async Task StringMode_Zod_EmitsPlainString_NoFormatsFile()
    {
        var doc = await PipelineTestHarness.LoadDocumentAsync("uuid-schema.json");
        var ctx = await _harness.RunAsync(doc, new TypeScriptGenerationOptions
        {
            Transports = ["fetch"],
            ValidationLibrary = "zod4",
            UuidFormat = "string",
        });

        var model = ModelSchema(ctx);
        model.Should().Contain("id: z.string()");
        model.Should().NotContain(".uuid()");
        model.Should().NotContain(".regex(");
        HasFormatsFile(ctx).Should().BeFalse();
    }

    [Test]
    public async Task RegexMode_Zod_ModelAndParams_ReuseSharedRegexConst()
    {
        var doc = await PipelineTestHarness.LoadDocumentAsync("uuid-schema.json");
        var ctx = await _harness.RunAsync(doc, new TypeScriptGenerationOptions
        {
            Transports = ["fetch"],
            ValidationLibrary = "zod4",
            UuidFormat = "regex",
        });

        // Model schema imports the shared const from the sibling formats file and uses .regex(UUID_REGEX).
        var model = ModelSchema(ctx);
        model.Should().Contain("import { UUID_REGEX } from \"../formats\"");
        model.Should().Contain("z.string().regex(UUID_REGEX)");
        model.Should().NotContain(".uuid()");

        // Route params schema reaches the const at the root schemas folder.
        var prms = ParamsSchema(ctx);
        prms.Should().Contain("import { UUID_REGEX } from \"../../models/formats\"");
        prms.Should().Contain("z.string().regex(UUID_REGEX)");

        // The shared constant file is generated once.
        var formats = ctx.GeneratedFiles.First(f => f.RelativePath.Replace('\\', '/').EndsWith("models/formats.ts", StringComparison.Ordinal));
        formats.Content.Should().Contain("export const UUID_PATTERN");
        formats.Content.Should().Contain("export const UUID_REGEX = new RegExp(UUID_PATTERN)");
    }

    [Test]
    public async Task RegexMode_Valibot_UsesRegexActionAndImportsConst()
    {
        var doc = await PipelineTestHarness.LoadDocumentAsync("uuid-schema.json");
        var ctx = await _harness.RunAsync(doc, new TypeScriptGenerationOptions
        {
            Transports = ["fetch"],
            ValidationLibrary = "valibot",
            UuidFormat = "regex",
        });

        var model = ModelSchema(ctx);
        model.Should().Contain("import { UUID_REGEX } from \"../formats\"");
        model.Should().Contain("v.regex(UUID_REGEX)");
        HasFormatsFile(ctx).Should().BeTrue();
    }

    [Test]
    public async Task RegexMode_TypeBox_UsesPatternOptionAndImportsConst()
    {
        var doc = await PipelineTestHarness.LoadDocumentAsync("uuid-schema.json");
        var ctx = await _harness.RunAsync(doc, new TypeScriptGenerationOptions
        {
            Transports = ["fetch"],
            ValidationLibrary = "typebox",
            UuidFormat = "regex",
        });

        var model = ModelSchema(ctx);
        model.Should().Contain("import { UUID_PATTERN } from \"../formats\"");
        model.Should().Contain("pattern: UUID_PATTERN");
        HasFormatsFile(ctx).Should().BeTrue();
    }

    [Test]
    public async Task StringMode_TypeBox_EmitsPlainStringWithoutFormat()
    {
        var doc = await PipelineTestHarness.LoadDocumentAsync("uuid-schema.json");
        var ctx = await _harness.RunAsync(doc, new TypeScriptGenerationOptions
        {
            Transports = ["fetch"],
            ValidationLibrary = "typebox",
            UuidFormat = "string",
        });

        var model = ModelSchema(ctx);
        model.Should().NotContain("format: \"uuid\"");
        model.Should().NotContain("UUID_PATTERN");
        HasFormatsFile(ctx).Should().BeFalse();
    }
}
