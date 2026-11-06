using FluentAssertions;
using NUnit.Framework;
using ValIO.Generators.TypeScript.Options;
using ValIO.Generators.TypeScript.Tests.TestHelpers;
using ValIO.Tests.Fixtures;

namespace ValIO.Generators.TypeScript.Tests.Steps;

/// <summary>Runs generated nullable model and enum validators as JavaScript for every supported validator backend.</summary>
[TestFixture]
public sealed class NullableReferenceUnionTests
{
    /// <summary>Checks type declarations and JavaScript validation of missing, null, populated and malformed values.</summary>
    /// <param name="validator">The generated validator backend.</param>
    /// <returns>The asynchronous generation and Node execution.</returns>
    [TestCase("zod3")]
    [TestCase("zod4")]
    [TestCase("valibot")]
    [TestCase("typebox")]
    [Category("NodeIntegration")]
    public async Task NullableReferencesPreserveWireSemantics(string validator)
    {
        using var harness = new PipelineTestHarness();
        var document = await PipelineTestHarness.LoadDocumentFromJsonAsync(OpenApiFixture.Read("CoreIR/nullable-reference-unions-3.1.json"));
        var context = await harness.RunAsync(document, new TypeScriptGenerationOptions { Transports = ["fetch"], ValidationLibrary = validator });
        var content = context.GeneratedFiles.Single(file => file.RelativePath.Replace('\\', '/').EndsWith("nullableReferences/nullableReferences.ts", StringComparison.Ordinal)).Content;
        content.Should().MatchRegex(@"country\s*:\s*NullableCountry\s*\|\s*null");
        content.Should().MatchRegex(@"optionalCountry\?\s*:\s*NullableCountry\s*\|\s*null");
        await GeneratedTypeScriptCompiler.Compile(context.GeneratedFiles.Where(file => file.RelativePath.EndsWith(".ts", StringComparison.Ordinal)).ToArray(),
            "nullable model and enum references with " + validator);
        var parse = validator switch
        {
            "valibot" => "require('valibot').parse(schema, value)",
            "typebox" => "require('@sinclair/typebox/value').Value.Decode(schema, value)",
            _ => "schema.parse(value)"
        };
        var output = await GeneratedTypeScriptRuntime.Run(context, $$"""
            const schema = require(path.join(root, 'models', 'nullableReferences', 'nullableReferences.schema.js')).NullableReferencesSchema;
            function parse(value) { return {{parse}}; }
            const empty = { country: null, state: null, countries: [null], states: { one: null } };
            const absent = parse(empty);
            assert.equal(absent.country, null);
            assert(!Object.hasOwn(absent, 'optionalCountry'));
            const explicit = parse({ ...empty, optionalCountry: null, optionalState: null });
            assert(Object.hasOwn(explicit, 'optionalCountry'));
            assert.equal(explicit.optionalCountry, null);
            const populated = parse({ country: { id: 7, name: 'DE' }, state: 'granted', optionalCountry: { id: 8, name: 'RU' }, optionalState: 'pending', countries: [null, { id: 9, name: 'US' }], states: { one: 'granted', two: null } });
            assert.equal(populated.country.id, 7);
            assert.equal(populated.state, 'granted');
            assert.equal(populated.countries[1].id, 9);
            assert.equal(populated.states.two, null);
            const { country, ...missing } = empty;
            assert.throws(() => parse(missing));
            assert.throws(() => parse({ ...empty, country: 'DE' }));
            assert.throws(() => parse({ ...empty, country: { id: '7', name: 'DE' } }));
            assert.throws(() => parse({ ...empty, state: 'unknown' }));
            console.log('nullable reference JavaScript runtime passed');
            """);
        output.Should().Contain("nullable reference JavaScript runtime passed");
    }
}
