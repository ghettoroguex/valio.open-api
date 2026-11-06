using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using FluentAssertions;
using NUnit.Framework;
using ValIO.Generators.TypeScript.Options;
using ValIO.Generators.TypeScript.Tests.TestHelpers;

namespace ValIO.Generators.TypeScript.Tests.Steps;

/// <summary>Проверяет циклические типизированные словари в сгенерированных модулях ES.</summary>
[TestFixture]
public sealed class ZodMutualRecordTests
{
    /// <summary>Компилирует и импортирует взаимно рекурсивные схемы словарей для обеих версий Zod.</summary>
    /// <param name="version">Выбранный псевдоним пакета Zod.</param>
    /// <returns>Задача, завершающаяся после проверки вложенных значений взаимно рекурсивными схемами ESM.</returns>
    [TestCase("zod3")]
    [TestCase("zod4")]
    public async Task Mutual_records_validate_nested_values_in_esm(string version)
    {
        var modules = Environment.GetEnvironmentVariable("VALIO_TEST_NODE_MODULES");
        if (string.IsNullOrWhiteSpace(modules) || !Directory.Exists(modules))
        {
            Assert.Fail("Set VALIO_TEST_NODE_MODULES to pinned typescript/zod3/zod4 node_modules.");
            return;
        }
        var packageRoot = Directory.GetParent(Path.GetFullPath(modules))!.FullName;
        var temporary = Path.Combine(packageRoot, "valio-record-cycle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            using var harness = new PipelineTestHarness();
            var document = await PipelineTestHarness.LoadDocumentAsync("mutual-record-cycles.json");
            var context = await harness.RunAsync(document, new TypeScriptGenerationOptions
                { Transports = ["fetch"], ValidationLibrary = version });
            foreach (var file in context.GeneratedFiles.Where(f => f.RelativePath.EndsWith(".ts", StringComparison.Ordinal)))
            {
                var destination = Path.Combine(temporary, file.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                var contents = file.Content.Replace("from 'zod/v4'", "from 'zod4'", StringComparison.Ordinal)
                    .Replace("from 'zod'", "from 'zod3'", StringComparison.Ordinal);
                await File.WriteAllTextAsync(destination, contents);
            }
            var alpha = Path.Combine(temporary, "models", "alpha", "alpha.schema.ts");
            var beta = Path.Combine(temporary, "models", "beta", "beta.schema.ts");
            var output = Path.Combine(temporary, "out");
            var compiler = Path.Combine(modules, "typescript", "bin", "tsc");
            await RunNode(temporary, modules, [compiler, "--strict", "--skipLibCheck", "--target", "ES2022",
                "--module", "ESNext", "--moduleResolution", "bundler", "--rootDir", temporary,
                "--outDir", output, alpha, beta]);

            // Node ESM requires extensions on relative imports; the generator targets bundlers.
            foreach (var file in Directory.EnumerateFiles(output, "*.js", SearchOption.AllDirectories))
            {
                var contents = await File.ReadAllTextAsync(file);
                contents = Regex.Replace(contents, @"(from\s+[""'])(\.{1,2}/[^""']+)([""'])",
                    match => match.Groups[1].Value + match.Groups[2].Value + ".js" + match.Groups[3].Value);
                await File.WriteAllTextAsync(file, contents);
            }
            await File.WriteAllTextAsync(Path.Combine(output, "package.json"), "{\"type\":\"module\"}");
            var compiled = Path.Combine(output, "models", "alpha", "alpha.schema.js");
            var script = "const {AlphaSchema:s}=await import(process.argv[1]);" +
                "const ok={name:'root',children:{entry:[{rank:'v',children:{back:{name:'inner'}}}]}};" +
                "if(!s.safeParse(ok).success)process.exit(1);" +
                "const bad=[{name:'root',children:{entry:[{rank:42}]}}," +
                "{name:'root',children:{entry:[{rank:'v',children:{back:{name:123}}}]}}];" +
                "if(bad.some(x=>s.safeParse(x).success))process.exit(2);";
            await RunNode(temporary, modules, ["--input-type=module", "--eval", script, new Uri(compiled).AbsoluteUri]);
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }

    /// <summary>Выполняет команду Node с тестовыми зависимостями закреплённых версий.</summary>
    /// <param name="workingDirectory">Изолированный каталог сгенерированных файлов.</param>
    /// <param name="modules">Каталог зависимостей закреплённых версий.</param>
    /// <param name="arguments">Аргументы команды Node.</param>
    /// <returns>Задача, завершающаяся после успешного выполнения команды.</returns>
    private static async Task RunNode(string workingDirectory, string modules, IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo("node")
        { WorkingDirectory = workingDirectory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.Environment["NODE_PATH"] = modules;
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        Process? process;
        try { process = Process.Start(start); }
        catch (Win32Exception) { Assert.Fail("Node.js is required for the mutual-record contract test."); return; }
        using (process)
        {
            process.Should().NotBeNull();
            var stdout = await process!.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            process.ExitCode.Should().Be(0, stdout + stderr);
        }
    }
}
