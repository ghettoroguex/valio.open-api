using System.Collections.Immutable;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Core.Ast.Visitors;
using ValIO.Generators.TypeScript.Zod.Ast.Enums;
using ValIO.Generators.TypeScript.Zod.Ast.Visitors;

namespace ValIO.Generators.TypeScript.Zod.Ast.Nodes;

/// <summary>Represents a Zod record with typed key and value schemas.</summary>
public sealed class ZodRecordNode : ZodSchemaNode
{
    /// <summary>Gets the schema applied to each record key.</summary>
    /// <value>The schema of a record key.</value>
    public ZodSchemaNode KeySchema { get; }

    /// <summary>Gets the schema applied to each record value.</summary>
    /// <value>The schema of a record value.</value>
    public ZodSchemaNode ValueSchema { get; }

    /// <summary>Gets whether value schema evaluation is deferred to avoid cyclic imports.</summary>
    /// <value>True when the value expression must be evaluated lazily.</value>
    public bool DeferValueSchema { get; }

    /// <inheritdoc />
    public override IReadOnlyList<AstNode> Children => new AstNode[] { KeySchema, ValueSchema };

    /// <summary>Creates a record schema node.</summary>
    /// <param name="keySchema">The schema applied to record keys.</param>
    /// <param name="valueSchema">The schema applied to record values.</param>
    /// <param name="type">The optional generated type.</param>
    /// <param name="metadata">The optional node metadata.</param>
    /// <param name="deferValueSchema">Whether to evaluate the value schema lazily.</param>
    /// <exception cref="ArgumentNullException">The key or value schema is null.</exception>
    public ZodRecordNode(ZodSchemaNode keySchema, ZodSchemaNode valueSchema, TypeNode? type = null,
        IReadOnlyDictionary<string, object>? metadata = null, bool deferValueSchema = false)
        : base(ZodSchemaKind.Expression, type, metadata)
    {
        KeySchema = keySchema ?? throw new ArgumentNullException(nameof(keySchema));
        ValueSchema = valueSchema ?? throw new ArgumentNullException(nameof(valueSchema));
        DeferValueSchema = deferValueSchema;
    }

    /// <inheritdoc />
    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor) =>
        visitor is IZodAstVisitor<TResult> zodVisitor ? zodVisitor.VisitZodSchema(this) : base.Accept(visitor);

    /// <inheritdoc />
    public override AstNode Clone() => new ZodRecordNode((ZodSchemaNode)KeySchema.Clone(), (ZodSchemaNode)ValueSchema.Clone(),
        Type != null ? (TypeNode)Type.Clone() : null, Metadata.ToImmutableDictionary(), DeferValueSchema);
}
