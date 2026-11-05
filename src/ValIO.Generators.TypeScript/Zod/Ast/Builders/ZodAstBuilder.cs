using System.Collections.Immutable;
using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Expressions;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Core.Interfaces;
using ValIO.Core.IR;
using ValIO.Core.IR.Constraints;
using ValIO.Core.IR.Builders;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Generators.TypeScript.Zod.Ast.Enums;
using ValIO.Generators.TypeScript.Zod.Ast.Nodes;

namespace ValIO.Generators.TypeScript.Zod.Ast.Builders;

/// <summary>
/// Построитель AST для Zod специфичных узлов
/// Использует IR для построения AST узлов
/// </summary>
public class ZodAstBuilder
{
    /// <summary>Gets or sets the document models available for schema composition.</summary>
    /// <value>The models keyed by their IR names.</value>
    public IReadOnlyDictionary<string, IrModel> ModelsByName { get; set; } = new Dictionary<string, IrModel>();

    private readonly INamingConverter _namingConverter;
    private readonly Func<IrType, TypeNode> _typeBuilder;
    private readonly bool _useBigInt;
    private readonly string? _uuidFormat;

    /// <summary>
    /// Конструктор
    /// </summary>
    /// <param name="namingConverter">Конвертер имен</param>
    /// <param name="typeBuilder">Функция для построения TypeNode из IrType</param>
    /// <param name="useBigInt">Использовать bigint для int64. По умолчанию false — number.</param>
    /// <param name="uuidFormat">Режим обработки Guid/Uuid полей (guid/string/regex). null → guid.</param>
    public ZodAstBuilder(
        INamingConverter namingConverter,
        Func<IrType, TypeNode> typeBuilder,
        bool useBigInt = false,
        string? uuidFormat = null)
    {
        _namingConverter = namingConverter ?? throw new ArgumentNullException(nameof(namingConverter));
        _typeBuilder = typeBuilder ?? throw new ArgumentNullException(nameof(typeBuilder));
        _useBigInt = useBigInt;
        _uuidFormat = uuidFormat;
    }
    
    /// <summary>Builds a model schema, preserving every inherited and locally declared allOf constraint.</summary>
    /// <param name="model">The model and its composition to translate.</param>
    /// <returns>A Zod object, union or intersection schema.</returns>
    public ZodSchemaNode BuildZodSchemaFromIrModel(IrModel model)
    {
        if (model.Composition != null && model.Composition.Types.Count > 0)
        {
            switch (model.Composition.Kind)
            {
                case IrCompositionKind.OneOf:
                case IrCompositionKind.AnyOf:
                    var unionVariants = model.Composition.Types
                        .Select(t => BuildZodSchemaFromIrType(t, model.Name))
                        .ToImmutableList();
                    if (unionVariants.Count == 1)
                        return unionVariants[0];
                    // z.discriminatedUnion needs every variant to be an object with a literal at the
                    // discriminator key. Enable it only for oneOf + a declared discriminator where every
                    // variant is a (non-self) model reference — the generated {Model}Schema are z.object,
                    // whereas a self-reference would render as z.lazy(...) which discriminatedUnion rejects.
                    // The key is sanitized exactly like model property names so it matches the variants.
                    var canDiscriminate = model.Composition.Kind == IrCompositionKind.OneOf
                        && !string.IsNullOrWhiteSpace(model.Composition.DiscriminatorPropertyName)
                        && model.Composition.Types.All(t =>
                            t is IrModelReference m && !m.ModelName.Equals(model.Name, StringComparison.OrdinalIgnoreCase));
                    var discriminator = canDiscriminate
                        ? _namingConverter.SanitizeReservedKeyword(
                            _namingConverter.SanitizeIdentifier(model.Composition.DiscriminatorPropertyName!))
                        : null;
                    return new ZodUnionNode(unionVariants, discriminator: discriminator);
                case IrCompositionKind.AllOf:
                    var allOfSchemas = model.Composition.Types
                        .Select(t => BuildZodSchemaFromIrType(t, model.Name))
                        .ToList();
                    // A local declaration may narrow a base property; both schemas must validate it.
                    if (model.Properties.Count > 0)
                    {
                        var ownPropsSchema = BuildZodObject(model, model.Properties);
                        allOfSchemas.Add(ownPropsSchema);
                    }
                    if (allOfSchemas.Count == 0)
                        return BuildZodObject(model);
                    if (allOfSchemas.Count == 1)
                        return allOfSchemas[0];
                    return new ZodIntersectionNode(allOfSchemas);
            }
        }

        return BuildZodObject(model);
    }

