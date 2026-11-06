using System.Collections.Immutable;
using System.Text.Json;
using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Expressions;
using ValIO.Generators.TypeScript.Ast.Nodes;
using ValIO.Generators.TypeScript.Ast.Nodes.Expressions;

namespace ValIO.Generators.TypeScript.Ast.Builders;

/// <summary>Builds TypeScript constants from explicit JSON defaults shared by models and parameters.</summary>
internal static class TypeScriptDefaultValues
{
    /// <summary>Creates an exported constant for the non-null defaults, omitting an empty constant.</summary>
    /// <param name="name">The constant name.</param>
    /// <param name="entries">The property names and IR default values.</param>
    /// <returns>The declaration, or null when there are no non-null defaults.</returns>
    internal static VariableDeclarationNode? Build(string name, IEnumerable<(string Name, JsonElement Value)> entries)
    {
        var properties = entries
            .Where(e => e.Value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
            .Select(e => new PropertyAssignmentNode(new IdentifierNode(e.Name), Expression(e.Value)))
            .ToImmutableList();
        return properties.Count == 0 ? null : new VariableDeclarationNode(
            new IdentifierNode(name), VariableDeclarationKind.Const,
            initializer: new ObjectLiteralExpressionNode(properties, preferMultilineFormat: true),
            isExported: true, asConstAssertion: true);
    }

    /// <summary>Converts a JSON value recursively to an equivalent TypeScript expression.</summary>
    /// <param name="value">The JSON value.</param>
    /// <returns>The corresponding expression.</returns>
    /// <exception cref="InvalidOperationException">The JSON value has no supported representation.</exception>
    private static ExpressionNode Expression(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => new LiteralExpressionNode(value.GetString(), LiteralKind.String),
        JsonValueKind.Number => new LiteralExpressionNode(value.GetRawText(), LiteralKind.Number),
        JsonValueKind.True => new LiteralExpressionNode(true, LiteralKind.Boolean),
        JsonValueKind.False => new LiteralExpressionNode(false, LiteralKind.Boolean),
        JsonValueKind.Null => new LiteralExpressionNode(null, LiteralKind.Null),
        JsonValueKind.Array => new ArrayLiteralExpressionNode(value.EnumerateArray().Select(Expression).ToImmutableList()),
        JsonValueKind.Object => new ObjectLiteralExpressionNode(value.EnumerateObject()
            .Select(p => new PropertyAssignmentNode(new IdentifierNode(p.Name), Expression(p.Value))).ToImmutableList()),
        _ => throw new InvalidOperationException("Unsupported JSON default value.")
    };
}
