using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Expressions;
using ValIO.Core.Ast.Nodes.Statements;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Generators.TypeScript.Ast.Nodes.Expressions;
using ValIO.Generators.TypeScript.Options;
using ValIO.Generators.TypeScript.Validation;
using static ValIO.Generators.TypeScript.Validation.ValidationAst;

namespace ValIO.Generators.TypeScript.TypeBox.Ast.Builders;

/// <summary>Registers standard string formats required by emitted TypeBox schemas.</summary>
internal static class TypeBoxFormatAst
{
    /// <summary>Wraps a string schema with idempotent registration of its standard format.</summary>
    /// <param name="format">The standard format name.</param>
    /// <param name="schema">The string schema, including local constraints.</param>
    /// <returns>The original schema after its runtime format has been registered.</returns>
    public static ExpressionNode Register(string format, ExpressionNode schema)
    {
        var predicate = StandardStringFormatAst.Build(format);
        var formatLiteral = new LiteralExpressionNode(format, LiteralKind.String);
        var registration = new IfStatementNode(new UnaryExpressionNode(UnaryOperator.LogicalNot,
            Call(Member(Id("FormatRegistry"), "Has"), formatLiteral)),
            new BlockNode([new ExpressionStatementNode(Call(Member(Id("FormatRegistry"), "Set"), formatLiteral, predicate))]));
        return new CallExpressionNode(new ArrowFunctionExpressionNode([], new BlockNode([
            registration, new ReturnStatementNode(schema)
        ])));
    }

    /// <summary>Determines whether inline schemas require a standard format registry import.</summary>
    /// <param name="type">The recursively inspected IR type.</param>
    /// <param name="uuidMode">The selected UUID validation mode.</param>
    /// <returns>True when a standard registered format is emitted locally.</returns>
    public static bool RequiresRegistry(IrType type, string uuidMode) => type switch
    {
        IrPrimitiveType { PrimitiveKind: IrPrimitiveKind.Date or IrPrimitiveKind.DateTime or IrPrimitiveKind.Time } => true,
        IrPrimitiveType { PrimitiveKind: IrPrimitiveKind.Uuid } => uuidMode == TypeScriptOptionIds.UuidFormat.Guid,
        IrArrayType array => RequiresRegistry(array.ElementType, uuidMode),
        IrDictionaryType dictionary => RequiresRegistry(dictionary.KeyType, uuidMode) || RequiresRegistry(dictionary.ValueType, uuidMode),
        IrNullableType nullable => RequiresRegistry(nullable.InnerType, uuidMode),
        IrOptionalType optional => RequiresRegistry(optional.InnerType, uuidMode),
        IrUnionType union => union.Types.Any(type => RequiresRegistry(type, uuidMode)),
        IrIntersectionType intersection => intersection.Types.Any(type => RequiresRegistry(type, uuidMode)),
        _ => false
    };

}
