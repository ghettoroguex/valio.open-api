using System.Collections.Immutable;
using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Visitors;

namespace ValIO.Core.Ast.Nodes;

/// <summary>
/// Узел объявления struct (универсальный: C#, C++ и др.).
/// Промоучен из C#-генератора. В отличие от C#, C++-структуры поддерживают
/// наследование — C++-рендер использует BaseTypes, C#-рендер их отвергает.
/// </summary>
public sealed class StructDeclarationNode : TypeDeclarationNode
{
    /// <summary>
    /// Является ли struct readonly (C#)
    /// </summary>
    public bool IsReadOnly { get; }


    /// <summary>
    /// Конструктор
    /// </summary>
    public StructDeclarationNode(
        IdentifierNode name,
        AccessModifier accessModifier = AccessModifier.Public,
        bool isReadOnly = false,
        IReadOnlyList<TypeNode>? baseTypes = null,
        IReadOnlyList<DeclarationNode>? members = null,
        CommentNode? documentation = null,
        IReadOnlyList<AttributeNode>? attributes = null,
        IReadOnlyDictionary<string, object>? metadata = null)
        : base(name, accessModifier, baseTypes, members, documentation, attributes, metadata)
    {
        IsReadOnly = isReadOnly;
    }

    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor)
    {
        return visitor.VisitStructDeclaration(this);
    }

    public override AstNode Clone()
    {
        return new StructDeclarationNode(
            (IdentifierNode)Name.Clone(),
            AccessModifier,
            IsReadOnly,
            BaseTypes.Select(t => (TypeNode)t.Clone()).ToImmutableList(),
            Members.Select(m => (DeclarationNode)m.Clone()).ToImmutableList(),
            Documentation != null ? (CommentNode)Documentation.Clone() : null,
            Attributes.Select(a => (AttributeNode)a.Clone()).ToImmutableList(),
            Metadata.ToImmutableDictionary());
    }

    public override string ToString()
        => $"struct {Name.Name} [{Members.Count} members]";
}
