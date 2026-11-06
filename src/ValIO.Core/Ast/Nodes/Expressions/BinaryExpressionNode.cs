using System.Collections.Immutable;
using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Visitors;

namespace ValIO.Core.Ast.Nodes.Expressions;

/// <summary>
/// Узел бинарного выражения
/// </summary>
public sealed class BinaryExpressionNode : ExpressionNode
{
    /// <summary>
    /// Левый операнд
    /// </summary>
    public ExpressionNode Left { get; }
    
    /// <summary>
    /// Оператор
    /// </summary>
    public BinaryOperator Operator { get; }
    
    /// <summary>
    /// Правый операнд
    /// </summary>
    public ExpressionNode Right { get; }
    
    /// <summary>
    /// Является ли выражение присваиванием (например, x = y)
    /// </summary>
    public bool IsAssignment { get; }
    
    /// <summary>
    /// Является ли выражение именованным аргументом атрибута (например, Value = "x" в [EnumMember(Value = "x")])
    /// </summary>
    public bool IsNamedArgument { get; }
    
    
    /// <inheritdoc/>
    public override IReadOnlyList<AstNode> Children => [Left, Right, .. base.Children];
    
    /// <summary>
    /// Конструктор
    /// </summary>
    public BinaryExpressionNode(
        ExpressionNode left,
        BinaryOperator @operator,
        ExpressionNode right,
        TypeNode? type = null,
        bool isAssignment = false,
        bool isNamedArgument = false,
        IReadOnlyDictionary<string, object>? metadata = null)
        : base(type, metadata)
    {
        Left = left ?? throw new ArgumentNullException(nameof(left));
        Operator = @operator;
        Right = right ?? throw new ArgumentNullException(nameof(right));
        IsAssignment = isAssignment;
        IsNamedArgument = isNamedArgument;
    }
    
    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor)
    {
        return visitor.VisitBinaryExpression(this);
    }
    
    public override AstNode Clone()
    {
        return new BinaryExpressionNode(
            (ExpressionNode)Left.Clone(),
            Operator,
            (ExpressionNode)Right.Clone(),
            Type != null ? (TypeNode)Type.Clone() : null,
            IsAssignment,
            IsNamedArgument,
            Metadata.ToImmutableDictionary());
    }
    
    public override string ToString()
    {
        var assignmentStr = IsAssignment ? " [assignment]" : "";
        var namedArgStr = IsNamedArgument ? " [named-arg]" : "";
        return $"BinaryExpression({Operator}){assignmentStr}{namedArgStr}";
    }
}
