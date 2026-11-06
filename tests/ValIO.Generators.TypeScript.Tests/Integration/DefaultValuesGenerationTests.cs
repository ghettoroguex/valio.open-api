using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using NUnit.Framework;
using ValIO.Generators.TypeScript.Options;
using ValIO.Generators.TypeScript.Tests.TestHelpers;

namespace ValIO.Generators.TypeScript.Tests.Integration;

/// <summary>Exercises explicit defaults through the native generation pipeline.</summary>
[TestFixture]
public sealed class DefaultValuesGenerationTests
{
    /// <summary>A contract with falsy, nested, null and absent defaults.</summary>
    private const string Contract = """
    {"openapi":"3.0.3","info":{"title":"Defaults","version":"1"},
     "paths":{"/items/{id}":{"get":{"operationId":"getItems","parameters":[
       {"name":"id","in":"path","required":true,"schema":{"type":"integer","default":0}},
       {"name":"active","in":"query","required":true,"schema":{"type":"boolean","default":false}},
       {"name":"text","in":"query","schema":{"type":"string","default":""}}
     ],"responses":{"200":{"description":"ok"}}}}},
     "components":{"schemas":{"Settings":{"type":"object","required":["MobileVisible","Missing"],"properties":{
       "MobileVisible":{"type":"boolean","default":true,"description":"Первая строка\nВторая строка\nТретья строка"},"disabled":{"type":"boolean","default":false},
       "count":{"type":"integer","minimum":0,"default":0},"text":{"type":"string","default":""},
       "missing":{"type":"string"},"nullable":{"type":"string","nullable":true,"default":null},
       "items":{"type":"array","items":{"type":"integer"},"default":[0,1]},
       "config":{"type":"object","default":{"foo-bar":"x","a b":false}}
     }}}}}
    """;

    /// <summary>Checks enabled and disabled output, required presence, constraints and runtime values.</summary>
    /// <param name="enabled">Whether defaults are requested.</param>
    /// <returns>The asynchronous pipeline test.</returns>
    [TestCase(false)]
    [TestCase(true)]
    public async Task GeneratesOnlyExplicitDefaults(bool enabled)
    {
        using var harness = new PipelineTestHarness();
        var document = await PipelineTestHarness.LoadDocumentFromJsonAsync(Contract);
        var context = await harness.RunAsync(document, new TypeScriptGenerationOptions { GenerateDefaultValues = enabled });
        var model = context.GeneratedFiles.Single(f => f.RelativePath.EndsWith("/settings.ts")).Content;
        var index = context.GeneratedFiles.Single(f => f.RelativePath == "index.ts").Content;
        index.Should().Contain("./models/settings/settings").And.Contain("./api/getItems/getItems.params");
        var parameters = context.GeneratedFiles.Single(f => f.RelativePath.EndsWith(".params.ts")).Content;
        model.Should().Contain("MobileVisible: boolean;").And.Contain("@default true");
        model.Should().MatchRegex(@"\* @description Первая строка\r?\n\s*\* Вторая строка\r?\n\s*\* Третья строка");
        model.Should().Contain("SettingsConstraints");
        if (!enabled)
        {
            model.Should().NotContain("SettingsDefaultValues");
            parameters.Should().NotContain("ParamsDefaultValues");
            return;
        }
        model.Should().Contain("SettingsDefaultValues").And.Contain("MobileVisible: true").And.Contain("disabled: false")
            .And.Contain("count: 0").And.Contain("text: \"\"").And.Contain("items: [0, 1]");
        parameters.Should().Contain("GetItemsRouteParamsDefaultValues").And.Contain("GetItemsQueryParamsDefaultValues");
        var dir = Path.Combine(Path.GetTempPath(), "valio-defaults-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var file in context.GeneratedFiles.Where(f => f.RelativePath.EndsWith(".ts")))
            {
                var path = Path.Combine(dir, file.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, file.Content);
            }
            // Node's ESM loader requires extensions; TypeScript resolves these generated barrel specifiers itself.
            await File.WriteAllTextAsync(Path.Combine(dir, "loader.mjs"), """
                export async function resolve(specifier, context, nextResolve) {
                    if (specifier.startsWith('.') && !specifier.endsWith('.ts') && !specifier.endsWith('.mjs')) specifier += '.ts';
                    return nextResolve(specifier, context);
                }
                """);
            await File.WriteAllTextAsync(Path.Combine(dir, "check.mjs"), """
                import { SettingsDefaultValues as m, GetItemsRouteParamsDefaultValues as r, GetItemsQueryParamsDefaultValues as q } from './index.ts';
                if (m.MobileVisible !== true || m.disabled !== false || m.count !== 0 || m.text !== '' || JSON.stringify(m.items) !== '[0,1]' || 'missing' in m || 'nullable' in m || m.config['foo-bar'] !== 'x' || m.config['a b'] !== false || r.id !== 0 || q.active !== false || q.text !== '') { console.error(JSON.stringify({m,r,q})); process.exit(1); }
                """);
            await File.WriteAllTextAsync(Path.Combine(dir, "register.mjs"), "import { register } from 'node:module'; register('./loader.mjs', import.meta.url);");
            using var process = Process.Start(new ProcessStartInfo("node", "--import ./register.mjs ./check.mjs") { WorkingDirectory = dir, RedirectStandardError = true, UseShellExecute = false })!;
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            process.ExitCode.Should().Be(0, error);
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>Verifies the configuration parser defaults to false and accepts a boolean.</summary>
    [Test]
    public void ParsesOption()
    {
        var descriptor = new TypeScriptOptionsDescriptor();
        var defaults = (TypeScriptGenerationOptions)descriptor.ParseOptions(new(), out var errors)!;
        errors.Should().BeEmpty();
        defaults.GenerateDefaultValues.Should().BeFalse();
        var enabled = (TypeScriptGenerationOptions)descriptor.ParseOptions(new() { ["generateDefaultValues"] = JsonSerializer.SerializeToElement(true) }, out errors)!;
        errors.Should().BeEmpty();
        enabled.GenerateDefaultValues.Should().BeTrue();
    }
}
