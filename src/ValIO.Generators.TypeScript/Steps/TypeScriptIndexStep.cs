using System.Text;
using System.Text.RegularExpressions;
using ValIO.Core.Plugins;
using ValIO.Core.Utilities;
using ValIO.Generators.TypeScript.Ast.JsDoc;

namespace ValIO.Generators.TypeScript.Steps;

/// <summary>
/// Generation step that emits the root <c>index.ts</c> barrel re-exporting every generated
/// TypeScript module (models, schemas, clients, params/results/query hooks).
/// </summary>
/// <remarks>
/// A naive <c>export * from "..."</c> for every file raises <c>TS2308</c> whenever two modules export
/// the same name — e.g. a DTO named <c>&lt;Op&gt;QueryResult</c> collides with a TanStack hook's
/// convenience alias <c>export type &lt;Op&gt;QueryResult = …</c>. To stay collision-free this step
/// tracks every exported identifier: the first module to claim a name keeps a clean <c>export *</c>,
/// while any later module that would re-export an already-claimed name switches to an explicit
/// re-export of only its remaining names, split into value (<c>export { … }</c>) and type-only
/// (<c>export type { … }</c>) forms so the output also satisfies <c>verbatimModuleSyntax</c>.
/// Model files are processed first so a real model/DTO always wins over a generated convenience alias.
/// </remarks>
public sealed class TypeScriptIndexStep : ICodeGenerationStep
{
    /// <summary>The unique step identifier within the TypeScript generation pipeline.</summary>
    public string Name => "typescript-index";

    /// <summary>The execution order; runs late so all module files already exist in the context.</summary>
    public int Order => 70;

    /// <summary>
    /// Determines whether the step should run for the given context.
    /// </summary>
    /// <param name="context">The current generation context.</param>
    /// <returns><see langword="true"/> when at least one generated <c>.ts</c> file exists to re-export.</returns>
    public bool ShouldExecute(GenerationContext context)
        => context.GeneratedFiles.Any(f => f.RelativePath.EndsWith(".ts"));

