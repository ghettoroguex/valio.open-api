using FluentAssertions;
using NUnit.Framework;
using ValIO.Generators.TypeScript.Options;
using ValIO.Generators.TypeScript.Tests.TestHelpers;
using ValIO.Tests.Fixtures;

namespace ValIO.Generators.TypeScript.Tests.Integration;

/// <summary>Type-checks complete generated clients and schemas against real transport and validator libraries.</summary>
/// <remarks>
/// Set VALIO_VALIDATOR_NODE_MODULES to an external dependency directory containing TypeScript, the selected
/// validators, and the transport packages. An absent variable skips this explicit integration fixture;
/// an invalid directory, missing dependency, or compiler error fails it. Failed output directories are
/// retained outside the repository so the exact generated sources and diagnostics remain inspectable.
/// </remarks>
[TestFixture]
[Category("NodeIntegration")]
[NonParallelizable]
public sealed class GeneratedTypeScriptCompileTests
{
    /// <summary>Supplies every HTTP transport and supported validator, including generation without validation.</summary>
    /// <returns>The independent strict-compilation cases.</returns>
    private static IEnumerable<TestCaseData> Cases()
    {
        foreach (var transport in new[]
        {
            "fetch", "axios", "nextjs", "ky", "got", "ofetch", "undici", "angular-rxjs", "angular-promise", "solid"
        })
        {
            foreach (var validation in new[] { "none", "zod3", "zod4", "valibot", "typebox" })
                yield return new TestCaseData(transport, validation).SetName($"GeneratedTypeScriptCompile [{transport}, {validation}]");
        }
    }

    /// <summary>Compiles actual pipeline output covering model composition, enums, request encodings, and empty responses.</summary>
    /// <param name="transport">The HTTP client or framework transport to generate.</param>
    /// <param name="validation">The selected validator, or <c>none</c> to omit runtime schemas.</param>
    /// <returns>The asynchronous generation and TypeScript compiler check.</returns>
    [TestCaseSource(nameof(Cases))]
    public async Task GeneratedClientAndSchemas_CompileWithStrictTypeChecking(string transport, string validation)
    {
        using var harness = new PipelineTestHarness();
        var document = await PipelineTestHarness.LoadDocumentFromJsonAsync(OpenApiFixture.Read("TypeScriptCompile/compile-contracts-3.1.json"));
        var context = await harness.RunAsync(document, new TypeScriptGenerationOptions
        {
            Transports = [transport],
            ValidationLibrary = validation == "none" ? null : validation,
        });

        var files = context.GeneratedFiles.Where(file => file.RelativePath.EndsWith(".ts", StringComparison.Ordinal)).ToArray();
        files.Should().NotBeEmpty("the actual pipeline must emit TypeScript source files");
        files.Select(file => file.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count().Should().Be(files.Length,
            "a compilation must not overwrite generated paths that differ only by letter casing");
        var clientSuffix = transport is "angular-rxjs" or "angular-promise" ? ".service.ts" : $".{transport}.ts";
        files.Should().Contain(file => file.RelativePath.EndsWith(clientSuffix, StringComparison.Ordinal), "the requested transport must be generated");
        files.Should().Contain(file => file.RelativePath.EndsWith("state/state.ts", StringComparison.Ordinal), "named enums must participate in the compilation");
        if (validation != "none")
            files.Should().Contain(file => file.RelativePath.EndsWith(".schema.ts", StringComparison.Ordinal), "the selected validator must contribute real schemas");

        await GeneratedTypeScriptCompiler.Compile(files, $"strict compilation for {transport}/{validation}");
    }
}
