using FluentAssertions;
using Microsoft.OpenApi;
using NUnit.Framework;
using ValIO.Core.Plugins;
using ValIO.Generators.TypeScript.Options;
using ValIO.Generators.TypeScript.Tests.TestHelpers;
using ValIO.Tests.Fixtures;

namespace ValIO.Generators.TypeScript.Tests.Steps;

/// <summary>
/// Тело ответа с union-типом (oneOf, напр. <c>ProblemDetails | HttpValidationProblemDetails</c>)
/// валидируется ВЫНЕСЕННОЙ именованной схемой из <c>{op}.results.schemas.ts</c> (строится один раз),
/// а клиент ссылается на неё по имени — не пересоздавая union-схему в рантайме на каждый вызов.
/// Голые ссылки на модель (200/404) валидируются напрямую <c>{Model}Schema</c>.
/// </summary>
[TestFixture]
public class UnionResponseValidationTests
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

    private async Task<GenerationContext> RunAsync(string validation)
        => await _harness.RunAsync(_document, new TypeScriptGenerationOptions
        {
            Transports = ["axios"],
            ValidationLibrary = validation,
        });

    private static string File(GenerationContext ctx, string suffix)
        => ctx.GeneratedFiles.First(f => f.RelativePath.Replace('\\', '/').EndsWith($"getThing{suffix}", StringComparison.Ordinal)).Content;

    /// <summary>Verifies response alternatives are checked exactly once and referenced through a shared schema.</summary>
    /// <param name="validation">The selected Zod version.</param>
    [TestCase("zod3")]
    [TestCase("zod4")]
    public async Task Zod_UnionResponse_ExtractedToResultsSchemas_AndReferencedByName(string validation)
    {
        var ctx = await RunAsync(validation);
        var schemas = File(ctx, ".results.schemas.ts");
        var client = File(ctx, ".axios.ts");

        // Union schema declared ONCE in the results.schemas file.
        schemas.Should().Contain("export const GetThingResult400Schema = z.custom<unknown>(")
            .And.Contain("z.union([ProblemDetailsSchema, HttpValidationProblemDetailsSchema])");
        schemas.Should().Contain("import { HttpValidationProblemDetailsSchema }");

        // Client references it by name (no inline z.union, no z import).
        client.Should().Contain("import { GetThingResult400Schema } from \"./getThing.results.schemas\"");
        client.Should().Contain("GetThingResult400Schema.parse(response.data)");
        client.Should().NotContain("z.union(");
        client.Should().NotContain("from \"zod");
        // Bare model ref (200) validated directly.
        client.Should().Contain("ThingSchema.parse(response.data)");
    }

    /// <summary>Verifies Valibot response alternatives retain exact-one matching.</summary>
    [Test]
    public async Task Valibot_UnionResponse_ExtractedToResultsSchemas()
    {
        var ctx = await RunAsync("valibot");
        var schemas = File(ctx, ".results.schemas.ts");
        var client = File(ctx, ".axios.ts");

        schemas.Should().Contain("import * as v from \"valibot\"");
        schemas.Should().Contain("export const GetThingResult400Schema = v.pipe(")
            .And.Contain("v.check(").And.Contain("v.union([ProblemDetailsSchema, HttpValidationProblemDetailsSchema])");

        client.Should().Contain("import { GetThingResult400Schema } from \"./getThing.results.schemas\"");
        client.Should().MatchRegex(@"v\.parse\(\s*GetThingResult400Schema");
        client.Should().NotContain("v.union(");
    }

    /// <summary>Verifies TypeBox response alternatives reject pairwise overlaps.</summary>
    [Test]
    public async Task TypeBox_UnionResponse_ExtractedToResultsSchemas()
    {
        var ctx = await RunAsync("typebox");
        var schemas = File(ctx, ".results.schemas.ts");
        var client = File(ctx, ".axios.ts");

        schemas.Should().Contain("import { Type } from \"@sinclair/typebox\"");
        schemas.Should().Contain("export const GetThingResult400Schema = Type.Intersect(")
            .And.Contain("Type.Union([ProblemDetailsSchema, HttpValidationProblemDetailsSchema])").And.Contain("Type.Not(");

        client.Should().Contain("import { GetThingResult400Schema } from \"./getThing.results.schemas\"");
        client.Should().MatchRegex(@"Value\.Decode\(\s*GetThingResult400Schema");
        client.Should().NotContain("Type.Union(");
    }

    /// <summary>Verifies binary responses use Blob validation and the matching Axios response codec.</summary>
    [Test]
    public async Task Zod_BinaryResponse_UsesBlobSchemaAndAxiosBlobResponseType()
    {
        var document = await PipelineTestHarness.LoadDocumentFromJsonAsync(OpenApiFixture.Read("Validators/binary-response.json"));
        var context = await _harness.RunAsync(document, new TypeScriptGenerationOptions
        {
            Transports = ["axios"],
            ValidationLibrary = "zod4",
        });

        var schemas = context.GeneratedFiles.Single(f => f.RelativePath.Replace('\\', '/').EndsWith("getBinary.results.schemas.ts", StringComparison.Ordinal)).Content;
        var client = context.GeneratedFiles.Single(f => f.RelativePath.Replace('\\', '/').EndsWith("getBinary.axios.ts", StringComparison.Ordinal)).Content;

        schemas.Should().Contain("z.instanceof(Blob)");
        client.Should().Contain("responseType: \"blob\"");
        client.Should().Contain("GetBinaryResult200Schema.parse(response.data)");
    }
}
