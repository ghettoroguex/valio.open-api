using System.Text.Json;
using FluentAssertions;
using NUnit.Framework;
using ValIO.Generators.TypeScript.Options;
using ValIO.Generators.TypeScript.Tests.TestHelpers;
using ValIO.Tests.Fixtures;

namespace ValIO.Generators.TypeScript.Tests.Steps;

/// <summary>Runs common wire-format vectors against every generated validator and supported OpenAPI dialect.</summary>
[TestFixture]
[Category("NodeIntegration")]
public sealed class StringFormatRuntimeIntegrationTests
{
    /// <summary>Provides the dialect and validator cross product for consistent string-format validation.</summary>
    /// <returns>The independently named runtime test cases.</returns>
    private static IEnumerable<TestCaseData> Cases()
    {
        foreach (var version in new[] { "2.0", "3.0.3", "3.1.0", "3.2.0" })
        {
            foreach (var validator in new[] { "zod3", "zod4", "valibot", "typebox" })
                yield return new TestCaseData(version, validator).SetName($"StringFormats [{version}, {validator}]");
        }
    }

    /// <summary>Verifies canonical UUIDs, Gregorian dates, RFC 3339 zones, precision, and possible leap-second boundaries.</summary>
    /// <param name="version">The input OpenAPI dialect.</param>
    /// <param name="validator">The generated validation library.</param>
    /// <remarks>Leap-second announcement tables are deliberately external; the validator checks the possible UTC boundary.</remarks>
    [TestCaseSource(nameof(Cases))]
    public async Task GeneratedFormats_AgreeOnWireValues(string version, string validator)
    {
        using var harness = new PipelineTestHarness();
        var document = await PipelineTestHarness.LoadDocumentFromJsonAsync(OpenApiFixture.Read($"Validators/validator-runtime-{version}.json"));
        var context = await harness.RunAsync(document, new TypeScriptGenerationOptions { Transports = ["fetch"], ValidationLibrary = validator });
        using var vectors = JsonDocument.Parse(OpenApiFixture.Read("Validators/string-format-vectors.json"));
        var parse = validator switch
        {
            "valibot" => "require('valibot').parse(schema, value)",
            "typebox" => "require('@sinclair/typebox/value').Value.Decode(schema, value)",
            _ => "schema.parse(value)"
        };
        var output = await GeneratedTypeScriptRuntime.Run(context, $$"""
            const schema = require(path.join(root, 'models', 'standardFormats', 'standardFormats.schema.js')).StandardFormatsSchema;
            const vectors = {{vectors.RootElement.GetRawText()}};
            const baseline = { id: '123e4567-e89b-12d3-a456-426614174000', date: '2024-02-29', timestamp: '2024-02-29T12:30:45Z' };
            function parse(value) { return {{parse}}; }
            let count = 0;
            for (const [property, cases] of Object.entries(vectors)) {
              for (const value of cases.valid) {
                const result = parse({ ...baseline, [property]: value });
                assert.equal(result[property], value, property + ' must retain its original string precision and offset');
                count++;
              }
              for (const value of cases.invalid) {
                assert.throws(() => parse({ ...baseline, [property]: value }), property + ' accepted ' + JSON.stringify(value));
                count++;
              }
            }
            console.log('string format runtime passed ' + count);
            """);
        output.Should().Contain("string format runtime passed");
    }
}
