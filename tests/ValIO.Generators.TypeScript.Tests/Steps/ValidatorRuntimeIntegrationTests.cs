using System.Text.Json;
using FluentAssertions;
using NUnit.Framework;
using ValIO.Generators.TypeScript.Options;
using ValIO.Generators.TypeScript.Tests.TestHelpers;
using ValIO.Tests.Fixtures;

namespace ValIO.Generators.TypeScript.Tests.Steps;

/// <summary>Executes generated validator modules against real libraries across OpenAPI versions.</summary>
/// <remarks>Set VALIO_VALIDATOR_NODE_MODULES to a node_modules directory containing zod 3.25, Valibot, TypeBox, and TypeScript.</remarks>
[TestFixture]
[Category("NodeIntegration")]
public sealed class ValidatorRuntimeIntegrationTests
{
    /// <summary>Provides every supported OpenAPI version and validation library combination.</summary>
    /// <returns>The version and library matrix.</returns>
    private static IEnumerable<TestCaseData> Cases()
    {
        foreach (var version in new[] { "2.0", "3.0.3", "3.1.0", "3.2.0" })
        {
            foreach (var validation in new[] { "zod3", "zod4", "valibot", "typebox" })
                yield return new TestCaseData(version, validation).SetName($"ValidatorRuntime [{version}, {validation}]");
        }
    }

    /// <summary>Verifies generated schemas reject missing siblings and overlapping oneOf alternatives at runtime.</summary>
    /// <param name="version">The input specification version.</param>
    /// <param name="validation">The selected validator.</param>
    [TestCaseSource(nameof(Cases))]
    public async Task GeneratedSchemas_PreserveCompositionAndPresence(string version, string validation)
    {
        using var harness = new PipelineTestHarness();
        var document = await PipelineTestHarness.LoadDocumentFromJsonAsync(OpenApiFixture.Read($"Validators/validator-runtime-{version}.json"));
        var context = await harness.RunAsync(document, new TypeScriptGenerationOptions { Transports = ["fetch"], ValidationLibrary = validation });
        var output = await GeneratedTypeScriptRuntime.Run(context, RuntimeScript(validation, version));
        output.Should().Contain("validator runtime passed");
    }

