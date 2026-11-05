using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using FluentAssertions;
using NUnit.Framework;
using ValIO.Generators.TypeScript.Options;
using ValIO.Core.IR.Enums;
using ValIO.Generators.TypeScript.Ast.Renderers;
using ValIO.Generators.TypeScript.Zod.Ast.Builders;
using ValIO.Generators.TypeScript.Tests.TestHelpers;

namespace ValIO.Generators.TypeScript.Tests.Steps;

/// <summary>Checks strict numeric validation for generated model schemas.</summary>
[TestFixture]
public sealed class ZodStrictNumericTests
{
    /// <summary>Compiles and executes generated schemas against both Zod major versions.</summary>
    /// <param name="version">The selected Zod package alias.</param>
    /// <returns>A task that completes after numeric schemas pass TypeScript and runtime checks.</returns>
    /// <param name="culture">The culture used while parsing and generating the model.</param>
    [TestCase("zod3", "en-US")]
    [TestCase("zod3", "ru-RU")]
    [TestCase("zod4", "en-US")]
    [TestCase("zod4", "ru-RU")]
    public async Task Model_numbers_reject_coercion_and_enforce_constraints(string version, string culture)
    {
        var modules = Environment.GetEnvironmentVariable("VALIO_TEST_NODE_MODULES");
        if (string.IsNullOrWhiteSpace(modules) || !Directory.Exists(modules))
        {
            Assert.Fail("Set VALIO_TEST_NODE_MODULES to pinned typescript/zod3/zod4 node_modules.");
            return;
        }
        var previousCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        var packageRoot = Directory.GetParent(Path.GetFullPath(modules))!.FullName;
        var temporary = Path.Combine(packageRoot, "valio-numeric-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            using var harness = new PipelineTestHarness();
            var document = await PipelineTestHarness.LoadDocumentAsync("strict-numeric.json");
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
            var schemaPath = Path.Combine(temporary, "models", "numericModel", "numericModel.schema.ts");
            var output = Path.Combine(temporary, "out");
            var compiler = Path.Combine(modules, "typescript", "bin", "tsc");
            await RunNode(temporary, modules, [compiler, "--strict", "--skipLibCheck", "--target", "ES2020",
                "--module", "Node16", "--moduleResolution", "Node16", "--rootDir", temporary, "--outDir", output, schemaPath]);
            var compiled = Path.Combine(output, "models", "numericModel", "numericModel.schema.js");
            var bigint = new TypeScriptAstRenderer().Visit(
                ZodExpressionBuilder.BuildPrimitiveExpression(IrPrimitiveKind.Long, useBigInt: true).Expression);
            var script = "const z=require('" + version + "').z;const s=require(process.argv[1]).NumericModelSchema;" +
                "const ok={count:7,price:1.5,nullableCount:null,longValue:12};" +
                "if(!s.safeParse(ok).success||!s.safeParse({...ok,nullableCount:0}).success)process.exit(1);" +
                "const bad=[...[-1,1.5,'7',null].map(count=>({...ok,count}))," +
                "{...ok,price:'1.5'},{...ok,price:0.4},{...ok,nullableCount:'2'}," +
                "{...ok,longValue:'12'},{...ok,longValue:null},{...ok,longValue:1.5}];" +
                "for(const item of bad)if(s.safeParse(item).success){console.error('Unexpectedly accepted: '+JSON.stringify(item));process.exit(2);}" +
                "const big=" + bigint + ";if(!big.safeParse(12n).success||big.safeParse('12').success||big.safeParse(null).success)process.exit(3);";
            await RunNode(temporary, modules, ["--eval", script, compiled]);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            Directory.Delete(temporary, recursive: true);
        }
    }

    /// <summary>Runs a Node command with the pinned test dependencies.</summary>
    /// <param name="workingDirectory">The isolated generated-file directory.</param>
    /// <param name="modules">The pinned dependency directory.</param>
    /// <param name="arguments">The Node command arguments.</param>
    /// <returns>A task that completes after the command succeeds.</returns>
    private static async Task RunNode(string workingDirectory, string modules, IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo("node")
        { WorkingDirectory = workingDirectory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.Environment["NODE_PATH"] = modules;
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        Process? process;
        try { process = Process.Start(start); }
        catch (Win32Exception) { Assert.Fail("Node.js is required for the numeric contract test."); return; }
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
