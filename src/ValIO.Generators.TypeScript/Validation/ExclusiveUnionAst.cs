using System.Collections.Immutable;
using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Expressions;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Generators.TypeScript.Ast.Nodes.Expressions;
using static ValIO.Generators.TypeScript.Validation.ValidationAst;

namespace ValIO.Generators.TypeScript.Validation;

/// <summary>Builds exact-one schema validation without losing the original input to union transformations.</summary>
internal static class ExclusiveUnionAst
{
    /// <summary>Checks exactly one Zod arm before parsing through the union.</summary>
    /// <param name="schemas">The ordered alternatives; duplicate arms must remain observable.</param>
    /// <returns>A schema accepting exactly one matching alternative.</returns>
    public static ExpressionNode Zod(IReadOnlyList<ExpressionNode> schemas)
    {
        if (schemas.Count == 0)
            return Call(Member(Id("z"), "never"));
        if (schemas.Count == 1)
            return schemas[0];
        var predicate = MatchCountPredicate(schemas, Member(Call(Member(Id("schema"), "safeParse"), Id("value")), "success"));
        // Custom predicates abort on failure in both Zod majors, including early Zod 4 pipe implementations.
        var checkedInput = new CallExpressionNode(Member(Id("z"), "custom"), [predicate],
            genericTypeArgument: new SimpleTypeNode(new IdentifierNode("unknown"), isPrimitive: true));
        var union = Call(Member(Id("z"), "union"), new ArrayLiteralExpressionNode(schemas));
        return Call(Member(checkedInput, "pipe"), union);
    }

    /// <summary>Checks exactly one Valibot arm before object schemas can strip unknown keys.</summary>
    /// <param name="schemas">The ordered alternatives, preserving duplicate matches.</param>
    /// <returns>A schema accepting exactly one matching alternative.</returns>
    public static ExpressionNode Valibot(IReadOnlyList<ExpressionNode> schemas)
    {
        if (schemas.Count == 0)
            return Call(Member(Id("v"), "never"));
        if (schemas.Count == 1)
            return schemas[0];
        var predicate = MatchCountPredicate(schemas, Call(Member(Id("v"), "is"), Id("schema"), Id("value")));
        return Call(Member(Id("v"), "pipe"),
            Call(Member(Id("v"), "unknown")),
            Call(Member(Id("v"), "check"), predicate),
            Call(Member(Id("v"), "union"), new ArrayLiteralExpressionNode(schemas)));
    }

    /// <summary>Expresses exact-one using supported TypeBox unions, intersections, and negation.</summary>
    /// <param name="schemas">The ordered alternatives, preserving duplicate matches.</param>
    /// <returns>A union excluding every pairwise overlap.</returns>
    /// <remarks>Any value matching two or more alternatives matches a pairwise intersection and is rejected.</remarks>
    public static ExpressionNode TypeBox(IReadOnlyList<ExpressionNode> schemas)
    {
        if (schemas.Count == 0)
            return Call(Member(Id("Type"), "Never"));
        if (schemas.Count == 1)
            return schemas[0];
        var overlaps = new List<ExpressionNode>();
        for (var left = 0; left < schemas.Count; left++)
        {
            for (var right = left + 1; right < schemas.Count; right++)
            {
                overlaps.Add(Call(Member(Id("Type"), "Intersect"), new ArrayLiteralExpressionNode([schemas[left], schemas[right]])));
            }
        }
        var anyOverlap = overlaps.Count == 1 ? overlaps[0]
            : Call(Member(Id("Type"), "Union"), new ArrayLiteralExpressionNode(overlaps));
        return Call(Member(Id("Type"), "Intersect"), new ArrayLiteralExpressionNode([
            Call(Member(Id("Type"), "Union"), new ArrayLiteralExpressionNode(schemas)),
            Call(Member(Id("Type"), "Not"), anyOverlap)
        ]));
    }

    /// <summary>Builds a predicate counting successful validations against the unmodified input.</summary>
    /// <param name="schemas">The schema expressions to test.</param>
    /// <param name="match">The expression deciding whether the current schema accepts the current value.</param>
    /// <returns>The exact-one predicate.</returns>
    private static ArrowFunctionExpressionNode MatchCountPredicate(IReadOnlyList<ExpressionNode> schemas, ExpressionNode match)
    {
        var filter = Call(Member(new ArrayLiteralExpressionNode(schemas), "filter"),
            new ArrowFunctionExpressionNode([Parameter("schema")], match));
        return new ArrowFunctionExpressionNode([Parameter("value")],
            new BinaryExpressionNode(Member(filter, "length"), BinaryOperator.Equal, new LiteralExpressionNode(1, LiteralKind.Number)));
    }

    /// <summary>Creates an inferred callback parameter.</summary>
    /// <param name="name">The callback parameter name.</param>
    /// <returns>The parameter declaration.</returns>
    private static ParameterDeclarationNode Parameter(string name) => new(new IdentifierNode(name), type: null, isRequired: true);
}