    /// <summary>
    /// Builds the root <c>index.ts</c> barrel and appends it to <see cref="GenerationContext.GeneratedFiles"/>.
    /// </summary>
    /// <param name="context">The current generation context; its generated files are scanned and added to.</param>
    /// <param name="ct">A cancellation token (unused; the work is synchronous and fast).</param>
    /// <returns>A completed task.</returns>
    /// <seealso cref="BarrelModule"/>
    public Task ExecuteAsync(GenerationContext context, CancellationToken ct = default)
    {
        // Collect the generated .ts modules (excluding any index.ts), each with its barrel import path,
        // parsed top-level exports, and a precedence (models before everything else so real DTOs win).
        var modules = context.GeneratedFiles
            .Where(f => f.RelativePath.EndsWith(".ts", StringComparison.Ordinal))
            .Select(f => (Path: f.RelativePath.Replace('\\', '/'), f.Content))
            .Where(t => t.Path != "index.ts" && !t.Path.EndsWith("/index.ts", StringComparison.Ordinal))
            .Select(t => new BarrelModule(
                ImportPath: ToImportPath(t.Path),
                SortKey: t.Path,
                Precedence: t.Path.StartsWith("models/", StringComparison.Ordinal) ? 0 : 1,
                Exports: ExtractExports(t.Content)))
            .ToList();

        // Decide each module's re-export form in precedence order (so earlier = higher-priority modules
        // claim shared names first), but emit the lines back in the original ordinal path order to keep
        // the barrel layout stable.
        var claimed = new HashSet<string>(StringComparer.Ordinal);
        var rendered = new List<(string SortKey, string Lines)>(modules.Count);

        foreach (var module in modules.OrderBy(m => m.Precedence).ThenBy(m => m.SortKey, StringComparer.Ordinal))
            rendered.Add((module.SortKey, RenderModuleReExport(module, claimed)));

        var sb = new StringBuilder();
        foreach (var (_, lines) in rendered.OrderBy(r => r.SortKey, StringComparer.Ordinal))
            sb.Append(lines);

        if (sb.Length > 0)
        {
            var header = new StringBuilder();
            CommentGenerator.GenerateJSDocComment(header, 0,
                TypeScriptDoc.FileHeader("index.ts", "index", "Barrel file re-exporting all generated TypeScript modules.").Text);
            header.AppendLine();
            context.GeneratedFiles.Add(new GeneratedFile("index.ts", header.ToString() + sb.ToString()));
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Produces the re-export statement(s) for a single module and records the names it claims.
    /// </summary>
    /// <param name="module">The module to re-export.</param>
    /// <param name="claimed">
    /// The set of identifiers already exported by earlier (higher-precedence) modules. Updated in place
    /// with the names this module ends up owning.
    /// </param>
    /// <returns>
    /// One <c>export * from "…";</c> line when the module has no name clash, otherwise explicit
    /// <c>export { … }</c> / <c>export type { … }</c> lines covering only its still-unclaimed names
    /// (possibly empty when every name was already taken).
    /// </returns>
    private static string RenderModuleReExport(BarrelModule module, HashSet<string> claimed)
    {
        var hasCollision = module.Exports.Any(e => claimed.Contains(e.Name));

        if (!hasCollision)
        {
            // No clash → keep a clean star re-export and claim every exported name.
            foreach (var export in module.Exports)
                claimed.Add(export.Name);
            return $"export * from \"{module.ImportPath}\";{Environment.NewLine}";
        }

        // Re-export only the names not already owned by an earlier module, split value vs type-only
        // so the barrel compiles under verbatimModuleSyntax (a type re-exported via `export { }` is TS1205).
        var fresh = module.Exports.Where(e => !claimed.Contains(e.Name)).ToList();
        var values = fresh.Where(e => !e.IsType).Select(e => e.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
        var types = fresh.Where(e => e.IsType).Select(e => e.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();

        var sb = new StringBuilder();
        if (values.Count > 0)
            sb.AppendLine($"export {{ {string.Join(", ", values)} }} from \"{module.ImportPath}\";");
        if (types.Count > 0)
            sb.AppendLine($"export type {{ {string.Join(", ", types)} }} from \"{module.ImportPath}\";");

        foreach (var export in fresh)
            claimed.Add(export.Name);

        return sb.ToString();
    }

    /// <summary>
    /// Converts a generated file's relative path into the module specifier used by the barrel
    /// (root-relative, without the <c>.ts</c> extension).
    /// </summary>
    /// <param name="path">The forward-slashed relative path of a generated <c>.ts</c> file.</param>
    /// <returns>The <c>./…</c> import specifier with the extension stripped.</returns>
    private static string ToImportPath(string path)
    {
        var importPath = "./" + path;
        return importPath.EndsWith(".ts", StringComparison.Ordinal)
            ? importPath[..^3]
            : importPath;
    }

    /// <summary>
    /// Extracts the top-level exported declarations of a generated module from its source text.
    /// </summary>
    /// <remarks>
    /// Generated modules only ever use a small, regular set of export forms
    /// (<c>export interface/type/const/function/class/enum NAME …</c>), so a line-anchored regex over
    /// the rendered content is sufficient and avoids threading export metadata through the pipeline.
    /// </remarks>
    /// <param name="content">The module's full rendered source.</param>
    /// <returns>The distinct exported symbols, each flagged as a type or a value.</returns>
    private static IReadOnlyList<ExportSymbol> ExtractExports(string content)
    {
        var symbols = new List<ExportSymbol>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match match in ExportDeclarationRegex.Matches(content))
        {
            var name = match.Groups["name"].Value;
            if (!seen.Add(name))
                continue;

            var kind = match.Groups["kind"].Value;
            var isType = kind is "type" or "interface";
            symbols.Add(new ExportSymbol(name, isType));
        }

        return symbols;
    }

    /// <summary>
    /// Matches a single top-level <c>export</c> declaration and captures its kind and identifier.
    /// Handles the modifiers (<c>declare</c>, <c>abstract</c>, <c>async</c>) the generators can emit.
    /// </summary>
    private static readonly Regex ExportDeclarationRegex = new(
        @"^export\s+(?:declare\s+)?(?:abstract\s+|async\s+)?(?<kind>type|interface|class|enum|const|let|var|function)\s+(?<name>[A-Za-z_$][A-Za-z0-9_$]*)",
        RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>
    /// A generated module participating in the barrel: its import specifier, ordinal sort key,
    /// precedence (lower wins shared names first), and parsed exported symbols.
    /// </summary>
    /// <param name="ImportPath">The <c>./…</c> module specifier used in the re-export statement.</param>
    /// <param name="SortKey">The original relative path, used for deterministic ordinal output ordering.</param>
    /// <param name="Precedence">Claim priority: <c>0</c> for <c>models/</c> files, <c>1</c> otherwise.</param>
    /// <param name="Exports">The module's top-level exported symbols.</param>
    private sealed record BarrelModule(
        string ImportPath,
        string SortKey,
        int Precedence,
        IReadOnlyList<ExportSymbol> Exports);

    /// <summary>A top-level export of a generated module.</summary>
    /// <param name="Name">The exported identifier.</param>
    /// <param name="IsType">
    /// <see langword="true"/> for type-only declarations (<c>interface</c>/<c>type</c>), which must be
    /// re-exported via <c>export type { }</c>; <see langword="false"/> for runtime values.
    /// </param>
    private sealed record ExportSymbol(string Name, bool IsType);
}
