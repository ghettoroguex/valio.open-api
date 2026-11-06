using FluentAssertions;
using NUnit.Framework;
using ValIO.Core.Models;
using ValIO.Generators.TypeScript.Options;
using ValIO.Generators.TypeScript.Tests.TestHelpers;

namespace ValIO.Generators.TypeScript.Tests.Steps;

/// <summary>
/// Tests for <c>TypeScriptParamsSchemasStep</c> with Zod — emits
/// <c>{operation}.params.schema.ts</c> with Route/Query schemas
/// when validation is zod3 or zod4 and the operation has route/query params.
/// </summary>
[TestFixture]
public class ZodParamsSchemasStepTests
{
    private PipelineTestHarness _harness = null!;

    [OneTimeSetUp]
    public void Setup() => _harness = new PipelineTestHarness();

    [OneTimeTearDown]
    public void Teardown() => _harness.Dispose();

    [Test]
    public async Task NoZodValidation_NoParamsSchemaFiles()
    {
        var doc = await PipelineTestHarness.LoadDocumentAsync("simple-schema.json");
        var ctx = await _harness.RunAsync(doc, new TypeScriptGenerationOptions { Transports = ["fetch"] });

        ctx.GeneratedFiles.Should().NotContain(f =>
            f.RelativePath.EndsWith(".params.schema.ts", StringComparison.Ordinal));
    }

    [Test]
    public async Task Zod3WithPathParam_EmitsRouteParamsSchema()
    {
        var doc = await PipelineTestHarness.LoadDocumentAsync("simple-schema.json");
        var ctx = await _harness.RunAsync(doc, new TypeScriptGenerationOptions
        {
            Transports = ["fetch"],
            ValidationLibrary = "zod3",
        });

        var paramsSchema = ctx.GeneratedFiles.FirstOrDefault(f =>
            f.RelativePath.EndsWith("getUserById.params.schema.ts", StringComparison.Ordinal));
        paramsSchema.Should().NotBeNull();
        paramsSchema!.Content.Should().Contain("GetUserByIdRouteParamsSchema");
    }

    /// <summary>Verifies Zod schemas accept unknown input while checking their output against the parameter interface.</summary>
    [Test]
    public async Task Zod_ParamsSchema_IsTypedWithParamsInterface()
    {
        var doc = await PipelineTestHarness.LoadDocumentAsync("simple-schema.json");
        var ctx = await _harness.RunAsync(doc, new TypeScriptGenerationOptions
        {
            Transports = ["fetch"],
            ValidationLibrary = "zod3",
        });

        var paramsSchema = ctx.GeneratedFiles.First(f =>
            f.RelativePath.EndsWith("getUserById.params.schema.ts", StringComparison.Ordinal));
        // `ZodSchema` — только тип: под verbatimModuleSyntax импортируется type-only, отдельно от значения `z`.
        paramsSchema.Content.Should().Contain("import { z } from \"zod\"");
        paramsSchema.Content.Should().Contain("import type { ZodSchema } from \"zod\"");
        paramsSchema.Content.Should().Contain("import type { GetUserByIdParams } from \"./getUserById.params\"");
        paramsSchema.Content.Should().Contain("GetUserByIdRouteParamsSchema: ZodSchema<GetUserByIdParams, z.ZodTypeDef, unknown>");
    }

    /// <summary>Verifies parameter coercion preserves false strings alongside defaults and descriptions.</summary>
    [Test]
    public async Task Zod_QueryParams_Coerce_Default_AndDescribe()
    {
        var doc = await PipelineTestHarness.LoadDocumentAsync("numeric-query-param.json");
        var ctx = await _harness.RunAsync(doc, new TypeScriptGenerationOptions
        {
            Transports = ["fetch"],
            ValidationLibrary = "zod3",
        });

        var paramsSchema = ctx.GeneratedFiles.First(f =>
            f.RelativePath.EndsWith("listItems.params.schema.ts", StringComparison.Ordinal));
        // page: integer, optional, default 10, described.
        paramsSchema.Content.Should().Contain("z.coerce.number().int().optional().default(10).describe(\"Page number (1-based).\")");
        // The nonempty string "false" must be parsed as false instead of JavaScript truthiness.
        paramsSchema.Content.Should().Contain("z.enum([\"true\", \"false\"]).transform((value) => (value === \"true\"))");
        paramsSchema.Content.Should().NotContain("z.coerce.boolean()");
    }

