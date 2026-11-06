using System.Diagnostics;
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

namespace ValIO.Generators.TypeScript.Tests.Steps;

/// <summary>Exercises optional manual IR parameter serialization through generated clients.</summary>
[TestFixture]
public sealed class OptionalParameterSerializationTests
{
    /// <summary>Checks that optional values are omitted only when undefined, including in required groups.</summary>
    /// <returns>A task that completes when both generated clients have been executed.</returns>
    [Test]
    public async Task OptionalTypesPreservePresentValuesInQueryAndHeaders()
    {
        var modules = Environment.GetEnvironmentVariable("VALIO_TEST_NODE_MODULES");
        if (string.IsNullOrWhiteSpace(modules) || !Directory.Exists(modules))
            Assert.Fail("Set VALIO_TEST_NODE_MODULES to the pinned node_modules directory.");

        var operation = new IrOperation
        {
            MethodName = "Example", HttpMethod = "GET", Path = "/example", PathParameters = [],
            QueryParameters = Parameters(), HeaderParameters = Parameters(),
            Responses = [new IrResponse { StatusCode = "204" }],
        };
        var builder = new TypeScriptAstBuilder(IrDocumentContexts.Create(), new NamingConverter(),
            new TypeScriptGenerationOptions { ValidationLibrary = "none" });
        var renderer = new TypeScriptAstRenderer([new AxiosNodeRenderer()]);
        var directory = Path.Combine(Path.GetTempPath(), $"valio-optional-wire-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "axios.ts"), renderer.Render(builder.BuildSourceFileForOperation(operation, transport: "axios")));
            await File.WriteAllTextAsync(Path.Combine(directory, "fetch.ts"), renderer.Render(builder.BuildSourceFileForOperation(operation, transport: "fetch")));
            const string script = """
            const ts=require('typescript'),fs=require('fs'),vm=require('vm'),assert=require('assert');
            let config,url,init;
            const client={request:async value=>{config=value;return {status:204}}};
            const load=file=>{const js=ts.transpileModule(fs.readFileSync(file,'utf8'),{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2020}}).outputText;
              const exports={};vm.runInNewContext(js,{exports,require:()=>({default:client}),URLSearchParams,fetch:async (value,options)=>{url=value;init=options;return {status:204}}});return Object.values(exports).find(v=>typeof v==='function');};
            (async()=>{
              const values={anchor:'ok',flag:false,count:0,nullable:null};
              const params={query:values,headers:values};
              await load('axios.ts')(params,undefined,undefined,client);
              for(const object of [config.params,config.headers]){
                assert.equal(Object.hasOwn(object,'Missing.Value'),false);
                assert.equal(object.Flag,false);assert.equal(object.Count,0);assert.equal(object.Nullable,null);
              }
              await load('fetch.ts')(params);
              const query=new URL(url,'http://example.test').searchParams;
              assert.equal(query.has('Missing.Value'),false);assert.equal(query.get('Flag'),'false');assert.equal(query.get('Count'),'0');assert.equal(query.get('Nullable'),'null');
              assert.equal(Object.hasOwn(init.headers,'Missing.Value'),false);
              assert.equal(init.headers.Flag,false);assert.equal(init.headers.Count,0);assert.equal(init.headers.Nullable,null);
            })().catch(error=>{console.error(error);process.exit(1)});
            """;
            var start = new ProcessStartInfo("node") { WorkingDirectory = directory, RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
            start.ArgumentList.Add("--eval");
            start.ArgumentList.Add(script);
            start.Environment["NODE_PATH"] = modules;
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.That(process.ExitCode, Is.Zero, $"{await stdout}\n{await stderr}");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Creates required and optional parameter types in the same group.</summary>
    /// <returns>The query or header parameter declarations.</returns>
    private static IReadOnlyList<IrParameter> Parameters() =>
    [
        Parameter("anchor", "Anchor", new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.String }),
        Parameter("missing", "Missing.Value", Optional(IrPrimitiveKind.String)),
        Parameter("flag", "Flag", Optional(IrPrimitiveKind.Boolean)),
        Parameter("count", "Count", Optional(IrPrimitiveKind.Integer)),
        Parameter("nullable", "Nullable", new IrNullableType { InnerType = Optional(IrPrimitiveKind.String) }),
    ];

    /// <summary>Creates a required IR parameter with an explicit wire name.</summary>
    /// <param name="name">The generated member name.</param>
    /// <param name="wireName">The original HTTP name.</param>
    /// <param name="type">The parameter value type.</param>
    /// <returns>The parameter declaration.</returns>
    private static IrParameter Parameter(string name, string wireName, IrType type) =>
        new() { Name = name, OriginalName = wireName, Type = type, IsRequired = true };

    /// <summary>Creates an optional primitive type.</summary>
    /// <param name="kind">The primitive kind.</param>
    /// <returns>The optional type wrapper.</returns>
    private static IrOptionalType Optional(IrPrimitiveKind kind) =>
        new() { InnerType = new IrPrimitiveType { PrimitiveKind = kind } };
}
