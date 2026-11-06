using System.Collections.Immutable;
using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes.Expressions;
using ValIO.Core.Ast.Visitors;

namespace ValIO.Core.Ast.Nodes;

/// <summary>
/// Узел объявления поля (универсальный: C#, C++ и др.).
/// Промоучен из C#-генератора; C++ std-профиль использует тот же узел
/// (Type + Name + DefaultValue — default member initializer в C++).
/// </summary>
public sealed class FieldDeclarationNode : DeclarationNode
{
    /// <summary>
    /// Тип поля
    /// </summary>
    public TypeNode Type { get; }

    /// <summary>
    /// Значение по умолчанию (в C++ — default member initializer)
    /// </summary>
    public ExpressionNode? DefaultValue { get; }

    /// <summary>
    /// Модификатор доступа
    /// </summary>
    public AccessModifier AccessModifier { get; }

    /// <summary>
    /// Является ли поле readonly
    /// </summary>
    public bool IsReadOnly { get; }

    /// <summary>
    /// Является ли поле static
    /// </summary>
    public bool IsStatic { get; }


    public override IReadOnlyList<AstNode> Children
    {
        get
        {
            var children = new List<AstNode> { Name, Type };
            if (Documentation != null)
                children.Add(Documentation);
            children.AddRange(Attributes);
            if (DefaultValue != null)
                children.Add(DefaultValue);
            return children;
        }
    }

    /// <summary>
    /// Конструктор
    /// </summary>
    public FieldDeclarationNode(
        IdentifierNode name,
        TypeNode type,
        AccessModifier accessModifier = AccessModifier.Private,
        bool isReadOnly = false,
        bool isStatic = false,
        ExpressionNode? defaultValue = null,
        CommentNode? documentation = null,
        IReadOnlyList<AttributeNode>? attributes = null,
        IReadOnlyDictionary<string, object>? metadata = null)
        : base(name, documentation, attributes, metadata)
    {
        Type = type ?? throw new ArgumentNullException(nameof(type));
        AccessModifier = accessModifier;
        IsReadOnly = isReadOnly;
        IsStatic = isStatic;
        DefaultValue = defaultValue;
    }

    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor)
    {
        return visitor.VisitFieldDeclaration(this);
    }

    public override AstNode Clone()
    {
        return new FieldDeclarationNode(
            (IdentifierNode)Name.Clone(),
            (TypeNode)Type.Clone(),
            AccessModifier,
            IsReadOnly,
            IsStatic,
            DefaultValue != null ? (ExpressionNode)DefaultValue.Clone() : null,
            Documentation != null ? (CommentNode)Documentation.Clone() : null,
            Attributes.Select(a => (AttributeNode)a.Clone()).ToImmutableList(),
            Metadata.ToImmutableDictionary());
    }

    public override string ToString()
        => $"field {Type} {Name.Name}";
}
