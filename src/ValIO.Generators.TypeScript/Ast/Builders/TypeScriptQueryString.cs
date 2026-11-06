using System.Collections.Immutable;
using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Expressions;
using ValIO.Core.Ast.Nodes.Statements;
using ValIO.Core.IR;
using ValIO.Generators.TypeScript.Ast.Nodes.Expressions;

namespace ValIO.Generators.TypeScript.Ast.Builders;

/// <summary>
/// Builds the query-string portion of a request URL for transports that fold parameters into the URL itself
/// (the plain <c>fetch</c> client and the Next.js App Router client), using an explicit
/// <see cref="System.Collections.Specialized.NameValueCollection">URLSearchParams</see> builder with a presence
/// check per optional parameter.
/// </summary>
/// <remarks>
/// This centralizes the query-string generation that both the fetch and Next.js transports need, replacing an
/// earlier inline template-literal concatenation (which appended <c>name=undefined</c> for absent optional params
/// and hand-rolled <c>encodeURIComponent</c>). The emitted shape is:
/// <code>
/// const query = new URLSearchParams();
/// query.set("required", String(source.required));
/// if (source.optional !== undefined) query.set("optional", String(source.optional));
/// // …
/// const url = path + (query.size &gt; 0 ? `?${query}` : "");
/// </code>
/// <c>URLSearchParams</c> performs the percent-encoding, so no manual <c>encodeURIComponent</c> is emitted. Array
/// values are comma-joined by <c>String(...)</c> — adequate for v1. Each URLSearchParams key uses
/// <see cref="IrParameter.OriginalName"/>, while its value access uses the sanitized
/// <see cref="IrParameter.Name"/>.
/// </remarks>
/// <seealso cref="TypeScriptRequestPath"/>
public static class TypeScriptQueryString
{
    /// <summary>
    /// Appends the query-string build for <paramref name="operation"/> to <paramref name="statements"/> and returns
    /// the full request-URL expression (<paramref name="urlExpr"/> plus the conditional <c>?query</c> tail).
    /// </summary>
    /// <param name="operation">The IR operation whose query parameters are folded into the URL.</param>
    /// <param name="urlExpr">The already-built path URL expression (literal or template literal).</param>
    /// <param name="querySource">
    /// The query object the parameter values are read from (e.g. <c>params.query</c>, <c>params</c>, or a validated
    /// <c>parsedQuery</c>), or <see langword="null"/> when the operation has no query source.
    /// </param>
    /// <param name="queryMayBeUndefined">
    /// Whether <paramref name="querySource"/> itself may be <c>undefined</c> at runtime; when <see langword="true"/>
    /// per-parameter access uses optional chaining.
    /// </param>
    /// <param name="statements">The function-body statement list the URLSearchParams build is appended to.</param>
    /// <returns>
    /// The full URL expression. When the operation has no query parameters (or no source) this is
    /// <paramref name="urlExpr"/> unchanged and no statements are appended.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/>, <paramref name="urlExpr"/> or <paramref name="statements"/> is <see langword="null"/>.</exception>
    public static ExpressionNode AppendQueryToUrl(
        IrOperation operation,
        ExpressionNode urlExpr,
        ExpressionNode? querySource,
        bool queryMayBeUndefined,
        List<StatementNode> statements)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(urlExpr);
        ArgumentNullException.ThrowIfNull(statements);

        if (operation.QueryParameters.Count == 0 || querySource == null)
            return urlExpr;

        statements.Add(new VariableDeclarationStatementNode(
            new IdentifierNode("query"),
            type: null,
            initializer: new CallExpressionNode(Id("URLSearchParams"), ImmutableList<ExpressionNode>.Empty, isNewExpression: true)));

        foreach (var parameter in operation.QueryParameters)
        {
            var access = TypeScriptRequestPath.BuildParameterAccess(querySource, parameter.Name, queryMayBeUndefined);
            // Scalars stringify directly; array values are comma-joined by String(...) — adequate for v1.
            // The key is the wire name (Paging.Offset), not the sanitized TS property (Paging_Offset):
            // ASP.NET binds a nested model by its dotted name and finds nothing under the underscore.
            var setCall = new ExpressionStatementNode(new CallExpressionNode(
                Member(Id("query"), "set"),
                ImmutableList.Create<ExpressionNode>(Str(parameter.OriginalName),
                    new CallExpressionNode(Id("String"), ImmutableList.Create<ExpressionNode>(access)))));
            if (parameter.IsRequired)
                statements.Add(setCall);
            else
                statements.Add(new IfStatementNode(
                    new BinaryExpressionNode(access, BinaryOperator.NotEqual, Id("undefined")),
                    new BlockNode(ImmutableList.Create<StatementNode>(setCall))));
        }
        statements.Add(new BlankLineStatementNode());

        var queryTail = new ConditionalExpressionNode(
            new BinaryExpressionNode(Member(Id("query"), "size"), BinaryOperator.GreaterThan, Num(0)),
            new TemplateLiteralExpressionNode(ImmutableList.Create(new TemplateLiteralPart("?"), new TemplateLiteralPart(Id("query")))),
            Str(string.Empty));
        return new BinaryExpressionNode(urlExpr, BinaryOperator.Add, queryTail);
    }

    /// <summary>Builds an identifier expression.</summary>
    /// <param name="name">The identifier name.</param>
    /// <returns>The identifier expression node.</returns>
    private static IdentifierExpressionNode Id(string name) => new(new IdentifierNode(name));

    /// <summary>Builds a plain member-access expression <c>source.member</c>.</summary>
    /// <param name="source">The object expression.</param>
    /// <param name="member">The member name.</param>
    /// <returns>The member-access expression node.</returns>
    private static MemberAccessExpressionNode Member(ExpressionNode source, string member) =>
        new(source, new IdentifierNode(member));

    /// <summary>Builds a string literal expression.</summary>
    /// <param name="value">The literal string value.</param>
    /// <returns>The string literal expression node.</returns>
    private static LiteralExpressionNode Str(string value) => new(value, LiteralKind.String);

    /// <summary>Builds a numeric literal expression.</summary>
    /// <param name="value">The literal integer value.</param>
    /// <returns>The numeric literal expression node.</returns>
    private static LiteralExpressionNode Num(int value) => new(value, LiteralKind.Number);
}
