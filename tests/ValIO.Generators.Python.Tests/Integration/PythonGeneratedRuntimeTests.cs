using System.Diagnostics;
using System.Text;
using FluentAssertions;
using Microsoft.OpenApi;
using NUnit.Framework;
using ValIO.Core.Plugins;
using ValIO.Generators.Python.Options;
using ValIO.Generators.Python.Tests.TestHelpers;
using ValIO.Tests.Fixtures;

namespace ValIO.Generators.Python.Tests.Integration;

/// <summary>Compiles and executes generated packages with the real Pydantic and HTTPX runtimes.</summary>
/// <remarks>
/// Set VALIO_PYTHON_EXECUTABLE to a Python 3.11+ executable with Pydantic 2 and HTTPX installed.
/// An absent setting explicitly skips these native checks; an invalid configured executable fails them.
/// </remarks>
[TestFixture]
[Category("PythonIntegration")]
[NonParallelizable]
public sealed class PythonGeneratedRuntimeTests
{
    /// <summary>Checks model imports, nested validation, JSON serialization, HTTPX pipelines, and cancellation.</summary>
    /// <param name="mode">The generated synchronous or asynchronous HTTPX mode.</param>
    /// <returns>A task completing after the generated package passes its Python assertions.</returns>
    [TestCase("sync")]
    [TestCase("async")]
    public async Task GeneratedPackage_ExecutesPydanticAndHttpxContracts(string mode)
    {
        var executable = GetPythonExecutable();
        var document = await LoadFixtureAsync("Python/runtime-3.1.2.json");
        using var harness = new PythonPipelineTestHarness();
        var context = await harness.RunAsync(document, new PythonGenerationOptions { HttpxMode = mode });
        context.IrDocument.Clients.SelectMany(client => client.Operations)
            .Single(operation => operation.Path == "/optional-body").RequestBody!.IsRequired.Should().BeFalse(
                "required fields constrain a present body and do not make the entire request body mandatory");
        await ExecutePythonAsync(executable, context, mode);
    }

    /// <summary>Compiles model modules and validates inherited fields across each supported OpenAPI dialect.</summary>
    /// <param name="version">The Swagger or OpenAPI fixture version.</param>
    /// <returns>A task completing after imports and Pydantic inherited-property validation pass.</returns>
    [TestCase("2.0")]
    [TestCase("3.0.4")]
    [TestCase("3.1.2")]
    [TestCase("3.2.0")]
    public async Task GeneratedInheritance_ImportsAcrossOpenApiDialects(string version)
    {
        var executable = GetPythonExecutable();
        var document = await LoadFixtureAsync($"Inheritance/chain-{version}.json");
        using var harness = new PythonPipelineTestHarness();
        var context = await harness.RunAsync(document);
        await ExecutePythonAsync(executable, context, "imports");
    }

    /// <summary>Resolves the explicitly configured runtime without silently falling back to another interpreter.</summary>
    /// <returns>The absolute configured Python executable path.</returns>
    private static string GetPythonExecutable()
    {
        var executable = Environment.GetEnvironmentVariable("VALIO_PYTHON_EXECUTABLE");
        if (string.IsNullOrWhiteSpace(executable))
            Assert.Ignore("Set VALIO_PYTHON_EXECUTABLE to run native generated Python/Pydantic/HTTPX checks.");
        File.Exists(executable).Should().BeTrue($"configured Python executable '{executable}' must exist");
        return Path.GetFullPath(executable!);
    }

    /// <summary>Loads an explicit disk fixture through the actual OpenAPI reader.</summary>
    /// <param name="path">The fixture path relative to the shared OpenAPI fixture directory.</param>
    /// <returns>The parsed document without reader diagnostics.</returns>
    private static async Task<OpenApiDocument> LoadFixtureAsync(string path)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(OpenApiFixture.Read(path)));
        var parsed = await OpenApiDocument.LoadAsync(stream, "json");
        (parsed.Diagnostic?.Errors.Select(error => error.Message) ?? []).Should().BeEmpty();
        parsed.Document.Should().NotBeNull();
        return parsed.Document!;
    }

    /// <summary>Writes the unmodified generated files and runs portable Python assertions in an isolated package.</summary>
    /// <param name="executable">The explicitly configured Python executable.</param>
    /// <param name="context">The completed generation output to execute.</param>
    /// <param name="mode">The Python assertion mode selecting sync, async, or dialect import coverage.</param>
    /// <returns>A task completing after the native process exits successfully.</returns>
    private static async Task ExecutePythonAsync(string executable, GenerationContext context, string mode)
    {
        var directory = Path.Combine(Path.GetTempPath(), "valio-python-runtime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var succeeded = false;
        try
        {
            foreach (var generatedFile in context.GeneratedFiles)
            {
                var filePath = Path.Combine(directory, "generated", generatedFile.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                await File.WriteAllTextAsync(filePath, generatedFile.Content, new UTF8Encoding(false));
            }

            await using var resource = typeof(PythonGeneratedRuntimeTests).Assembly.GetManifestResourceStream(
                "ValIO.Generators.Python.Tests.TestData.runtime_assertions.py")
                ?? throw new InvalidOperationException("The portable Python runtime assertions resource is missing.");
            using var reader = new StreamReader(resource);
            var scriptPath = Path.Combine(directory, "runtime_assertions.py");
            await File.WriteAllTextAsync(scriptPath, await reader.ReadToEndAsync(), new UTF8Encoding(false));

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(executable)
                {
                    WorkingDirectory = directory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };
            process.StartInfo.ArgumentList.Add(scriptPath);
            process.StartInfo.ArgumentList.Add(mode);
            process.StartInfo.Environment["PYTHONUTF8"] = "1";
            process.Start().Should().BeTrue();
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                Assert.Fail($"Generated Python runtime check timed out. Preserved output: {directory}");
            }
            var output = await standardOutput;
            var errors = await standardError;
            TestContext.Out.WriteLine(output);
            process.ExitCode.Should().Be(0, $"generated Python assertions must pass; output preserved at {directory}\n{output}\n{errors}");
            succeeded = true;
        }
        finally
        {
            if (succeeded) Directory.Delete(directory, recursive: true);
            else TestContext.Out.WriteLine($"Preserved generated Python package: {directory}");
        }
    }
}
