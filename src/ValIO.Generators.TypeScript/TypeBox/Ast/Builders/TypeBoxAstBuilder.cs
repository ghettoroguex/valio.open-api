using System.Collections.Immutable;
using System.Globalization;
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
using ValIO.Generators.TypeScript.Validation;

namespace ValIO.Generators.TypeScript.TypeBox.Ast.Builders;

/// <summary>
/// Builds TypeBox schema expression AST from IR types.
/// </summary>
public sealed class TypeBoxAstBuilder
{
    private readonly string _uuidFormat;

    public TypeBoxAstBuilder(string? uuidFormat = null)
    {
        _uuidFormat = TypeScriptOptionIds.UuidFormat.Resolve(uuidFormat);
    }

    /// <summary>Builds a model schema, applying sibling properties to every union or intersection arm.</summary>
    /// <param name="model">The model being validated.</param>
    /// <returns>The complete TypeBox schema, wrapped recursively when necessary.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is null.</exception>
    public ExpressionNode BuildSchemaFromIrModel(IrModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        ExpressionNode body;
        if (model.Composition != null && model.Composition.Types.Count > 0)
        {
            var expressions = model.Composition.Types
                .Select(type => BuildSchemaFromIrType(type, model.Name))
                .ToList();

            body = model.Composition.Kind switch
            {
                IrCompositionKind.AllOf => BuildIntersection(expressions),
                IrCompositionKind.OneOf => ExclusiveUnionAst.TypeBox(expressions),
                IrCompositionKind.AnyOf => BuildUnion(expressions),
                _ => BuildObjectSchema(model)
            };

            if (model.Properties.Count > 0)
                body = BuildIntersection([body, BuildObjectSchema(model)]);
        }
        else
        {
            body = BuildObjectSchema(model);
        }

        // Type.Recursive binds self references to the callback's This schema.
        if (IsSelfRecursive(model))
        {
            var arrow = new ArrowFunctionExpressionNode(
                ImmutableList.Create(
                    new ParameterDeclarationNode(new IdentifierNode("This"), type: null, isRequired: true)),
                body);
            return Call("Recursive", arrow);
        }
        return body;
    }

    private static bool IsSelfRecursive(IrModel model)
    {
        var seen = new HashSet<IrType>();
        foreach (var property in model.Properties)
        {
            if (ReferencesSelf(property.Type, model.Name, seen))
                return true;
        }
        if (model.Composition != null)
        {
            foreach (var type in model.Composition.Types)
            {
                if (ReferencesSelf(type, model.Name, seen))
                    return true;
            }
        }
        return false;
    }

    private static bool IsRecursiveSelfReference(IrModelReference reference, string? currentModelName) =>
        !string.IsNullOrEmpty(currentModelName)
        && string.Equals(reference.ModelName, currentModelName, StringComparison.OrdinalIgnoreCase);

    private static bool ReferencesSelf(IrType type, string ownName, HashSet<IrType> seen)
    {
        if (!seen.Add(type)) return false;
        switch (type)
        {
            case IrModelReference m:
                return string.Equals(m.ModelName, ownName, StringComparison.OrdinalIgnoreCase);
            case IrArrayType a:
                return ReferencesSelf(a.ElementType, ownName, seen);
            case IrDictionaryType d:
                return ReferencesSelf(d.KeyType, ownName, seen) || ReferencesSelf(d.ValueType, ownName, seen);
            case IrNullableType n:
                return ReferencesSelf(n.InnerType, ownName, seen);
            case IrOptionalType o:
                return ReferencesSelf(o.InnerType, ownName, seen);
            case IrUnionType u:
                return u.Types.Any(t => ReferencesSelf(t, ownName, seen));
            case IrIntersectionType i:
                return i.Types.Any(t => ReferencesSelf(t, ownName, seen));
            default:
                return false;
        }
    }

    public ExpressionNode BuildEnumSchema(IrEnum enumModel)
    {
        ArgumentNullException.ThrowIfNull(enumModel);

        // Переиспользуем {Name}Values из модели enum, не дублируя литералы:
        // Type.Union({Name}Values.map((value) => Type.Literal(value))).
        var arrow = new ArrowFunctionExpressionNode(
            ImmutableList.Create(new ParameterDeclarationNode(new IdentifierNode("value"), type: null, isRequired: true)),
            Call("Literal", Id("value")));

        var mapCall = new CallExpressionNode(
            new MemberAccessExpressionNode(Id($"{enumModel.Name}Values"), new IdentifierNode("map")),
            ImmutableList.Create<ExpressionNode>(arrow));

        return Call("Union", mapCall);
    }

