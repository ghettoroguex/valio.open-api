using System.Collections.Immutable;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Expressions;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Core.IR.Constraints;
using ValIO.Core.IR.Enums;
using ValIO.Generators.TypeScript.Ast.Nodes;
using ValIO.Generators.TypeScript.Ast.Nodes.Expressions;
using ValIO.Generators.TypeScript.Options;
using ValIO.Generators.TypeScript.Zod.Ast.Nodes;

namespace ValIO.Generators.TypeScript.Zod.Ast.Builders;

/// <summary>
/// Строит AST выражений для Zod схем (цепочки вызовов z.string().uuid().min() и т.д.).
/// Использует CallExpressionNode, MemberAccessExpressionNode — renderer только отрисовывает код.
/// </summary>
public static class ZodExpressionBuilder
{
    /// <summary>Builds a strict model primitive schema with its format and constraints.</summary>
    /// <param name="primitiveKind">The primitive IR type.</param>
    /// <param name="useBigInt">Whether long values use the bigint type.</param>
    /// <param name="constraints">The optional property constraints.</param>
    /// <param name="constraintsRef">The optional reference to generated constraint constants.</param>
    /// <param name="uuidFormat">The configured UUID validation mode.</param>
    /// <param name="description">The optional property description.</param>
    /// <returns>The schema expression with applicable refinements.</returns>
    public static ZodExpressionSchemaNode BuildPrimitiveExpression(
        IrPrimitiveKind primitiveKind,
        bool useBigInt,
        IrPropertyConstraints? constraints = null,
        string? constraintsRef = null,
        string? uuidFormat = null,
        string? description = null)
    {
        var expr = BuildBasePrimitive(primitiveKind, useBigInt);
        expr = ApplyFormatRefinements(expr, primitiveKind, TypeScriptOptionIds.UuidFormat.Resolve(uuidFormat));
        expr = ApplyConstraints(expr, primitiveKind, constraints, constraintsRef);
        // Carry the OpenAPI property description into the schema as Zod metadata (.describe(...)).
        // Collapse whitespace to keep multiline descriptions inside a valid string literal.
        if (!string.IsNullOrWhiteSpace(description))
        {
            var singleLineDescription = System.Text.RegularExpressions.Regex.Replace(description!, @"\s+", " ").Trim();
            expr = Chain(expr, "describe",
                new[] { (ExpressionNode)new LiteralExpressionNode(singleLineDescription, ValIO.Core.Ast.Enums.LiteralKind.String) });
        }
        return new ZodExpressionSchemaNode(expr);
    }

    /// <summary>Builds a non-coercing primitive schema for model values.</summary>
    /// <param name="kind">The primitive IR kind.</param>
    /// <param name="useBigInt">Whether long values use the bigint type.</param>
    /// <returns>The base Zod expression before refinements.</returns>
    private static ExpressionNode BuildBasePrimitive(IrPrimitiveKind kind, bool useBigInt)
    {
        ExpressionNode baseCall;
        switch (kind)
        {
            case IrPrimitiveKind.String:
            case IrPrimitiveKind.DateTime:
            case IrPrimitiveKind.Date:
            case IrPrimitiveKind.Time:
            case IrPrimitiveKind.Uuid:
                baseCall = Call(MemberAccess(Identifier("z"), "string"), Array.Empty<ExpressionNode>());
                break;
            case IrPrimitiveKind.Number:
                baseCall = Call(
                    MemberAccess(Identifier("z"), "number"),
                    Array.Empty<ExpressionNode>());
                break;
            case IrPrimitiveKind.Integer:
                baseCall = Call(
                    MemberAccess(Identifier("z"), "number"),
                    Array.Empty<ExpressionNode>());
                baseCall = Chain(baseCall, "int", Array.Empty<ExpressionNode>());
                break;
            case IrPrimitiveKind.Long when useBigInt:
                baseCall = Call(
                    MemberAccess(Identifier("z"), "bigint"),
                    Array.Empty<ExpressionNode>());
                break;
            case IrPrimitiveKind.Long:
                baseCall = Call(
                    MemberAccess(Identifier("z"), "number"),
                    Array.Empty<ExpressionNode>());
                baseCall = Chain(baseCall, "int", Array.Empty<ExpressionNode>());
                break;
            case IrPrimitiveKind.Boolean:
                baseCall = Call(MemberAccess(Identifier("z"), "boolean"), Array.Empty<ExpressionNode>());
                break;
            case IrPrimitiveKind.Null:
                baseCall = Call(MemberAccess(Identifier("z"), "null"), Array.Empty<ExpressionNode>());
                break;
            case IrPrimitiveKind.Any:
            default:
                baseCall = Call(MemberAccess(Identifier("z"), "any"), Array.Empty<ExpressionNode>());
                break;
        }

        return baseCall;
    }

