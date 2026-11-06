using System.Diagnostics;
using System.Text;
using FluentAssertions;
using Microsoft.OpenApi;
using NUnit.Framework;
using ValIO.Core.Plugins;
using ValIO.Generators.FSharp.Tests.TestHelpers;
using ValIO.Tests.Fixtures;

namespace ValIO.Generators.FSharp.Tests.Integration;

/// <summary>Compiles real generated FSharp model files with the native language compiler.</summary>
/// <remarks>Missing VALIO_FSHARP_FSI skips this opt-in gate; an invalid configured compiler or any diagnostic fails it.</remarks>
[TestFixture]
[Category("NativeIntegration")]
[NonParallelizable]
public sealed class GeneratedFSharpNativeTests
{
    /// <summary>Enumerates explicit dialect, inheritance, composition, enum and collection fixtures.</summary>
    /// <returns>The fixture paths copied into the test output directory.</returns>
    private static IEnumerable<string> Fixtures()
    {
        foreach (var version in new[] { "2.0", "3.0.4", "3.1.2", "3.2.0" })
            yield return $"Inheritance/chain-{version}.json";
        foreach (var version in new[] { "3.0.4", "3.1.2", "3.2.0" })
        {
            foreach (var scenario in new[] { "nested-oneOf", "nested-anyOf", "anyOf-in-allOf", "allOf-in-oneOf" })
                yield return $"Inheritance/{scenario}-{version}.json";
        }
        foreach (var version in new[] { "3.0", "3.1", "3.2" })
            yield return $"CoreIR/composition-presence-{version}.json";
        yield return "TypeScriptCompile/compile-contracts-3.1.json";
    }

    /// <summary>Parses a disk fixture, runs the generator, and compiles its model and enum output without hand edits.</summary>
    /// <param name="fixture">The explicit OpenAPI document to generate.</param>
    /// <returns>The asynchronous native compiler verification.</returns>
    [TestCaseSource(nameof(Fixtures))]
    public async Task GeneratedModels_CompileWithNativeCompiler(string fixture)
    {
        var compiler = Environment.GetEnvironmentVariable("VALIO_FSHARP_FSI");
        if (string.IsNullOrWhiteSpace(compiler))
        {
            Assert.Ignore("Set VALIO_FSHARP_FSI to run the real FSharp compiler gate.");
            return;
        }
        File.Exists(compiler).Should().BeTrue("VALIO_FSHARP_FSI must reference an existing compiler file when configured");
        await using var input = new MemoryStream(Encoding.UTF8.GetBytes(OpenApiFixture.Read(fixture)));
        var parsed = await OpenApiDocument.LoadAsync(input, "json");
        parsed.Document.Should().NotBeNull();
        (parsed.Diagnostic?.Errors.Select(error => error.Message) ?? []).Should().BeEmpty();
        using var harness = new FSharpPipelineTestHarness();
        var context = await harness.RunAsync(parsed.Document!);
        var files = context.GeneratedFiles.Where(file =>
            file.RelativePath.Replace('\\', '/').StartsWith("models/", StringComparison.Ordinal) &&
            file.RelativePath.EndsWith(".fs", StringComparison.Ordinal)).ToArray();
        files.Should().NotBeEmpty();
        var workspace = Path.Combine(Path.GetTempPath(), $"valio-fsharp-native-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);
        var passed = false;
        try
        {
            foreach (var file in files)
            {
                var path = Path.Combine(workspace, Path.GetFileName(file.RelativePath));
                await File.WriteAllTextAsync(path, file.Content, new UTF8Encoding(false));
            }
            var names = context.IrDocument.Enums.Select(value => value.OriginalName)
                .Concat(context.DocumentContext.ModelsInDependencyOrder.Select(model => model.OriginalName)).ToArray();
            var script = new StringBuilder();
            foreach (var name in names)
            {
                var file = files.Single(candidate => Path.GetFileNameWithoutExtension(candidate.RelativePath) == name);
                var path = Path.Combine(workspace, Path.GetFileName(file.RelativePath)).Replace("\"", "\"\"");
                script.AppendLine($"#load @\"{path}\"");
            }
            script.AppendLine("printfn \"native F# model compilation passed\"");
            var scriptPath = Path.Combine(workspace, "models.fsx");
            await File.WriteAllTextAsync(scriptPath, script.ToString(), new UTF8Encoding(false));
            if (compiler.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                await RunCompiler("dotnet", new[] { compiler, "--exec", scriptPath }, workspace);
            else
                await RunCompiler(compiler, new[] { "--exec", scriptPath }, workspace);
            passed = true;
        }
        finally
        {
            if (passed)
                Directory.Delete(workspace, recursive: true);
            else
                TestContext.Out.WriteLine($"Generated FSharp sources retained at: {workspace}");
        }
    }

    /// <summary>Runs a bounded compiler process and preserves full diagnostics when it fails.</summary>
    /// <param name="executable">The configured native compiler host.</param>
    /// <param name="arguments">The unquoted process arguments.</param>
    /// <param name="workspace">The test-owned source directory.</param>
    /// <returns>The asynchronous exit-code check.</returns>
    internal static async Task RunCompiler(string executable, IEnumerable<string> arguments, string workspace)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(executable)
            {
                WorkingDirectory = workspace,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.Start().Should().BeTrue();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            Assert.Fail($"Native FSharp compilation timed out. Sources: {workspace}\n{await output}\n{await error}");
        }
        process.ExitCode.Should().Be(0, "all generated files must compile unchanged. Sources: {0}\n{1}\n{2}",
            workspace, await output, await error);
    }
}
