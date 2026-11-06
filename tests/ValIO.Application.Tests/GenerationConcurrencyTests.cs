using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using ValIO.Application.DependencyInjection;
using ValIO.Application.Services;
using ValIO.Core.Models;
using ValIO.Core.Plugins;
using ValIO.Generators.Cpp.Ast.Renderers;
using ValIO.Generators.Cpp.Models;
using ValIO.Generators.CSharp.Ast.Renderers;
using ValIO.Generators.CSharp.Options;
using ValIO.Plugins.Bootstrap;
using ValIO.Tests.Fixtures;

namespace ValIO.Application.Tests;

/// <summary>Checks that one application provider isolates concurrent documents, pipelines, and renderer state.</summary>
[TestFixture]
public sealed class GenerationConcurrencyTests
{
    /// <summary>Bounds the parallel workload while giving both fixture documents repeated overlapping requests.</summary>
    private const int ConcurrentRequestCount = 16;

    /// <summary>Compares complete parallel output with each document's independently generated sequential output.</summary>
    /// <param name="languageId">The C# or standalone C++ plugin identity.</param>
    /// <returns>The asynchronous regression check.</returns>
    [TestCase("csharp")]
    [TestCase("cpp-std")]
    public async Task GenerateAsync_ConcurrentDocumentsMatchTheirOwnSequentialFiles(string languageId)
    {
        using var provider = CreateProvider();
        var orchestrator = provider.GetRequiredService<IGenerationOrchestrator>();
        provider.GetRequiredService<IGenerationOrchestrator>().Should().BeSameAs(orchestrator);

        var fixtureDirectory = Directory.CreateTempSubdirectory("valio-generation-concurrency-");
        var firstPath = Path.Combine(fixtureDirectory.FullName, "first.json");
        var secondPath = Path.Combine(fixtureDirectory.FullName, "second.json");
        try
        {
            await File.WriteAllTextAsync(firstPath,
                OpenApiFixture.Read("Orchestration/concurrent-first-3.1.json"));
            await File.WriteAllTextAsync(secondPath,
                OpenApiFixture.Read("Orchestration/concurrent-second-3.1.json"));

            var firstExpected = await GenerateFileMapAsync(orchestrator, languageId, firstPath);
            var secondExpected = await GenerateFileMapAsync(orchestrator, languageId, secondPath);
            firstExpected.Select(file => file.RelativePath).Should()
                .Equal(secondExpected.Select(file => file.RelativePath));
            firstExpected.Should().NotEqual(secondExpected,
                "the same declaration names must retain each document's different definitions");
            string.Join('\n', firstExpected.Select(file => file.Content)).Should()
                .ContainEquivalentOf("firstText").And.NotContainEquivalentOf("secondText")
                .And.ContainEquivalentOf("firstReady").And.NotContainEquivalentOf("secondPending");
            string.Join('\n', secondExpected.Select(file => file.Content)).Should()
                .ContainEquivalentOf("secondText").And.NotContainEquivalentOf("firstText")
                .And.ContainEquivalentOf("secondPending").And.NotContainEquivalentOf("firstReady");

            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var requests = Enumerable.Range(0, ConcurrentRequestCount)
                .Select(index => Task.Run(async () =>
                {
                    await start.Task;
                    var useFirstDocument = index % 2 == 0;
                    var actual = await GenerateFileMapAsync(orchestrator, languageId,
                        useFirstDocument ? firstPath : secondPath);
                    actual.Should().Equal(useFirstDocument ? firstExpected : secondExpected,
                        $"request {index} must retain its own document and renderer state");
                }))
                .ToArray();

            start.SetResult();
            await Task.WhenAll(requests);
        }
        finally
        {
            // Delete only the two known files and the newly owned, empty directory; never recurse.
            File.Delete(firstPath);
            File.Delete(secondPath);
            fixtureDirectory.Delete();
        }
    }

    /// <summary>Ensures plugin factories resolve transient renderers and produce independently owned pipeline steps.</summary>
    [Test]
    public void RendererRegistrationsAndPluginPipelines_HaveIndependentInstances()
    {
        using var provider = CreateProvider();
        provider.GetRequiredService<ICSharpAstRenderer>().Should()
            .NotBeSameAs(provider.GetRequiredService<ICSharpAstRenderer>());
        provider.GetRequiredService<ICppAstRenderer>().Should()
            .NotBeSameAs(provider.GetRequiredService<ICppAstRenderer>());

        var registry = provider.GetRequiredService<IGeneratorRegistry>();
        foreach (var languageId in new[] { "csharp", "cpp-std" })
        {
            var plugin = registry.FindPlugin(languageId);
            plugin.Should().NotBeNull();
            var firstPipeline = plugin!.CreatePipeline(provider);
            var secondPipeline = plugin.CreatePipeline(provider);
            firstPipeline.Should().NotBeSameAs(secondPipeline);
            firstPipeline.Steps.Should().NotBeEmpty().And.OnlyContain(firstStep =>
                secondPipeline.Steps.All(secondStep => !ReferenceEquals(firstStep, secondStep)));
        }
    }

    /// <summary>Builds the actual application composition root with logging and HTTP services required by its parser.</summary>
    /// <returns>A provider owning one singleton orchestrator and every production generator plugin.</returns>
    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddHttpClient();
        services.AddSingleton<ILogger<GenerationOrchestrator>>(NullLogger<GenerationOrchestrator>.Instance);
        services.AddValIOCore().AddValIOApplication().AddAllGeneratorPlugins();
        return services.BuildServiceProvider();
    }

    /// <summary>Generates a fresh request and preserves exact file paths and contents in deterministic path order.</summary>
    /// <param name="orchestrator">The shared application orchestrator under test.</param>
    /// <param name="languageId">The requested plugin identity.</param>
    /// <param name="schemaPath">The owned on-disk fixture copy parsed by production code.</param>
    /// <returns>The generated file map sorted with ordinal path comparison.</returns>
    private static async Task<IReadOnlyList<GeneratedFile>> GenerateFileMapAsync(
        IGenerationOrchestrator orchestrator, string languageId, string schemaPath)
    {
        var configuration = new GenerationConfiguration
        {
            LanguageId = languageId,
            Namespace = "ConcurrentModels",
            GenerateModels = true,
            GenerateEnums = true,
            GenerateClients = false,
            PluginOptions = languageId == "cpp-std"
                ? new CppGenerationOptions { Runtime = CppRuntimeProfile.Std, EmitSerializers = false }
                : new CSharpGenerationOptions(),
        };
        var result = await orchestrator.GenerateAsync(configuration, schemaPath);
        result.Success.Should().BeTrue($"generation must succeed: {result.ErrorMessage}");
        result.Files.Should().NotBeEmpty();
        result.Files.Select(file => file.RelativePath).Distinct(StringComparer.Ordinal).Should()
            .HaveCount(result.Files.Count, "each output path must identify exactly one generated file");
        return result.Files.OrderBy(file => file.RelativePath, StringComparer.Ordinal).ToArray();
    }
}
