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
using static ValIO.Generators.TypeScript.Validation.ValidationAst;

namespace ValIO.Generators.TypeScript.Validation;

/// <summary>
/// Строит выражение Zod-схемы (<see cref="ExpressionNode"/>) из <see cref="IrType"/>:
/// ссылки на модель → <c>{Model}Schema</c>, union → <c>z.union([...])</c>, array → <c>z.array(...)</c> и т.д.
/// Член-схемы ссылаются по имени <c>{Model}Schema</c> и импортируются отдельно.
/// </summary>
internal sealed class ZodSchemaExpressionBuilder
{
    private readonly string _uuidFormat;

    public ZodSchemaExpressionBuilder(string? uuidFormat = null)
    {
        _uuidFormat = TypeScriptOptionIds.UuidFormat.Resolve(uuidFormat);
    }

    /// <summary>
    /// Builds a Zod schema expression from an <see cref="IrType"/>.
    /// </summary>
    /// <param name="type">The IR type to build a schema for.</param>
    /// <param name="coerceParams">
    /// When <see langword="true"/>, numeric/boolean primitives use <c>z.coerce.*</c> for route/query parameters.
    /// </param>
    /// <returns>The schema expression for the supplied type.</returns>
    public ExpressionNode Build(IrType type, bool coerceParams = false) =>
        type switch
        {
            IrModelReference reference => Id($"{reference.ModelName}Schema"),
            IrArrayType array => Call(Member(Id("z"), "array"), Build(array.ElementType, coerceParams)),
            IrDictionaryType dictionary => Call(Member(Id("z"), "record"),
                Call(Member(Id("z"), "string")), Build(dictionary.ValueType, coerceParams)),
            IrNullableType nullable => Call(Member(Build(nullable.InnerType, coerceParams), "nullable")),
            IrOptionalType optional => Call(Member(Build(optional.InnerType, coerceParams), "optional")),
            IrUnionType union => Call(
                Member(Id("z"), "union"),
                new ArrayLiteralExpressionNode(union.Types.Select(t => Build(t, coerceParams)).ToImmutableList())),
            IrIntersectionType intersection => BuildIntersection(intersection.Types),
            IrPrimitiveType primitive => BuildPrimitive(primitive.PrimitiveKind, coerceParams),
            _ => Call(Member(Id("z"), "unknown"))
        };

