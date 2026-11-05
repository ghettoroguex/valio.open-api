using System.Collections.Immutable;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Core.Ast.Visitors;
using ValIO.Generators.TypeScript.Zod.Ast.Enums;
using ValIO.Generators.TypeScript.Zod.Ast.Visitors;

namespace ValIO.Generators.TypeScript.Zod.Ast.Nodes;

/// <summary>Represents a string-keyed Zod record with a typed value schema.</summary>
public sealed class ZodRecordNode : ZodSchemaNode
{
    /// <summary>Gets the schema applied to each record value.</summary>
    /// <value>The schema of a record value.</value>
    public ZodSchemaNode ValueSchema { get; }

    /// <summary>Gets whether value schema evaluation is deferred to avoid cyclic imports.</summary>
    /// <value>True when the value expression must be evaluated lazily.</value>
    public bool DeferValueSchema { get; }

    /// <inheritdoc />
    public override IReadOnlyList<AstNode> Children => new AstNode[] { ValueSchema };

    /// <summary>Creates a record schema node.</summary>
    /// <param name="valueSchema">The schema applied to record values.</param>
    /// <param name="type">The optional generated type.</param>
    /// <param name="metadata">The optional node metadata.</param>
    /// <param name="deferValueSchema">Whether to evaluate the value schema lazily.</param>
    /// <exception cref="ArgumentNullException">The value schema is null.</exception>
    public ZodRecordNode(ZodSchemaNode valueSchema, TypeNode? type = null,
        IReadOnlyDictionary<string, object>? metadata = null, bool deferValueSchema = false)
        : base(ZodSchemaKind.Expression, type, metadata)
    {
        ValueSchema = valueSchema ?? throw new ArgumentNullException(nameof(valueSchema));
        DeferValueSchema = deferValueSchema;
    }

    /// <inheritdoc />
    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor) =>
        visitor is IZodAstVisitor<TResult> zodVisitor ? zodVisitor.VisitZodSchema(this) : base.Accept(visitor);

    /// <inheritdoc />
    public override AstNode Clone() => new ZodRecordNode((ZodSchemaNode)ValueSchema.Clone(),
        Type != null ? (TypeNode)Type.Clone() : null, Metadata.ToImmutableDictionary(), DeferValueSchema);
}
