using System.Collections.Immutable;
using System.Globalization;
using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Expressions;
using ValIO.Core.IR;
using ValIO.Core.IR.Constraints;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Core.Utilities;
using ValIO.Generators.TypeScript.Ast.Nodes;
using ValIO.Generators.TypeScript.Ast.Nodes.Expressions;
using ValIO.Generators.TypeScript.Options;
using ValIO.Generators.TypeScript.Validation;

namespace ValIO.Generators.TypeScript.Valibot.Ast.Builders;

/// <summary>
/// Builds Valibot schema expression AST from IR types.
/// </summary>
public sealed class ValibotAstBuilder
{
    private readonly string _uuidFormat;

    public ValibotAstBuilder(string? uuidFormat = null)
    {
        _uuidFormat = TypeScriptOptionIds.UuidFormat.Resolve(uuidFormat);
    }

    /// <summary>Builds a model schema with sibling properties applied to every composition alternative.</summary>
    /// <param name="model">The model being validated.</param>
    /// <returns>The complete Valibot schema expression.</returns>
    public ExpressionNode BuildSchemaFromIrModel(IrModel model)
    {
        if (model.Composition != null && model.Composition.Types.Count > 0)
        {
            return model.Composition.Kind switch
            {
                IrCompositionKind.OneOf or IrCompositionKind.AnyOf => BuildModelUnion(model),
                IrCompositionKind.AllOf => BuildModelIntersection(model),
                _ => BuildObject(model)
            };
        }

        return BuildObject(model);
    }

    /// <summary>Intersects sibling object constraints with a model's alternatives.</summary>
    /// <param name="model">The composed model.</param>
    /// <returns>A union schema, intersected with sibling properties when present.</returns>
    private ExpressionNode BuildModelUnion(IrModel model)
    {
        var alternatives = BuildUnion(model.Composition!.Types, model.Name,
            exclusive: model.Composition.Kind == IrCompositionKind.OneOf);
        return model.Properties.Count == 0 ? alternatives : BuildIntersection([alternatives, BuildObject(model)]);
    }

    public ExpressionNode BuildEnumSchema(IrEnum enumModel)
    {
        return Call("picklist", Id($"{enumModel.Name}Values"));
    }

    /// <summary>Builds a schema while preserving constraints across nullable and optional wrappers.</summary>
    /// <param name="irType">The IR type being validated.</param>
    /// <param name="currentModelName">The containing model for recursive references.</param>
    /// <param name="constraints">Constraints applying to the current value rather than collection elements.</param>
    /// <returns>The Valibot schema expression.</returns>
    public ExpressionNode BuildSchemaFromIrType(
        IrType irType,
        string? currentModelName = null,
        IrPropertyConstraints? constraints = null)
    {
        constraints ??= irType.Constraints;
        if (irType is IrPrimitiveType constrainedPrimitive && constrainedPrimitive.Constraints is { } local
            && constraints is not null && !ValidationConstraints.Equivalent(local, constraints))
            return BuildIntersection([
                BuildPrimitive(constrainedPrimitive.PrimitiveKind, local),
                BuildPrimitive(constrainedPrimitive.PrimitiveKind, constraints)
            ]);
        return irType switch
        {
            IrPrimitiveType primitive => BuildPrimitive(primitive.PrimitiveKind, constraints),
            IrModelReference reference => BuildModelReference(reference, currentModelName),
            IrArrayType array => Call("array", BuildSchemaFromIrType(array.ElementType, currentModelName)),
            IrDictionaryType dictionary => Call(
                "record",
                BuildSchemaFromIrType(dictionary.KeyType, currentModelName),
                BuildSchemaFromIrType(dictionary.ValueType, currentModelName)),
            IrUnionType union => BuildUnion(union.Types, currentModelName, constraints, exclusive: union.Kind == IrUnionKind.OneOf),
            IrIntersectionType intersection => BuildIntersection(intersection.Types, currentModelName, constraints),
            IrNullableType nullable => Call("nullable", BuildSchemaFromIrType(nullable.InnerType, currentModelName, constraints)),
            IrOptionalType optional => Call("optional", BuildSchemaFromIrType(optional.InnerType, currentModelName, constraints)),
            _ => Call("unknown")
        };
    }

    private ExpressionNode BuildObject(IrModel model)
    {
        var properties = model.Properties
            .Select(property => new PropertyAssignmentNode(
                new IdentifierNode(property.Name),
                BuildPropertySchema(property, model.Name)))
            .ToImmutableList();

        return Call("object", new ObjectLiteralExpressionNode(properties, preferMultilineFormat: true));
    }

