using System.ComponentModel;
using System.Diagnostics;
using FluentAssertions;
using NUnit.Framework;
using ValIO.Core.Plugins;
using ValIO.Generators.TypeScript.Options;
using ValIO.Generators.TypeScript.Tests.TestHelpers;

namespace ValIO.Generators.TypeScript.Tests.Steps;

/// <summary>Проверяет контракт Identity для необязательности Zod, типизированных словарей и общих форматов UUID.</summary>
[TestFixture]
public sealed class IdentityZodUuidContractTests
{
    /// <summary>Создаёт конвейер генерации для одного изолированного теста.</summary>
    /// <param name="fixture">Имя встроенного документа OpenAPI.</param>
    /// <param name="validation">Используемая версия Zod.</param>
    /// <param name="transport">Нативный HTTP-транспорт для генерации.</param>
    /// <returns>Сгенерированные файлы.</returns>
    private static async Task<GenerationContext> Generate(string fixture, string validation, string transport = "axios")
    {
        using var harness = new PipelineTestHarness();
        var document = await PipelineTestHarness.LoadDocumentAsync(fixture);
        return await harness.RunAsync(document, new TypeScriptGenerationOptions
        { Transports = [transport], ValidationLibrary = validation, UuidFormat = "regex" });
    }

    /// <summary>Компилирует встроенную проверку UUID запроса со всеми нативными транспортами и валидаторами.</summary>
    /// <param name="transport">Нативный HTTP-транспорт, включая клиент Angular на основе Promise.</param>
    /// <param name="validation">Библиотека валидации, константу UUID которой необходимо импортировать.</param>
    /// <returns>Задача, завершающаяся после строгой семантической компиляции всех сгенерированных исходных файлов.</returns>
    [Test]
    public async Task InlineUuidBodyCompilesAcrossNativeTransports(
        [Values("axios", "fetch", "solid", "nextjs", "got", "ky", "ofetch", "undici", "angular-promise", "angular-rxjs")] string transport,
        [Values("zod3", "zod4", "valibot", "typebox")] string validation)
    {
        var context = await Generate("uuid-only-body.json", validation, transport);
        context.GeneratedFiles.Should().Contain(file => file.RelativePath.Replace('\\', '/').EndsWith("models/formats.ts", StringComparison.Ordinal));
        await GeneratedTypeScriptCompiler.Compile(context.GeneratedFiles
            .Where(file => file.RelativePath.EndsWith(".ts", StringComparison.Ordinal)).ToArray(),
            $"inline UUID body: {transport}/{validation}");
    }

    /// <summary>Проверяет окончания строк платформы в сгенерированных схемах Zod и многострочных клиентах TypeScript.</summary>
    /// <param name="version">Выбранная версия Zod.</param>
    /// <returns>Задача, завершающаяся после проверки единообразия окончаний строк в сгенерированных файлах.</returns>
    [TestCase("zod3")]
    [TestCase("zod4")]
    public async Task GeneratedSchemasAndClientsUseConsistentLineEndings(string version)
    {
        var context = await Generate("zod-contract.json", version);
        var schema = context.GeneratedFiles.First(f => f.RelativePath.Replace('\\', '/').EndsWith("models/contract/contract.schema.ts", StringComparison.Ordinal));
        var client = context.GeneratedFiles.First(f => f.RelativePath.EndsWith(".axios.ts", StringComparison.Ordinal));
        schema.Content.Should().Contain("z.record(z.string(), z.array(z.string()))");
        client.Content.Should().Contain("signal?: AbortSignal");
        foreach (var file in new[] { schema, client })
        {
            var remainder = file.Content.Replace(Environment.NewLine, "", StringComparison.Ordinal);
            remainder.Should().NotContain("\n", file.RelativePath).And.NotContain("\r", file.RelativePath);
        }
        foreach (var file in context.GeneratedFiles.Where(f => f.RelativePath.EndsWith(".ts", StringComparison.Ordinal)))
        {
            var withoutCrLf = file.Content.Replace("\r\n", "", StringComparison.Ordinal);
            withoutCrLf.Should().NotContain("\r", file.RelativePath);
            if (file.Content.Contains("\r\n", StringComparison.Ordinal))
                withoutCrLf.Should().NotContain("\n", file.RelativePath);
        }
    }

