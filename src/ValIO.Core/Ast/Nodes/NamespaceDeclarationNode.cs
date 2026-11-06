using System.Collections.Immutable;
using ValIO.Core.Ast.Visitors;

namespace ValIO.Core.Ast.Nodes;

/// <summary>
/// Узел объявления namespace (универсальный: C#, C++ и др.).
/// Промоучен из C#-генератора: C++ std-профиль использует тот же узел.
/// </summary>
public sealed class NamespaceDeclarationNode : DeclarationNode
{
    /// <summary>
    /// Объявления в namespace
    /// </summary>
    public IReadOnlyList<DeclarationNode> Declarations { get; }


    public override IReadOnlyList<AstNode> Children
    {
        get
        {
            var children = new List<AstNode> { Name };
            if (Documentation != null)
                children.Add(Documentation);
            children.AddRange(Attributes);
            children.AddRange(Declarations);
            return children;
        }
    }

    /// <summary>
    /// Конструктор
    /// </summary>
    public NamespaceDeclarationNode(
        IdentifierNode name,
        IReadOnlyList<DeclarationNode>? declarations = null,
        CommentNode? documentation = null,
        IReadOnlyList<AttributeNode>? attributes = null,
        IReadOnlyDictionary<string, object>? metadata = null)
        : base(name, documentation, attributes, metadata)
    {
        Declarations = declarations?.ToImmutableList() ?? ImmutableList<DeclarationNode>.Empty;
    }

    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor)
    {
        return visitor.VisitNamespaceDeclaration(this);
    }

    public override AstNode Clone()
    {
        return new NamespaceDeclarationNode(
            (IdentifierNode)Name.Clone(),
            Declarations.Select(d => (DeclarationNode)d.Clone()).ToImmutableList(),
            Documentation != null ? (CommentNode)Documentation.Clone() : null,
            Attributes.Select(a => (AttributeNode)a.Clone()).ToImmutableList(),
            Metadata.ToImmutableDictionary());
    }

    public override string ToString()
        => $"namespace {Name.Name} [{Declarations.Count} declarations]";
}
