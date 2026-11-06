using System.Collections.Immutable;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Visitors;

namespace ValIO.Core.Ast.Nodes.Expressions;

/// <summary>
/// Узел выражения доступа к члену (object.member)
/// </summary>
public sealed class MemberAccessExpressionNode : ExpressionNode
{
    /// <summary>
    /// Объект
    /// </summary>
    public ExpressionNode Object { get; }
    
    /// <summary>
    /// Член
    /// </summary>
    public IdentifierNode Member { get; }
    
    /// <summary>
    /// Использовать как оператор (например, "as" в C# для приведения типов)
    /// </summary>
    public bool UseAsOperator { get; }

    /// <summary>
    /// Использовать оператор -> вместо . (для C++ pointer member access)
    /// </summary>
    public bool UseArrowOperator { get; }

    /// <summary>
    /// Использовать оператор :: (для C++ scope/static member access: Class::Member)
    /// </summary>
    public bool UseScopeOperator { get; }

    /// <summary>
    /// Use optional chaining (?.) for TypeScript: obj?.member instead of obj.member
    /// </summary>
    public bool UseOptionalChaining { get; }
    
    
    /// <inheritdoc/>
    public override IReadOnlyList<AstNode> Children => [Object, Member, .. base.Children];
    
    /// <summary>
    /// Конструктор
    /// </summary>
    public MemberAccessExpressionNode(
        ExpressionNode @object,
        IdentifierNode member,
        TypeNode? type = null,
        IReadOnlyDictionary<string, object>? metadata = null,
        bool useAsOperator = false,
        bool useArrowOperator = false,
        bool useScopeOperator = false,
        bool useOptionalChaining = false)
        : base(type, metadata)
    {
        Object = @object ?? throw new ArgumentNullException(nameof(@object));
        Member = member ?? throw new ArgumentNullException(nameof(member));
        UseAsOperator = useAsOperator;
        UseArrowOperator = useArrowOperator;
        UseScopeOperator = useScopeOperator;
        UseOptionalChaining = useOptionalChaining;
    }
    
    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor)
    {
        return visitor.VisitMemberAccessExpression(this);
    }
    
    public override AstNode Clone()
    {
        return new MemberAccessExpressionNode(
            (ExpressionNode)Object.Clone(),
            (IdentifierNode)Member.Clone(),
            Type != null ? (TypeNode)Type.Clone() : null,
            Metadata.ToImmutableDictionary(),
            UseAsOperator,
            UseArrowOperator,
            UseScopeOperator,
            UseOptionalChaining);
    }
}
