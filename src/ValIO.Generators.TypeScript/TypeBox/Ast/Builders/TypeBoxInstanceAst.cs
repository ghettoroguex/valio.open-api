using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Expressions;
using ValIO.Core.Ast.Nodes.Statements;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Generators.TypeScript.Ast.Nodes.Expressions;

namespace ValIO.Generators.TypeScript.TypeBox.Ast.Builders;

/// <summary>Builds registered TypeBox instance schemas for browser binary objects.</summary>
internal static class TypeBoxInstanceAst
{
    /// <summary>Creates a typed custom schema whose runtime kind validates the instance prototype.</summary>
    /// <param name="constructor">The browser instance constructor, such as Blob or File.</param>
    /// <returns>A registered schema expression usable by Value.Check and Value.Decode.</returns>
    public static ExpressionNode Build(string constructor)
    {
        var kind = new LiteralExpressionNode($"ValIO.{constructor}", LiteralKind.String);
        var predicate = new ArrowFunctionExpressionNode([
            new ParameterDeclarationNode(new IdentifierNode("_schema"), null, isRequired: true),
            new ParameterDeclarationNode(new IdentifierNode("value"), null, isRequired: true)
        ], Call(Member(Member(Id(constructor), "prototype"), "isPrototypeOf"), Call(Id("Object"), Id("value"))));
        var registration = Call(Member(Id("TypeRegistry"), "Set"), kind, predicate);
        var options = Call(Member(Id("Object"), "fromEntries"), new ArrayLiteralExpressionNode([
            new ArrayLiteralExpressionNode([Id("Kind"), kind])
        ]));
        var schema = new CallExpressionNode(Member(Id("Type"), "Unsafe"), [options],
            genericTypeArgument: new SimpleTypeNode(new IdentifierNode(constructor)));
        var factory = new ArrowFunctionExpressionNode([], new BlockNode([
            new ExpressionStatementNode(registration), new ReturnStatementNode(schema)
        ]));
        return Call(factory);
    }

    /// <summary>Determines whether inline schemas require the custom binary instance registry imports.</summary>
    /// <param name="type">The recursively inspected type.</param>
    /// <returns>True when Blob or File is built in the current schema.</returns>
    public static bool RequiresRegistry(IrType type) => type switch
    {
        IrPrimitiveType { PrimitiveKind: IrPrimitiveKind.Binary or IrPrimitiveKind.File } => true,
        IrArrayType array => RequiresRegistry(array.ElementType),
        IrDictionaryType dictionary => RequiresRegistry(dictionary.KeyType) || RequiresRegistry(dictionary.ValueType),
        IrNullableType nullable => RequiresRegistry(nullable.InnerType),
        IrOptionalType optional => RequiresRegistry(optional.InnerType),
        IrUnionType union => union.Types.Any(RequiresRegistry),
        IrIntersectionType intersection => intersection.Types.Any(RequiresRegistry),
        _ => false
    };

    /// <summary>Creates an identifier expression.</summary>
    /// <param name="name">The identifier name.</param>
    /// <returns>The expression node.</returns>
    private static IdentifierExpressionNode Id(string name) => new(new IdentifierNode(name));

    /// <summary>Creates a property access expression.</summary>
    /// <param name="target">The target object.</param>
    /// <param name="name">The property name.</param>
    /// <returns>The member access node.</returns>
    private static MemberAccessExpressionNode Member(ExpressionNode target, string name) => new(target, new IdentifierNode(name));

    /// <summary>Creates a call expression.</summary>
    /// <param name="target">The callable expression.</param>
    /// <param name="arguments">The call arguments.</param>
    /// <returns>The call node.</returns>
    private static CallExpressionNode Call(ExpressionNode target, params ExpressionNode[] arguments) => new(target, arguments);
}
