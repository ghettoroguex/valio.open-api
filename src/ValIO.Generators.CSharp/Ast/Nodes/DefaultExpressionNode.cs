using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Visitors;
using ValIO.Generators.CSharp.Ast.Visitors;

namespace ValIO.Generators.CSharp.Ast.Nodes;

/// <summary>Represents the C# default expression without overloading string literal semantics.</summary>
public sealed class DefaultExpressionNode : ExpressionNode
{
    /// <summary>Gets whether the expression suppresses nullable analysis for an initializer.</summary>
    /// <value>Whether to emit the postfix null-forgiving operator.</value>
    public bool SuppressNullableWarning { get; }

    /// <summary>Initializes a contextual default value, optionally with an explicit target type.</summary>
    /// <param name="suppressNullableWarning">Whether to suppress nullable initialization warnings.</param>
    /// <param name="type">The explicit target type, or null for contextual type inference.</param>
    /// <param name="metadata">Optional metadata copied into the node.</param>
    public DefaultExpressionNode(
        bool suppressNullableWarning = false,
        TypeNode? type = null,
        IReadOnlyDictionary<string, object>? metadata = null)
        : base(type, metadata)
    {
        SuppressNullableWarning = suppressNullableWarning;
    }

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">The visitor does not support C# nodes.</exception>
    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor) =>
        visitor is ICSharpAstVisitor<TResult> csharpVisitor
            ? csharpVisitor.VisitDefaultExpression(this)
            : throw new NotSupportedException("The visitor must support C# default expressions.");

    /// <inheritdoc/>
    public override AstNode Clone() => new DefaultExpressionNode(
        SuppressNullableWarning,
        Type is null ? null : (TypeNode)Type.Clone(),
        Metadata);
}