    /// <summary>
    /// Строит ZodObjectNode из IrModel (только properties, без composition)
    /// </summary>
    /// <param name="model">IR модель</param>
    /// <returns>AST узел Zod object схемы</returns>
    public ZodObjectNode BuildZodObject(IrModel model)
        => BuildZodObject(model, model.Properties);

    /// <summary>
    /// Строит ZodObjectNode из заданного набора свойств модели (для allOf — только собственные,
    /// без унаследованных от базовых классов).
    /// </summary>
    public ZodObjectNode BuildZodObject(IrModel model, IReadOnlyList<IrProperty> properties)
    {
        var constraintsName = $"{model.Name}Constraints";
        var props = properties
            .Select(p => BuildZodProperty(p, model.Name, constraintsName))
            .ToImmutableList();
        return new ZodObjectNode(props);
    }

    /// <summary>Builds an object schema for route or query parameters.</summary>
    /// <param name="parameters">The parameter definitions to translate.</param>
    /// <returns>A Zod object schema retaining parameter optionality and nullability.</returns>
    public ZodObjectNode BuildZodObjectFromParameters(IReadOnlyList<IrParameter> parameters)
    {
        var properties = parameters
            .Select(p =>
            {
                var unwrap = TryUnwrapIrNullableRoot(p.Type, out var inner);
                var schemaIr = unwrap ? inner! : p.Type;
                return new ZodPropertyNode(
                    name: new IdentifierNode(p.Name),
                    schema: BuildZodSchemaFromIrType(schemaIr),
                    isRequired: p.IsRequired,
                    isNullable: unwrap,
                    isOptional: !p.IsRequired);
            })
            .ToImmutableList();
        return new ZodObjectNode(properties);
    }
    
    /// <summary>Builds a property schema with independent required and nullable decisions.</summary>
    /// <param name="property">The IR property to translate.</param>
    /// <param name="currentModelName">The containing model for constraints and self-references.</param>
    /// <param name="constraintsObjectName">The generated constraints constant, if present.</param>
    /// <returns>The property node and its value schema.</returns>
    public ZodPropertyNode BuildZodProperty(IrProperty property, string? currentModelName = null, string? constraintsObjectName = null)
    {
        var name = new IdentifierNode(property.Name);
        var unwrapNullable = TryUnwrapIrNullableRoot(property.Type, out var innerIr);
        var schemaIr = unwrapNullable ? innerIr! : property.Type;
        var schema = BuildZodSchemaFromIrType(schemaIr, currentModelName, property, constraintsObjectName);
        
        return new ZodPropertyNode(
            name: name,
            schema: schema,
            isRequired: property.IsRequired,
            isNullable: unwrapNullable,
            isOptional: !property.IsRequired);
    }
    
    /// <summary>Builds a Zod schema from an IR type, preserving nested type information.</summary>
    /// <param name="irType">The type to translate.</param>
    /// <param name="currentModelName">The containing model, if any.</param>
    /// <param name="property">The property supplying constraints, if any.</param>
    /// <param name="constraintsObjectName">The generated constraints constant, if present.</param>
    /// <returns>The translated Zod schema.</returns>
    public ZodSchemaNode BuildZodSchemaFromIrType(
        ValIO.Core.IR.Types.IrType irType,
        string? currentModelName = null,
        IrProperty? property = null,
        string? constraintsObjectName = null)
    {
        return irType switch
        {
            IrPrimitiveType p => BuildZodPrimitive(
                p,
                property?.Constraints,
                property != null && !string.IsNullOrEmpty(constraintsObjectName)
                    ? $"{constraintsObjectName}.{property.Name}"
                    : null,
                property?.Description),
            IrModelReference r => BuildZodModelReference(r, currentModelName),
            IrArrayType arr => BuildZodArray(arr, currentModelName),
            IrDictionaryType dict => BuildZodDictionary(dict, currentModelName),
            IrUnionType union => BuildZodUnion(union, currentModelName, property, constraintsObjectName),
            IrIntersectionType intersection => BuildZodIntersection(intersection, currentModelName, property, constraintsObjectName),
            IrNullableType nullable => BuildZodNullable(nullable, currentModelName, property, constraintsObjectName),
            IrOptionalType optional => BuildZodOptional(optional, currentModelName, property, constraintsObjectName),
            _ => BuildZodAny()
        };
    }
    
