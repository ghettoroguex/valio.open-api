using System.Text.Json;
using FluentAssertions;
using NUnit.Framework;
using ValIO.Generators.TypeScript.Options;
using ValIO.Generators.TypeScript.Tests.TestHelpers;
using ValIO.Tests.Fixtures;

namespace ValIO.Generators.TypeScript.Tests.Integration;

/// <summary>Exercises caller-configured native transports, bounded token refresh, and request cancellation.</summary>
[TestFixture]
public sealed class NativeTransportInjectionTests
{
    /// <summary>Preserves every existing argument position while appending the complete native client type.</summary>
    /// <param name="transport">The generated HTTP transport.</param>
    /// <param name="clientType">The native callable type expected in its operation signature.</param>
    /// <param name="defaultClient">The imported or global fallback transport.</param>
    /// <returns>The asynchronous generation check.</returns>
    [TestCase("fetch", "typeof fetch", "fetch")]
    [TestCase("axios", "AxiosInstance", "axios")]
    [TestCase("ky", "KyInstance", "ky")]
    [TestCase("got", "Got", "got")]
    [TestCase("ofetch", "$Fetch", "$fetch")]
    [TestCase("undici", "typeof request", "request")]
    [TestCase("nextjs", "typeof fetch", "fetch")]
    [TestCase("solid", "typeof fetch", "fetch")]
    public async Task NativeClientArgument_AppendsWithoutReplacingTransportOptions(string transport, string clientType, string defaultClient)
    {
        using var harness = new PipelineTestHarness();
        var document = await PipelineTestHarness.LoadDocumentFromJsonAsync(OpenApiFixture.Read("Transport/runtime-contracts-3.0.json"));
        var context = await harness.RunAsync(document, new TypeScriptGenerationOptions { Transports = [transport] });
        var source = context.GeneratedFiles.Single(file => file.RelativePath.EndsWith($"mixed.{transport}.ts", StringComparison.Ordinal)).Content;

        source.Should().Contain($"client?: {clientType}");
        source.Should().Contain($"(client ?? {defaultClient})");
        source.IndexOf("signal?: AbortSignal", StringComparison.Ordinal).Should().BeLessThan(source.IndexOf($"client?: {clientType}", StringComparison.Ordinal));
    }

    /// <summary>Resolves configured exports independently from same-named generated arguments and locals.</summary>
    /// <param name="exportName">The configured Axios export that would otherwise be shadowed.</param>
    /// <returns>The asynchronous collision regression check.</returns>
    [TestCase("client")]
    [TestCase("params")]
    [TestCase("options")]
    [TestCase("signal")]
    [TestCase("axiosInstance")]
    public async Task ConfiguredAxiosExport_CannotBeShadowed(string exportName)
    {
        using var harness = new PipelineTestHarness();
        var document = await PipelineTestHarness.LoadDocumentFromJsonAsync(OpenApiFixture.Read("Transport/runtime-contracts-3.0.json"));
        var context = await harness.RunAsync(document, new TypeScriptGenerationOptions
        {
            Transports = ["axios"],
            Axios = new TypeScriptAxiosOptions { CustomInstancePath = "@app/native-client", CustomInstanceName = exportName },
        });
        var source = context.GeneratedFiles.Single(file => file.RelativePath.EndsWith("mixed.axios.ts", StringComparison.Ordinal)).Content;

        source.Should().Contain("import * as axiosDefaults from \"@app/native-client\"");
        source.Should().Contain($"(client ?? axiosDefaults.{exportName})");
    }

    /// <summary>Runs real fetch and Axios against a local HTTP server with native interceptors and abort signals.</summary>
    /// <param name="transport">The native HTTP transport under test.</param>
    /// <returns>The asynchronous runtime contract check.</returns>
    [TestCase("fetch")]
    [TestCase("axios")]
    [Category("NodeIntegration")]
    public async Task NativeInterceptors_RefreshOnceReplayAndCancel(string transport)
    {
        using var harness = new PipelineTestHarness();
        var document = await PipelineTestHarness.LoadDocumentFromJsonAsync(OpenApiFixture.Read("Transport/runtime-contracts-3.0.json"));
        var context = await harness.RunAsync(document, new TypeScriptGenerationOptions { Transports = [transport], ValidationLibrary = "zod4" });
        var script = $"const transport = {JsonSerializer.Serialize(transport)};" + Environment.NewLine + ReadScript("native-http-pipeline.cjs");

        var output = await GeneratedTypeScriptRuntime.Run(context, script);

        output.Should().Contain("native HTTP injection runtime passed");
    }

    /// <summary>Checks pre-abort, active unsubscription, listener cleanup, and validated success through actual RxJS.</summary>
    /// <returns>The asynchronous Angular Promise cancellation check.</returns>
    [Test]
    [Category("NodeIntegration")]
    public async Task AngularPromise_AbortSignalUnsubscribesAndPreservesValidation()
    {
        using var harness = new PipelineTestHarness();
        var document = await PipelineTestHarness.LoadDocumentFromJsonAsync(OpenApiFixture.Read("Transport/runtime-contracts-3.0.json"));
        var context = await harness.RunAsync(document, new TypeScriptGenerationOptions { Transports = ["angular-promise"], ValidationLibrary = "zod4" });

        var output = await GeneratedTypeScriptRuntime.Run(context, ReadScript("angular-promise-abort.cjs"));

        output.Should().Contain("Angular Promise cancellation runtime passed");
    }

    /// <summary>Reads an embedded runtime scenario independently from the OpenAPI input fixture.</summary>
    /// <param name="name">The JavaScript resource file name.</param>
    /// <returns>The complete Node runtime scenario.</returns>
    /// <exception cref="InvalidOperationException">The requested embedded runtime scenario is missing.</exception>
    private static string ReadScript(string name)
    {
        using var stream = typeof(NativeTransportInjectionTests).Assembly.GetManifestResourceStream($"ValIO.Generators.TypeScript.Tests.TestData.{name}")
            ?? throw new InvalidOperationException($"Missing embedded native transport scenario: {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