    /// <summary>
    /// Builds <c>z.object({...})</c> from a set of route/query parameters: numeric/boolean primitives are
    /// coerced (params arrive as strings), optional params get <c>.optional()</c>, OpenAPI defaults become
    /// <c>.default(...)</c>, and descriptions become <c>.describe(...)</c>.
    /// </summary>
    /// <param name="parameters">The route or query parameters.</param>
    /// <returns>The <c>z.object(...)</c> schema expression.</returns>
    public ExpressionNode BuildObjectFromParameters(IReadOnlyList<IrParameter> parameters, string? constraintsRefPrefix = null)
    {
        var properties = parameters
            .Select(parameter =>
            {
                var schema = Build(parameter.Type, coerceParams: true);

                // Constraint chain (.min/.max/.gt/.lt/.regex) referencing the exported constants object, mirroring
                // model-property constraints. Applied to the base primitive before .optional()/.default().
                if (constraintsRefPrefix != null && parameter.Type is IrPrimitiveType prim && parameter.Constraints != null)
                    schema = ApplyConstraints(schema, prim.PrimitiveKind, parameter.Constraints, $"{constraintsRefPrefix}.{parameter.Name}");

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

    /// <summary>
    /// Applies the length/range/pattern constraint chain to a parameter's base schema, referencing the exported
    /// constants object (<c>{ref}.min</c>, <c>{ref}.maxLength</c>, …) exactly as the model-property schemas do.
    /// Pattern is emitted as an inline <c>.regex(new RegExp("…"))</c> (matching the model builder).
    /// </summary>
    /// <param name="schema">The base primitive schema expression.</param>
    /// <param name="kind">The parameter's primitive kind (selects numeric vs string rules).</param>
    /// <param name="constraints">The parameter's constraints.</param>
    /// <param name="constraintsRef">The dotted reference to the parameter's constraints entry (e.g. <c>GetTreeQueryParamsConstraints.limit</c>).</param>
    /// <returns>The schema with the constraint chain applied.</returns>
    private static ExpressionNode ApplyConstraints(ExpressionNode schema, IrPrimitiveKind kind, IrPropertyConstraints constraints, string constraintsRef)
    {
        var isNumeric = kind is IrPrimitiveKind.Number or IrPrimitiveKind.Integer or IrPrimitiveKind.Long;
        var isString = kind is IrPrimitiveKind.String or IrPrimitiveKind.Uuid or IrPrimitiveKind.DateTime
            or IrPrimitiveKind.Date or IrPrimitiveKind.Time;

        if (isNumeric)
        {
            if (constraints.Min.HasValue)
                schema = Call(Member(schema, "min"), Ref(constraintsRef, "min"));
            if (constraints.Max.HasValue)
                schema = Call(Member(schema, "max"), Ref(constraintsRef, "max"));
            if (constraints.ExclusiveMin.HasValue)
                schema = Call(Member(schema, "gt"), Ref(constraintsRef, "exclusiveMin"));
            if (constraints.ExclusiveMax.HasValue)
                schema = Call(Member(schema, "lt"), Ref(constraintsRef, "exclusiveMax"));
        }
        else if (isString)
        {
            if (constraints.MinLength.HasValue)
                schema = Call(Member(schema, "min"), Ref(constraintsRef, "minLength"));
            if (constraints.MaxLength.HasValue)
                schema = Call(Member(schema, "max"), Ref(constraintsRef, "maxLength"));
            if (!string.IsNullOrEmpty(constraints.Pattern))
            {
                var regex = new CallExpressionNode(
                    new IdentifierExpressionNode(new IdentifierNode("RegExp")),
                    ImmutableList.Create<ExpressionNode>(new LiteralExpressionNode(constraints.Pattern, LiteralKind.String)),
                    isNewExpression: true);
                schema = Call(Member(schema, "regex"), regex);
            }
        }

        return schema;
    }

    /// <summary>Builds a member reference <c>{constraintsRef}.{field}</c> (dotted path split into member accesses).</summary>
    /// <param name="constraintsRef">The dotted constraints reference (e.g. <c>GetTreeQueryParamsConstraints.limit</c>).</param>
    /// <param name="field">The rule field (e.g. <c>min</c>, <c>maxLength</c>).</param>
    /// <returns>The member-access expression.</returns>
    private static ExpressionNode Ref(string constraintsRef, string field)
    {
        var parts = constraintsRef.Split('.');
        ExpressionNode current = Id(parts[0]);
        for (var i = 1; i < parts.Length; i++)
            current = Member(current, parts[i]);
        return Member(current, field);
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

    private ExpressionNode BuildIntersection(IReadOnlyList<IrType> types)
    {
        // z.intersection(a, b); для >2 членов сворачиваем через .and(...).
        if (types.Count == 0)
            return Call(Member(Id("z"), "unknown"));
        if (types.Count == 1)
            return Build(types[0]);

        var result = Call(Member(Id("z"), "intersection"), Build(types[0]), Build(types[1]));
        for (var i = 2; i < types.Count; i++)
            result = Call(Member(result, "and"), Build(types[i]));
        return result;
    }

    private ExpressionNode BuildPrimitive(IrPrimitiveKind kind, bool coerce = false) =>
        kind switch
        {
            IrPrimitiveKind.String => Call(Member(Id("z"), "string")),
            IrPrimitiveKind.Number => ZodNumber(coerce),
            IrPrimitiveKind.Integer or IrPrimitiveKind.Long => Call(Member(ZodNumber(coerce), "int")),
            IrPrimitiveKind.Boolean => coerce
                ? Call(Member(Member(Id("z"), "coerce"), "boolean"))
                : Call(Member(Id("z"), "boolean")),
            IrPrimitiveKind.DateTime or IrPrimitiveKind.Date or IrPrimitiveKind.Time => Call(Member(Id("z"), "string")),
            IrPrimitiveKind.Uuid => _uuidFormat switch
            {
                TypeScriptOptionIds.UuidFormat.String => Call(Member(Id("z"), "string")),
                TypeScriptOptionIds.UuidFormat.Regex => Call(Member(Call(Member(Id("z"), "string")), "regex"),
                    Id(TypeScriptOptionIds.UuidFormat.RegexConstName)),
                _ => Call(Member(Call(Member(Id("z"), "string")), "uuid"))
            },
            IrPrimitiveKind.Void => Call(Member(Id("z"), "void")),
            IrPrimitiveKind.Null => Call(Member(Id("z"), "null")),
            _ => Call(Member(Id("z"), "unknown"))
        };

    /// <summary><c>z.coerce.number()</c> when <paramref name="coerce"/>, otherwise <c>z.number()</c>.</summary>
    private static ExpressionNode ZodNumber(bool coerce) =>
        coerce ? Call(Member(Member(Id("z"), "coerce"), "number")) : Call(Member(Id("z"), "number"));
}
