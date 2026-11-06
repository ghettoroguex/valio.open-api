using System.Collections.Immutable;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Core.Ast.Visitors;
using ValIO.Generators.TypeScript.Zod.Ast.Enums;
using ValIO.Generators.TypeScript.Zod.Ast.Visitors;

namespace ValIO.Generators.TypeScript.Zod.Ast.Nodes;

/// <summary>Представляет запись Zod с типизированными схемами ключей и значений.</summary>
public sealed class ZodRecordNode : ZodSchemaNode
{
    /// <summary>Возвращает схему для каждого ключа записи.</summary>
    /// <value>Схема ключа записи.</value>
    public ZodSchemaNode KeySchema { get; }

    /// <summary>Возвращает схему для каждого значения записи.</summary>
    /// <value>Схема значения записи.</value>
    public ZodSchemaNode ValueSchema { get; }

    /// <summary>Возвращает признак отложенного вычисления схемы значений для предотвращения циклических импортов.</summary>
    /// <value>True, если выражение значения нужно вычислять отложенно.</value>
    public bool DeferValueSchema { get; }

    /// <inheritdoc />
    public override IReadOnlyList<AstNode> Children => new AstNode[] { KeySchema, ValueSchema };

    /// <summary>Создаёт узел схемы записи.</summary>
    /// <param name="keySchema">Схема для ключей записи.</param>
    /// <param name="valueSchema">Схема для значений записи.</param>
    /// <param name="type">Необязательный сгенерированный тип.</param>
    /// <param name="metadata">Необязательные метаданные узла.</param>
    /// <param name="deferValueSchema">Следует ли вычислять схему значений отложенно.</param>
    /// <exception cref="ArgumentNullException">Схема ключа или значения равна null.</exception>
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
