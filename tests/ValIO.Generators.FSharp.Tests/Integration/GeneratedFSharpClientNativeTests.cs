using System.Text;
using FluentAssertions;
using Microsoft.OpenApi;
using NUnit.Framework;
using ValIO.Generators.FSharp.Tests.TestHelpers;
using ValIO.Tests.Fixtures;

namespace ValIO.Generators.FSharp.Tests.Integration;

/// <summary>Executes generated F# operations through the real RestSharp and HttpClient pipelines.</summary>
[TestFixture]
[Category("NativeIntegration")]
[NonParallelizable]
public sealed class GeneratedFSharpClientNativeTests
{
    /// <summary>Verifies typed decoding, injected authentication/retry hooks, headers, HTTP errors and transport cancellation.</summary>
    /// <returns>The native F# consumer execution.</returns>
    [Test]
    public async Task GeneratedClient_UsesInjectedPipelineAndPropagatesCancellation()
    {
        var compiler = Environment.GetEnvironmentVariable("VALIO_FSHARP_FSI");
        var restSharp = Environment.GetEnvironmentVariable("VALIO_RESTSHARP_ASSEMBLY");
        if (string.IsNullOrWhiteSpace(compiler) || string.IsNullOrWhiteSpace(restSharp))
        {
            Assert.Ignore("Set VALIO_FSHARP_FSI and VALIO_RESTSHARP_ASSEMBLY to run the real F# client integration.");
            return;
        }
        File.Exists(compiler).Should().BeTrue("the configured F# compiler must exist");
        File.Exists(restSharp).Should().BeTrue("the configured real RestSharp assembly must exist");
        await using var input = new MemoryStream(Encoding.UTF8.GetBytes(OpenApiFixture.Read("NativeClients/requests-3.1.json")));
        var parsed = await OpenApiDocument.LoadAsync(input, "json");
        parsed.Document.Should().NotBeNull();
        (parsed.Diagnostic?.Errors.Select(error => error.Message) ?? []).Should().BeEmpty();
        using var harness = new FSharpPipelineTestHarness();
        var context = await harness.RunAsync(parsed.Document!);
        var workspace = Path.Combine(Path.GetTempPath(), $"valio-fsharp-client-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);
        var passed = false;
        try
        {
            var script = new StringBuilder();
            script.AppendLine($"#r @\"{Path.GetFullPath(restSharp).Replace("\"", "\"\"")}\"");
            foreach (var file in context.GeneratedFiles.Where(file => file.RelativePath.EndsWith(".fs", StringComparison.Ordinal)))
            {
                var path = Path.Combine(workspace, Path.GetFileName(file.RelativePath));
                await File.WriteAllTextAsync(path, file.Content, new UTF8Encoding(false));
                script.AppendLine($"#load @\"{path.Replace("\"", "\"\"")}\"");
            }
            script.AppendLine(OpenApiFixture.Read("NativeClients/fsharp-consumer.fsx"));
            var scriptPath = Path.Combine(workspace, "consumer.fsx");
            await File.WriteAllTextAsync(scriptPath, script.ToString(), new UTF8Encoding(false));
            if (compiler.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                await GeneratedFSharpNativeTests.RunCompiler("dotnet", [compiler, "--exec", scriptPath], workspace);
            else
                await GeneratedFSharpNativeTests.RunCompiler(compiler, ["--exec", scriptPath], workspace);
            passed = true;
        }
        finally
        {
            if (passed) Directory.Delete(workspace, recursive: true);
            else TestContext.Out.WriteLine($"Generated F# client integration retained at: {workspace}");
        }
    }
}