    /// <summary>
    /// Строит ZodEnumNode из IrEnum
    /// </summary>
    /// <param name="enumModel">IR enum</param>
    /// <returns>AST узел Zod enum схемы</returns>
    public ZodEnumNode BuildZodEnum(IrEnum enumModel)
    {
        var values = enumModel.Values.Select(v =>
        {
            var name = new IdentifierNode(v.Name);
            var value = v.Value switch
            {
                string str => new LiteralExpressionNode(str, LiteralKind.String),
                int i => new LiteralExpressionNode(i, LiteralKind.Number),
                long l => new LiteralExpressionNode(l, LiteralKind.Number),
                short s => new LiteralExpressionNode(s, LiteralKind.Number),
                byte b => new LiteralExpressionNode(b, LiteralKind.Number),
                _ => new LiteralExpressionNode(v.Value?.ToString() ?? "", LiteralKind.String)
            };
            return new ZodEnumValueNode(name, value);
        }).ToImmutableList();
        
        return new ZodEnumNode(values);
    }
    
    /// <summary>
    /// Строит ZodUnionNode для oneOf/anyOf
    /// </summary>
    public ZodUnionNode BuildZodUnion(IReadOnlyList<ZodSchemaNode> variants)
    {
        return new ZodUnionNode(variants);
    }
    
    /// <summary>
    /// Строит ZodIntersectionNode для allOf
    /// </summary>
    public ZodIntersectionNode BuildZodIntersection(IReadOnlyList<ZodSchemaNode> schemas)
    {
        return new ZodIntersectionNode(schemas);
    }
    
    // Приватные вспомогательные методы для IR типов
    
    /// <summary>
    /// Строит Zod схему из IrPrimitiveType.
    /// Использует ZodExpressionBuilder для AST цепочки вызовов (uuid, datetime, min, max и т.д.).
    /// </summary>
    private ZodSchemaNode BuildZodPrimitive(
        IrPrimitiveType primitiveType,
        IrPropertyConstraints? constraints = null,
        string? constraintsRef = null,
        string? description = null)
    {
        var kind = primitiveType.PrimitiveKind;
        // Binary/ByteArray/File сериализуются runtime'ом как Blob/File.
        // Используем z.instanceof(Blob|File) — это правильно типизируется
        // (vs. z.any(), который выводится как `any | undefined` и ломает
        // required‑поля).
        if (kind is IrPrimitiveKind.Binary or IrPrimitiveKind.ByteArray)
            return BuildZodInstanceOf("Blob");
        if (kind is IrPrimitiveKind.File)
            return BuildZodInstanceOf("File");

        return ZodExpressionBuilder.BuildPrimitiveExpression(kind, _useBigInt, constraints, constraintsRef, _uuidFormat, description);
    }

    private static ZodExpressionSchemaNode BuildZodInstanceOf(string typeName)
    {
        return new ZodExpressionSchemaNode(new CallExpressionNode(
            new MemberAccessExpressionNode(
                new IdentifierExpressionNode(new IdentifierNode("z")),
                new IdentifierNode("instanceof")),
            ImmutableList.Create<ExpressionNode>(new IdentifierExpressionNode(new IdentifierNode(typeName)))));
    }
    
    /// <summary>
    /// Строит Zod схему из IrModelReference.
    /// При self-reference (modelReference.ModelName == currentModelName) использует z.lazy().
    /// </summary>
    private ZodSchemaNode BuildZodModelReference(IrModelReference modelReference, string? currentModelName = null)
    {
        var schemaName = $"{modelReference.ModelName}Schema";
        var isLazy = !string.IsNullOrEmpty(currentModelName)
            && string.Equals(modelReference.ModelName, currentModelName, StringComparison.OrdinalIgnoreCase);
        return new ZodPrimitiveSchemaNode(ZodSchemaKind.Any, schemaReference: schemaName, isLazy: isLazy);
    }
    