    /// <summary>
    /// Builds an object schema with parameter constraints and independent optionality and nullability.
    /// </summary>
    /// <param name="parameters">The operation parameters to validate.</param>
    /// <returns>The Valibot object expression.</returns>
    public ExpressionNode BuildObjectFromParameters(IReadOnlyList<IrParameter> parameters)
    {
        var properties = parameters
            .Select(parameter =>
            {
                var schema = BuildSchemaFromIrType(parameter.Type, constraints: parameter.Constraints);
                return new PropertyAssignmentNode(
                    new IdentifierNode(parameter.Name),
                    parameter.IsRequired ? schema : Call("optional", schema));
            })
            .ToImmutableList();

        return Call("object", new ObjectLiteralExpressionNode(properties, preferMultilineFormat: true));
    }

    /// <summary>Builds a property schema without conflating missing keys with explicit null values.</summary>
    /// <param name="property">The property and its validation constraints.</param>
    /// <param name="currentModelName">The containing model for recursive references.</param>
    /// <returns>The schema with an optional wrapper only when the property is not required.</returns>
    private ExpressionNode BuildPropertySchema(IrProperty property, string currentModelName)
    {
        var schema = BuildSchemaFromIrType(property.Type, currentModelName, property.Constraints);
        return property.IsRequired ? schema : Call("optional", schema);
    }

    private ExpressionNode BuildModelIntersection(IrModel model)
    {
        var schemas = model.Composition!.Types
            .Select(type => BuildSchemaFromIrType(type, model.Name))
            .ToList();

        if (model.Properties.Count > 0)
            schemas.Add(BuildObject(model));

        return BuildIntersection(schemas);
    }

    /// <summary>Combines alternatives without losing value constraints.</summary>
    /// <param name="types">The alternative types.</param>
    /// <param name="currentModelName">The containing model for recursive references.</param>
    /// <param name="constraints">Constraints applied to compatible primitive alternatives.</param>
    /// <param name="exclusive">Whether exactly one alternative must match.</param>
    /// <returns>The union schema, collapsing a single alternative.</returns>
    private ExpressionNode BuildUnion(IReadOnlyList<IrType> types, string? currentModelName, IrPropertyConstraints? constraints = null, bool exclusive = false)
    {
        var schemas = types
            .Select(type => BuildSchemaFromIrType(type, currentModelName, constraints))
            .ToImmutableList();

        if (exclusive)
            return ExclusiveUnionAst.Valibot(schemas);
        return schemas.Count switch
        {
            0 => Call("unknown"),
            1 => schemas[0],
            _ => Call("union", new ArrayLiteralExpressionNode(schemas))
        };
    }

    /// <summary>Combines intersecting types while retaining primitive constraints.</summary>
    /// <param name="types">The participating types.</param>
    /// <param name="currentModelName">The containing model for recursive references.</param>
    /// <param name="constraints">Constraints applied to compatible primitive members.</param>
    /// <returns>The intersection expression.</returns>
    private ExpressionNode BuildIntersection(IReadOnlyList<IrType> types, string? currentModelName, IrPropertyConstraints? constraints = null)
    {
        var schemas = types
            .Select(type => BuildSchemaFromIrType(type, currentModelName, constraints))
            .ToList();

        return BuildIntersection(schemas);
    }

    private static ExpressionNode BuildIntersection(IReadOnlyList<ExpressionNode> schemas)
    {
        return schemas.Count switch
        {
            0 => Call("unknown"),
            1 => schemas[0],
            _ => Call("intersect", new ArrayLiteralExpressionNode(schemas.ToImmutableList()))
        };
    }

    private static ExpressionNode BuildModelReference(IrModelReference reference, string? currentModelName)
    {
        var schemaName = $"{reference.ModelName}Schema";
        var isSelfReference = !string.IsNullOrEmpty(currentModelName)
            && string.Equals(reference.ModelName, currentModelName, StringComparison.OrdinalIgnoreCase);

        return isSelfReference
            ? Call("lazy", new ArrowFunctionExpressionNode(ImmutableList<ParameterDeclarationNode>.Empty, Id(schemaName)))
            : Id(schemaName);
    }

