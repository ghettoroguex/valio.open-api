using System.Collections.Immutable;
using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Expressions;
using ValIO.Core.IR;
using ValIO.Core.IR.Constraints;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Generators.TypeScript.Ast.Nodes;
using ValIO.Generators.TypeScript.Ast.Nodes.Expressions;
using ValIO.Generators.TypeScript.Options;
using ValIO.Generators.TypeScript.Zod.Ast.Builders;
using static ValIO.Generators.TypeScript.Validation.ValidationAst;

namespace ValIO.Generators.TypeScript.Validation;

/// <summary>
/// Строит выражение Zod-схемы (<see cref="ExpressionNode"/>) из <see cref="IrType"/>:
/// ссылки на модель → <c>{Model}Schema</c>, union → <c>z.union([...])</c>, array → <c>z.array(...)</c> и т.д.
/// Член-схемы ссылаются по имени <c>{Model}Schema</c> и импортируются отдельно.
/// </summary>
internal sealed class ZodSchemaExpressionBuilder
{
    /// <summary>The configured UUID validation mode shared by parameter and response schemas.</summary>
    private readonly string _uuidFormat;

    /// <summary>Creates a schema builder for a configured UUID representation.</summary>
    /// <param name="uuidFormat">The UUID validation mode, or the default when absent.</param>
    public ZodSchemaExpressionBuilder(string? uuidFormat = null)
    {
        _uuidFormat = TypeScriptOptionIds.UuidFormat.Resolve(uuidFormat);
    }

    /// <summary>
    /// Builds a Zod schema expression from an <see cref="IrType"/>.
    /// </summary>
    /// <param name="type">The IR type to build a schema for.</param>
    /// <param name="coerceParams">
    /// When <see langword="true"/>, numeric strings are coerced and exact boolean strings are decoded.
    /// Response and body schemas pass <see langword="false"/>.
    /// </param>
    /// <param name="constraints">Constraints applying to the current value.</param>
    /// <param name="constraintsRef">An optional reference to exported constraint constants.</param>
    /// <returns>The schema expression preserving wrappers and composition context.</returns>
    public ExpressionNode Build(
        IrType type,
        bool coerceParams = false,
        IrPropertyConstraints? constraints = null,
        string? constraintsRef = null)
    {
        constraints ??= type.Constraints;
        if (type is IrPrimitiveType constrainedPrimitive && constrainedPrimitive.Constraints is { } local
            && constraints is not null && !ValidationConstraints.Equivalent(local, constraints))
            return Call(Member(Id("z"), "intersection"),
                BuildPrimitive(constrainedPrimitive.PrimitiveKind, coerceParams, local, constraintsRef: null),
                BuildPrimitive(constrainedPrimitive.PrimitiveKind, coerceParams, constraints, constraintsRef));
        return type switch
        {
            IrModelReference reference => Id($"{reference.ModelName}Schema"),
            IrArrayType array => Call(Member(Id("z"), "array"), Build(array.ElementType, coerceParams)),
            IrDictionaryType dictionary => Call(Member(Id("z"), "record"),
                Build(dictionary.KeyType, coerceParams), Build(dictionary.ValueType, coerceParams)),
            IrNullableType nullable => Call(Member(Build(nullable.InnerType, coerceParams, constraints, constraintsRef), "nullable")),
            IrOptionalType optional => Call(Member(Build(optional.InnerType, coerceParams, constraints, constraintsRef), "optional")),
            IrUnionType union => BuildUnion(union.Types, coerceParams, constraints, constraintsRef, union.Kind == IrUnionKind.OneOf),
            IrIntersectionType intersection => BuildIntersection(intersection.Types, coerceParams, constraints, constraintsRef),
            IrPrimitiveType primitive => BuildPrimitive(primitive.PrimitiveKind, coerceParams, constraints, constraintsRef),
            _ => Call(Member(Id("z"), "unknown"))
        };
    }

