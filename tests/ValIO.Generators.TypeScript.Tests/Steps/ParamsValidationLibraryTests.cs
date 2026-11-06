using FluentAssertions;
using NUnit.Framework;
using ValIO.Generators.TypeScript.Options;
using ValIO.Generators.TypeScript.Tests.TestHelpers;

namespace ValIO.Generators.TypeScript.Tests.Steps;

/// <summary>
/// Route/query параметры валидируются единообразно для ВСЕХ библиотек по импортируемой именованной
/// схеме из <c>{op}.params.schema.ts</c> (<c>{Op}RouteParamsSchema</c>). Сама схема
/// (<c>z.object</c> / <c>v.object</c> / <c>Type.Object</c>) объявляется в отдельном файле — клиент её
/// только импортирует и парсит. Раньше zod импортировал схему, а valibot/typebox строили её inline
/// (расхождение реализаций).
/// </summary>
[TestFixture]
public class ParamsValidationLibraryTests
{
    private PipelineTestHarness _harness = null!;

    [OneTimeSetUp]
    public void Setup() => _harness = new PipelineTestHarness();

    [OneTimeTearDown]
    public void Teardown() => _harness.Dispose();

    private async Task<(string Client, string ParamsSchema)> GetPetByIdFilesAsync(string validation)
    {
        var doc = await PipelineTestHarness.LoadDocumentAsync("petstore.json");
        var ctx = await _harness.RunAsync(doc, new TypeScriptGenerationOptions
        {
            Transports = ["axios"],
            ValidationLibrary = validation,
        });

        var client = ctx.GeneratedFiles
            .First(f => f.RelativePath.EndsWith("getPetById.axios.ts", StringComparison.Ordinal))
            .Content;
        var paramsSchema = ctx.GeneratedFiles
            .First(f => f.RelativePath.EndsWith("getPetById.params.schema.ts", StringComparison.Ordinal))
            .Content;
        return (client, paramsSchema);
    }

    /// <summary>Verifies Zod imports a named parameter schema and uses the selected validation runtime.</summary>
    /// <param name="validation">The selected Zod major.</param>
    [TestCase("zod3")]
    [TestCase("zod4")]
    public async Task Zod_RouteParams_UseImportedNamedSchema(string validation)
    {
        var (client, paramsSchema) = await GetPetByIdFilesAsync(validation);
        client.Should().Contain("GetPetByIdRouteParamsSchema.parse");
        client.Should().Contain(".params.schema");
        client.Should().NotContain("v.parse(");
        client.Should().NotContain("Value.Decode(");
        paramsSchema.Should().Contain("export const GetPetByIdRouteParamsSchema");
        paramsSchema.Should().Contain("z.object(");
    }

    [Test]
    public async Task Valibot_RouteParams_ImportedNamedSchema_BuiltInParamsFile()
    {
        var (client, paramsSchema) = await GetPetByIdFilesAsync("valibot");
        // Клиент импортирует именованную схему и парсит её — без inline v.object.
        client.Should().Contain("import * as v from \"valibot\"");
        client.Should().MatchRegex(@"v\.parse\(\s*GetPetByIdRouteParamsSchema");
        client.Should().Contain(".params.schema");
        client.Should().NotContain("v.object(");
        // Схема строится в отдельном файле.
        paramsSchema.Should().Contain("export const GetPetByIdRouteParamsSchema");
        paramsSchema.Should().Contain("v.object(");
    }

    /// <summary>Verifies TypeBox validates against the parameter schema declared in its own module.</summary>
    [Test]
    public async Task TypeBox_RouteParams_ImportedNamedSchema_BuiltInParamsFile()
    {
        var (client, paramsSchema) = await GetPetByIdFilesAsync("typebox");
        client.Should().Contain("@sinclair/typebox");
        client.Should().MatchRegex(@"Value\.Decode\(\s*GetPetByIdRouteParamsSchema");
        client.Should().Contain(".params.schema");
        client.Should().NotContain("Type.Object(");
        paramsSchema.Should().Contain("export const GetPetByIdRouteParamsSchema");
        paramsSchema.Should().Contain("Type.Object(");
    }
}
