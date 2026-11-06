using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Expressions;
using ValIO.Core.IR;
using ValIO.Generators.TypeScript.Ast.Nodes.Expressions;

namespace ValIO.Generators.TypeScript.Ast.Builders;

/// <summary>Builds response decoding that respects protocol and documented no-body responses.</summary>
public static class TypeScriptResponseBody
{
    /// <summary>Skips a response body decoder for HEAD, 204/205, and explicitly bodyless response variants.</summary>
    /// <param name="operation">The operation whose response contract determines decoding.</param>
    /// <param name="status">The received response status expression.</param>
    /// <param name="decoder">The body decoder expression, evaluated only when content is permitted.</param>
    /// <returns>The conditional decoded value, or undefined for HEAD.</returns>
    public static ExpressionNode Read(IrOperation operation, ExpressionNode status, ExpressionNode decoder)
    {
        var undefined = new IdentifierExpressionNode(new IdentifierNode("undefined"));
        if (string.Equals(operation.HttpMethod, "HEAD", StringComparison.OrdinalIgnoreCase))
            return undefined;

        var bodylessStatuses = new HashSet<int> { 204, 205 };
        foreach (var response in operation.Responses)
        {
            if (response.Type == null && int.TryParse(response.StatusCode, out var statusCode))
                bodylessStatuses.Add(statusCode);
        }

        ExpressionNode? condition = null;
        foreach (var statusCode in bodylessStatuses.Order())
        {
            var matches = new BinaryExpressionNode(status, BinaryOperator.Equal, new LiteralExpressionNode(statusCode, LiteralKind.Number));
            condition = condition == null ? matches : new BinaryExpressionNode(condition, BinaryOperator.LogicalOr, matches);
        }

        return new ConditionalExpressionNode(condition!, undefined, decoder);
    }
}