    /// <summary>
    /// Builds <c>z.object({...})</c> from a set of route/query parameters: numeric/boolean primitives are
    /// coerced (params arrive as strings), optional params get <c>.optional()</c>, OpenAPI defaults become
    /// <c>.default(...)</c>, and descriptions become <c>.describe(...)</c>.
    /// </summary>
    /// <param name="parameters">The route or query parameters.</param>
    /// <param name="constraintsRefPrefix">An optional reference to the exported parameter constraints.</param>
    /// <returns>The <c>z.object(...)</c> schema expression.</returns>
    public ExpressionNode BuildObjectFromParameters(IReadOnlyList<IrParameter> parameters, string? constraintsRefPrefix = null)
    {
        var properties = parameters
            .Select(parameter =>
            {
                var schema = Build(parameter.Type, coerceParams: true, parameter.Constraints,
                    constraintsRefPrefix == null ? null : $"{constraintsRefPrefix}.{parameter.Name}");

                if (!parameter.IsRequired)
                    schema = Call(Member(schema, "optional"));
                // `.default(x)` after `.optional()` so an undefined param is replaced by the OpenAPI default.
                if (TryDefaultLiteral(parameter.DefaultValue, out var defaultLiteral))
                    schema = Call(Member(schema, "default"), defaultLiteral!);
                if (!string.IsNullOrWhiteSpace(parameter.Description))
                    schema = Call(Member(schema, "describe"),
                        new LiteralExpressionNode(parameter.Description!, LiteralKind.String));

                return new PropertyAssignmentNode(new IdentifierNode(parameter.Name), schema);
            })
            .ToImmutableList();

        return Call(Member(Id("z"), "object"), new ObjectLiteralExpressionNode(properties, preferMultilineFormat: true));
    }

    /// <summary>Converts an OpenAPI default value to a Zod <c>.default(...)</c> literal argument.</summary>
    /// <param name="value">The default value (number/string/bool, or a <see cref="System.Text.Json"/> node).</param>
    /// <param name="literal">The resulting literal expression, or <see langword="null"/> if unrenderable.</param>
    /// <returns><see langword="true"/> when a literal could be produced.</returns>
    private static bool TryDefaultLiteral(object? value, out ExpressionNode? literal)
    {
        literal = value switch
        {
            bool b => new LiteralExpressionNode(b, LiteralKind.Boolean),
            string s => new LiteralExpressionNode(s, LiteralKind.String),
            sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal
                => new LiteralExpressionNode(value, LiteralKind.Number),
            System.Text.Json.JsonElement el => JsonElementLiteral(el),
            System.Text.Json.Nodes.JsonValue jv => JsonValueLiteral(jv),
            _ => null
        };
        return literal != null;
    }

    private static ExpressionNode? JsonElementLiteral(System.Text.Json.JsonElement el) => el.ValueKind switch
    {
        System.Text.Json.JsonValueKind.Number => new LiteralExpressionNode(el.GetRawText(), LiteralKind.Number),
        System.Text.Json.JsonValueKind.String => new LiteralExpressionNode(el.GetString(), LiteralKind.String),
        System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False
            => new LiteralExpressionNode(el.GetBoolean(), LiteralKind.Boolean),
        _ => null
    };

    private static ExpressionNode? JsonValueLiteral(System.Text.Json.Nodes.JsonValue jv) => jv.GetValueKind() switch
    {
        System.Text.Json.JsonValueKind.Number => new LiteralExpressionNode(jv.ToJsonString(), LiteralKind.Number),
        System.Text.Json.JsonValueKind.String => new LiteralExpressionNode(jv.GetValue<string>(), LiteralKind.String),
        System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False
            => new LiteralExpressionNode(jv.GetValue<bool>(), LiteralKind.Boolean),
        _ => null
    };

