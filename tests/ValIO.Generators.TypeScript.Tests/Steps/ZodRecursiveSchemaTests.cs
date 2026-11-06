using FluentAssertions;
using NUnit.Framework;
using ValIO.Core.Plugins;
using ValIO.Generators.TypeScript.Options;
using ValIO.Generators.TypeScript.Tests.TestHelpers;

namespace ValIO.Generators.TypeScript.Tests.Steps;

/// <summary>Guards runtime-safe references across direct and mutually recursive Zod model schemas.</summary>
[TestFixture]
public sealed class ZodRecursiveSchemaTests
{
    /// <summary>Verifies only cycle edges are deferred, including nullable records and recursive arrays.</summary>
    /// <param name="validator">The supported Zod generation mode.</param>
    [TestCase("zod3")]
    [TestCase("zod4")]
    public async Task RecursiveReferences_AreLazyWhileAcyclicReferencesStayEager(string validator)
    {
        var context = await Generate(validator);
        var field = Schema(context, "field");
        var reference = Schema(context, "reference");
        var leaf = Schema(context, "leaf");

        field.Should().Contain("z.lazy(() => ReferenceSchema)").And.Contain("LeafSchema");
        field.Should().NotContain("z.lazy(() => LeafSchema)");
        reference.Should().MatchRegex(@"z\.record\(\s*z\.string\(\),\s*z\.lazy\(\(\) => FieldSchema\)\s*\)")
            .And.Contain("z.array(z.lazy(() => FieldSchema))");
        leaf.Should().NotContain("z.lazy");
        Schema(context, "tree").Should().Contain("z.array(z.lazy(() => TreeSchema))");
        Schema(context, "consumer").Should().Contain("FieldSchema")
            .And.NotContain("z.lazy", "a consumer outside the cycle cannot close it");
    }

    /// <summary>Executes recursive schemas and retains nested validation, nullability, and container shape.</summary>
    /// <param name="validator">The supported Zod generation mode.</param>
    [TestCase("zod3")]
    [TestCase("zod4")]
    [Category("NodeIntegration")]
    public async Task RecursiveSchemas_ValidateNestedValuesWithoutInitializationErrors(string validator)
    {
        var context = await Generate(validator);
        var output = await GeneratedTypeScriptRuntime.Run(context, """
            const { ReferenceSchema } = require(path.join(root, 'models/reference/reference.schema.js'));
            const { FieldSchema } = require(path.join(root, 'models/field/field.schema.js'));
            const { TreeSchema } = require(path.join(root, 'models/tree/tree.schema.js'));
            assert.equal(FieldSchema.safeParse({ Value: 4, Reference: null }).success, true);
            assert.equal(ReferenceSchema.safeParse({ Fields: null, Children: [] }).success, true);
            assert.equal(ReferenceSchema.safeParse({ Fields: { child: { Value: 2, Reference: { Fields: null } } } }).success, true);
            assert.equal(ReferenceSchema.safeParse({ Fields: { child: { Value: 'invalid' } } }).success, false);
            assert.equal(ReferenceSchema.safeParse({ Children: [{ Value: 'invalid' }] }).success, false);
            assert.equal(ReferenceSchema.safeParse({ Fields: [4] }).success, false);
            assert.equal(TreeSchema.safeParse({ Children: [{ Children: [] }] }).success, true);
            assert.equal(TreeSchema.safeParse({ Children: [4] }).success, false);
            console.log('recursive schema runtime passed');
            """);
        output.Should().Contain("recursive schema runtime passed");
    }

    /// <summary>Generates a fixture containing independent, direct, and mutual model dependencies.</summary>
    /// <param name="validator">The selected Zod mode.</param>
    /// <returns>The generated source files and finalized document context.</returns>
    private static async Task<GenerationContext> Generate(string validator)
    {
        using var harness = new PipelineTestHarness();
        var document = await PipelineTestHarness.LoadDocumentFromJsonAsync("""
            {
              "openapi":"3.0.3", "info":{"title":"Recursive schemas","version":"1"}, "paths":{},
              "components":{"schemas":{
                "Field":{"type":"object","properties":{
                  "Value":{"type":"integer","format":"int32"},
                  "Reference":{"allOf":[{"$ref":"#/components/schemas/Reference"}],"nullable":true},
                  "Leaf":{"$ref":"#/components/schemas/Leaf"}
                }},
                "Reference":{"type":"object","properties":{
                  "Fields":{"type":"object","additionalProperties":{"$ref":"#/components/schemas/Field"},"nullable":true},
                  "Children":{"type":"array","items":{"$ref":"#/components/schemas/Field"}}
                }},
                "Leaf":{"type":"object","properties":{"Name":{"type":"string"}}},
                "Tree":{"type":"object","properties":{"Children":{"type":"array","items":{"$ref":"#/components/schemas/Tree"}}}},
                "Consumer":{"type":"object","properties":{"Field":{"$ref":"#/components/schemas/Field"}}}
              }}
            }
            """);
        return await harness.RunAsync(document, new TypeScriptGenerationOptions
        {
            Transports = ["fetch"],
            ValidationLibrary = validator
        });
    }

    /// <summary>Finds the schema source belonging to one generated model.</summary>
    /// <param name="context">The generation result.</param>
    /// <param name="model">The camel-cased model file stem.</param>
    /// <returns>The generated Zod schema text.</returns>
    private static string Schema(GenerationContext context, string model) =>
        context.GeneratedFiles.Single(file => file.RelativePath.Replace('\\', '/')
            .EndsWith($"models/{model}/{model}.schema.ts", StringComparison.Ordinal)).Content;
}
