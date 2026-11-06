using System.Collections.Immutable;
using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Core.Ast.Visitors;

namespace ValIO.Core.Ast.Nodes;

/// <summary>
/// Узел объявления метода
/// </summary>
public sealed class MethodDeclarationNode : DeclarationNode
{
    /// <summary>
    /// Возвращаемый тип
    /// </summary>
    public TypeNode? ReturnType { get; }
    
    /// <summary>
    /// Параметры метода
    /// </summary>
    public IReadOnlyList<ParameterDeclarationNode> Parameters { get; }
    
    /// <summary>
    /// Тело метода
    /// </summary>
    public BlockNode? Body { get; }
    
    /// <summary>
    /// Является ли метод async
    /// </summary>
    public bool IsAsync { get; }
    
    /// <summary>
    /// Модификатор доступа
    /// </summary>
    public AccessModifier AccessModifier { get; }
    
    /// <summary>
    /// Является ли метод методом интерфейса (без тела, только сигнатура)
    /// </summary>
    public bool IsInterfaceMethod { get; }
    
    /// <summary>
    /// Использовать inheritdoc для документации метода
    /// </summary>
    public bool UseInheritDoc { get; }
    
    /// <summary>
    /// Является ли метод переопределением базового метода (override)
    /// </summary>
    public bool IsOverride { get; }
    
    /// <summary>
    /// Generic параметры типа метода (например, ["T", "U"] для метода Measure&lt;T, U&gt;)
    /// </summary>
    public IReadOnlyList<string>? GenericTypeParameters { get; }

    /// <summary>
    /// Является ли метод static
    /// </summary>
    public bool IsStatic { get; }

    /// <summary>
    /// Является ли методом virtual (C# virtual / C++ virtual)
    /// </summary>
    public bool IsVirtual { get; }

    /// <summary>
    /// Является ли метод абстрактным (C# abstract / C++ pure virtual «= 0»)
    /// </summary>
    public bool IsAbstract { get; }

    /// <summary>
    /// Является ли метод константным (C++ const member function)
    /// </summary>
    public bool IsConst { get; }

    /// <summary>
    /// Помечен ли метод noexcept (C++)
    /// </summary>
    public bool Noexcept { get; }

    
    public override IReadOnlyList<AstNode> Children
    {
        get
        {
            var children = new List<AstNode> { Name };
            if (Documentation != null)
                children.Add(Documentation);
            children.AddRange(Attributes);
            if (ReturnType != null)
                children.Add(ReturnType);
            children.AddRange(Parameters);
            if (Body != null)
                children.Add(Body);
            return children;
        }
    }
    
    /// <summary>
    /// Конструктор
    /// </summary>
    public MethodDeclarationNode(
        IdentifierNode name,
        TypeNode? returnType = null,
        IReadOnlyList<ParameterDeclarationNode>? parameters = null,
        BlockNode? body = null,
        bool isAsync = false,
        AccessModifier accessModifier = AccessModifier.Public,
        CommentNode? documentation = null,
        IReadOnlyList<AttributeNode>? attributes = null,
        IReadOnlyDictionary<string, object>? metadata = null,
        bool isInterfaceMethod = false,
        bool useInheritDoc = false,
        bool isOverride = false,
        IReadOnlyList<string>? genericTypeParameters = null,
        bool isStatic = false,
        bool isVirtual = false,
        bool isAbstract = false,
        bool isConst = false,
        bool noexcept = false)
        : base(name, documentation, attributes, metadata)
    {
        ReturnType = returnType;
        Parameters = parameters?.ToImmutableList() ?? ImmutableList<ParameterDeclarationNode>.Empty;
        Body = body;
        IsAsync = isAsync;
        AccessModifier = accessModifier;
        IsInterfaceMethod = isInterfaceMethod;
        UseInheritDoc = useInheritDoc;
        IsOverride = isOverride;
        GenericTypeParameters = genericTypeParameters?.ToImmutableList();
        IsStatic = isStatic;
        IsVirtual = isVirtual;
        IsAbstract = isAbstract;
        IsConst = isConst;
        Noexcept = noexcept;
    }
    
    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor)
    {
        return visitor.VisitMethodDeclaration(this);
    }
    
    public override AstNode Clone()
    {
        return new MethodDeclarationNode(
            (IdentifierNode)Name.Clone(),
            ReturnType != null ? (TypeNode)ReturnType.Clone() : null,
            Parameters.Select(p => (ParameterDeclarationNode)p.Clone()).ToImmutableList(),
            Body != null ? (BlockNode)Body.Clone() : null,
            IsAsync,
            AccessModifier,
            Documentation != null ? (CommentNode)Documentation.Clone() : null,
            Attributes.Select(a => (AttributeNode)a.Clone()).ToImmutableList(),
            Metadata.ToImmutableDictionary(),
            IsInterfaceMethod,
            UseInheritDoc,
            IsOverride,
            GenericTypeParameters?.ToImmutableList(),
            IsStatic,
            IsVirtual,
            IsAbstract,
            IsConst,
            Noexcept);
    }
    
    public override string ToString()
    {
        var returnTypeStr = ReturnType != null ? ReturnType.ToString() : "void";
        var asyncStr = IsAsync ? "async " : "";
        var paramsStr = Parameters.Count > 0 
            ? string.Join(", ", Parameters.Select(p => $"{p.Type} {p.Name.Name}"))
            : "";
        var interfaceStr = IsInterfaceMethod ? " [interface]" : "";
        return $"{asyncStr}{returnTypeStr} {Name.Name}({paramsStr}){interfaceStr}";
    }
}
