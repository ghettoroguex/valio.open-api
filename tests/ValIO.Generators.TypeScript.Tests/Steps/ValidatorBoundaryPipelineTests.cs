using FluentAssertions;
using NUnit.Framework;
using ValIO.Generators.TypeScript.Options;
using ValIO.Generators.TypeScript.Tests.TestHelpers;
using ValIO.Tests.Fixtures;

namespace ValIO.Generators.TypeScript.Tests.Steps;

/// <summary>Exercises validator construction through parsing, IR extraction, and emitted client schema files.</summary>
[TestFixture]
public sealed class ValidatorBoundaryPipelineTests
{
    /// <summary>Verifies dictionary responses validate values using the record overload supported by both Zod versions.</summary>
    /// <param name="validation">The selected validator version.</param>
    [TestCase("zod3")]
    [TestCase("zod4")]
    public async Task DictionaryResponse_RetainsKeyAndValueSchemas(string validation)
    {
        using var harness = new PipelineTestHarness();
        var document = await PipelineTestHarness.LoadDocumentFromJsonAsync(OpenApiFixture.Read("Validators/dictionary-response.json"));

        var context = await harness.RunAsync(document, new TypeScriptGenerationOptions { Transports = ["fetch"], ValidationLibrary = validation });
        var schema = context.GeneratedFiles.Single(file => file.RelativePath.EndsWith("getValues.results.schemas.ts", StringComparison.Ordinal)).Content;

        schema.Should().Contain("z.record(").And.Contain("z.string()").And.Contain("z.number().int()");
        schema.Should().NotContain("z.coerce.number()");
    }

    /// <summary>Verifies primitive formats survive response collection schema construction.</summary>
    /// <param name="validation">The selected validator version.</param>
    [TestCase("zod3")]
    [TestCase("zod4")]
    public async Task DateArrayResponse_RetainsDateFormatValidation(string validation)
    {
        using var harness = new PipelineTestHarness();
        var document = await PipelineTestHarness.LoadDocumentFromJsonAsync(OpenApiFixture.Read("Validators/date-array-response.json"));

        var context = await harness.RunAsync(document, new TypeScriptGenerationOptions { Transports = ["fetch"], ValidationLibrary = validation });
        var schema = context.GeneratedFiles.Single(file => file.RelativePath.EndsWith("getDates.results.schemas.ts", StringComparison.Ordinal)).Content;

        schema.Should().Contain("z.array(z.string().refine(");
    }

    /// <summary>Verifies nullable parameter constraints are applied before the nullable wrapper.</summary>
    /// <param name="validation">The selected validator.</param>
    /// <param name="expectedConstraint">The constraint syntax expected from that validator.</param>
    [TestCase("zod3", ".min(FindValuesQueryParamsConstraints.value.minLength).nullable()")]
    [TestCase("zod4", ".min(FindValuesQueryParamsConstraints.value.minLength).nullable()")]
    [TestCase("valibot", "v.minLength(3)")]
    [TestCase("typebox", "minLength: 3")]
    public async Task NullableParameter_RetainsConstraints(string validation, string expectedConstraint)
    {
        using var harness = new PipelineTestHarness();
        var document = await PipelineTestHarness.LoadDocumentFromJsonAsync(OpenApiFixture.Read("Validators/nullable-parameter.json"));

        var context = await harness.RunAsync(document, new TypeScriptGenerationOptions { Transports = ["fetch"], ValidationLibrary = validation });
        var schema = context.GeneratedFiles.Single(file => file.RelativePath.EndsWith("findValues.params.schema.ts", StringComparison.Ordinal)).Content;

        schema.Should().Contain(expectedConstraint);
        schema.Should().NotContain(".optional()").And.NotContain("v.optional(").And.NotContain("Type.Optional(");
    }
}
