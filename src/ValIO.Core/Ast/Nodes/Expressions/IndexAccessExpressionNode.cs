using System.Collections.Immutable;
using ValIO.Core.Ast.Visitors;

namespace ValIO.Core.Ast.Nodes.Expressions;

/// <summary>
/// Узел выражения индексации: <c>obj[index]</c>. Универсален для C++/C#/TS/JS/Rust
/// (пример C++: <c>j["id"]</c> — subscript nlohmann::json).
/// </summary>
public sealed class IndexAccessExpressionNode : ExpressionNode
{
    /// <summary>
    /// Индексируемое выражение
    /// </summary>
    public ExpressionNode Object { get; }

    /// <summary>
    /// Выражение-индекс
    /// </summary>
    public ExpressionNode Index { get; }


    /// <inheritdoc/>
    public override IReadOnlyList<AstNode> Children => [Object, Index, .. base.Children];

    /// <summary>
    /// Конструктор
    /// </summary>
    public IndexAccessExpressionNode(
        ExpressionNode @object,
        ExpressionNode index,
        TypeNode? type = null,
        IReadOnlyDictionary<string, object>? metadata = null)
        : base(type, metadata)
    {
        Object = @object ?? throw new ArgumentNullException(nameof(@object));
        Index = index ?? throw new ArgumentNullException(nameof(index));
    }

    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor)
    {
        return visitor.VisitIndexAccessExpression(this);
    }

    public override AstNode Clone()
    {
        return new IndexAccessExpressionNode(
            (ExpressionNode)Object.Clone(),
            (ExpressionNode)Index.Clone(),
            Type != null ? (TypeNode)Type.Clone() : null,
            Metadata.ToImmutableDictionary());
    }

    public override string ToString() => $"{Object}[{Index}]";
}