    /// <summary>Builds a Node script that transpiles generated modules and checks their observable validation behavior.</summary>
    /// <param name="validation">The selected validator.</param>
    /// <param name="version">The input specification version.</param>
    /// <returns>The executable CommonJS script.</returns>
    private static string RuntimeScript(string validation, string version)
    {
        var parse = validation switch
        {
            "valibot" => "v.parse(schema, value)",
            "typebox" => "Value.Decode(schema, value)",
            _ => "schema.parse(value)"
        };
        return $$"""
            const v = require('valibot');
            const Value = require('@sinclair/typebox/value').Value;
            function schema(name) { return require(path.join(root, 'models', name, name + '.schema.js'))[name[0].toUpperCase() + name.slice(1) + 'Schema']; }
            function parse(schema, value) { return {{parse}}; }
            function accepts(name, value) { return parse(schema(name), value); }
            function rejects(name, value) { assert.throws(() => accepts(name, value), name + ' accepted ' + JSON.stringify(value)); }
            accepts('allOf', { a: 'a', b: 1, tag: 'tag' });
            rejects('allOf', { a: 'a', b: 1 });
            rejects('allOf', { tag: 'tag' });
            rejects('allOf', { a: 'a', b: '1', tag: 'tag' });
            const binary = accepts('binaryValues', { file: new Blob(['body']), bytes: new Uint8Array([1, 2]) });
            assert(binary.file instanceof Blob);
            assert(binary.bytes instanceof Uint8Array);
            rejects('binaryValues', { file: 'body', bytes: new Uint8Array([1]) });
            rejects('binaryValues', { file: new Blob(['body']), bytes: 'AQ==' });
            const formatted = { id: '123e4567-e89b-12d3-a456-426614174000', date: '2024-02-29', timestamp: '2024-02-29T12:30:45Z' };
            accepts('standardFormats', formatted);
            for (const timestamp of [
              '2024-02-29T12:30:45Z', '2024-02-29T12:30:45.1Z',
              '2024-02-29T12:30:45.123456789Z', '2024-02-29T12:30:45+03:00',
              '2024-02-29T12:30:45.123-05:30'
            ]) accepts('standardFormats', { ...formatted, timestamp });
            if ({{JsonSerializer.Serialize(validation)}} === 'valibot') {
              for (const timestamp of [
                '2024-02-29T12:30Z', '2024-02-29T12:30:45',
                '2024-02-29T25:30:45Z', '2024-02-29T12:30:45+24:00',
                '2024-02-29T12:30:45+03:60', '2024-02-29T12:30:45+0300',
                '2024-02-29T12:30:45+03', '2024-02-29 12:30:45Z',
                '2023-02-29T12:30:45Z', '2024-04-31T12:30:45+03:00'
              ]) rejects('standardFormats', { ...formatted, timestamp });
            }
            rejects('standardFormats', { ...formatted, id: 'invalid-uuid' });
            rejects('standardFormats', { ...formatted, date: '2023-02-29' });
            rejects('standardFormats', { ...formatted, timestamp: 'not-a-timestamp' });
            rejects('standardFormats', { ...formatted, timestamp: '2023-02-29T12:30:45Z' });
            if ({{JsonSerializer.Serialize(validation)}} === 'typebox') {
              accepts('standardFormats', { ...formatted, timeValue: '12:30:45Z' });
              accepts('standardFormats', { ...formatted, timeValue: '12:30:45+03:00' });
              rejects('standardFormats', { ...formatted, timeValue: '25:30:45Z' });
              rejects('standardFormats', { ...formatted, timeValue: '12:30:45' });
            }
            if ({{JsonSerializer.Serialize(version)}} !== '2.0') {
              for (const name of ['oneOf', 'anyOf']) {
                accepts(name, { a: 'a', tag: 'tag' });
                accepts(name, { b: 1, tag: 'tag' });
                rejects(name, { a: 'a' });
                rejects(name, { tag: 'tag' });
              }
              rejects('oneOf', { a: 'a', b: 1, tag: 'tag' });
              accepts('anyOf', { a: 'a', b: 1, tag: 'tag' });
              accepts('patternChoice', { value: 'alpha' });
              accepts('patternChoice', { value: 'buzz' });
              rejects('patternChoice', { value: 'az' });
              rejects('patternChoice', { value: 'middle' });
              accepts('rangeChoice', { value: 2 });
              accepts('rangeChoice', { value: 12 });
              rejects('rangeChoice', { value: 7 });
              rejects('rangeChoice', { value: -1 });
              rejects('rangeChoice', { value: 16 });
              rejects('duplicateChoice', { value: 'text' });
              rejects('duplicateChoice', { value: 1 });
              accepts('nestedAnyAll', { value: { a: 'a', b: 1 } });
              accepts('nestedAnyAll', { value: { a: 'a', c: true } });
              accepts('nestedAnyAll', { value: { a: 'a', b: 1, c: true } });
              rejects('nestedAnyAll', { value: { a: 'a' } });
              rejects('nestedAnyAll', { value: { b: 1 } });
              accepts('nestedAllOne', { value: { a: 'a', b: 1 } });
              accepts('nestedAllOne', { value: { a: 'a', c: true } });
              rejects('nestedAllOne', { value: { a: 'a', b: 1, c: true } });
              rejects('nestedAllOne', { value: { a: 'a' } });
            }
            if (['3.1.0', '3.2.0'].includes({{JsonSerializer.Serialize(version)}})) {
              accepts('nullChoice', { value: 'alpha' });
              rejects('nullChoice', { value: 'middle' });
              rejects('nullChoice', { value: null });
            }
            accepts('presence', { nullable: null });
            accepts('presence', { nullable: 'text' });
            rejects('presence', {});
            rejects('presence', { nullable: 'text', optional: null });
            (async () => {
              let calls = 0;
              let responseBody = { a: 'a', b: 1 };
              globalThis.fetch = async (_, init) => {
                calls++;
                assert.deepEqual(JSON.parse(init.body), { a: 'a', b: 1 });
                return { status: 200, ok: true, headers: new Headers({ 'Content-Type': 'application/json' }), json: async () => responseBody };
              };
              const { SubmitChoice } = require(path.join(root, 'api', 'submitChoice', 'submitChoice.fetch.js'));
              if ({{JsonSerializer.Serialize(version)}} !== '2.0') {
                await assert.rejects(() => SubmitChoice({ body: { a: 'a', b: 1, c: true } }));
                assert.equal(calls, 0, 'invalid request must fail before HTTP');
              }
              const result = await SubmitChoice({ body: { a: 'a', b: 1 } });
              assert.equal(result.status, 200);
              assert.equal(result.body.a, 'a');
              responseBody = { a: 'a' };
              await assert.rejects(() => SubmitChoice({ body: { a: 'a', b: 1 } }));
              console.log('validator runtime passed');
            })().catch(error => { console.error(error); process.exitCode = 1; });
            """;
    }