    /// <summary>Проверяет все сочетания обязательности и допустимости null и типизированные рекурсивные словари в обеих версиях Zod.</summary>
    /// <param name="version">Выбранная версия Zod.</param>
    /// <returns>Задача, завершающаяся после проверки наличия свойств модели, допустимости null и схем словарей.</returns>
    [TestCase("zod3")]
    [TestCase("zod4")]
    public async Task Model_distinguishes_absence_from_null_and_keeps_dictionary_values(string version)
    {
        var context = await Generate("zod-contract.json", version);
        var schema = context.GeneratedFiles.First(f => f.RelativePath.Replace('\\', '/').EndsWith("models/contract/contract.schema.ts", StringComparison.Ordinal)).Content;
        schema.Should().Contain("requiredValue: z.string()");
        schema.Should().NotContain("requiredValue: z.string().optional()");
        schema.Should().NotContain("requiredValue: z.string().nullish()");
        schema.Should().Contain("requiredNullable: z.string().nullable()");
        schema.Should().Contain("optionalValue: z.string().optional()");
        schema.Should().Contain("optionalNullable: z.string().nullable().optional()");
        schema.Should().Contain("optionalNullableArray: z.array(z.number().int()).nullable().optional()");
        schema.Should().Contain("requiredNullableDictionary: z.record(z.string(), z.string()).nullable()");
        schema.Should().Contain("nullableValues: z.record(z.string(), z.string().nullable()).optional()");
        schema.Should().Contain("errors: z.record(z.string(), z.array(z.string())).optional()");
        schema.Should().Contain("z.record(z.string(), z.lazy(() => ContractSchema))");
    }

