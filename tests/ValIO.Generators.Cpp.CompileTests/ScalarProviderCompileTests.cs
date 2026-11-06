using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using ValIO.Core.IR;
using ValIO.Core.IR.Builders;
using ValIO.Core.Models;
using ValIO.Core.Plugins;
using ValIO.Generators.Cpp.Ast.Renderers;
using ValIO.Generators.Cpp.Models;

namespace ValIO.Generators.Cpp.CompileTests;

/// <summary>Compiles actual provider libraries with complete generated DTO, serializer, and client implementations.</summary>
/// <remarks>Provider source overrides and one installed CMake dependency prefix permit deterministic offline acceptance.</remarks>
[TestFixture]
[Category("CompileFixture")]
[Parallelizable(ParallelScope.None)]
public sealed class ScalarProviderCompileTests
{
    /// <summary>Captures the complete native tool output together with its verified process exit status.</summary>
    /// <param name="ExitCode">The exit status, or minus one after the bounded process-tree timeout.</param>
    /// <param name="Output">Combined standard output and standard error text.</param>
    private sealed record ToolResult(int ExitCode, string Output);

    /// <summary>Locates an installed CMake supporting the modern Visual Studio toolchain.</summary>
    /// <returns>The CMake executable path, or null when CMake is unavailable.</returns>
    private static string? FindCmake()
    {
        var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "CMake", "bin", "cmake.exe");
        if (OperatingSystem.IsWindows() && File.Exists(installed)) return installed;
        return (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator)
            .Where(directory => !string.IsNullOrWhiteSpace(directory))
            .Select(directory => Path.Combine(directory.Trim('"'), OperatingSystem.IsWindows() ? "cmake.exe" : "cmake"))
            .FirstOrDefault(File.Exists);
    }

    /// <summary>Runs one tool while draining both output pipes and retaining diagnostics on failure.</summary>
    /// <param name="executable">Executable file path.</param>
    /// <param name="arguments">Individual arguments passed without shell interpolation.</param>
    /// <param name="workRoot">Isolated native acceptance directory.</param>
    /// <param name="logName">Diagnostic log file name.</param>
    /// <returns>The exit status and complete tool output.</returns>
    private static async Task<ToolResult> RunTool(string executable, IEnumerable<string> arguments, string workRoot, string logName)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(executable)
            {
                WorkingDirectory = workRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            timedOut = true;
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
        var output = await standardOutput + await standardError;
        await File.WriteAllTextAsync(Path.Combine(workRoot, logName), output);
        return new ToolResult(timedOut ? -1 : process.ExitCode, output);
    }

    /// <summary>Adds explicit, validated provider source or installed dependency directories.</summary>
    /// <param name="arguments">CMake configure arguments being assembled.</param>
    /// <param name="environmentVariable">Environment variable identifying the dependency location.</param>
    /// <param name="cmakeVariable">CMake variable receiving the normalized path.</param>
    private static void AddDirectoryArgument(List<string> arguments, string environmentVariable, string cmakeVariable)
    {
        var configured = Environment.GetEnvironmentVariable(environmentVariable);
        if (string.IsNullOrWhiteSpace(configured)) return;
        var paths = configured.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(Path.GetFullPath).ToArray();
        foreach (var path in paths) Directory.Exists(path).Should().BeTrue($"{environmentVariable} must identify an existing dependency directory");
        arguments.Add($"-D{cmakeVariable}={string.Join(';', paths.Select(path => path.Replace('\\', '/')))}");
    }

    /// <summary>Generates the same scalar models and client operations for each representation matrix entry.</summary>
    /// <param name="outputDirectory">Isolated generated source directory.</param>
    /// <param name="standard">Requested C++ language standard.</param>
    /// <param name="temporal">Temporal representation.</param>
    /// <param name="uuid">UUID representation.</param>
    /// <returns>The asynchronous pipeline execution.</returns>
    private static async Task Generate(string outputDirectory, int standard, CppTemporalBackend temporal, CppUuidBackend uuid)
    {
        var options = new CppGenerationOptions
        {
            Runtime = CppRuntimeProfile.Std,
            Namespace = "ScalarProviders",
            CxxStandard = standard,
            ApiStyle = CppApiStyle.Sync,
            EmitCmake = true,
            TemporalBackend = temporal,
            UuidBackend = uuid,
        };
        var models = new[] { UuidProviderFixture.CreateModel(), TemporalProviderFixture.CreateModel() }
            .Concat(ProviderClientFixture.CreateFormModels()).ToArray();
        var document = new IrDocument
        {
            Metadata = new IrDocumentMetadata { Title = "Scalar provider acceptance", Version = "1.0" },
            Models = models,
            Enums = [],
            Clients = [ProviderClientFixture.CreateClient(), ProviderClientFixture.CreateFormClient()],
            TypeDependencies = new IrTypeDependencyGraphBuilder().Build(models, []),
        };
        using var services = new ServiceCollection().AddSingleton<ICppAstRenderer, CppAstRenderer>().BuildServiceProvider();
        var context = new GenerationContext
        {
            IrDocument = document,
            Configuration = new GenerationConfiguration { LanguageId = "cpp", PluginOptions = options },
            PluginOptions = options,
        };
        await new CppGeneratorPlugin().CreatePipeline(services).ExecuteAsync(context);
        foreach (var file in context.GeneratedFiles)
        {
            var path = Path.Combine(outputDirectory, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, file.Content);
        }
    }

    /// <summary>Compiles pure DTO consumers, complete client implementations, and strict bidirectional provider codecs.</summary>
    /// <param name="standard">C++17, C++20, or C++23.</param>
    /// <param name="temporal">Temporal representation under test.</param>
    /// <param name="uuid">UUID representation under test.</param>
    /// <returns>The asynchronous native acceptance run.</returns>
    [TestCase(17, CppTemporalBackend.String, CppUuidBackend.String)]
    [TestCase(17, CppTemporalBackend.String, CppUuidBackend.BoostUuid)]
    [TestCase(17, CppTemporalBackend.HowardDate, CppUuidBackend.StdUuid)]
    [TestCase(17, CppTemporalBackend.BoostDateTime, CppUuidBackend.BoostUuid)]
    [TestCase(17, CppTemporalBackend.Valio, CppUuidBackend.StdUuid)]
    [TestCase(20, CppTemporalBackend.Chrono, CppUuidBackend.ModernUuid)]
    [TestCase(23, CppTemporalBackend.Chrono, CppUuidBackend.String)]
    public async Task Providers_CompileAndRoundTrip(int standard, CppTemporalBackend temporal, CppUuidBackend uuid)
    {
        var cmake = FindCmake();
        if (cmake is null) Assert.Ignore("Native scalar acceptance requires CMake and a C++ toolchain.");
        var workRoot = Path.Combine(Path.GetTempPath(), $"valio-scalars-{standard}-{temporal}-{uuid}-{Guid.NewGuid():N}");
        var generated = Path.Combine(workRoot, "generated");
        var build = Path.Combine(workRoot, "build");
        var fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");
        Directory.CreateDirectory(workRoot);
        TestContext.Progress.WriteLine($"Native scalar provider evidence: {workRoot}");
        await Generate(generated, standard, temporal, uuid);
        var configure = new List<string>
        {
            "-S", Path.Combine(fixtures, "scalar-providers"), "-B", build,
            $"-DGEN_DIR={generated.Replace('\\', '/')}", $"-DCMAKE_CXX_STANDARD={standard}",
            $"-DVALIO_UUID_BACKEND={uuid}", $"-DVALIO_TEMPORAL_BACKEND={temporal}",
            $"-DVALIO_TEMPORAL_BACKEND_INDEX={(int)temporal}", "-DCMAKE_BUILD_TYPE=Release",
        };
        AddDirectoryArgument(configure, "VALIO_CPP_SCALAR_CMAKE_PREFIX_PATH", "CMAKE_PREFIX_PATH");
        AddDirectoryArgument(configure, "VALIO_CPP_STDUUID_SOURCE", "VALIO_STDUUID_SOURCE");
        AddDirectoryArgument(configure, "VALIO_CPP_MODERN_UUID_SOURCE", "VALIO_MODERN_UUID_SOURCE");
        var configured = await RunTool(cmake!, configure, workRoot, "configure.log");
        configured.ExitCode.Should().Be(0, $"real provider dependency configuration must succeed:\n{configured.Output}");
        var compiled = await RunTool(cmake!, ["--build", build, "--config", "Release"], workRoot, "build.log");
        compiled.ExitCode.Should().Be(0, $"generated models, codecs, and clients must compile with the selected actual providers:\n{compiled.Output}");
        var smoke = Directory.EnumerateFiles(build, "scalar_provider_main*", SearchOption.AllDirectories)
            .FirstOrDefault(path => Path.GetExtension(path) is ".exe" or "");
        smoke.Should().NotBeNull($"the compiled provider acceptance executable must exist:\n{compiled.Output}");
        var executed = await RunTool(smoke!, [Path.Combine(fixtures, "uuid-values.json"), Path.Combine(fixtures, "temporal-values.json")], workRoot, "acceptance.log");
        executed.ExitCode.Should().Be(0, $"UUID and temporal parsing, JSON round-trips, and actual client wire conversion must agree:\n{executed.Output}");
    }
}