    /// <summary>Builds a strict primitive schema matching the generated runtime representation.</summary>
    /// <param name="kind">The primitive IR kind.</param>
    /// <param name="constraints">The primitive's value constraints.</param>
    /// <returns>The constrained primitive schema.</returns>
    private ExpressionNode BuildPrimitive(IrPrimitiveKind kind, IrPropertyConstraints? constraints = null)
    {
        var baseSchema = kind switch
        {
            IrPrimitiveKind.String => Call("string"),
            IrPrimitiveKind.Number => Call("number"),
            IrPrimitiveKind.Integer => Pipe(Call("number"), Call("integer")),
            IrPrimitiveKind.Long => Pipe(Call("number"), Call("integer")),
            IrPrimitiveKind.Boolean => Call("boolean"),
            IrPrimitiveKind.DateTime => Pipe(Call("string"),
                Call("check", StandardStringFormatAst.Build("date-time"))),
            IrPrimitiveKind.Date => Pipe(Call("string"), Call("check", StandardStringFormatAst.Build("date"))),
            IrPrimitiveKind.Time => Pipe(Call("string"), Call("check", StandardStringFormatAst.Build("time"))),
            IrPrimitiveKind.Uuid => BuildUuidSchema(),
            IrPrimitiveKind.Any => Call("unknown"),
            IrPrimitiveKind.Void => Call("undefined"),
            IrPrimitiveKind.Null => Call("null"),
            IrPrimitiveKind.Binary => Call("instance", Id("Blob")),
            IrPrimitiveKind.ByteArray => Call("instance", Id("Uint8Array")),
            IrPrimitiveKind.File => Call("instance", Id("File")),
            _ => Call("unknown")
        };

        return ApplyConstraints(baseSchema, kind, constraints);
    }

    /// <summary>
    /// UUID schema per configured mode: guid uses the shared canonical text predicate; string uses v.string();
    /// regex → v.pipe(v.string(), v.regex(UUID_REGEX)).
    /// </summary>
    /// <returns>The UUID schema following the selected validation policy.</returns>
    private ExpressionNode BuildUuidSchema()
    {
        return _uuidFormat switch
        {
            TypeScriptOptionIds.UuidFormat.String => Call("string"),
            TypeScriptOptionIds.UuidFormat.Regex => Pipe(Call("string"), Call("regex", Id(TypeScriptOptionIds.UuidFormat.RegexConstName))),
            _ => Pipe(Call("string"), Call("check", StandardStringFormatAst.Build("uuid"))),
        };
    }

    private static ExpressionNode ApplyConstraints(ExpressionNode schema, IrPrimitiveKind kind, IrPropertyConstraints? constraints)
    {
        if (constraints == null)
            return schema;

        var actions = new List<ExpressionNode>();
        var isNumeric = kind is IrPrimitiveKind.Number or IrPrimitiveKind.Integer or IrPrimitiveKind.Long;
        var isString = kind is IrPrimitiveKind.String or IrPrimitiveKind.Uuid or IrPrimitiveKind.DateTime
            or IrPrimitiveKind.Date or IrPrimitiveKind.Time;

        if (isNumeric)
        {
            if (constraints.Min.HasValue)
                actions.Add(Call("minValue", Number(constraints.Min.Value)));
            if (constraints.Max.HasValue)
                actions.Add(Call("maxValue", Number(constraints.Max.Value)));
            if (constraints.ExclusiveMin.HasValue)
                actions.Add(Call("gtValue", Number(constraints.ExclusiveMin.Value)));
            if (constraints.ExclusiveMax.HasValue)
                actions.Add(Call("ltValue", Number(constraints.ExclusiveMax.Value)));
        }
        else if (isString)
        {
            if (constraints.MinLength.HasValue)
                actions.Add(Call("minLength", Number(constraints.MinLength.Value)));
            if (constraints.MaxLength.HasValue)
                actions.Add(Call("maxLength", Number(constraints.MaxLength.Value)));
            if (!string.IsNullOrEmpty(constraints.Pattern))
            {
                var regex = new CallExpressionNode(
                    Id("RegExp"),
                    new[] { StringLiteral(constraints.Pattern) }.ToImmutableList(),
                    isNewExpression: true);
                actions.Add(Call("regex", regex));
            }
        }

        return actions.Count == 0
            ? schema
            : Pipe([schema, .. actions]);
    }

    private static ExpressionNode Pipe(params ExpressionNode[] expressions)
    {
        return expressions.Length == 1
            ? expressions[0]
            : Call("pipe", expressions);
    }

    private static CallExpressionNode Call(string methodName, params ExpressionNode[] arguments)
    {
        return new CallExpressionNode(
            new MemberAccessExpressionNode(Id("v"), new IdentifierNode(methodName)),
            arguments.ToImmutableList());
    }

    private static IdentifierExpressionNode Id(string name)
        => new(new IdentifierNode(name));

    private static LiteralExpressionNode StringLiteral(string value)
        => new(value, LiteralKind.String);

    private static LiteralExpressionNode Number(int value)
        => new(value, LiteralKind.Number);

    private static LiteralExpressionNode Number(decimal value)
        => new(value.ToString(CultureInfo.InvariantCulture), LiteralKind.Number);
}
