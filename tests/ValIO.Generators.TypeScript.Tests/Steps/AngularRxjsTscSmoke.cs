using NUnit.Framework;
using ValIO.Generators.TypeScript.Options;
using ValIO.Generators.TypeScript.Tests.TestHelpers;

namespace ValIO.Generators.TypeScript.Tests.Steps;

/// <summary>
/// Checks the complete Angular RxJS Petstore output using real Angular and TypeScript declarations.
/// </summary>
/// <remarks>
/// Uses VALIO_VALIDATOR_NODE_MODULES and an isolated owned temporary directory, as do the transport matrix checks.
/// Missing optional configuration skips this test; an invalid explicitly configured environment fails it.
/// </remarks>
[TestFixture]
public sealed class AngularRxjsTscSmoke
{
    /// <summary>Compiles actual Petstore models, request types, result types, and Angular services under strict TypeScript.</summary>
    /// <returns>The asynchronous generation and real compiler check.</returns>
    [Test]
    [Category("Tsc")]
    [Category("NodeIntegration")]
    public async Task GeneratedAngularOutput_CompilesUnderTscStrict()
    {
        using var harness = new PipelineTestHarness();
        var doc = await PipelineTestHarness.LoadDocumentAsync("petstore.json");
        var ctx = await harness.RunAsync(doc, new TypeScriptGenerationOptions
        {
            Transports = ["angular-rxjs"],
        });

        var files = ctx.GeneratedFiles.Where(file => file.RelativePath.EndsWith(".ts", StringComparison.Ordinal)).ToArray();
        await GeneratedTypeScriptCompiler.Compile(files, "strict Angular RxJS Petstore compilation");
    }
}
