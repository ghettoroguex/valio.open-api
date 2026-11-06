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
using ValIO.Generators.TypeScript.Validation;

namespace ValIO.Generators.TypeScript.Zod.Ast.Builders;

/// <summary>
/// Builds Zod schema expressions from structured AST nodes, retaining wire formats and value constraints.
/// </summary>
public static class ZodExpressionBuilder
{
    /// <summary>
    /// Builds a primitive schema with shared format and value refinements.
    /// </summary>
    /// <param name="primitiveKind">The primitive IR kind.</param>
    /// <param name="useBigInt">Whether int64 values use JavaScript bigint.</param>
    /// <param name="constraints">Value constraints to retain in the schema.</param>
    /// <param name="constraintsRef">An optional reference to exported constraint constants.</param>
    /// <param name="uuidFormat">The configured UUID validation mode.</param>
    /// <param name="description">An optional description attached as schema metadata.</param>
    /// <param name="coerceNumbers">Whether numeric input may be coerced to its runtime representation.</param>
    /// <returns>The complete schema expression.</returns>
    public static ZodExpressionSchemaNode BuildPrimitiveExpression(
        IrPrimitiveKind primitiveKind,
        bool useBigInt,
        IrPropertyConstraints? constraints = null,
        string? constraintsRef = null,
        string? uuidFormat = null,
        string? description = null,
        bool coerceNumbers = false)
    {
        var expr = BuildBasePrimitive(primitiveKind, useBigInt, coerceNumbers);
        expr = ApplyConstraints(expr, primitiveKind, constraints, constraintsRef);
        expr = ApplyFormatRefinements(expr, primitiveKind, TypeScriptOptionIds.UuidFormat.Resolve(uuidFormat));
        // Carry the OpenAPI property description into the schema as Zod metadata (.describe(...)).
        if (!string.IsNullOrWhiteSpace(description))
        {
            var singleLineDescription = System.Text.RegularExpressions.Regex.Replace(description!, @"\s+", " ").Trim();
            expr = Chain(expr, "describe",
                new[] { (ExpressionNode)new LiteralExpressionNode(singleLineDescription, ValIO.Core.Ast.Enums.LiteralKind.String) });
        }
        return new ZodExpressionSchemaNode(expr);
    }

