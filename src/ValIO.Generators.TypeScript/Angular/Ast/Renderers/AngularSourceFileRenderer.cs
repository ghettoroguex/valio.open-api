using System.Text;
using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Utilities;
using ValIO.Generators.TypeScript.Angular.Ast.Nodes;
using ValIO.Generators.TypeScript.Ast.Nodes;
using ValIO.Generators.TypeScript.Ast.Renderers;

namespace ValIO.Generators.TypeScript.Angular.Ast.Renderers;

/// <summary>
/// Renders an Angular service <see cref="SourceFileNode"/>: imports plus a
/// single <see cref="AngularServiceClassNode"/> declaration.
/// <para/>
/// The base <see cref="TypeScriptAstRenderer"/> dispatches declarations through
/// the visitor; <see cref="AngularServiceClassNode"/> intentionally throws from
/// <c>Accept</c> because it is a renderer-specific composite. This helper does
/// the dispatch manually and delegates to <see cref="AngularServiceRenderer"/>.
/// </summary>
public sealed class AngularSourceFileRenderer
{
    /// <summary>The renderer for ordinary TypeScript declarations.</summary>
    private readonly TypeScriptAstRenderer _innerRenderer;
    /// <summary>The service renderer using the same validation-node support.</summary>
    private readonly AngularServiceRenderer _classRenderer;

    /// <summary>Creates an Angular source renderer with optional validation AST support.</summary>
    /// <param name="nodeRenderers">The configured expression renderer chain.</param>
    public AngularSourceFileRenderer(IEnumerable<ITypeScriptNodeRenderer>? nodeRenderers = null)
    {
        var renderers = nodeRenderers?.ToArray() ?? [];
        _innerRenderer = new TypeScriptAstRenderer(renderers);
        _classRenderer = new AngularServiceRenderer(renderers);
    }

    public string Render(SourceFileNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        var sb = new StringBuilder();

        // Imports (delegate via the inner renderer's existing logic by
        // reusing its formatting — rendered manually here to control spacing).
        foreach (var import in node.Imports)
            RenderImport(sb, import);
        if (node.Imports.Count > 0)
            CodeFormatter.AppendEmptyLine(sb);

        for (var i = 0; i < node.Declarations.Count; i++)
        {
            var declaration = node.Declarations[i];
            if (declaration is AngularServiceClassNode classNode)
            {
                sb.Append(_classRenderer.Render(classNode));
            }
            else
            {
                // Fallback: regular declaration via the standard renderer.
                sb.Append(declaration.Accept(_innerRenderer));
            }

            if (i < node.Declarations.Count - 1)
                CodeFormatter.AppendEmptyLine(sb);
        }

        return sb.ToString();
    }

    private static void RenderImport(StringBuilder sb, ImportNode import)
    {
        var from = !string.IsNullOrEmpty(import.From) ? $" from \"{import.From}\"" : "";
        if (import.ImportKind == ImportKind.Default && import.Items.Count > 0)
        {
            CodeFormatter.AppendLine(sb, 0, $"import {import.Items[0].Name}{from};");
        }
        else if (import.ImportKind == ImportKind.Namespace)
        {
            var name = import.Items.Count > 0 ? import.Items[0].Name : "namespace";
            CodeFormatter.AppendLine(sb, 0, $"import * as {name}{from};");
        }
        else
        {
            var namedImports = string.Join(", ", import.Items.Select(i => i.Name));
            var typeKeyword = import.IsTypeOnly ? "import type " : "import ";
            CodeFormatter.AppendLine(sb, 0, $"{typeKeyword}{{ {namedImports} }}{from};");
        }
    }
}
