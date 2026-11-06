using System.Collections.Immutable;
using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Expressions;
using ValIO.Core.Ast.Nodes.Statements;
using ValIO.Core.IR;
using ValIO.Generators.TypeScript.Ast.Nodes.Expressions;

namespace ValIO.Generators.TypeScript.Ast.Builders;

/// <summary>Builds normalized fetch headers while preserving caller overrides and OpenAPI wire names.</summary>
public static class TypeScriptFetchHeaders
{
    /// <summary>
    /// Creates a native Headers instance from any caller HeadersInit shape, then fills missing operation headers.
    /// </summary>
    /// <param name="operation">The operation whose headers and content type are emitted.</param>
    /// <param name="source">The raw or validated header parameter group, when present.</param>
    /// <param name="sourceMayBeUndefined">Whether the header group itself may be absent.</param>
    /// <param name="callerHeaders">The optional caller HeadersInit expression.</param>
    /// <param name="statements">The function statements receiving header construction.</param>
    /// <returns>The normalized requestHeaders local expression.</returns>
    public static ExpressionNode Build(
        IrOperation operation,
        ExpressionNode? source,
        bool sourceMayBeUndefined,
        ExpressionNode callerHeaders,
        List<StatementNode> statements)
    {
        var headers = Id("requestHeaders");
        statements.Add(new VariableDeclarationStatementNode(
            new IdentifierNode("requestHeaders"),
            type: null,
            initializer: new CallExpressionNode(Id("Headers"), ImmutableList.Create(callerHeaders), isNewExpression: true)));

        if (source != null)
        {
            foreach (var parameter in operation.HeaderParameters)
            {
                var value = TypeScriptRequestPath.BuildParameterAccess(source, parameter.Name, sourceMayBeUndefined);
                var present = new BinaryExpressionNode(
                    new BinaryExpressionNode(value, BinaryOperator.NotEqual, Id("undefined")),
                    BinaryOperator.LogicalAnd,
                    new BinaryExpressionNode(value, BinaryOperator.NotEqual, Id("null")));
                var missing = new UnaryExpressionNode(UnaryOperator.LogicalNot,
                    new CallExpressionNode(Member(headers, "has"), ImmutableList.Create<ExpressionNode>(Str(parameter.OriginalName))));
                statements.Add(new IfStatementNode(
                    new BinaryExpressionNode(present, BinaryOperator.LogicalAnd, missing),
                    new BlockNode(ImmutableList.Create<StatementNode>(new ExpressionStatementNode(new CallExpressionNode(
                        Member(headers, "set"),
                        ImmutableList.Create<ExpressionNode>(Str(parameter.OriginalName), new CallExpressionNode(Id("String"), ImmutableList.Create(value)))))))));
            }
        }

        var contentType = TypeScriptWireParameterObject.DefaultContentType(operation.RequestBody, includeJson: true);
        if (contentType != null)
        {
            var missing = new UnaryExpressionNode(UnaryOperator.LogicalNot,
                new CallExpressionNode(Member(headers, "has"), ImmutableList.Create<ExpressionNode>(Str("Content-Type"))));
            statements.Add(new IfStatementNode(missing,
                new BlockNode(ImmutableList.Create<StatementNode>(new ExpressionStatementNode(new CallExpressionNode(
                    Member(headers, "set"), ImmutableList.Create<ExpressionNode>(Str("Content-Type"), Str(contentType))))))));
        }

        return headers;
    }

    /// <summary>Creates a runtime identifier expression.</summary>
    /// <param name="name">The identifier name.</param>
    /// <returns>The identifier expression.</returns>
    private static IdentifierExpressionNode Id(string name) => new(new IdentifierNode(name));

    /// <summary>Creates a member access expression.</summary>
    /// <param name="source">The object being accessed.</param>
    /// <param name="name">The member name.</param>
    /// <returns>The member access expression.</returns>
    private static MemberAccessExpressionNode Member(ExpressionNode source, string name) => new(source, new IdentifierNode(name));

    /// <summary>Creates a string literal expression.</summary>
    /// <param name="value">The unescaped string.</param>
    /// <returns>The string literal expression.</returns>
    private static LiteralExpressionNode Str(string value) => new(value, LiteralKind.String);
}
