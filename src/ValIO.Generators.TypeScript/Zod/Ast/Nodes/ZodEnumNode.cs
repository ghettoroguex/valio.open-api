using System.Collections.Immutable;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Core.Ast.Visitors;
using ValIO.Generators.TypeScript.Zod.Ast.Enums;
using ValIO.Generators.TypeScript.Zod.Ast.Visitors;

namespace ValIO.Generators.TypeScript.Zod.Ast.Nodes;

/// <summary>
/// Узел Zod enum схемы
/// </summary>
public sealed class ZodEnumNode : ZodSchemaNode
{
    /// <summary>
    /// Значения enum
    /// </summary>
    public IReadOnlyList<ZodEnumValueNode> Values { get; }
    
    
    public override IReadOnlyList<AstNode> Children => Values.Cast<AstNode>().ToImmutableList();
    
    /// <summary>
    /// Creates an enumeration schema, including an empty enumeration represented as an impossible value.
    /// </summary>
    /// <param name="values">The allowed enum values.</param>
    /// <param name="type">The optional inferred expression type.</param>
    /// <param name="metadata">Additional AST metadata.</param>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> is null.</exception>
    public ZodEnumNode(
        IReadOnlyList<ZodEnumValueNode> values,
        TypeNode? type = null,
        IReadOnlyDictionary<string, object>? metadata = null)
        : base(ZodSchemaKind.Enum, type, metadata)
    {
        ArgumentNullException.ThrowIfNull(values);
        
        Values = values.ToImmutableList();
    }
    
    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor)
    {
        if (visitor is IZodAstVisitor<TResult> zodVisitor)
        {
            return zodVisitor.VisitZodEnum(this);
        }
        return base.Accept(visitor);
    }
    
    public override AstNode Clone()
    {
        return new ZodEnumNode(
            Values.Select(v => (ZodEnumValueNode)v.Clone()).ToImmutableList(),
            Type != null ? (TypeNode)Type.Clone() : null,
            Metadata.ToImmutableDictionary());
    }
}
