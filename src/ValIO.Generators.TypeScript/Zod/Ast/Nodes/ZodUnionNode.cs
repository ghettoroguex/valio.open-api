using System.Collections.Immutable;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Core.Ast.Visitors;
using ValIO.Generators.TypeScript.Zod.Ast.Enums;
using ValIO.Generators.TypeScript.Zod.Ast.Visitors;

namespace ValIO.Generators.TypeScript.Zod.Ast.Nodes;

/// <summary>
/// Узел Zod union схемы
/// </summary>
public sealed class ZodUnionNode : ZodSchemaNode
{
    /// <summary>
    /// Варианты union
    /// </summary>
    public IReadOnlyList<ZodSchemaNode> Variants { get; }

    /// <summary>
    /// Discriminator property name (sanitized). When non-null, the union renders as
    /// <c>z.discriminatedUnion("name", [...])</c> instead of <c>z.union([...])</c> — faster validation and
    /// far better error messages. Requires every variant to be an object with a literal value at this key.
    /// </summary>
    public string? Discriminator { get; }

    /// <summary>Gets whether exactly one variant must accept the original input.</summary>
    /// <value>True for OpenAPI oneOf; false for inclusive anyOf alternatives.</value>
    public bool IsExclusive { get; }

    public override IReadOnlyList<AstNode> Children => Variants.Cast<AstNode>().ToImmutableList();

    /// <summary>
    /// Creates an inclusive or exclusive union schema.
    /// </summary>
    /// <param name="variants">The ordered alternatives, preserving exclusive duplicate matches.</param>
    /// <param name="type">The optional expression type.</param>
    /// <param name="metadata">Additional AST metadata.</param>
    /// <param name="discriminator">A discriminator only when object-schema eligibility has been proven.</param>
    /// <param name="isExclusive">Whether exactly one variant must match.</param>
    /// <exception cref="ArgumentException"><paramref name="variants"/> is null or empty.</exception>
    public ZodUnionNode(
        IReadOnlyList<ZodSchemaNode> variants,
        TypeNode? type = null,
        IReadOnlyDictionary<string, object>? metadata = null,
        string? discriminator = null,
        bool isExclusive = false)
        : base(ZodSchemaKind.Union, type, metadata)
    {
        if (variants == null || variants.Count == 0)
            throw new ArgumentException("Union must have at least one variant", nameof(variants));

        Variants = variants.ToImmutableList();
        Discriminator = discriminator;
        IsExclusive = isExclusive;
    }
    
    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor)
    {
        if (visitor is IZodAstVisitor<TResult> zodVisitor)
        {
            return zodVisitor.VisitZodUnion(this);
        }
        return base.Accept(visitor);
    }
    
    /// <inheritdoc />
    public override AstNode Clone()
    {
        return new ZodUnionNode(
            Variants.Select(v => (ZodSchemaNode)v.Clone()).ToImmutableList(),
            Type != null ? (TypeNode)Type.Clone() : null,
            Metadata.ToImmutableDictionary(),
            Discriminator,
            IsExclusive);
    }
}
