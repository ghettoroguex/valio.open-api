using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Visitors;

namespace ValIO.Core.Ast.Nodes.Expressions;

/// <summary>
/// Represents an operator applied to one operand, such as <c>*x</c>, <c>&amp;x</c>, or <c>!x</c>.
/// </summary>
public sealed class UnaryExpressionNode : ExpressionNode
{
    /// <summary>Gets the operation applied to <see cref="Operand"/>.</summary>
    /// <value>The unary operator.</value>
    public UnaryOperator Operator { get; }

    /// <summary>Gets the expression on which the operator acts.</summary>
    /// <value>The required operand.</value>
    public ExpressionNode Operand { get; }


    /// <inheritdoc/>
    public override IReadOnlyList<AstNode> Children => [Operand, .. base.Children];

    /// <summary>Initializes a unary operation with its optional inferred type and metadata.</summary>
    /// <param name="operator">The unary operation.</param>
    /// <param name="operand">The expression on which the operation acts.</param>
    /// <param name="type">The optional inferred result type.</param>
    /// <param name="metadata">Optional metadata copied into the node.</param>
    /// <exception cref="ArgumentNullException"><paramref name="operand"/> is <see langword="null"/>.</exception>
    public UnaryExpressionNode(
        UnaryOperator @operator,
        ExpressionNode operand,
        TypeNode? type = null,
        IReadOnlyDictionary<string, object>? metadata = null)
        : base(type, metadata)
    {
        Operator = @operator;
        Operand = operand ?? throw new ArgumentNullException(nameof(operand));
    }

    /// <inheritdoc/>
    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor)
    {
        return visitor.VisitUnaryExpression(this);
    }

    /// <inheritdoc/>
    public override AstNode Clone()
    {
        return new UnaryExpressionNode(
            Operator,
            (ExpressionNode)Operand.Clone(),
            Type != null ? (TypeNode)Type.Clone() : null,
            Metadata);
    }
}
