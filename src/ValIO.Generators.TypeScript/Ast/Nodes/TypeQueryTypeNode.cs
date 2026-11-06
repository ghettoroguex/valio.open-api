using System.Collections.Immutable;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Visitors;

namespace ValIO.Generators.TypeScript.Ast.Nodes;

/// <summary>References the static type of a runtime value without copying its transport-specific signature.</summary>
public sealed class TypeQueryTypeNode : TypeNode
{
    /// <summary>Gets the runtime value whose type is queried.</summary>
    /// <value>An identifier or qualified member expression.</value>
    public ExpressionNode Expression { get; }

    /// <inheritdoc/>
    public override IReadOnlyList<AstNode> Children => [Expression];

    /// <summary>Creates a TypeScript type query for a runtime transport or other declared value.</summary>
    /// <param name="expression">The value whose complete static type is retained.</param>
    /// <param name="metadata">Optional annotations preserved when cloning the node.</param>
    /// <exception cref="ArgumentNullException"><paramref name="expression"/> is null.</exception>
    public TypeQueryTypeNode(ExpressionNode expression, IReadOnlyDictionary<string, object>? metadata = null)
        : base(metadata: metadata)
    {
        ArgumentNullException.ThrowIfNull(expression);
        Expression = expression;
    }

    /// <inheritdoc/>
    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor) => visitor.DefaultVisit(this);

    /// <inheritdoc/>
    public override AstNode Clone() => new TypeQueryTypeNode(
        (ExpressionNode)Expression.Clone(), Metadata.ToImmutableDictionary());
}