    /// <summary>Checks the full composition, requiredness, and nullability cross product at runtime.</summary>
    /// <param name="version">The fixture's OpenAPI minor version.</param>
    /// <param name="validation">The selected validation library.</param>
    [TestCase("3.0", "zod3")]
    [TestCase("3.0", "zod4")]
    [TestCase("3.0", "valibot")]
    [TestCase("3.0", "typebox")]
    [TestCase("3.1", "zod3")]
    [TestCase("3.1", "zod4")]
    [TestCase("3.1", "valibot")]
    [TestCase("3.1", "typebox")]
    [TestCase("3.2", "zod3")]
    [TestCase("3.2", "zod4")]
    [TestCase("3.2", "valibot")]
    [TestCase("3.2", "typebox")]
    public async Task CompositionProperties_PreservePresenceAndNullability(string version, string validation)
    {
        using var harness = new PipelineTestHarness();
        var document = await PipelineTestHarness.LoadDocumentFromJsonAsync(OpenApiFixture.Read($"CoreIR/composition-presence-{version}.json"));
        var context = await harness.RunAsync(document, new TypeScriptGenerationOptions { Transports = ["fetch"], ValidationLibrary = validation });
        var parse = validation switch
        {
            "valibot" => "require('valibot').parse(schema, value)",
            "typebox" => "require('@sinclair/typebox/value').Value.Decode(schema, value)",
            _ => "schema.parse(value)"
        };
        var output = await GeneratedTypeScriptRuntime.Run(context, $$"""
            const schema = require(path.join(root, 'models', 'presenceMatrix', 'presenceMatrix.schema.js')).PresenceMatrixSchema;
            function parse(value) { return {{parse}}; }
            function baseline() { return {
              anyOfRequiredNonnullable: 'valid', anyOfRequiredNullable: null,
              oneOfRequiredNonnullable: 'valid', oneOfRequiredNullable: null,
              allOfRequiredNonnullable: 'valid', allOfRequiredNullable: null,
              requiredNullableArray: null, requiredDictionaryWithNullableValues: { text: 'valid', empty: null }
            }; }
            parse(baseline());
            for (const operator of ['anyOf', 'oneOf', 'allOf']) {
              for (const requiredness of ['Required', 'Optional']) {
                for (const nullability of ['Nonnullable', 'Nullable']) {
                  const key = operator + requiredness + nullability;
                  parse({ ...baseline(), [key]: 'valid' });
                  const absent = baseline(); delete absent[key];
                  if (requiredness === 'Required') assert.throws(() => parse(absent), key + ' absent');
                  else parse(absent);
                  const explicitNull = { ...baseline(), [key]: null };
                  if (nullability === 'Nullable') parse(explicitNull);
                  else assert.throws(() => parse(explicitNull), key + ' null');
                  assert.throws(() => parse({ ...baseline(), [key]: '' }), key + ' minimum length');
                }
              }
            }
            parse({ ...baseline(), requiredNullableArray: ['valid', 1, null],
              optionalArrayWithNullableItems: ['valid', null], optionalNullableDictionary: { empty: null, text: 'valid', number: 1 },
              optionalNullableObject: { child: null } });
            parse({ ...baseline(), optionalNullableDictionary: null, optionalNullableObject: null });
            assert.throws(() => parse({ ...baseline(), optionalArrayWithNullableItems: null }));
            assert.throws(() => parse({ ...baseline(), requiredDictionaryWithNullableValues: null }));
            assert.throws(() => parse({ ...baseline(), requiredNullableArray: [''] }));
            assert.throws(() => parse({ ...baseline(), requiredDictionaryWithNullableValues: { text: 'too-long-string' } }));
            console.log('composition presence runtime passed');
            """);
        output.Should().Contain("composition presence runtime passed");
    }

    /// <summary>Verifies collapsing a single composition member preserves both member and sibling constraints.</summary>
    /// <param name="fixture">The fixture defining competing member and enclosing constraints.</param>
    /// <param name="validation">The selected validation library.</param>
    [TestCase("collapsed-anyof-patterns", "zod3")]
    [TestCase("collapsed-anyof-patterns", "zod4")]
    [TestCase("collapsed-anyof-patterns", "valibot")]
    [TestCase("collapsed-anyof-patterns", "typebox")]
    [TestCase("collapsed-allof-bounds", "zod3")]
    [TestCase("collapsed-allof-bounds", "zod4")]
    [TestCase("collapsed-allof-bounds", "valibot")]
    [TestCase("collapsed-allof-bounds", "typebox")]
    public async Task CollapsedComposition_PreservesAllConstraintRules(string fixture, string validation)
    {
        using var harness = new PipelineTestHarness();
        var document = await PipelineTestHarness.LoadDocumentFromJsonAsync(OpenApiFixture.Read($"CoreIR/{fixture}.json"));
        var context = await harness.RunAsync(document, new TypeScriptGenerationOptions { Transports = ["fetch"], ValidationLibrary = validation });
        var parse = validation switch
        {
            "valibot" => "require('valibot').parse(schema, value)",
            "typebox" => "require('@sinclair/typebox/value').Value.Decode(schema, value)",
            _ => "schema.parse(value)"
        };
        var output = await GeneratedTypeScriptRuntime.Run(context, $$"""
            const schema = require(path.join(root, 'models', 'constraintContainer', 'constraintContainer.schema.js')).ConstraintContainerSchema;
            function parse(value) { return {{parse}}; }
            if ({{JsonSerializer.Serialize(fixture)}} === 'collapsed-anyof-patterns') {
              parse({value:'az'});
              assert.throws(() => parse({value:'bz'}));
              assert.throws(() => parse({value:'ab'}));
            } else {
              parse({value:0}); parse({value:10});
              assert.throws(() => parse({value:-1}));
              assert.throws(() => parse({value:11}));
            }
            console.log('collapsed constraints runtime passed');
            """);
        output.Should().Contain("collapsed constraints runtime passed");
    }
}
