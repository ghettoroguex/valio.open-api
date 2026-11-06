using System.Collections.Immutable;
using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Visitors;

namespace ValIO.Core.Ast.Nodes;

/// <summary>
/// Узел объявления класса (универсальный: C#, C++ и др.).
/// Промоучен из C#-генератора; C++ std-профиль использует тот же узел
/// (IsSealed/IsStatic/IsPartial — C#-модификаторы, C++-рендер игнорирует их).
/// </summary>
public sealed class ClassDeclarationNode : TypeDeclarationNode
{
    /// <summary>
    /// Является ли класс sealed
    /// </summary>
    public bool IsSealed { get; }

    /// <summary>
    /// Является ли класс static
    /// </summary>
    public bool IsStatic { get; }

    /// <summary>
    /// Является ли класс partial
    /// </summary>
    public bool IsPartial { get; }


    /// <summary>
    /// Конструктор
    /// </summary>
    public ClassDeclarationNode(
        IdentifierNode name,
        AccessModifier accessModifier = AccessModifier.Public,
        bool isSealed = false,
        bool isStatic = false,
        bool isPartial = false,
        IReadOnlyList<TypeNode>? baseTypes = null,
        IReadOnlyList<DeclarationNode>? members = null,
        CommentNode? documentation = null,
        IReadOnlyList<AttributeNode>? attributes = null,
        IReadOnlyDictionary<string, object>? metadata = null)
        : base(name, accessModifier, baseTypes, members, documentation, attributes, metadata)
    {
        IsSealed = isSealed;
        IsStatic = isStatic;
        IsPartial = isPartial;
    }

    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor)
    {
        return visitor.VisitClassDeclaration(this);
    }

    public override AstNode Clone()
    {
        return new ClassDeclarationNode(
            (IdentifierNode)Name.Clone(),
            AccessModifier,
            IsSealed,
            IsStatic,
            IsPartial,
            BaseTypes.Select(t => (TypeNode)t.Clone()).ToImmutableList(),
            Members.Select(m => (DeclarationNode)m.Clone()).ToImmutableList(),
            Documentation != null ? (CommentNode)Documentation.Clone() : null,
            Attributes.Select(a => (AttributeNode)a.Clone()).ToImmutableList(),
            Metadata.ToImmutableDictionary());
    }

    public override string ToString()
        => $"class {Name.Name} [{Members.Count} members]";
}