    /// <summary>
    /// Строит Zod схему из IrArrayType
    /// </summary>
    private ZodSchemaNode BuildZodArray(IrArrayType arrayType, string? currentModelName = null)
    {
        var elementSchema = BuildZodSchemaFromIrType(arrayType.ElementType, currentModelName, null, null);
        return new ZodArrayNode(elementSchema);
    }
    
    /// <summary>Builds a string-keyed record and retains the value schema and recursive model context.</summary>
    /// <param name="dictionaryType">The dictionary type to translate.</param>
    /// <param name="currentModelName">The containing model, used to resolve self-references lazily.</param>
    /// <returns>A Zod record schema with the translated value type.</returns>
    private ZodSchemaNode BuildZodDictionary(IrDictionaryType dictionaryType, string? currentModelName)
    {
        var valueSchema = BuildZodSchemaFromIrType(dictionaryType.ValueType, currentModelName);
        var dependencies = new IrTypeDependencyGraphBuilder().ExtractDependenciesFromType(dictionaryType.ValueType);
        var deferValue = dependencies.Any(name => !string.Equals(name, currentModelName, StringComparison.OrdinalIgnoreCase));
        return new ZodRecordNode(valueSchema, deferValueSchema: deferValue);
    }
    
    /// <summary>
    /// Строит Zod схему из IrUnionType
    /// </summary>
    private ZodSchemaNode BuildZodUnion(
        IrUnionType unionType,
        string? currentModelName = null,
        IrProperty? property = null,
        string? constraintsObjectName = null)
    {
        var variants = unionType.Types
            .Select(t => BuildZodSchemaFromIrType(t, currentModelName, property, constraintsObjectName))
            .ToImmutableList();
        if (variants.Count == 0)
            return BuildZodAny();
        if (variants.Count == 1)
            return variants[0];
        return new ZodUnionNode(variants);
    }
    
    /// <summary>
    /// Строит Zod схему из IrIntersectionType
    /// </summary>
    private ZodSchemaNode BuildZodIntersection(
        IrIntersectionType intersectionType,
        string? currentModelName = null,
        IrProperty? property = null,
        string? constraintsObjectName = null)
    {
        var schemas = intersectionType.Types
            .Select(t => BuildZodSchemaFromIrType(t, currentModelName, property, constraintsObjectName))
            .ToImmutableList();
        if (schemas.Count == 0)
            return BuildZodAny();
        if (schemas.Count == 1)
            return schemas[0];
        return new ZodIntersectionNode(schemas);
    }
    
    /// <summary>
    /// Строит Zod схему из IrNullableType
    /// </summary>
    private ZodSchemaNode BuildZodNullable(
        IrNullableType nullableType,
        string? currentModelName = null,
        IrProperty? property = null,
        string? constraintsObjectName = null)
    {
        var innerSchema = BuildZodSchemaFromIrType(nullableType.InnerType, currentModelName, property, constraintsObjectName);
        return new ZodEffectsNode(innerSchema, ZodEffectKind.Nullable);
    }
    
    /// <summary>
    /// Строит Zod схему из IrOptionalType
    /// </summary>
    private ZodSchemaNode BuildZodOptional(
        IrOptionalType optionalType,
        string? currentModelName = null,
        IrProperty? property = null,
        string? constraintsObjectName = null)
    {
        var innerSchema = BuildZodSchemaFromIrType(optionalType.InnerType, currentModelName, property, constraintsObjectName);
        return new ZodEffectsNode(innerSchema, ZodEffectKind.Optional);
    }
    
    private ZodSchemaNode BuildZodAny()
    {
        return new ZodPrimitiveSchemaNode(ZodSchemaKind.Any);
    }
    
    private static bool TryUnwrapIrNullableRoot(IrType type, out IrType? inner)
    {
        if (type is IrNullableType n)
        {
            inner = n.InnerType;
            return true;
        }
        
        inner = null;
        return false;
    }
}