    /// <summary>Builds a schema preserving wrappers, constraints, and exact-one alternative semantics.</summary>
    /// <param name="irType">The IR type to validate.</param>
    /// <param name="currentModelName">The model for recursive self references.</param>
    /// <param name="constraints">Constraints on the current value.</param>
    /// <returns>The TypeBox schema expression.</returns>
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
            IrModelReference reference => IsRecursiveSelfReference(reference, currentModelName)
                ? Id("This")
                : Id($"{reference.ModelName}Schema"),
            IrArrayType array => Call("Array", BuildSchemaFromIrType(array.ElementType, currentModelName)),
            IrDictionaryType dictionary => Call(
                "Record",
                BuildSchemaFromIrType(dictionary.KeyType, currentModelName),
                BuildSchemaFromIrType(dictionary.ValueType, currentModelName)),
            IrNullableType nullable => BuildUnion([
                BuildSchemaFromIrType(nullable.InnerType, currentModelName, constraints),
                Call("Null")
            ]),
            IrOptionalType optional => Call("Optional", BuildSchemaFromIrType(optional.InnerType, currentModelName, constraints)),
            IrUnionType union => union.Kind == IrUnionKind.OneOf
                ? ExclusiveUnionAst.TypeBox(union.Types.Select(type => BuildSchemaFromIrType(type, currentModelName, constraints)).ToArray())
                : BuildUnion(union.Types.Select(type => BuildSchemaFromIrType(type, currentModelName, constraints))),
            IrIntersectionType intersection => BuildIntersection(intersection.Types.Select(type => BuildSchemaFromIrType(type, currentModelName, constraints))),
            _ => Call("Unknown")
        };
    }

    private ExpressionNode BuildObjectSchema(IrModel model)
    {
        var properties = model.Properties
            .Select(property => new PropertyAssignmentNode(
                name: new IdentifierNode(property.Name),
                value: BuildPropertySchema(property, model.Name)))
            .ToImmutableList();

        return Call("Object", new ObjectLiteralExpressionNode(properties, preferMultilineFormat: true));
    }

    /// <summary>
    /// Строит <c>Type.Object({...})</c> из набора параметров операции (route/query):
    /// необязательные параметры оборачиваются в <c>Type.Optional(...)</c>.
    /// </summary>
    public ExpressionNode BuildObjectFromParameters(IReadOnlyList<IrParameter> parameters)
    {
        var properties = parameters
            .Select(parameter =>
            {
                // Thread the parameter's constraints so lengths/ranges/patterns are inlined into the schema
                // options, mirroring model-property schemas.
                var schema = BuildSchemaFromIrType(parameter.Type, currentModelName: null, constraints: parameter.Constraints);
                return new PropertyAssignmentNode(
                    name: new IdentifierNode(parameter.Name),
                    value: parameter.IsRequired ? schema : Call("Optional", schema));
            })
            .ToImmutableList();

        return Call("Object", new ObjectLiteralExpressionNode(properties, preferMultilineFormat: true));
    }

    private ExpressionNode BuildPropertySchema(IrProperty property, string currentModelName)
    {
        var expression = BuildSchemaFromIrType(property.Type, currentModelName, property.Constraints);
        return property.IsRequired ? expression : Call("Optional", expression);
    }

    /// <summary>Builds a primitive schema matching the generated runtime representation.</summary>
    /// <param name="kind">The primitive IR kind.</param>
    /// <param name="constraints">The primitive's value constraints.</param>
    /// <returns>The constrained primitive schema.</returns>
    private ExpressionNode BuildPrimitive(IrPrimitiveKind kind, IrPropertyConstraints? constraints)
    {
        return kind switch
        {
            IrPrimitiveKind.String => CallOptional("String", BuildStringOptions(null, constraints)),
            IrPrimitiveKind.Uuid => BuildUuidSchema(constraints),
            IrPrimitiveKind.Date => BuildFormattedString("date", constraints),
            IrPrimitiveKind.DateTime => BuildFormattedString("date-time", constraints),
            IrPrimitiveKind.Time => BuildFormattedString("time", constraints),
            IrPrimitiveKind.Binary => TypeBoxInstanceAst.Build("Blob"),
            IrPrimitiveKind.File => TypeBoxInstanceAst.Build("File"),
            IrPrimitiveKind.ByteArray => Call("Uint8Array"),
            IrPrimitiveKind.Integer or IrPrimitiveKind.Long => CallOptional("Integer", BuildNumberOptions(constraints)),
            IrPrimitiveKind.Number => CallOptional("Number", BuildNumberOptions(constraints)),
            IrPrimitiveKind.Boolean => Call("Boolean"),
            IrPrimitiveKind.Void => Call("Void"),
            IrPrimitiveKind.Null => Call("Null"),
            IrPrimitiveKind.Any => Call("Unknown"),
            _ => Call("Unknown")
        };
    }

    private static ExpressionNode BuildUnion(IEnumerable<ExpressionNode> expressions)
    {
        var schemas = expressions.ToImmutableList();

        return schemas.Count switch
        {
            0 => Call("Unknown"),
            1 => schemas[0],
            _ => Call("Union", new ArrayLiteralExpressionNode(schemas))
        };
    }

    private static ExpressionNode BuildIntersection(IEnumerable<ExpressionNode> expressions)
    {
        var schemas = expressions.ToImmutableList();

        return schemas.Count switch
        {
            0 => Call("Unknown"),
            1 => schemas[0],
            _ => Call("Intersect", new ArrayLiteralExpressionNode(schemas))
        };
    }

    /// <summary>
    /// UUID schema per configured mode: guid → Type.String({format:"uuid"}); string →
    /// Type.String(); regex → Type.String({pattern: UUID_PATTERN}).
    /// </summary>
    private ExpressionNode BuildUuidSchema(IrPropertyConstraints? constraints)
    {
        return _uuidFormat switch
        {
            TypeScriptOptionIds.UuidFormat.String => CallOptional("String", BuildStringOptions(null, constraints)),
            TypeScriptOptionIds.UuidFormat.Regex => Call("String",
                BuildStringOptionsWithPattern(Id(TypeScriptOptionIds.UuidFormat.PatternConstName), constraints)),
            _ => BuildFormattedString("uuid", constraints),
        };
    }

    /// <summary>Determines whether the selected representation emits standard registered string formats.</summary>
    /// <param name="type">The recursively inspected inline type.</param>
    /// <returns>True when a FormatRegistry runtime import is required.</returns>
    internal bool RequiresFormatRegistry(IrType type) => TypeBoxFormatAst.RequiresRegistry(type, _uuidFormat);

    /// <summary>Builds a formatted string after registering its standard runtime predicate.</summary>
    /// <param name="format">The standard format name.</param>
    /// <param name="constraints">Additional string value constraints.</param>
    /// <returns>The registered and constrained string schema.</returns>
    private static ExpressionNode BuildFormattedString(string format, IrPropertyConstraints? constraints) =>
        TypeBoxFormatAst.Register(format, CallOptional("String", BuildStringOptions(format, constraints)));

    /// <summary>
    /// String options carrying an explicit <c>pattern</c> expression (e.g. the imported
    /// <c>UUID_PATTERN</c> identifier) plus any length constraints.
    /// </summary>
    private static ObjectLiteralExpressionNode BuildStringOptionsWithPattern(ExpressionNode patternValue, IrPropertyConstraints? constraints)
    {
        var properties = ImmutableList.CreateBuilder<PropertyAssignmentNode>();
        properties.Add(Property("pattern", patternValue));
        if (constraints?.MinLength is { } minLength)
            properties.Add(Property("minLength", Number(minLength)));
        if (constraints?.MaxLength is { } maxLength)
            properties.Add(Property("maxLength", Number(maxLength)));
        return new ObjectLiteralExpressionNode(properties.ToImmutableList());
    }

    private static ObjectLiteralExpressionNode? BuildStringOptions(string? format, IrPropertyConstraints? constraints)
    {
        var properties = ImmutableList.CreateBuilder<PropertyAssignmentNode>();

        if (!string.IsNullOrWhiteSpace(format))
            properties.Add(Property("format", String(format)));
        if (constraints?.MinLength is { } minLength)
            properties.Add(Property("minLength", Number(minLength)));
        if (constraints?.MaxLength is { } maxLength)
            properties.Add(Property("maxLength", Number(maxLength)));
        if (!string.IsNullOrWhiteSpace(constraints?.Pattern))
            properties.Add(Property("pattern", String(constraints.Pattern)));

        return properties.Count == 0
            ? null
            : new ObjectLiteralExpressionNode(properties.ToImmutableList());
    }

    private static ObjectLiteralExpressionNode? BuildNumberOptions(IrPropertyConstraints? constraints)
    {
        var properties = ImmutableList.CreateBuilder<PropertyAssignmentNode>();

        if (constraints?.Min is { } min)
            properties.Add(Property("minimum", Number(min)));
        if (constraints?.Max is { } max)
            properties.Add(Property("maximum", Number(max)));
        if (constraints?.ExclusiveMin is { } exclusiveMin)
            properties.Add(Property("exclusiveMinimum", Number(exclusiveMin)));
        if (constraints?.ExclusiveMax is { } exclusiveMax)
            properties.Add(Property("exclusiveMaximum", Number(exclusiveMax)));

        return properties.Count == 0
            ? null
            : new ObjectLiteralExpressionNode(properties.ToImmutableList());
    }

    private static ExpressionNode CallOptional(string methodName, ExpressionNode? options)
    {
        return options == null ? Call(methodName) : Call(methodName, options);
    }

    private static CallExpressionNode Call(string methodName, params ExpressionNode[] arguments)
    {
        return new CallExpressionNode(
            new MemberAccessExpressionNode(Id("Type"), new IdentifierNode(methodName)),
            arguments.ToImmutableList());
    }

    private static ObjectLiteralExpressionNode Object(params (string Name, ExpressionNode Value)[] properties)
    {
        return new ObjectLiteralExpressionNode(properties
            .Select(property => Property(property.Name, property.Value))
            .ToImmutableList());
    }

    private static PropertyAssignmentNode Property(string name, ExpressionNode value)
    {
        return new PropertyAssignmentNode(new IdentifierNode(name), value);
    }

    private static IdentifierExpressionNode Id(string name)
        => new(new IdentifierNode(name));

    private static LiteralExpressionNode String(string value)
        => new(value, LiteralKind.String);

    private static LiteralExpressionNode Number(object value)
        => new(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0", LiteralKind.Number);
}
