using System.Collections.Immutable;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Visitors;

namespace ValIO.Core.Ast.Nodes.Expressions;

/// <summary>
/// Узел условного выражения (ternary operator: condition ? true : false)
/// </summary>
public sealed class ConditionalExpressionNode : ExpressionNode
{
    /// <summary>
    /// Условие
    /// </summary>
    public ExpressionNode Condition { get; }
    
    /// <summary>
    /// Выражение для true ветки
    /// </summary>
    public ExpressionNode TrueExpression { get; }
    
    /// <summary>
    /// Выражение для false ветки
    /// </summary>
    public ExpressionNode FalseExpression { get; }
    
    
    /// <inheritdoc/>
    public override IReadOnlyList<AstNode> Children => [Condition, TrueExpression, FalseExpression, .. base.Children];
    
    /// <summary>
    /// Конструктор
    /// </summary>
    public ConditionalExpressionNode(
        ExpressionNode condition,
        ExpressionNode trueExpression,
        ExpressionNode falseExpression,
        TypeNode? type = null,
        IReadOnlyDictionary<string, object>? metadata = null)
        : base(type, metadata)
    {
        Condition = condition ?? throw new ArgumentNullException(nameof(condition));
        TrueExpression = trueExpression ?? throw new ArgumentNullException(nameof(trueExpression));
        FalseExpression = falseExpression ?? throw new ArgumentNullException(nameof(falseExpression));
    }
    
    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor)
    {
        return visitor.VisitConditionalExpression(this);
    }
    
    public override AstNode Clone()
    {
        return new ConditionalExpressionNode(
            (ExpressionNode)Condition.Clone(),
            (ExpressionNode)TrueExpression.Clone(),
            (ExpressionNode)FalseExpression.Clone(),
            Type != null ? (TypeNode)Type.Clone() : null,
            Metadata.ToImmutableDictionary());
    }
}
