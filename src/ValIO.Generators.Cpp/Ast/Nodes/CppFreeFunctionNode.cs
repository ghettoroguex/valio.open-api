using System.Collections.Immutable;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Visitors;
using ValIO.Generators.Cpp.Ast.Visitors;

namespace ValIO.Generators.Cpp.Ast.Nodes;

/// <summary>
/// Узел свободной (free) функции C++, не являющейся членом класса.
/// Используется для нативных TFuture API-методов и Parse-функций.
/// Может представлять как объявление (declaration в .h), так и определение (definition в .cpp).
/// </summary>
public sealed class CppFreeFunctionNode : DeclarationNode
{
    /// <summary>
    /// Тип возвращаемого значения
    /// </summary>
    public TypeNode ReturnType { get; }

    /// <summary>
    /// Параметры функции
    /// </summary>
    public IReadOnlyList<CppParameterNode> Parameters { get; }

    /// <summary>
    /// Тело функции (null для forward declaration в .h)
    /// </summary>
    public BlockNode? Body { get; }

    /// <summary>
    /// Является ли это только объявлением (без тела, для .h файла)
    /// </summary>
    public bool IsDeclarationOnly { get; }

    /// <summary>
    /// Является ли функция константной (const member function определения метода класса)
    /// </summary>
    public bool IsConst { get; }

    /// <summary>
    /// constexpr-функция (неявно inline — обязательна для свободных функций в общих хедерах)
    /// </summary>
    public bool IsConstexpr { get; }

    /// <summary>Whether a header-defined function uses inline linkage without requiring constexpr evaluation.</summary>
    /// <value>True for non-constexpr function definitions shared by multiple translation units.</value>
    public bool IsInline { get; }

    /// <summary>Gets structured member or delegating initializers for a constructor definition.</summary>
    /// <value>The ordered initialization expressions, empty for ordinary functions.</value>
    public IReadOnlyList<ExpressionNode> Initializers { get; }

    /// <inheritdoc />
    public override IReadOnlyList<AstNode> Children
    {
        get
        {
            var children = new List<AstNode> { Name, ReturnType };
            children.AddRange(Parameters);
            children.AddRange(Initializers);
            if (Body != null)
                children.Add(Body);
            return children;
        }
    }

    /// <summary>
    /// Creates a standalone C++ function declaration or definition.
    /// </summary>
    /// <param name="name">The optionally qualified function name.</param>
    /// <param name="returnType">The function's return type.</param>
    /// <param name="parameters">The ordered parameters.</param>
    /// <param name="body">The optional function body.</param>
    /// <param name="isDeclarationOnly">Whether the node declares a function without a definition.</param>
    /// <param name="documentation">The optional generated documentation.</param>
    /// <param name="metadata">The optional construction metadata.</param>
    /// <param name="isConst">Whether the out-of-class member definition is const-qualified.</param>
    /// <param name="isConstexpr">Whether the function permits constant evaluation.</param>
    /// <param name="isInline">Whether the definition has inline linkage.</param>
    /// <param name="initializers">The constructor's structured initialization expressions.</param>
    /// <exception cref="ArgumentNullException">The return type is missing.</exception>
    public CppFreeFunctionNode(
        IdentifierNode name,
        TypeNode returnType,
        IReadOnlyList<CppParameterNode>? parameters = null,
        BlockNode? body = null,
        bool isDeclarationOnly = false,
        CommentNode? documentation = null,
        IReadOnlyDictionary<string, object>? metadata = null,
        bool isConst = false,
        bool isConstexpr = false,
        bool isInline = false,
        IReadOnlyList<ExpressionNode>? initializers = null)
        : base(name, documentation, metadata: metadata)
    {
        ReturnType = returnType ?? throw new ArgumentNullException(nameof(returnType));
        Parameters = parameters?.ToImmutableList() ?? ImmutableList<CppParameterNode>.Empty;
        Body = body;
        IsDeclarationOnly = isDeclarationOnly;
        IsConst = isConst;
        IsConstexpr = isConstexpr;
        IsInline = isInline;
        Initializers = initializers?.ToImmutableList() ?? ImmutableList<ExpressionNode>.Empty;
    }

    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor)
    {
        if (visitor is ICppAstVisitor<TResult> cppVisitor)
            return cppVisitor.VisitCppFreeFunction(this);
        throw new NotSupportedException("Visitor must implement ICppAstVisitor<TResult> to visit CppFreeFunctionNode");
    }

    /// <inheritdoc />
    public override AstNode Clone()
    {
        return new CppFreeFunctionNode(
            (IdentifierNode)Name.Clone(),
            (TypeNode)ReturnType.Clone(),
            Parameters.Select(p => (CppParameterNode)p.Clone()).ToImmutableList(),
            Body != null ? (BlockNode)Body.Clone() : null,
            IsDeclarationOnly,
            Documentation != null ? (CommentNode)Documentation.Clone() : null,
            Metadata.ToImmutableDictionary(),
            IsConst,
            IsConstexpr,
            IsInline,
            Initializers.Select(initializer => (ExpressionNode)initializer.Clone()).ToImmutableList());
    }

    public override string ToString()
    {
        var paramsStr = string.Join(", ", Parameters.Select(p => p.ToString()));
        var suffix = IsDeclarationOnly ? ";" : " { ... }";
        return $"{ReturnType} {Name.Name}({paramsStr}){suffix}";
    }
}
