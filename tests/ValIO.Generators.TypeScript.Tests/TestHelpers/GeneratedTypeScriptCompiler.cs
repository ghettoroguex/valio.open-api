using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using NUnit.Framework;
using ValIO.Core.Plugins;

namespace ValIO.Generators.TypeScript.Tests.TestHelpers;

/// <summary>Checks generated TypeScript using the real compiler and externally installed dependency declarations.</summary>
internal static class GeneratedTypeScriptCompiler
{
    /// <summary>Runs a strict semantic check over every supplied generated source file.</summary>
    /// <param name="files">The actual TypeScript files emitted by the generation pipeline.</param>
    /// <param name="caseName">The scenario included in failure diagnostics.</param>
    /// <returns>The successful compiler output, including its version and checked source count.</returns>
    public static async Task<string> Compile(IReadOnlyList<GeneratedFile> files, string caseName)
    {
        using var workspace = new GeneratedTypeScriptTestWorkspace();
        var sourcePaths = await workspace.WriteSources(files);
        var output = await workspace.RunNode("compile.cjs", CompilerScript(workspace.Root, workspace.Modules, sourcePaths), caseName);
        output.Should().Contain("strict compilation passed", "the real compiler must complete its semantic check");
        workspace.MarkSuccessful();
        return output;
    }

    /// <summary>Builds a compiler program that resolves actual external package exports and declaration files.</summary>
    /// <param name="workspace">The temporary generated-source root.</param>
    /// <param name="modules">The external node_modules directory containing real package declarations.</param>
    /// <param name="sourcePaths">Every emitted TypeScript source path, including clients, models, schemas, and exports.</param>
    /// <returns>A CommonJS Node script that fails on any compiler diagnostic.</returns>
    private static string CompilerScript(string workspace, string modules, IReadOnlyList<string> sourcePaths) => $$"""
        const path = require('node:path');
        const modules = {{JsonSerializer.Serialize(modules)}};
        const ts = require(path.join(modules, 'typescript', 'lib', 'typescript.js'));
        const root = {{JsonSerializer.Serialize(workspace)}};
        const files = {{JsonSerializer.Serialize(sourcePaths)}};
        const options = {
          strict: true,
          noEmit: true,
          target: ts.ScriptTarget.ES2022,
          module: ts.ModuleKind.ESNext,
          moduleResolution: ts.ModuleResolutionKind.Bundler,
          esModuleInterop: true,
          allowSyntheticDefaultImports: true,
          experimentalDecorators: true,
          forceConsistentCasingInFileNames: true,
          skipLibCheck: true,
          lib: ['lib.es2022.d.ts', 'lib.dom.d.ts', 'lib.dom.iterable.d.ts'],
          types: ['node'],
          typeRoots: [path.join(modules, '@types')]
        };
        const host = ts.createCompilerHost(options);
        const cache = ts.createModuleResolutionCache(root, file => file, options);
        const dependencyEntry = path.join(path.dirname(modules), '__valio_dependency_lookup__.ts');
        host.resolveModuleNames = (names, containingFile) => names.map(name => {
          let resolved = ts.resolveModuleName(name, containingFile, options, host, cache).resolvedModule;
          if (!resolved && !name.startsWith('.') && !path.isAbsolute(name))
            resolved = ts.resolveModuleName(name, dependencyEntry, options, host, cache).resolvedModule;
          return resolved;
        });
        const program = ts.createProgram(files, options, host);
        const diagnostics = ts.getPreEmitDiagnostics(program);
        if (diagnostics.length > 0) {
          process.stderr.write(ts.formatDiagnosticsWithColorAndContext(diagnostics, {
            getCanonicalFileName: file => file,
            getCurrentDirectory: () => root,
            getNewLine: () => '\n'
          }));
          process.exitCode = 1;
        } else {
          console.log(`strict compilation passed: ${files.length} generated files, TypeScript ${ts.version}`);
        }
        """;
}

