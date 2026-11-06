using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Visitors;
using ValIO.Generators.CSharp.Ast.Visitors;

namespace ValIO.Generators.CSharp.Ast.Nodes;

/// <summary>Represents a C# typeof operand as a traversable type instead of source text.</summary>
public sealed class TypeOfExpressionNode : ExpressionNode
{
    /// <summary>Gets the type whose runtime metadata is requested.</summary>
    /// <value>The typed operand of the expression.</value>
    public TypeNode TargetType { get; }

    /// <summary>Initializes a typeof expression with its required operand.</summary>
    /// <param name="targetType">The type whose metadata is requested.</param>
    /// <param name="metadata">Optional syntax metadata.</param>
    /// <exception cref="ArgumentNullException">The target type is null.</exception>
    public TypeOfExpressionNode(TypeNode targetType, IReadOnlyDictionary<string, object>? metadata = null)
        : base(metadata: metadata)
    {
        TargetType = targetType ?? throw new ArgumentNullException(nameof(targetType));
    }

    /// <inheritdoc />
    public override IReadOnlyList<AstNode> Children => [TargetType];

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">The visitor does not support C# expressions.</exception>
    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor) =>
        visitor is ICSharpAstVisitor<TResult> csharpVisitor
            ? csharpVisitor.VisitTypeOfExpression(this)
            : throw new NotSupportedException("The visitor must support C# typeof expressions.");

    /// <inheritdoc />
    public override AstNode Clone() => new TypeOfExpressionNode((TypeNode)TargetType.Clone(), Metadata);
}