    [Test]
    public async Task Zod4_UsesZodV4ImportPath()
    {
        var doc = await PipelineTestHarness.LoadDocumentAsync("simple-schema.json");
        var ctx = await _harness.RunAsync(doc, new TypeScriptGenerationOptions
        {
            Transports = ["fetch"],
            ValidationLibrary = "zod4",
        });

        var paramsSchema = ctx.GeneratedFiles.First(f =>
            f.RelativePath.EndsWith("getUserById.params.schema.ts", StringComparison.Ordinal));
        paramsSchema.Content.Should().Contain("from \"zod/v4\"");
    }

    [Test]
    public async Task QueryParamReferencingEnum_ImportsEnumSchema()
    {
        // Regression: a query param that $refs a named enum renders as `UserRoleSchema`
        // in the Zod params schema, but the import for it was dropped (TS2304 / ReferenceError),
        // even though the params interface correctly imported the `UserRole` type.
        var doc = await PipelineTestHarness.LoadDocumentAsync("enum-query-param.json");
        var ctx = await _harness.RunAsync(doc, new TypeScriptGenerationOptions
        {
            Transports = ["fetch"],
            ValidationLibrary = "zod3",
        });

        var paramsSchema = ctx.GeneratedFiles.First(f =>
            f.RelativePath.EndsWith("listUsers.params.schema.ts", StringComparison.Ordinal));

        // The generated schema references the enum schema...
        paramsSchema.Content.Should().Contain("UserRoleSchema");
        // ...and now imports it from the schemas folder (separate-models subfolder layout).
        paramsSchema.Content.Should().Contain(
            "import { UserRoleSchema } from \"../../models/userRole/userRole.schema\";");
    }

    [Test]
    public async Task QueryParamReferencingEnum_FlatSchemas_ImportsEnumSchemaWithFlatPath()
    {
        // When SeparateModels is false, schema files are flat ({schemas}/{name}.schema.ts),
        // so the dependency import must not include the per-name subfolder.
        var doc = await PipelineTestHarness.LoadDocumentAsync("enum-query-param.json");
        var ctx = await _harness.RunAsync(
            doc,
            new TypeScriptGenerationOptions { Transports = ["fetch"], ValidationLibrary = "zod3" },
            new FileOrganization
            {
                SeparateModels = false,
                SeparateClients = true,
                ModelsPath = "models",
                ClientsPath = "api",
            });

        var paramsSchema = ctx.GeneratedFiles.First(f =>
            f.RelativePath.EndsWith("listUsers.params.schema.ts", StringComparison.Ordinal));

        paramsSchema.Content.Should().Contain(
            "import { UserRoleSchema } from \"../../models/userRole.schema\";");
        paramsSchema.Content.Should().NotContain("models/userRole/userRole.schema");
    }

    [Test]
    public async Task OperationWithoutPathOrQueryParams_SkipsParamsSchemaFile()
    {
        var doc = await PipelineTestHarness.LoadDocumentAsync("simple-schema.json");
        var ctx = await _harness.RunAsync(doc, new TypeScriptGenerationOptions
        {
            Transports = ["fetch"],
            ValidationLibrary = "zod3",
        });

        // getUsers has no path/query — skip.
        var skipped = ctx.GeneratedFiles.FirstOrDefault(f =>
            f.RelativePath.EndsWith("getUsers.params.schema.ts", StringComparison.Ordinal));
        skipped.Should().BeNull();
    }
}
