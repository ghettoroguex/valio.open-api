using System.Collections.Immutable;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Statements;
using ValIO.Core.Ast.Visitors;

namespace ValIO.Core.Ast.Nodes.Expressions;

/// <summary>
/// Узел выражения вызова функции/метода
/// </summary>
public sealed class CallExpressionNode : ExpressionNode
{
    /// <summary>
    /// Вызываемый объект/функция
    /// </summary>
    public ExpressionNode Target { get; }
    
    /// <summary>
    /// Аргументы вызова
    /// </summary>
    public IReadOnlyList<ExpressionNode> Arguments { get; }
    
    /// <summary>
    /// Является ли вызов конструктором объекта (new Type())
    /// </summary>
    public bool IsNewExpression { get; }
    
    /// <summary>
    /// Является ли вызов вызовом базового конструктора (base(...))
    /// </summary>
    public bool IsConstructorBaseCall { get; }
    
    /// <summary>
    /// Имя generic типа для вызова generic метода (например, "KeyValuePair<string, object?>")
    /// </summary>
    public string? GenericTypeName { get; }
    
    /// <summary>
    /// Generic аргумент типа для вызова generic метода (например, для Deserialize&lt;T&gt;())
    /// </summary>
    public TypeNode? GenericTypeArgument { get; }
    
    /// <summary>
    /// Является ли это лямбда-выражением (для генерации async () => ...)
    /// </summary>
    public bool IsLambda { get; }
    
    /// <summary>
    /// Тело лямбда-выражения (используется когда IsLambda = true)
    /// </summary>
    public ExpressionNode? LambdaBody { get; }
    
    /// <summary>
    /// Является ли это переменной using statement (для генерации using var x = ...)
    /// </summary>
    public bool IsUsingVariable { get; }
    
    /// <summary>
    /// Декларация переменной для using statement (используется когда IsUsingVariable = true)
    /// </summary>
    public VariableDeclarationStatementNode? VariableDeclaration { get; }
    
    
    /// <inheritdoc/>
    public override IReadOnlyList<AstNode> Children
    {
        get
        {
            var children = new List<AstNode> { Target };
            children.AddRange(Arguments);
            if (LambdaBody != null)
                children.Add(LambdaBody);
            if (VariableDeclaration != null)
                children.Add(VariableDeclaration);
            if (GenericTypeArgument != null)
                children.Add(GenericTypeArgument);
            children.AddRange(base.Children);
            return children;
        }
    }
    
    /// <summary>
    /// Конструктор
    /// </summary>
    public CallExpressionNode(
        ExpressionNode target,
        IReadOnlyList<ExpressionNode>? arguments = null,
        TypeNode? type = null,
        bool isNewExpression = false,
        bool isConstructorBaseCall = false,
        string? genericTypeName = null,
        TypeNode? genericTypeArgument = null,
        IReadOnlyDictionary<string, object>? metadata = null,
        bool isLambda = false,
        ExpressionNode? lambdaBody = null,
        bool isUsingVariable = false,
        VariableDeclarationStatementNode? variableDeclaration = null)
        : base(type, metadata)
    {
        Target = target ?? throw new ArgumentNullException(nameof(target));
        Arguments = arguments?.ToImmutableList() ?? ImmutableList<ExpressionNode>.Empty;
        IsNewExpression = isNewExpression;
        IsConstructorBaseCall = isConstructorBaseCall;
        GenericTypeName = genericTypeName;
        GenericTypeArgument = genericTypeArgument;
        IsLambda = isLambda;
        LambdaBody = lambdaBody;
        IsUsingVariable = isUsingVariable;
        VariableDeclaration = variableDeclaration;
    }
    
    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor)
    {
        return visitor.VisitCallExpression(this);
    }
    
    public override AstNode Clone()
    {
        return new CallExpressionNode(
            (ExpressionNode)Target.Clone(),
            Arguments.Select(a => (ExpressionNode)a.Clone()).ToImmutableList(),
            Type != null ? (TypeNode)Type.Clone() : null,
            IsNewExpression,
            IsConstructorBaseCall,
            GenericTypeName,
            GenericTypeArgument != null ? (TypeNode)GenericTypeArgument.Clone() : null,
            Metadata.ToImmutableDictionary(),
            IsLambda,
            LambdaBody != null ? (ExpressionNode)LambdaBody.Clone() : null,
            IsUsingVariable,
            VariableDeclaration != null ? (VariableDeclarationStatementNode)VariableDeclaration.Clone() : null);
    }
    
    public override string ToString()
    {
        var newStr = IsNewExpression ? " [new]" : "";
        var baseStr = IsConstructorBaseCall ? " [base]" : "";
        var genericStr = GenericTypeName != null ? $" <{GenericTypeName}>" : "";
        return $"CallExpression{newStr}{baseStr}{genericStr}";
    }
}