/// <summary>Owns isolated generated sources, bounded Node execution, and failure artifacts shared by integration checks.</summary>
internal sealed class GeneratedTypeScriptTestWorkspace : IDisposable
{
    /// <summary>Limits each captured stream while continuing to drain the process pipes.</summary>
    private const int MaximumOutputCharacters = 1024 * 1024;

    /// <summary>Bounds the Node compiler or runtime execution for each scenario.</summary>
    private static readonly TimeSpan ExecutionTimeout = TimeSpan.FromSeconds(60);

    /// <summary>Compares normalized filesystem paths according to the host filesystem convention.</summary>
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    /// <summary>Stores the canonical parent used to verify cleanup remains within the temporary directory.</summary>
    private readonly string _temporaryRoot;

    /// <summary>Records whether the caller completed every compilation or runtime assertion.</summary>
    private bool _successful;

    /// <summary>Creates a unique owned directory after validating the explicitly configured dependency environment.</summary>
    /// <remarks>An absent environment variable skips optional Node integration; an invalid configured value fails.</remarks>
    public GeneratedTypeScriptTestWorkspace()
    {
        Modules = NodeModulesDirectory();
        _temporaryRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        Root = Path.Combine(_temporaryRoot, $"valio-typescript-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Root);
    }

    /// <summary>Gets the unique temporary source directory owned by this integration scenario.</summary>
    /// <value>The absolute workspace path.</value>
    public string Root { get; }

    /// <summary>Gets the verified external installation containing TypeScript and the real libraries.</summary>
    /// <value>The absolute node_modules path supplied through VALIO_VALIDATOR_NODE_MODULES.</value>
    public string Modules { get; }

    /// <summary>Validates every destination before writing generated TypeScript into the owned workspace.</summary>
    /// <param name="files">The actual source files emitted by the generation pipeline.</param>
    /// <returns>The absolute compiler input paths in the original generation order.</returns>
    public async Task<IReadOnlyList<string>> WriteSources(IReadOnlyList<GeneratedFile> files)
    {
        files.Should().NotBeEmpty("the generation pipeline must emit TypeScript sources");
        var sourcePaths = files.Select(file => GeneratedSourcePath(file.RelativePath)).ToArray();
        sourcePaths.Distinct(StringComparer.OrdinalIgnoreCase).Count().Should().Be(sourcePaths.Length,
            "generated files must not overwrite destinations that differ only by letter casing");

        for (var index = 0; index < files.Count; index++)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePaths[index])!);
            await File.WriteAllTextAsync(sourcePaths[index], files[index].Content, Encoding.UTF8);
        }
        return sourcePaths;
    }

    /// <summary>Runs one Node script with concurrent bounded output and preserves diagnostics on failure.</summary>
    /// <param name="scriptName">The single-file CommonJS script name within the workspace.</param>
    /// <param name="script">The compiler or runtime JavaScript source.</param>
    /// <param name="caseName">The scenario included in errors and saved diagnostics.</param>
    /// <returns>The combined standard output and standard error of the successful process.</returns>
    public async Task<string> RunNode(string scriptName, string script, string caseName)
    {
        Path.GetFileName(scriptName).Should().Be(scriptName, "the Node harness script must remain in the owned workspace root");
        var scriptPath = Path.Combine(Root, scriptName);
        await File.WriteAllTextAsync(scriptPath, script, Encoding.UTF8);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("node")
            {
                WorkingDirectory = Root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };
        process.StartInfo.ArgumentList.Add(scriptPath);
        process.StartInfo.Environment["NODE_PATH"] = Modules;
        process.Start().Should().BeTrue("Node must start the generated TypeScript check for {0}", caseName);
        var outputTask = ReadOutput(process.StandardOutput);
        var errorTask = ReadOutput(process.StandardError);
        using var timeout = new CancellationTokenSource(ExecutionTimeout);
        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            timedOut = true;
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }

        var standardOutput = await outputTask;
        var standardError = await errorTask;
        await File.WriteAllTextAsync(Path.Combine(Root, "stdout.log"), standardOutput, Encoding.UTF8);
        await File.WriteAllTextAsync(Path.Combine(Root, "stderr.log"), standardError, Encoding.UTF8);
        var output = standardOutput + Environment.NewLine + standardError;
        timedOut.Should().BeFalse("Node execution must finish within {0} for {1}. Sources and logs: {2}\n{3}",
            ExecutionTimeout, caseName, Root, output);
        process.ExitCode.Should().Be(0, "generated TypeScript must satisfy {0}. Sources and logs: {1}\n{2}", caseName, Root, output);
        return output;
    }

    /// <summary>Allows cleanup only after the caller has completed every scenario assertion.</summary>
    public void MarkSuccessful() => _successful = true;

    /// <summary>Deletes only successful owned output and retains failed sources and logs for inspection.</summary>
    public void Dispose()
    {
        if (!_successful)
        {
            TestContext.Out.WriteLine($"Generated TypeScript sources and logs retained at: {Root}");
            return;
        }

        Path.GetFullPath(Root).StartsWith(_temporaryRoot + Path.DirectorySeparatorChar, PathComparison)
            .Should().BeTrue("recursive cleanup must remain inside the verified temporary root");
        Directory.Delete(Root, recursive: true);
    }

    /// <summary>Resolves a relative generated TypeScript destination without allowing traversal outside the workspace.</summary>
    /// <param name="relativePath">The pipeline-provided source path.</param>
    /// <returns>The normalized absolute destination path.</returns>
    private string GeneratedSourcePath(string relativePath)
    {
        string.IsNullOrWhiteSpace(relativePath).Should().BeFalse("generated sources require a relative file path");
        Path.IsPathRooted(relativePath).Should().BeFalse("generated sources must use workspace-relative paths");
        relativePath.EndsWith(".ts", StringComparison.Ordinal).Should().BeTrue("only emitted TypeScript sources belong in this workspace");
        var normalizedPath = relativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        var destination = Path.GetFullPath(Path.Combine(Root, normalizedPath));
        destination.StartsWith(Root + Path.DirectorySeparatorChar, PathComparison).Should().BeTrue(
            "generated sources must remain within the owned workspace: {0}", relativePath);
        return destination;
    }

    /// <summary>Validates the optional real-library setup while distinguishing missing and invalid configuration.</summary>
    /// <returns>The absolute directory containing the real TypeScript compiler.</returns>
    private static string NodeModulesDirectory()
    {
        var modules = Environment.GetEnvironmentVariable("VALIO_VALIDATOR_NODE_MODULES");
        if (modules is null)
        {
            Assert.Ignore("Set VALIO_VALIDATOR_NODE_MODULES to run generated TypeScript checks against real libraries.");
            return string.Empty;
        }

        string.IsNullOrWhiteSpace(modules).Should().BeFalse("VALIO_VALIDATOR_NODE_MODULES is explicitly configured and cannot be blank");
        var directory = Path.GetFullPath(modules);
        Directory.Exists(directory).Should().BeTrue("VALIO_VALIDATOR_NODE_MODULES must identify an existing dependency directory: {0}", directory);
        File.Exists(Path.Combine(directory, "typescript", "lib", "typescript.js")).Should().BeTrue(
            "the configured dependency directory must contain the real TypeScript compiler");
        return directory;
    }

    /// <summary>Drains one redirected stream while keeping its diagnostic buffer within the output bound.</summary>
    /// <param name="reader">The redirected standard output or standard error reader.</param>
    /// <returns>The captured diagnostic prefix, with a marker when additional output was discarded.</returns>
    private static async Task<string> ReadOutput(StreamReader reader)
    {
        var output = new StringBuilder();
        var buffer = new char[4096];
        var truncated = false;
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory())) > 0)
        {
            var retained = Math.Min(count, MaximumOutputCharacters - output.Length);
            output.Append(buffer, 0, retained);
            truncated |= retained < count;
        }
        if (truncated)
            output.AppendLine().Append("[Additional process output omitted after the diagnostic limit.]");
        return output.ToString();
    }
}
