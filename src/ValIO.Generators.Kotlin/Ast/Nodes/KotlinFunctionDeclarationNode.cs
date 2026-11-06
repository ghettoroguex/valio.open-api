using System.Collections.Immutable;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Core.Ast.Nodes.Statements;
using ValIO.Core.Ast.Visitors;
using ValIO.Generators.Kotlin.Ast.Visitors;

namespace ValIO.Generators.Kotlin.Ast.Nodes;

/// <summary>
/// Kotlin function declaration:
///   <c>[suspend] fun name(p: T): R { body }</c>
///   <c>[suspend] fun name(p: T): R = expression</c>
/// Supports structured statement blocks and existing text-backed declarations.
/// </summary>
public sealed class KotlinFunctionDeclarationNode : DeclarationNode
{
    /// <summary>If true, emit the <c>suspend</c> modifier.</summary>
    public bool IsSuspend { get; }

    /// <summary>Parameters.</summary>
    public IReadOnlyList<KotlinParameterNode> Parameters { get; }

    /// <summary>Return type. Null = <c>Unit</c> (omit annotation).</summary>
    public TypeNode? ReturnType { get; }

    /// <summary>Body lines (pre-rendered).</summary>
    public IReadOnlyList<string> Body { get; }

    /// <summary>Gets a structured function body when the declaration uses shared statement AST nodes.</summary>
    /// <value>The body block, or null for legacy text-backed declarations.</value>
    public BlockNode? StructuredBody { get; }

    /// <summary>Gets whether this declaration implements an inherited or interface function.</summary>
    /// <value>True to emit the Kotlin override modifier.</value>
    public bool IsOverride { get; }

    /// <summary>Function-level annotations.</summary>
    public IReadOnlyList<KotlinAnnotationNode> KotlinAnnotations { get; }

    /// <summary>Visibility modifier (e.g. <c>private</c>, <c>internal</c>).</summary>
    public string? Visibility { get; }

    /// <summary>
    /// When true, emit the function as <c>fun name(...) = expression</c>
    /// (the body's first non-blank line is used as the expression).
    /// </summary>
    public bool IsExpressionBody { get; }

    /// <inheritdoc/>
    public override IReadOnlyList<AstNode> Children
    {
        get
        {
            var children = new List<AstNode> { Name };
            if (Documentation != null) children.Add(Documentation);
            children.AddRange(KotlinAnnotations);
            children.AddRange(Parameters);
            if (ReturnType != null) children.Add(ReturnType);
            if (StructuredBody != null) children.Add(StructuredBody);
            return children;
        }
    }

    /// <summary>Creates a function declaration with one explicit body representation.</summary>
    /// <param name="name">The function identifier.</param>
    /// <param name="parameters">The ordered function parameters.</param>
    /// <param name="returnType">The return type, or null to omit Unit.</param>
    /// <param name="body">Existing text-backed body lines.</param>
    /// <param name="isSuspend">Whether the function can suspend.</param>
    /// <param name="annotations">The function annotations.</param>
    /// <param name="visibility">The optional Kotlin visibility modifier.</param>
    /// <param name="documentation">The optional generated documentation.</param>
    /// <param name="isExpressionBody">Whether text-backed lines form an expression body.</param>
    /// <param name="metadata">Optional node metadata.</param>
    /// <param name="structuredBody">The structured statement block.</param>
    /// <param name="isOverride">Whether the function implements an inherited declaration.</param>
    /// <exception cref="ArgumentException">Structured and text-backed body representations are combined.</exception>
    public KotlinFunctionDeclarationNode(
        IdentifierNode name,
        IReadOnlyList<KotlinParameterNode>? parameters = null,
        TypeNode? returnType = null,
        IReadOnlyList<string>? body = null,
        bool isSuspend = false,
        IReadOnlyList<KotlinAnnotationNode>? annotations = null,
        string? visibility = null,
        CommentNode? documentation = null,
        bool isExpressionBody = false,
        IReadOnlyDictionary<string, object>? metadata = null,
        BlockNode? structuredBody = null,
        bool isOverride = false)
        : base(name, documentation, attributes: null, metadata)
    {
        IsSuspend = isSuspend;
        Parameters = parameters?.ToImmutableList() ?? ImmutableList<KotlinParameterNode>.Empty;
        ReturnType = returnType;
        Body = body?.ToImmutableList() ?? ImmutableList<string>.Empty;
        KotlinAnnotations = annotations?.ToImmutableList() ?? ImmutableList<KotlinAnnotationNode>.Empty;
        Visibility = visibility;
        IsExpressionBody = isExpressionBody;
        StructuredBody = structuredBody;
        IsOverride = isOverride;
        if (structuredBody != null && (Body.Count != 0 || isExpressionBody))
            throw new ArgumentException("A structured block body cannot be combined with legacy text or an expression body.", nameof(structuredBody));
    }

    /// <inheritdoc/>
    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor)
    {
        if (visitor is IKotlinAstVisitor<TResult> ktVisitor)
            return ktVisitor.VisitKotlinFunctionDeclaration(this);
        throw new NotSupportedException("Visitor must implement IKotlinAstVisitor<TResult> to visit KotlinFunctionDeclarationNode");
    }

    /// <inheritdoc/>
    public override AstNode Clone() =>
        new KotlinFunctionDeclarationNode(
            (IdentifierNode)Name.Clone(),
            Parameters.Select(p => (KotlinParameterNode)p.Clone()).ToImmutableList(),
            ReturnType != null ? (TypeNode)ReturnType.Clone() : null,
            Body.ToImmutableList(),
            IsSuspend,
            KotlinAnnotations.Select(a => (KotlinAnnotationNode)a.Clone()).ToImmutableList(),
            Visibility,
            Documentation != null ? (CommentNode)Documentation.Clone() : null,
            IsExpressionBody,
            Metadata.ToImmutableDictionary(),
            StructuredBody == null ? null : (BlockNode)StructuredBody.Clone(),
            IsOverride);
}
