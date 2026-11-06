using System.Collections.Immutable;
using ValIO.Core.Ast.Visitors;

namespace ValIO.Core.Ast.Nodes;

/// <summary>
/// Узел объявления члена enum
/// </summary>
public sealed class EnumMemberDeclarationNode : DeclarationNode
{
    /// <summary>
    /// Значение enum
    /// </summary>
    public ExpressionNode? Value { get; }

    /// <summary>
    /// Wire-имя значения (исходная строка/число из OpenAPI, попадает в JSON/URL без
    /// изменений, даже когда Name санитизирован). null — wire совпадает с Name
    /// или значение не строковое (тогда используется Value).
    /// </summary>
    public string? WireValue { get; }

    
    public override IReadOnlyList<AstNode> Children
    {
        get
        {
            var children = new List<AstNode> { Name };
            if (Documentation != null)
                children.Add(Documentation);
            children.AddRange(Attributes);
            if (Value != null)
                children.Add(Value);
            return children;
        }
    }
    
    /// <summary>
    /// Конструктор
    /// </summary>
    public EnumMemberDeclarationNode(
        IdentifierNode name,
        ExpressionNode? value = null,
        CommentNode? documentation = null,
        IReadOnlyList<AttributeNode>? attributes = null,
        IReadOnlyDictionary<string, object>? metadata = null,
        string? wireValue = null)
        : base(name, documentation, attributes, metadata)
    {
        Value = value;
        WireValue = wireValue;
    }
    
    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor)
    {
        return visitor.VisitEnumMemberDeclaration(this);
    }
    
    public override AstNode Clone()
    {
        return new EnumMemberDeclarationNode(
            (IdentifierNode)Name.Clone(),
            Value != null ? (ExpressionNode)Value.Clone() : null,
            Documentation != null ? (CommentNode)Documentation.Clone() : null,
            Attributes.Select(a => (AttributeNode)a.Clone()).ToImmutableList(),
            Metadata.ToImmutableDictionary(),
            WireValue);
    }
}
