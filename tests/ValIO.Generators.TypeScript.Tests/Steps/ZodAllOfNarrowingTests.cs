using System.ComponentModel;
using System.Diagnostics;
using FluentAssertions;
using NUnit.Framework;
using ValIO.Generators.TypeScript.Options;
using ValIO.Generators.TypeScript.Tests.TestHelpers;

namespace ValIO.Generators.TypeScript.Tests.Steps;

/// <summary>Проверяет, что валидация allOf сохраняет унаследованные и локально суженные ограничения.</summary>
[TestFixture]
public sealed class ZodAllOfNarrowingTests
{
    /// <summary>Компилирует и выполняет сгенерированные схемы с обеими основными версиями Zod.</summary>
    /// <param name="version">Выбранный псевдоним пакета Zod.</param>
    /// <returns>Задача, завершающаяся после проверки унаследованных ограничений в TypeScript и среде выполнения.</returns>
    [TestCase("zod3")]
    [TestCase("zod4")]
    public async Task Child_requires_nonnull_id_and_preserves_both_length_constraints(string version)
    {
        var modules = Environment.GetEnvironmentVariable("VALIO_TEST_NODE_MODULES");
        if (string.IsNullOrWhiteSpace(modules) || !Directory.Exists(modules))
        {
            Assert.Fail("Set VALIO_TEST_NODE_MODULES to pinned typescript/zod3/zod4 node_modules.");
            return;
        }
        var packageRoot = Directory.GetParent(Path.GetFullPath(modules))!.FullName;
        var temporary = Path.Combine(packageRoot, "valio-allof-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            using var harness = new PipelineTestHarness();
            var document = await PipelineTestHarness.LoadDocumentAsync("allof-narrowing.json");
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
            var schemaPath = Path.Combine(temporary, "models", "child", "child.schema.ts");
            var output = Path.Combine(temporary, "out");
            var compiler = Path.Combine(modules, "typescript", "bin", "tsc");
            await RunNode(temporary, modules, [compiler, "--strict", "--skipLibCheck", "--target", "ES2020",
                "--module", "Node16", "--moduleResolution", "Node16", "--rootDir", temporary, "--outDir", output, schemaPath]);
            var compiled = Path.Combine(output, "models", "child", "child.schema.js");
            var script = "const s=require(process.argv[1]).ChildSchema;" +
                "const ok={name:'n',id:'abc'};if(!s.safeParse(ok).success)process.exit(1);" +
                "const bad=[{name:'n'},{name:'n',id:null},{name:'n',id:'ab'},{name:'n',id:'abcdefg'},{id:'abc'}];" +
                "if(bad.some(x=>s.safeParse(x).success))process.exit(2);";
            await RunNode(temporary, modules, ["--eval", script, compiled]);
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
        catch (Win32Exception) { Assert.Fail("Node.js is required for the allOf contract test."); return; }
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