    /// <summary>
    /// Builds the primitive schema before format and constraint refinements.
    /// </summary>
    /// <param name="kind">The primitive IR kind.</param>
    /// <param name="useBigInt">Whether int64 values use bigint.</param>
    /// <param name="coerceNumbers">Whether numeric input is coerced.</param>
    /// <returns>The base schema expression.</returns>
    private static ExpressionNode BuildBasePrimitive(IrPrimitiveKind kind, bool useBigInt, bool coerceNumbers)
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
                baseCall = BuildNumber("number", coerceNumbers);
                break;
            case IrPrimitiveKind.Integer:
                baseCall = BuildNumber("number", coerceNumbers);
                baseCall = Chain(baseCall, "int", Array.Empty<ExpressionNode>());
                break;
            case IrPrimitiveKind.Long when useBigInt:
                baseCall = BuildNumber("bigint", coerceNumbers);
                break;
            case IrPrimitiveKind.Long:
                baseCall = Chain(BuildNumber("number", coerceNumbers), "int", Array.Empty<ExpressionNode>());
                break;
            case IrPrimitiveKind.Boolean:
                baseCall = Call(MemberAccess(Identifier("z"), "boolean"), Array.Empty<ExpressionNode>());
                break;
            case IrPrimitiveKind.Null:
                baseCall = Call(MemberAccess(Identifier("z"), "null"), Array.Empty<ExpressionNode>());
                break;
            case IrPrimitiveKind.Void:
                baseCall = Call(MemberAccess(Identifier("z"), "void"), Array.Empty<ExpressionNode>());
                break;
            case IrPrimitiveKind.Any:
            default:
                baseCall = Call(MemberAccess(Identifier("z"), "any"), Array.Empty<ExpressionNode>());
                break;
        }

        return baseCall;
    }

    /// <summary>Builds a numeric schema with coercion confined to the requested input boundary.</summary>
    /// <param name="method">The Zod numeric factory.</param>
    /// <param name="coerce">Whether coercion is enabled.</param>
    /// <returns>The numeric schema expression.</returns>
    private static ExpressionNode BuildNumber(string method, bool coerce)
    {
        var target = coerce ? MemberAccess(Identifier("z"), "coerce") : Identifier("z");
        return Call(MemberAccess(target, method), Array.Empty<ExpressionNode>());
    }

    /// <summary>Applies the shared wire-format predicates after string constraints, preserving Zod 3 effects compatibility.</summary>
    /// <param name="expr">The constrained primitive schema.</param>
    /// <param name="kind">The primitive kind.</param>
    /// <param name="uuidFormat">The normalized UUID policy.</param>
    /// <returns>The schema with consistent format validation across validator libraries.</returns>
    private static ExpressionNode ApplyFormatRefinements(ExpressionNode expr, IrPrimitiveKind kind, string uuidFormat)
    {
        return kind switch
        {
            IrPrimitiveKind.Uuid => uuidFormat switch
            {
                TypeScriptOptionIds.UuidFormat.String => expr,
                TypeScriptOptionIds.UuidFormat.Regex => Chain(expr, "regex",
                    new[] { Identifier(TypeScriptOptionIds.UuidFormat.RegexConstName) }),
                _ => Chain(expr, "refine", [StandardStringFormatAst.Build("uuid")]),
            },
            IrPrimitiveKind.DateTime => Chain(expr, "refine", [StandardStringFormatAst.Build("date-time")]),
            IrPrimitiveKind.Date => Chain(expr, "refine", [StandardStringFormatAst.Build("date")]),
            IrPrimitiveKind.Time => Chain(expr, "refine", [StandardStringFormatAst.Build("time")]),
            _ => expr
        };
    }

    /// <summary>Applies numeric or string constraints before effects wrap a primitive schema.</summary>
    /// <param name="expr">The primitive schema expression.</param>
    /// <param name="kind">The primitive kind deciding compatible constraints.</param>
    /// <param name="constraints">The value constraints, when present.</param>
    /// <param name="constraintsRef">An optional generated constants object reference.</param>
    /// <returns>The constrained schema expression.</returns>
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

    /// <summary>Creates a decimal literal without converting it through a binary floating-point representation.</summary>
    /// <param name="value">The exact numeric value.</param>
    /// <returns>The literal expression.</returns>
    private static ExpressionNode Lit(decimal value)
    {
        return new LiteralExpressionNode(value, ValIO.Core.Ast.Enums.LiteralKind.Number);
    }

    /// <summary>Creates an integer constraint literal.</summary>
    /// <param name="value">The exact integer value.</param>
    /// <returns>The literal expression.</returns>
    private static ExpressionNode Lit(int value)
    {
        return new LiteralExpressionNode(value, ValIO.Core.Ast.Enums.LiteralKind.Number);
    }

    /// <summary>Creates structured member access to a generated constraint constant.</summary>
    /// <param name="constraintsRef">The dotted owning object path.</param>
    /// <param name="field">The constraint field name.</param>
    /// <returns>The member-access expression.</returns>
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

    /// <summary>Appends an ordinary method call to a schema expression.</summary>
    /// <param name="expr">The owning schema expression.</param>
    /// <param name="method">The method name.</param>
    /// <param name="args">The call arguments.</param>
    /// <returns>The method-call expression.</returns>
    private static ExpressionNode Chain(ExpressionNode expr, string method, IReadOnlyList<ExpressionNode> args)
    {
        var member = new MemberAccessExpressionNode(expr, new IdentifierNode(method));
        return new CallExpressionNode(member, args?.ToImmutableList() ?? ImmutableList<ExpressionNode>.Empty);
    }

    /// <summary>Creates a structured call to a schema factory.</summary>
    /// <param name="target">The called factory expression.</param>
    /// <param name="args">The call arguments.</param>
    /// <returns>The call expression.</returns>
    private static ExpressionNode Call(ExpressionNode target, IReadOnlyList<ExpressionNode> args)
    {
        return new CallExpressionNode(
            target,
            args?.ToImmutableList() ?? ImmutableList<ExpressionNode>.Empty);
    }

    /// <summary>Creates a schema namespace or constant identifier.</summary>
    /// <param name="name">The identifier name.</param>
    /// <returns>The identifier expression.</returns>
    private static ExpressionNode Identifier(string name)
    {
        return new IdentifierExpressionNode(new IdentifierNode(name));
    }

    /// <summary>Creates member access without embedding source code into an identifier.</summary>
    /// <param name="obj">The owning expression.</param>
    /// <param name="member">The member name.</param>
    /// <returns>The member-access expression.</returns>
    private static ExpressionNode MemberAccess(ExpressionNode obj, string member)
    {
        return new MemberAccessExpressionNode(obj, new IdentifierNode(member));
    }
}