    /// <summary>
    /// Применяет уточнения формата: uuid, datetime, date, time
    /// </summary>
    private static ExpressionNode ApplyFormatRefinements(ExpressionNode expr, IrPrimitiveKind kind, string uuidFormat)
    {
        return kind switch
        {
            IrPrimitiveKind.Uuid => uuidFormat switch
            {
                TypeScriptOptionIds.UuidFormat.String => expr,
                TypeScriptOptionIds.UuidFormat.Regex => Chain(expr, "regex",
                    new[] { Identifier(TypeScriptOptionIds.UuidFormat.RegexConstName) }),
                _ => Chain(expr, "uuid", Array.Empty<ExpressionNode>()),
            },
            IrPrimitiveKind.DateTime => Chain(expr, "datetime", new[]
            {
                new ObjectLiteralExpressionNode(new[]
                {
                    new PropertyAssignmentNode(
                        new IdentifierNode("offset"),
                        new LiteralExpressionNode(true, ValIO.Core.Ast.Enums.LiteralKind.Boolean))
                }.ToImmutableList())
            }),
            IrPrimitiveKind.Date => Chain(expr, "date", Array.Empty<ExpressionNode>()),
            IrPrimitiveKind.Time => Chain(expr, "time", Array.Empty<ExpressionNode>()),
            _ => expr
        };
    }

    /// <summary>
    /// Применяет ограничения min, max, minLength, maxLength, regex.
    /// Использует ссылку на const объект (constraintsRef.propertyName) или литералы при отсутствии ref.
    /// </summary>
    private static ExpressionNode ApplyConstraints(
        ExpressionNode expr,
        IrPrimitiveKind kind,
        IrPropertyConstraints? constraints,
        string? constraintsRef)
    {
        if (constraints == null)
            return expr;

        var isNumeric = kind is IrPrimitiveKind.Number or IrPrimitiveKind.Integer or IrPrimitiveKind.Long;
        var isString = kind is IrPrimitiveKind.String or IrPrimitiveKind.Uuid or IrPrimitiveKind.DateTime
            or IrPrimitiveKind.Date or IrPrimitiveKind.Time;
        var useRef = !string.IsNullOrEmpty(constraintsRef);

        if (isNumeric)
        {
            if (constraints.Min.HasValue)
                expr = Chain(expr, "min", new[] { useRef ? Ref(constraintsRef!, "min") : Lit(constraints.Min.Value) });
            if (constraints.Max.HasValue)
                expr = Chain(expr, "max", new[] { useRef ? Ref(constraintsRef!, "max") : Lit(constraints.Max.Value) });
            if (constraints.ExclusiveMin.HasValue)
                expr = Chain(expr, "gt", new[] { useRef ? Ref(constraintsRef!, "exclusiveMin") : Lit(constraints.ExclusiveMin.Value) });
            if (constraints.ExclusiveMax.HasValue)
                expr = Chain(expr, "lt", new[] { useRef ? Ref(constraintsRef!, "exclusiveMax") : Lit(constraints.ExclusiveMax.Value) });
        }
        else if (isString)
        {
            if (constraints.MinLength.HasValue)
                expr = Chain(expr, "min", new[] { useRef ? Ref(constraintsRef!, "minLength") : Lit(constraints.MinLength.Value) });
            if (constraints.MaxLength.HasValue)
                expr = Chain(expr, "max", new[] { useRef ? Ref(constraintsRef!, "maxLength") : Lit(constraints.MaxLength.Value) });
            if (!string.IsNullOrEmpty(constraints.Pattern))
            {
                var patternLit = new LiteralExpressionNode(constraints.Pattern, ValIO.Core.Ast.Enums.LiteralKind.String);
                var regexExpr = new CallExpressionNode(
                    new IdentifierExpressionNode(new IdentifierNode("RegExp")),
                    new[] { patternLit }.ToImmutableList(),
                    isNewExpression: true);
                expr = Chain(expr, "regex", new[] { regexExpr });
            }
        }

        return expr;
    }

    private static ExpressionNode Lit(decimal value)
    {
        return new LiteralExpressionNode(value, ValIO.Core.Ast.Enums.LiteralKind.Number);
    }

    private static ExpressionNode Lit(int value)
    {
        return new LiteralExpressionNode(value, ValIO.Core.Ast.Enums.LiteralKind.Number);
    }

    /// <summary>
    /// Ссылка на поле объекта констант: "ConstraintsModel.prop.field" → MemberAccess(MemberAccess(Id(ConstraintsModel), prop), field)
    /// </summary>
    private static ExpressionNode Ref(string constraintsRef, string field)
    {
        var parts = constraintsRef.Split('.');
        ExpressionNode current = new IdentifierExpressionNode(new IdentifierNode(parts[0]));
        for (var i = 1; i < parts.Length; i++)
        {
            current = new MemberAccessExpressionNode(current, new IdentifierNode(parts[i]));
        }
        return new MemberAccessExpressionNode(current, new IdentifierNode(field));
    }

    /// <summary>
    /// Цепочка: expr.method(args)
    /// </summary>
    private static ExpressionNode Chain(ExpressionNode expr, string method, IReadOnlyList<ExpressionNode> args)
    {
        var member = new MemberAccessExpressionNode(expr, new IdentifierNode(method));
        return new CallExpressionNode(member, args?.ToImmutableList() ?? ImmutableList<ExpressionNode>.Empty);
    }

    private static ExpressionNode Call(ExpressionNode target, IReadOnlyList<ExpressionNode> args)
    {
        return new CallExpressionNode(
            target,
            args?.ToImmutableList() ?? ImmutableList<ExpressionNode>.Empty);
    }

    private static ExpressionNode Member(string name)
    {
        return new IdentifierExpressionNode(new IdentifierNode(name));
    }

    private static ExpressionNode Identifier(string name)
    {
        return new IdentifierExpressionNode(new IdentifierNode(name));
    }

    private static ExpressionNode MemberAccess(ExpressionNode obj, string member)
    {
        return new MemberAccessExpressionNode(obj, new IdentifierNode(member));
    }
}
