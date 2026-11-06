using System.Text.Json;
using ValIO.Core.Plugins;

namespace ValIO.Generators.TypeScript.Tests.TestHelpers;

/// <summary>Runs generated TypeScript modules using external Node dependencies without modifying repository packages.</summary>
internal static class GeneratedTypeScriptRuntime
{
    /// <summary>Transpiles generated modules and executes assertions against their runtime behavior.</summary>
    /// <param name="context">The completed generation pipeline result.</param>
    /// <param name="runtimeScript">JavaScript assertions with root, path, assert, and require available.</param>
    /// <returns>The process output after successful execution.</returns>
    public static async Task<string> Run(GenerationContext context, string runtimeScript)
    {
        using var workspace = new GeneratedTypeScriptTestWorkspace();
        var files = context.GeneratedFiles.Where(file => file.RelativePath.EndsWith(".ts", StringComparison.Ordinal)).ToArray();
        var sourcePaths = await workspace.WriteSources(files);
        var script = Bootstrap(workspace.Root, workspace.Modules, sourcePaths) + Environment.NewLine + runtimeScript;
        var output = await workspace.RunNode("runtime.cjs", script, "generated module runtime contracts");
        workspace.MarkSuccessful();
        return output;
    }

    /// <summary>Creates shared transpilation infrastructure for generated modules.</summary>
    /// <param name="workspace">The generated module directory.</param>
    /// <param name="modules">The external node_modules directory.</param>
    /// <param name="sourcePaths">The validated generated TypeScript files to transpile.</param>
    /// <returns>The JavaScript bootstrap, defining root, path, and assert for callers.</returns>
    private static string Bootstrap(string workspace, string modules, IReadOnlyList<string> sourcePaths) => $$"""
        const fs = require('node:fs');
        const path = require('node:path');
        const assert = require('node:assert/strict');
        const ts = require(path.join({{JsonSerializer.Serialize(modules)}}, 'typescript', 'lib', 'typescript.js'));
        const root = {{JsonSerializer.Serialize(workspace)}};
        const files = {{JsonSerializer.Serialize(sourcePaths)}};
        for (const file of files) {
          const result = ts.transpileModule(fs.readFileSync(file, 'utf8'), {
            compilerOptions: {
              module: ts.ModuleKind.CommonJS,
              target: ts.ScriptTarget.ES2022,
              experimentalDecorators: true
            },
            reportDiagnostics: true,
            fileName: file
          });
          if (result.diagnostics?.length) {
            throw new Error(ts.formatDiagnosticsWithColorAndContext(result.diagnostics, {
              getCanonicalFileName: file => file,
              getCurrentDirectory: () => root,
              getNewLine: () => '\n'
            }));
          }
          fs.writeFileSync(file.slice(0, -3) + '.js', result.outputText);
        }
        """;
}