    /// <summary>Builds an alternative schema, collapsing degenerate unions.</summary>
    /// <param name="types">The alternative types.</param>
    /// <param name="coerce">Whether parameter input is coerced.</param>
    /// <param name="constraints">Constraints applying to compatible primitive alternatives.</param>
    /// <param name="constraintsRef">An optional reference to exported constraints.</param>
    /// <param name="exclusive">Whether exactly one alternative must match.</param>
    /// <returns>The combined schema expression.</returns>
    private ExpressionNode BuildUnion(IReadOnlyList<IrType> types, bool coerce, IrPropertyConstraints? constraints, string? constraintsRef, bool exclusive)
    {
        if (exclusive)
            return ExclusiveUnionAst.Zod(types.Select(type => Build(type, coerce, constraints, constraintsRef)).ToArray());
        return types.Count switch
        {
            0 => Call(Member(Id("z"), "unknown")),
            1 => Build(types[0], coerce, constraints, constraintsRef),
            _ => Call(Member(Id("z"), "union"), new ArrayLiteralExpressionNode(
                types.Select(type => Build(type, coerce, constraints, constraintsRef)).ToImmutableList()))
        };
    }

    /// <summary>Builds binary intersections while preserving parameter coercion through every arm.</summary>
    /// <param name="types">The intersecting types.</param>
    /// <param name="coerce">Whether parameter input is coerced.</param>
    /// <param name="constraints">Constraints applying to compatible primitive members.</param>
    /// <param name="constraintsRef">An optional reference to exported constraints.</param>
    /// <returns>The combined schema expression.</returns>
    private ExpressionNode BuildIntersection(IReadOnlyList<IrType> types, bool coerce, IrPropertyConstraints? constraints, string? constraintsRef)
    {
        if (types.Count == 0)
            return Call(Member(Id("z"), "unknown"));
        if (types.Count == 1)
            return Build(types[0], coerce, constraints, constraintsRef);

        var result = Call(Member(Id("z"), "intersection"),
            Build(types[0], coerce, constraints, constraintsRef), Build(types[1], coerce, constraints, constraintsRef));
        for (var i = 2; i < types.Count; i++)
            result = Call(Member(result, "and"), Build(types[i], coerce, constraints, constraintsRef));
        return result;
    }

    /// <summary>Builds primitives using the same format and constraint rules as model schemas.</summary>
    /// <param name="kind">The primitive IR kind.</param>
    /// <param name="coerce">Whether parameter input is coerced.</param>
    /// <param name="constraints">Constraints on the primitive value.</param>
    /// <param name="constraintsRef">An optional reference to exported constraints.</param>
    /// <returns>The primitive schema expression.</returns>
    private ExpressionNode BuildPrimitive(IrPrimitiveKind kind, bool coerce, IrPropertyConstraints? constraints, string? constraintsRef) =>
        kind switch
        {
            IrPrimitiveKind.Boolean when coerce => BuildBooleanParameter(),
            IrPrimitiveKind.Binary =>
                Call(Member(Id("z"), "instanceof"), Id("Blob")),
            IrPrimitiveKind.ByteArray =>
                Call(Member(Id("z"), "instanceof"), Id("Uint8Array")),
            IrPrimitiveKind.File =>
                Call(Member(Id("z"), "instanceof"), Id("File")),
            IrPrimitiveKind.Any => Call(Member(Id("z"), "unknown")),
            _ => ZodExpressionBuilder.BuildPrimitiveExpression(kind, useBigInt: false,
                constraints, constraintsRef, _uuidFormat, coerceNumbers: coerce).Expression
        };

    /// <summary>Parses OpenAPI boolean strings without treating the nonempty string "false" as true.</summary>
    /// <returns>A boolean schema accepting booleans and the exact strings "true" and "false".</returns>
    private static ExpressionNode BuildBooleanParameter()
    {
        var strings = Call(Member(Id("z"), "enum"), new ArrayLiteralExpressionNode(
            ImmutableList.Create<ExpressionNode>(
                new LiteralExpressionNode("true", LiteralKind.String),
                new LiteralExpressionNode("false", LiteralKind.String))));
        var transform = new ArrowFunctionExpressionNode(
            ImmutableList.Create(new ParameterDeclarationNode(new IdentifierNode("value"), type: null, isRequired: true)),
            new BinaryExpressionNode(Id("value"), BinaryOperator.Equal, new LiteralExpressionNode("true", LiteralKind.String)));
        return Call(Member(Id("z"), "union"), new ArrayLiteralExpressionNode(
            ImmutableList.Create<ExpressionNode>(
                Call(Member(Id("z"), "boolean")),
                Call(Member(strings, "transform"), transform))));
    }
}
