using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Expressions;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Generators.TypeScript.Ast.Nodes.Expressions;
using ValIO.Generators.TypeScript.Options;
using static ValIO.Generators.TypeScript.Validation.ValidationAst;

namespace ValIO.Generators.TypeScript.Validation;

/// <summary>Builds shared standard string format predicates using ordinary TypeScript AST expressions.</summary>
internal static class StandardStringFormatAst
{
    /// <summary>Matches a four-digit date before checking month and leap-year validity.</summary>
    private const string DatePattern = @"^\d{4}-(?:0[1-9]|1[0-2])-(?:0[1-9]|[12]\d|3[01])$";

    /// <summary>Matches an RFC 3339 full time, including an explicit offset and optional fractional seconds.</summary>
    private const string TimePattern = @"(?:[01]\d|2[0-3]):[0-5]\d:(?:[0-5]\d|60)(?:\.\d+)?(?:[Zz]|[+-](?:[01]\d|2[0-3]):[0-5]\d)";

    /// <summary>Advances the leap-second boundary probe by one ordinary UTC day without changing the input.</summary>
    private const int MillisecondsPerDay = 86_400_000;

    /// <summary>Builds a complete standard format callback for registry-based validators.</summary>
    /// <param name="format">The standard format name.</param>
    /// <returns>The inferred string predicate callback.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The format is unsupported.</exception>
    public static ArrowFunctionExpressionNode Build(string format)
    {
        var value = Id("value");
        return Predicate(format switch
        {
            "uuid" => Matches(TypeScriptOptionIds.UuidFormat.Pattern, value),
            "date" => CalendarDate(value),
            "time" => And(Matches($"^{TimePattern}$", value), LeapSecond(value, timestamp: false)),
            "date-time" => And(Matches($"^\\d{{4}}-\\d{{2}}-\\d{{2}}[Tt]{TimePattern}$", value),
                And(CalendarDate(Call(Member(value, "slice"), Number(0), Number(10))), LeapSecond(value, timestamp: true))),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported standard string format.")
        });
    }

    /// <summary>Restricts second 60 to a possible UTC leap-second boundary, accounting for the supplied offset.</summary>
    /// <param name="value">The lexically valid RFC 3339 string.</param>
    /// <param name="timestamp">Whether a calendar date is available to check the UTC month boundary.</param>
    /// <returns>A predicate accepting ordinary seconds or a possible leap-second boundary.</returns>
    /// <remarks>Leap-second announcements are external data. This checks the UTC boundary without predicting or embedding an announcement table.</remarks>
    private static ExpressionNode LeapSecond(ExpressionNode value, bool timestamp)
    {
        var seconds = Call(Member(value, "slice"), Number(timestamp ? 17 : 6), Number(timestamp ? 19 : 8));
        var ordinary = new BinaryExpressionNode(seconds, BinaryOperator.NotEqual, new LiteralExpressionNode("60", LiteralKind.String));
        ExpressionNode normalized = Call(Member(value, "replace"), new LiteralExpressionNode(":60", LiteralKind.String), new LiteralExpressionNode(":59", LiteralKind.String));
        normalized = Call(Member(normalized, "replace"), new LiteralExpressionNode("t", LiteralKind.String), new LiteralExpressionNode("T", LiteralKind.String));
        normalized = Call(Member(normalized, "replace"), new LiteralExpressionNode("z", LiteralKind.String), new LiteralExpressionNode("Z", LiteralKind.String));
        if (!timestamp)
            normalized = new BinaryExpressionNode(new LiteralExpressionNode("2000-12-31T", LiteralKind.String), BinaryOperator.Add, normalized);
        var instant = Id("instant");
        ExpressionNode boundary = And(Equal(Call(Member(instant, "getUTCHours")), 23), Equal(Call(Member(instant, "getUTCMinutes")), 59));
        if (timestamp)
        {
            var nextDay = new CallExpressionNode(Id("Date"), [new BinaryExpressionNode(Call(Member(instant, "getTime")), BinaryOperator.Add, Number(MillisecondsPerDay))], isNewExpression: true);
            boundary = And(boundary, Equal(Call(Member(nextDay, "getUTCDate")), 1));
        }
        var callback = new ArrowFunctionExpressionNode([
            new ParameterDeclarationNode(new IdentifierNode("instant"), new SimpleTypeNode(new IdentifierNode("Date")), isRequired: true)
        ], boundary);
        var date = new CallExpressionNode(Id("Date"), [normalized], isNewExpression: true);
        return new BinaryExpressionNode(ordinary, BinaryOperator.LogicalOr, Call(callback, date));
    }

    /// <summary>Compares an expression to an integer without converting the underlying value.</summary>
    /// <param name="value">The expression being compared.</param>
    /// <param name="expected">The required integer.</param>
    /// <returns>The equality expression.</returns>
    private static ExpressionNode Equal(ExpressionNode value, int expected) => new BinaryExpressionNode(value, BinaryOperator.Equal, Number(expected));

    /// <summary>Checks lexical shape and rejects dates normalized into another calendar day.</summary>
    /// <param name="value">The string date expression.</param>
    /// <returns>A predicate that rejects invalid dates without throwing from toISOString.</returns>
    private static ExpressionNode CalendarDate(ExpressionNode value)
    {
        var validTime = new UnaryExpressionNode(UnaryOperator.LogicalNot,
            Call(Member(Id("Number"), "isNaN"), Call(Member(Id("Date"), "parse"), value)));
        var date = new CallExpressionNode(Id("Date"), [value], isNewExpression: true);
        var normalized = Call(Member(Call(Member(date, "toISOString")), "slice"), Number(0), Number(10));
        return And(Matches(DatePattern, value), And(validTime, new BinaryExpressionNode(normalized, BinaryOperator.Equal, value)));
    }

    /// <summary>Builds a callback with an inferred string parameter.</summary>
    /// <param name="body">The predicate result.</param>
    /// <returns>The callback expression.</returns>
    private static ArrowFunctionExpressionNode Predicate(ExpressionNode body) => new([
        new ParameterDeclarationNode(new IdentifierNode("value"), null, isRequired: true)
    ], body);

    /// <summary>Builds an ECMAScript regular-expression test.</summary>
    /// <param name="pattern">The anchored pattern source.</param>
    /// <param name="value">The tested string expression.</param>
    /// <returns>The regular-expression predicate.</returns>
    private static ExpressionNode Matches(string pattern, ExpressionNode value)
    {
        // ECMAScript dollar also matches before a final line terminator; formats must consume the entire string.
        var completePattern = pattern.EndsWith('$') ? pattern[..^1] + @"(?![\s\S])" : pattern;
        return Call(Member(new CallExpressionNode(Id("RegExp"), [new LiteralExpressionNode(completePattern, LiteralKind.String)], isNewExpression: true), "test"), value);
    }

    /// <summary>Combines predicates with short-circuit logical conjunction.</summary>
    /// <param name="left">The first predicate.</param>
    /// <param name="right">The second predicate.</param>
    /// <returns>The conjunction expression.</returns>
    private static ExpressionNode And(ExpressionNode left, ExpressionNode right) => new BinaryExpressionNode(left, BinaryOperator.LogicalAnd, right);

    /// <summary>Creates an integer literal.</summary>
    /// <param name="value">The integer value.</param>
    /// <returns>The literal node.</returns>
    private static LiteralExpressionNode Number(int value) => new(value, LiteralKind.Number);
}
