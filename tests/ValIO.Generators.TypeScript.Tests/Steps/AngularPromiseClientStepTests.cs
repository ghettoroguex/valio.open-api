using FluentAssertions;
using NUnit.Framework;
using ValIO.Generators.TypeScript.Options;
using ValIO.Generators.TypeScript.Tests.TestHelpers;
using ValIO.Tests.Fixtures;

namespace ValIO.Generators.TypeScript.Tests.Steps;

/// <summary>Verifies the advertised Angular Promise transport and coexistence with the RxJS service.</summary>
[TestFixture]
public sealed class AngularPromiseClientStepTests
{
    /// <summary>Checks that both Angular return styles have distinct paths, classes, and barrel exports.</summary>
    /// <returns>The asynchronous generation check.</returns>
    [Test]
    public async Task AngularModes_GenerateDistinctServices()
    {
        using var harness = new PipelineTestHarness();
        var document = await PipelineTestHarness.LoadDocumentFromJsonAsync(OpenApiFixture.Read("Transport/runtime-contracts-3.0.json"));
        var context = await harness.RunAsync(document, new TypeScriptGenerationOptions { Transports = ["angular-rxjs", "angular-promise"] });

        var services = context.GeneratedFiles.Where(file => file.RelativePath.EndsWith(".service.ts", StringComparison.Ordinal)).ToList();
        services.Should().HaveCount(2);
        services.Select(file => file.RelativePath).Should().OnlyHaveUniqueItems();
        services.Single(file => file.RelativePath.EndsWith("uploadsService.service.ts", StringComparison.Ordinal)).Content
            .Should().Contain("export class UploadsService").And.Contain("Observable<");
        services.Single(file => file.RelativePath.EndsWith("uploadsPromiseService.service.ts", StringComparison.Ordinal)).Content
            .Should().Contain("export class UploadsPromiseService").And.Contain("Promise<MixedResults>")
            .And.Contain("firstValueFrom(").And.Contain("observe: \"response\"");
        context.GeneratedFiles.Single(file => file.RelativePath == "index.ts").Content
            .Should().Contain("./api/uploadsService.service").And.Contain("./api/uploadsPromiseService.service");
    }

    /// <summary>Checks that Promise generation supports each validator and preserves bodyless response variants.</summary>
    /// <param name="validation">The selected runtime validator.</param>
    /// <returns>The asynchronous generation check.</returns>
    [TestCase(null)]
    [TestCase("zod3")]
    [TestCase("zod4")]
    [TestCase("valibot")]
    [TestCase("typebox")]
    public async Task AngularPromise_PreservesFormAndBodylessContracts(string? validation)
    {
        using var harness = new PipelineTestHarness();
        var document = await PipelineTestHarness.LoadDocumentFromJsonAsync(OpenApiFixture.Read("Transport/runtime-contracts-3.0.json"));
        var context = await harness.RunAsync(document, new TypeScriptGenerationOptions { Transports = ["angular-promise"], ValidationLibrary = validation });
        var service = context.GeneratedFiles.Single(file => file.RelativePath.EndsWith(".service.ts", StringComparison.Ordinal)).Content;

        service.Should().Contain("import { firstValueFrom, Observable, race } from \"rxjs\"")
            .And.Contain("Promise<MixedResults>").And.Contain("new FormData()").And.Contain("new URLSearchParams()")
            .And.Contain("body: undefined").And.Contain("isSuccessful: true").And.Contain(".then(() => undefined)");
        if (validation != null)
            service.Should().Contain("const parsedBody = ((params.form !== undefined) ?");
        else
            service.Should().Contain("response.body as string");
    }
}