    /// <summary>Выполняет сгенерированную модель с Zod для проверки допустимости null и валидации значений словаря.</summary>
    /// <param name="version">Выбранная версия Zod.</param>
    /// <returns>Задача, завершающаяся после проверки отклонения недопустимых значений сгенерированной схемой Zod.</returns>
    [TestCase("zod3")]
    [TestCase("zod4")]
    public async Task Generated_model_rejects_invalid_nulls_and_dictionary_values(string version)
    {
        var context = await Generate("zod-contract.json", version);
        var file = context.GeneratedFiles.First(f => f.RelativePath.Replace('\\', '/').EndsWith("models/contract/contract.schema.ts", StringComparison.Ordinal)).Content;
        var startIndex = file.IndexOf("= z.object({", StringComparison.Ordinal);
        startIndex.Should().BeGreaterThanOrEqualTo(0);
        var endIndex = file.IndexOf("});", startIndex, StringComparison.Ordinal);
        endIndex.Should().BeGreaterThan(startIndex);
        var expression = file[(startIndex + 2)..(endIndex + 2)];
        var load = version == "zod3"
            ? "let z;try{z=require('zod3').z}catch(e){process.exit(77)};"
            : "let z;try{z=require('zod4').z}catch(e){process.exit(77)};";
        var js = load + "const ContractSchema=" + expression + ";" +
            "const ok={requiredValue:'a',requiredNullable:null,requiredNullableDictionary:null,nullableValues:{a:null}};" +
            "if(!ContractSchema.safeParse(ok).success)process.exit(1);" +
            "const bad=[{...ok,optionalValue:null},{...ok,errors:{a:[42]}}," +
            "{...ok,requiredNullable:undefined},{...ok,requiredNullableDictionary:{a:5}}];" +
            "if(bad.some(x=>ContractSchema.safeParse(x).success))process.exit(2);";
        var processStart = new ProcessStartInfo("node") { RedirectStandardError = true, UseShellExecute = false };
        var moduleDir = Environment.GetEnvironmentVariable("VALIO_TEST_NODE_MODULES");
        if (!string.IsNullOrWhiteSpace(moduleDir)) processStart.Environment["NODE_PATH"] = moduleDir;
        processStart.ArgumentList.Add("--eval");
        processStart.ArgumentList.Add(js);
        Process? process;
        try { process = Process.Start(processStart); }
        catch (Win32Exception) { Assert.Fail("Node.js is required to execute Zod contract tests."); return; }
        using (process)
        {
            process.Should().NotBeNull();
            var error = await process!.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode == 77) Assert.Fail("Install pinned zod3/zod4 aliases and set VALIO_TEST_NODE_MODULES.");
            process.ExitCode.Should().Be(0, error);
        }
    }

    /// <summary>Компилирует сгенерированные схему и модель в строгом режиме TypeScript с выбранной основной версией Zod.</summary>
    /// <param name="version">Выбранная версия Zod.</param>
    /// <returns>Задача, завершающаяся после успешной строгой компиляции сгенерированных модели и схемы в TypeScript.</returns>
    [TestCase("zod3")]
    [TestCase("zod4")]
    public async Task Generated_model_and_schema_typecheck_strictly(string version)
    {
        var moduleDir = Environment.GetEnvironmentVariable("VALIO_TEST_NODE_MODULES");
        if (string.IsNullOrWhiteSpace(moduleDir) || !Directory.Exists(moduleDir))
        {
            Assert.Fail("Set VALIO_TEST_NODE_MODULES to pinned typescript/zod3/zod4 node_modules.");
            return;
        }
        var packageRoot = Directory.GetParent(Path.GetFullPath(moduleDir))!.FullName;
        var temporary = Path.Combine(packageRoot, "valio-zod-contract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var context = await Generate("zod-contract.json", version);
            var schema = context.GeneratedFiles.First(f => f.RelativePath.Replace('\\', '/').EndsWith("models/contract/contract.schema.ts", StringComparison.Ordinal)).Content;
            var model = context.GeneratedFiles.First(f => f.RelativePath.Replace('\\', '/').EndsWith("models/contract/contract.ts", StringComparison.Ordinal)).Content;
            schema = schema.Replace("from 'zod/v4'", "from 'zod4'", StringComparison.Ordinal)
                .Replace("from 'zod'", "from 'zod3'", StringComparison.Ordinal);
            await File.WriteAllTextAsync(Path.Combine(temporary, "contract.ts"), model);
            var schemaPath = Path.Combine(temporary, "contract.schema.ts");
            await File.WriteAllTextAsync(schemaPath, schema);
            var compiler = Path.Combine(moduleDir, "typescript", "bin", "tsc");
            var start = new ProcessStartInfo("node") { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
            foreach (var arg in new[] { compiler, "--strict", "--noEmit", "--skipLibCheck", "--target", "ES2020", "--module", "Node16", "--moduleResolution", "Node16", schemaPath })
                start.ArgumentList.Add(arg);
            using var process = Process.Start(start);
            process.Should().NotBeNull();
            var output = await process!.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            process.ExitCode.Should().Be(0, output + error);
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }

    /// <summary>Проверяет типизированные значения словаря во встроенных схемах запроса и результата.</summary>
    /// <param name="version">Выбранная версия Zod.</param>
    /// <returns>Задача, завершающаяся после проверки встроенных схем словаря для запроса и ответа.</returns>
    [TestCase("zod3")]
    [TestCase("zod4")]
    public async Task Inline_dictionaries_use_two_argument_record(string version)
    {
        var context = await Generate("zod-contract.json", version);
        var schema = context.GeneratedFiles.First(f => f.RelativePath.EndsWith("postDictionaries.results.schemas.ts", StringComparison.Ordinal)).Content;
        const string recordPattern = @"z\.record\(\s*z\.string\(\)\s*,\s*z\.array\(\s*z\.string\(\)\s*\)\s*\)";
        schema.Should().MatchRegex(recordPattern);
        var client = context.GeneratedFiles.First(f => f.RelativePath.EndsWith("postDictionaries.axios.ts", StringComparison.Ordinal)).Content;
        client.Should().MatchRegex(recordPattern);
    }

    /// <summary>Проверяет общее обнаружение UUID, даже если ни одна модель не содержит UUID.</summary>
    /// <param name="fixture">Документ с одним использованием UUID.</param>
    /// <returns>Задача, завершающаяся после проверки обнаружения UUID и общих импортов.</returns>
    [TestCase("uuid-only-response.json")]
    [TestCase("uuid-only-body.json")]
    [TestCase("uuid-only-header.json")]
    public async Task Inline_uuid_uses_generate_shared_formats(string fixture)
    {
        var context = await Generate(fixture, "zod4");
        context.GeneratedFiles.Should().Contain(f => f.RelativePath.Replace('\\', '/').EndsWith("models/formats.ts", StringComparison.Ordinal));
        if (fixture == "uuid-only-response.json")
        {
            var result = context.GeneratedFiles.First(f => f.RelativePath.EndsWith("getResponseOnly.results.schemas.ts", StringComparison.Ordinal)).Content;
            result.Should().Contain("import { UUID_REGEX } from \"../../models/formats\"");
            result.Should().Contain("z.string().regex(UUID_REGEX)");
        }
    }

    /// <summary>Выполняет сгенерированное регулярное выражение JavaScript на граничных случаях UUID Identity.</summary>
    /// <returns>Задача, завершающаяся после проверки сгенерированного регулярного выражения на наборе UUID.</returns>
    [Test]
    public async Task Generated_uuid_regex_rejects_every_trailing_character()
    {
        var context = await Generate("uuid-only-response.json", "zod4");
        var formats = context.GeneratedFiles.First(f => f.RelativePath.Replace('\\', '/').EndsWith("models/formats.ts", StringComparison.Ordinal)).Content;
        var js = formats.Replace("export const ", "const ", StringComparison.Ordinal) + "\n" +
            "const u='78d459e0-1269-40cf-b520-d31c21b13178';" +
            "const good=[u,u.toUpperCase(),'00000000-0000-0000-0000-000000000000'];" +
            "const bad=[...Array.from({length:128},(_,i)=>u+String.fromCharCode(i)),u+'\\u2028',u+'\\u2029',u+'\\r\\n',u+'💡','',u.slice(1),'x'+u,u.replace('-','_')];" +
            "if(good.some(x=>!UUID_REGEX.test(x))||bad.some(x=>UUID_REGEX.test(x))) process.exit(1);";
        var start = new ProcessStartInfo("node") { RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add("--eval");
        start.ArgumentList.Add(js);
        Process? process;
        try { process = Process.Start(start); }
        catch (Win32Exception) { Assert.Fail("Node.js is required to exercise JavaScript RegExp semantics."); return; }
        using (process)
        {
            process.Should().NotBeNull();
            var error = await process!.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            process.ExitCode.Should().Be(0, error);
        }
    }
}
