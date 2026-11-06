using System.Collections.Immutable;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Visitors;
using ValIO.Generators.Kotlin.Ast.Visitors;

namespace ValIO.Generators.Kotlin.Ast.Nodes;

/// <summary>Represents an anonymous Kotlin object implementing an interface with structured member declarations.</summary>
public sealed class KotlinObjectExpressionNode : ExpressionNode
{
    /// <summary>Gets the interface implemented by the anonymous object.</summary>
    /// <value>The interface type, including generic arguments.</value>
    public TypeNode InterfaceType { get; }

    /// <summary>Gets the anonymous object's member declarations.</summary>
    /// <value>An immutable snapshot of the declared members.</value>
    public IReadOnlyList<DeclarationNode> Members { get; }

    /// <inheritdoc/>
    public override IReadOnlyList<AstNode> Children => [InterfaceType, .. Members, .. base.Children];

    /// <summary>Creates an anonymous interface implementation without storing source text.</summary>
    /// <param name="interfaceType">The required implemented interface.</param>
    /// <param name="members">The declared implementation members.</param>
    /// <param name="metadata">Optional node metadata copied by the base node.</param>
    /// <exception cref="ArgumentNullException">A required interface type or member collection is null.</exception>
    public KotlinObjectExpressionNode(TypeNode interfaceType, IReadOnlyList<DeclarationNode> members,
        IReadOnlyDictionary<string, object>? metadata = null) : base(metadata: metadata)
    {
        InterfaceType = interfaceType ?? throw new ArgumentNullException(nameof(interfaceType));
        ArgumentNullException.ThrowIfNull(members);
        Members = members.ToImmutableArray();
    }

    /// <inheritdoc/>
    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor) => visitor is IKotlinAstVisitor<TResult> kotlin
        ? kotlin.VisitKotlinObjectExpression(this)
        : throw new NotSupportedException("A Kotlin visitor is required for anonymous object expressions.");

    /// <inheritdoc/>
    public override AstNode Clone() => new KotlinObjectExpressionNode((TypeNode)InterfaceType.Clone(),
        Members.Select(member => (DeclarationNode)member.Clone()).ToArray(), Metadata);
}
