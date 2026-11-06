using System.Text;
using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Core.Utilities;
using ValIO.Generators.TypeScript.Angular.Ast.Nodes;
using ValIO.Generators.TypeScript.Ast.Renderers;

namespace ValIO.Generators.TypeScript.Angular.Ast.Renderers;

/// <summary>
/// Renders an <see cref="AngularServiceClassNode"/> as TypeScript source.
/// Uses an inner <see cref="TypeScriptAstRenderer"/> to render the nested
/// type/expression/statement nodes so we do not duplicate that machinery.
/// </summary>
public sealed class AngularServiceRenderer
{
    private const int IndentSpaces = 4;

    /// <summary>The configured validation expression renderer chain.</summary>
    private readonly ITypeScriptNodeRenderer[] _nodeRenderers;

    /// <summary>Creates an Angular class renderer with optional validation AST support.</summary>
    /// <param name="nodeRenderers">The configured validation expression renderers.</param>
    public AngularServiceRenderer(IEnumerable<ITypeScriptNodeRenderer>? nodeRenderers = null) =>
        _nodeRenderers = nodeRenderers?.ToArray() ?? [];

    /// <summary>
    /// Renders the class declaration as a TypeScript snippet (no leading imports).
    /// </summary>
    /// <param name="node">The Angular service declaration.</param>
    /// <returns>The rendered injectable service.</returns>
    /// <exception cref="ArgumentNullException">The declaration is null.</exception>
    public string Render(AngularServiceClassNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        var sb = new StringBuilder();
        var inner = new TypeScriptAstRenderer(_nodeRenderers);

        // JSDoc
        if (node.Documentation != null && !string.IsNullOrWhiteSpace(node.Documentation.Text))
        {
            CodeFormatter.AppendLine(sb, 0, "/**");
            foreach (var line in node.Documentation.Text.Split('\n'))
                CodeFormatter.AppendLine(sb, 0, $" * {line.TrimEnd()}");
            CodeFormatter.AppendLine(sb, 0, " */");
        }

        // Attributes (decorators). Each attribute renders as `@Name(args)` on its own line.
        foreach (var attribute in node.Attributes)
            CodeFormatter.AppendLine(sb, 0, RenderDecorator(inner, attribute));

        var exportKw = node.IsExported ? "export " : "";
        CodeFormatter.AppendLine(sb, 0, $"{exportKw}class {node.Name.Name} {{");

        // Constructor — Angular DI ctor with parameter properties.
        RenderConstructor(sb, inner, node.ConstructorParameters);

        // Methods.
        for (var i = 0; i < node.Methods.Count; i++)
        {
            sb.AppendLine();
            RenderMethod(sb, inner, node.Methods[i]);
        }

        CodeFormatter.AppendLine(sb, 0, "}");
        return sb.ToString();
    }

    private static string RenderDecorator(TypeScriptAstRenderer inner, AttributeNode attribute)
    {
        if (attribute.Arguments.Count == 0)
            return $"@{attribute.Name.Name}()";

        var args = string.Join(", ", attribute.Arguments.Select(a => a.Accept(inner)));
        return $"@{attribute.Name.Name}({args})";
    }

    private static void RenderConstructor(
        StringBuilder sb,
        TypeScriptAstRenderer inner,
        IReadOnlyList<ParameterDeclarationNode> parameters)
    {
        if (parameters.Count == 0)
        {
            CodeFormatter.AppendLine(sb, 1, "constructor() {}");
            return;
        }

        var paramStrings = parameters
            .Select(p => RenderConstructorParameter(inner, p))
            .ToList();

        // Single-line ctor for ≤1 param, multi-line otherwise.
        if (paramStrings.Count == 1)
        {
            CodeFormatter.AppendLine(sb, 1, $"constructor({paramStrings[0]}) {{}}");
            return;
        }

        CodeFormatter.AppendLine(sb, 1, "constructor(");
        for (var i = 0; i < paramStrings.Count; i++)
        {
            var suffix = i < paramStrings.Count - 1 ? "," : "";
            CodeFormatter.AppendLine(sb, 2, paramStrings[i] + suffix);
        }
        CodeFormatter.AppendLine(sb, 1, ") {}");
    }

    private static string RenderConstructorParameter(
        TypeScriptAstRenderer inner,
        ParameterDeclarationNode parameter)
    {
        // Angular DI parameter property: `private readonly http: HttpClient`.
        // Modifiers carried via metadata so we don't pollute the generic node.
        var modifier = "private readonly";
        if (parameter.Metadata.TryGetValue("accessModifier", out var modObj) && modObj is string mod)
            modifier = mod;

        var typeStr = parameter.Type != null ? $": {parameter.Type.Accept(inner)}" : "";
        return $"{modifier} {parameter.Name.Name}{typeStr}";
    }

    private static void RenderMethod(
        StringBuilder sb,
        TypeScriptAstRenderer inner,
        MethodDeclarationNode method)
    {
        // JSDoc comment (if any).
        if (method.Documentation != null && !string.IsNullOrWhiteSpace(method.Documentation.Text))
        {
            CodeFormatter.AppendLine(sb, 1, "/**");
            foreach (var line in method.Documentation.Text.Split('\n'))
                CodeFormatter.AppendLine(sb, 1, $" * {line.TrimEnd()}");
            CodeFormatter.AppendLine(sb, 1, " */");
        }

        var paramStrings = method.Parameters
            .Select(p => RenderMethodParameter(inner, p))
            .ToList();
        var paramsStr = string.Join(", ", paramStrings);

        var returnTypeStr = method.ReturnType != null
            ? $": {method.ReturnType.Accept(inner)}"
            : "";

        var accessKeyword = method.AccessModifier switch
        {
            AccessModifier.Private => "private ",
            AccessModifier.Protected => "protected ",
            // Angular methods are public by default — Angular convention is to omit the modifier.
            _ => "",
        };

        CodeFormatter.AppendLine(sb, 1, $"{accessKeyword}{method.Name.Name}({paramsStr}){returnTypeStr} {{");

        if (method.Body != null)
        {
            foreach (var stmt in method.Body.Statements)
            {
                var rendered = stmt.Accept(inner);
                if (string.IsNullOrEmpty(rendered))
                {
                    CodeFormatter.AppendEmptyLine(sb);
                    continue;
                }
                // The inner renderer may yield multi-line statements (if/foreach).
                // Trim trailing whitespace before re-indenting line-by-line.
                foreach (var line in rendered.TrimEnd('\r', '\n').Split('\n'))
                {
                    CodeFormatter.AppendLine(sb, 2, line.TrimEnd('\r'));
                }
            }
        }

        CodeFormatter.AppendLine(sb, 1, "}");
    }

    private static string RenderMethodParameter(
        TypeScriptAstRenderer inner,
        ParameterDeclarationNode parameter)
    {
        var optional = parameter.IsRequired ? "" : "?";
        var typeStr = parameter.Type != null ? $": {parameter.Type.Accept(inner)}" : "";
        return $"{parameter.Name.Name}{optional}{typeStr}";
    }
}
