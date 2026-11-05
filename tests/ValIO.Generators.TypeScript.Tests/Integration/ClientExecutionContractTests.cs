using System.ComponentModel;
using System.Diagnostics;
using FluentAssertions;
using NUnit.Framework;
using ValIO.Core.IR;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Core.Services;
using ValIO.Generators.TypeScript.Ast.Builders;
using ValIO.Generators.TypeScript.Ast.Renderers;
using ValIO.Generators.TypeScript.Axios.Ast.Renderers;
using ValIO.Generators.TypeScript.Options;
using ValIO.Generators.TypeScript.Tests.TestHelpers;

namespace ValIO.Generators.TypeScript.Tests.Integration;

/// <summary>Exercises generated client behavior and generated TypeScript types.</summary>
[TestFixture]
public sealed class ClientExecutionContractTests
{
    /// <summary>Compiles nullable array elements and inherited reference intersections in strict mode.</summary>
    /// <returns>A task that completes after strict TypeScript compilation succeeds.</returns>
    [Test]
    public async Task NestedNullableArraysAndInheritedReferencesCompile()
    {
        var modules = GetModules();
        var temporary = CreateDirectory(modules);
        try
        {
            const string json = """
            {"openapi":"3.0.3","info":{"title":"types","version":"1"},"paths":{},"components":{"schemas":{
              "ValueModel":{"type":"object","properties":{"name":{"type":"string"}}},
              "Base":{"type":"object","properties":{"value":{"$ref":"#/components/schemas/ValueModel"}}},
              "Middle":{"allOf":[{"$ref":"#/components/schemas/Base"}]},
              "Child":{"allOf":[{"$ref":"#/components/schemas/Middle"},{"type":"object","properties":{"value":{"type":"string"}},"required":["value"]}]},
              "Nested":{"type":"object","required":["items","scalars"],"properties":{
                "items":{"type":"array","items":{"type":"array","nullable":true,"items":{"type":"string"}}},
                "scalars":{"type":"array","items":{"type":"string","nullable":true}}
              }}
            }}}
            """;
            using var harness = new PipelineTestHarness();
            var document = await PipelineTestHarness.LoadDocumentFromJsonAsync(json);
            var context = await harness.RunAsync(document, new TypeScriptGenerationOptions { Transports = ["fetch"] });
            foreach (var file in context.GeneratedFiles.Where(f => f.RelativePath.EndsWith(".ts", StringComparison.Ordinal)))
            {
                var path = Path.Combine(temporary, file.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, file.Content);
            }
            var nested = Path.Combine(temporary, "models", "nested", "nested.ts");
            var child = Path.Combine(temporary, "models", "child", "child.ts");
            var check = Path.Combine(temporary, "check.ts");
            await File.WriteAllTextAsync(check, "import type { Nested } from './models/nested/nested';\n" +
                "const ok: Nested = { items: [['x'], null], scalars: ['x', null] };\n" +
                "// @ts-expect-error nullable item is not a nullable outer array\nconst bad: Nested = { items: null, scalars: [] };\nvoid ok; void bad;\n");
            await RunNode(temporary, modules, [Path.Combine(modules, "typescript", "bin", "tsc"), "--strict", "--skipLibCheck", "--noEmit", "--target", "ES2020", "--module", "Node16", "--moduleResolution", "Node16", check, nested, child]);
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    /// <summary>Runs generated Axios and fetch functions with controlled transports and real response values.</summary>
    /// <returns>A task that completes after the generated transport assertions pass.</returns>
    [Test]
    public async Task ClientsSelectOverridesAndHandleBodylessSuccesses()
    {
        var modules = GetModules();
        var temporary = CreateDirectory(modules);
        try
        {
            var options = new TypeScriptGenerationOptions { Transports = ["axios"], Axios = new TypeScriptAxiosOptions
                { CustomInstancePath = "./defaults", CustomInstanceName = "apiMutator" } };
            var renderer = new TypeScriptAstRenderer([new AxiosNodeRenderer()]);
            var builder = new TypeScriptAstBuilder(new NamingConverter(), options);
            var mixed = Operation([new IrResponse { StatusCode = "200", Type = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.String } }, new IrResponse { StatusCode = "204", Type = null }]);
            var empty = Operation([new IrResponse { StatusCode = "204", Type = null }]);
            await File.WriteAllTextAsync(Path.Combine(temporary, "axios.ts"), renderer.Render(builder.BuildSourceFileForOperation(mixed, transport: "axios")));
            await File.WriteAllTextAsync(Path.Combine(temporary, "void.ts"), renderer.Render(builder.BuildSourceFileForOperation(empty, transport: "axios")));
            await File.WriteAllTextAsync(Path.Combine(temporary, "fetch.ts"), renderer.Render(builder.BuildSourceFileForOperation(mixed, transport: "fetch")));
            await File.WriteAllTextAsync(Path.Combine(temporary, "fetch-void.ts"), renderer.Render(builder.BuildSourceFileForOperation(empty, transport: "fetch")));
            const string script = """
            const ts=require('typescript'),fs=require('fs'),vm=require('vm'),assert=require('assert');
            let intercepted=0, defaultsCalls=0, overrideCalls=0, response={status:200,data:'hello'};
            const handlers=[];const defaults={interceptors:{response:{use:f=>handlers.push(f)}},request:async()=>{defaultsCalls++;return handlers.reduce((value,handler)=>handler(value),response);}};
            defaults.interceptors.response.use(value=>{intercepted++;return {...value,data:value.data+'-intercepted'};});
            const override={request:async()=>{overrideCalls++; return response;}};
            let jsonCalls=0;
            const load=(file)=>{const src=fs.readFileSync(file,'utf8');const js=ts.transpileModule(src,{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2020}}).outputText;
              const exports={};const ctx={exports,require:(name)=>name==='./defaults'?{apiMutator:defaults}:{},fetch:async()=>({status:response.status,json:async()=>{jsonCalls++;return response.data;}}),URLSearchParams};
              vm.runInNewContext(js,ctx); return Object.values(exports).find(v=>typeof v==='function');};
            (async()=>{const f=load('axios.ts'); let x=await f(undefined,undefined);assert.equal(x.status,200);assert.equal(x.body,'hello-intercepted');assert.equal(defaultsCalls,1);assert.equal(intercepted,1);
              response={status:204,data:undefined};x=await f(undefined,undefined,override);assert.equal(x.status,204);assert.equal(x.body,undefined);assert.equal(overrideCalls,1);assert.equal(defaultsCalls,1);
              assert.equal(await load('void.ts')(undefined,undefined,override),undefined);
              x=await load('fetch.ts')();assert.equal(x.status,204);assert.equal(x.body,undefined);assert.equal(jsonCalls,0);
              assert.equal(await load('fetch-void.ts')(),undefined);assert.equal(jsonCalls,0);
              response={status:200,data:'world'};x=await load('fetch.ts')();assert.equal(x.body,'world');assert.equal(jsonCalls,1);
            })().catch(e=>{console.error(e);process.exit(1)});
            """;
            await RunNode(temporary, modules, ["--eval", script]);
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    /// <summary>Creates an operation with specified responses.</summary>
    /// <param name="responses">The documented responses.</param>
    /// <returns>The operation.</returns>
    private static IrOperation Operation(IReadOnlyList<IrResponse> responses) => new()
    { MethodName = "Example", Path = "/example", HttpMethod = "GET", PathParameters = [], QueryParameters = [], HeaderParameters = [], RequestBody = null, Responses = responses };

    /// <summary>Locates pinned runtime dependencies.</summary>
    /// <returns>The node_modules directory.</returns>
    private static string GetModules()
    {
        var modules = Environment.GetEnvironmentVariable("VALIO_TEST_NODE_MODULES");
        if (string.IsNullOrWhiteSpace(modules) || !Directory.Exists(modules)) Assert.Fail("Set VALIO_TEST_NODE_MODULES to the pinned node_modules directory.");
        return Path.GetFullPath(modules!);
    }

    /// <summary>Creates an isolated directory beside node_modules for TypeScript resolution.</summary>
    /// <param name="modules">The dependency directory.</param>
    /// <returns>The created directory.</returns>
    private static string CreateDirectory(string modules)
    {
        var path = Path.Combine(Directory.GetParent(modules)!.FullName, "valio-client-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Runs Node with the pinned dependencies and checks its exit status.</summary>
    /// <param name="directory">The working directory.</param>
    /// <param name="modules">The node_modules directory.</param>
    /// <param name="arguments">The command arguments.</param>
    /// <returns>A task that completes when Node succeeds.</returns>
    private static async Task RunNode(string directory, string modules, IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo("node") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.Environment["NODE_PATH"] = modules;
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        Process? process;
        try { process = Process.Start(start); }
        catch (Win32Exception) { Assert.Fail("Node.js is required for generated client tests."); return; }
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
