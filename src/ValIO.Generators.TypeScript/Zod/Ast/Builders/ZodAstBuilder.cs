using System.Collections.Immutable;
using ValIO.Core.Ast.Builders;
using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Expressions;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Core.Interfaces;
using ValIO.Core.IR;
using ValIO.Core.IR.Constraints;
using ValIO.Core.IR.Utilities;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Generators.TypeScript.Zod.Ast.Enums;
using ValIO.Generators.TypeScript.Zod.Ast.Nodes;
using ValIO.Generators.TypeScript.Validation;

namespace ValIO.Generators.TypeScript.Zod.Ast.Builders;

/// <summary>
/// Builds Zod schema AST using the shared document indexes for named declarations.
/// </summary>
public class ZodAstBuilder : AstBuilderBase
{
    private readonly INamingConverter _namingConverter;
    private readonly Func<IrType, TypeNode> _typeBuilder;
    private readonly bool _useBigInt;
    private readonly string? _uuidFormat;

    /// <summary>
    /// Creates a schema builder bound to the finalized input document.
    /// </summary>
    /// <param name="documentContext">The shared immutable declaration indexes.</param>
    /// <param name="namingConverter">The language-specific naming converter.</param>
    /// <param name="typeBuilder">The conversion from an IR type to a TypeScript type node.</param>
    /// <param name="useBigInt">Whether int64 schemas validate bigint values.</param>
    /// <param name="uuidFormat">The selected UUID validation mode.</param>
    /// <exception cref="ArgumentNullException">A required dependency is null.</exception>
    public ZodAstBuilder(
        IrDocumentContext documentContext,
        INamingConverter namingConverter,
        Func<IrType, TypeNode> typeBuilder,
        bool useBigInt = false,
        string? uuidFormat = null)
        : base(documentContext)
    {
        _namingConverter = namingConverter ?? throw new ArgumentNullException(nameof(namingConverter));
        _typeBuilder = typeBuilder ?? throw new ArgumentNullException(nameof(typeBuilder));
        _useBigInt = useBigInt;
        _uuidFormat = uuidFormat;
    }
    
    /// <summary>
    /// Builds a model schema, intersecting sibling properties with composition constraints.
    /// </summary>
    /// <param name="model">The model being validated.</param>
    /// <returns>The complete model schema.</returns>
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
                    // Named schemas have the general ZodSchema<T> type and can contain intersections or
                    // lazy references. A discriminator alone cannot prove they are Zod object schemas.
                    ZodSchemaNode alternatives = unionVariants.Count == 1
                        ? unionVariants[0]
                        : new ZodUnionNode(unionVariants, isExclusive: model.Composition.Kind == IrCompositionKind.OneOf);
                    return model.Properties.Count == 0
                        ? alternatives
                        : new ZodIntersectionNode([alternatives, BuildZodObject(model)]);
                case IrCompositionKind.AllOf:
                    var allOfSchemas = model.Composition.Types
                        .Select(t => BuildZodSchemaFromIrType(t, model.Name))
                        .ToList();
                    // Preserve local narrowing while avoiding duplicate inherited validation.
                    var ownProperties = ExcludeIdenticalInheritedProperties(model);
                    if (ownProperties.Count > 0)
                    {
                        var ownPropsSchema = BuildZodObject(model, ownProperties);
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

    /// <summary>Исключает унаследованные свойства, только если все соответствующие объявления в базовых типах имеют одинаковые правила проверки.</summary>
    /// <param name="model">Модель allOf, локальные свойства которой проверяются.</param>
    /// <returns>Локальные свойства, добавляющие или изменяющие правила проверки.</returns>
    private IReadOnlyList<IrProperty> ExcludeIdenticalInheritedProperties(IrModel model)
    {
        var inherited = new Dictionary<string, List<IrProperty>>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal) { model.Name };
        var pending = new Stack<IrModel>();
        pending.Push(model);
        while (pending.TryPop(out var current))
        {
            if (current.Composition is not { Kind: IrCompositionKind.AllOf } composition)
                continue;
            foreach (var reference in composition.Types.OfType<IrModelReference>())
            {
                if (!visited.Add(reference.ModelName) || !ModelsByName.TryGetValue(reference.ModelName, out var parent))
                    continue;
                foreach (var property in parent.Properties)
                {
                    if (!inherited.TryGetValue(property.OriginalName, out var matches))
                        inherited[property.OriginalName] = matches = [];
                    matches.Add(property);
                }
                pending.Push(parent);
            }
        }

        return model.Properties.Where(property =>
            !inherited.TryGetValue(property.OriginalName, out var matches) ||
            !matches.All(parent => SameValidation(property, parent))).ToArray();
    }

    /// <summary>Сравнивает обязательность свойства, тип значения и ограничения проверки.</summary>
    /// <param name="first">Локальное объявление.</param>
    /// <param name="second">Унаследованное объявление.</param>
    /// <returns>True, если оба объявления задают одинаковые правила проверки.</returns>
    private static bool SameValidation(IrProperty first, IrProperty second) =>
        first.IsRequired == second.IsRequired &&
        IrTypeComparer.AreSemanticallyEqual(first.Type, second.Type, ignorePresence: false) &&
        (first.Constraints == second.Constraints ||
            first.Constraints is not null && second.Constraints is not null &&
            ValidationConstraints.Equivalent(first.Constraints, second.Constraints));

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

    /// <summary>Создаёт схему объекта для параметров маршрута или запроса.</summary>
    /// <param name="parameters">Преобразуемые определения параметров.</param>
    /// <returns>Схема объекта Zod, сохраняющая необязательность и допустимость null для параметров.</returns>
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
    
    /// <summary>
    /// Builds a property schema with independent presence and null acceptance.
    /// </summary>
    /// <param name="property">The property metadata and constraints.</param>
    /// <param name="currentModelName">The containing model for recursive references.</param>
    /// <param name="constraintsObjectName">An optional exported constraint constant object.</param>
    /// <returns>The property schema with independent nullable and optional wrappers.</returns>
    public ZodPropertyNode BuildZodProperty(IrProperty property, string? currentModelName = null, string? constraintsObjectName = null)
    {
        var name = new IdentifierNode(property.Name);
        var unwrapNullable = TryUnwrapIrNullableRoot(property.Type, out var innerIr);
        var schemaIr = unwrapNullable ? innerIr! : property.Type;
        var schema = BuildZodSchemaFromIrType(schemaIr, currentModelName, property, constraintsObjectName,
            property.Constraints ?? property.Type.Constraints);
        
        return new ZodPropertyNode(
            name: name,
            schema: schema,
            isRequired: property.IsRequired,
            isNullable: unwrapNullable,
            isOptional: !property.IsRequired);
    }
    
    /// <summary>
    /// Builds a schema from an IR type with constraints applied inside nullable and optional wrappers.
    /// </summary>
    /// <param name="irType">The type being validated.</param>
    /// <param name="currentModelName">The containing model for recursive references.</param>
    /// <param name="property">An optional property context.</param>
    /// <param name="constraintsObjectName">An optional exported property constraint object.</param>
    /// <param name="constraints">Constraints propagated through value wrappers.</param>
    /// <returns>The schema preserving type and composition metadata.</returns>
    public ZodSchemaNode BuildZodSchemaFromIrType(
        ValIO.Core.IR.Types.IrType irType,
        string? currentModelName = null,
        IrProperty? property = null,
        string? constraintsObjectName = null,
        IrPropertyConstraints? constraints = null)
    {
        constraints ??= property?.Constraints ?? irType.Constraints;
        if (irType is IrPrimitiveType constrainedPrimitive && constrainedPrimitive.Constraints is { } local
            && constraints is not null && !ValidationConstraints.Equivalent(local, constraints))
            return new ZodIntersectionNode([
                BuildZodPrimitive(constrainedPrimitive, local),
                BuildZodPrimitive(constrainedPrimitive, constraints,
                    property?.Constraints != null && !string.IsNullOrEmpty(constraintsObjectName)
                        ? $"{constraintsObjectName}.{property.Name}" : null, property?.Description)
            ]);
        return irType switch
        {
            IrPrimitiveType p => BuildZodPrimitive(
                p,
                constraints,
                property?.Constraints != null && !string.IsNullOrEmpty(constraintsObjectName)
                    ? $"{constraintsObjectName}.{property.Name}"
                    : null,
                property?.Description),
            IrModelReference r => BuildZodModelReference(r, currentModelName),
            IrArrayType arr => BuildZodArray(arr, currentModelName),
            IrDictionaryType dict => BuildZodDictionary(dict, currentModelName),
            IrUnionType union => BuildZodUnion(union, currentModelName, property, constraintsObjectName, constraints),
            IrIntersectionType intersection => BuildZodIntersection(intersection, currentModelName, property, constraintsObjectName, constraints),
            IrNullableType nullable => BuildZodNullable(nullable, currentModelName, property, constraintsObjectName, constraints),
            IrOptionalType optional => BuildZodOptional(optional, currentModelName, property, constraintsObjectName, constraints),
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
    /// Builds runtime instance schemas for binary types and shared primitive format or constraint expressions.
    /// </summary>
    /// <param name="primitiveType">The primitive's IR metadata.</param>
    /// <param name="constraints">The primitive's validation constraints.</param>
    /// <param name="constraintsRef">An optional exported constraint constant reference.</param>
    /// <param name="description">An optional schema description.</param>
    /// <returns>The complete primitive schema.</returns>
    private ZodSchemaNode BuildZodPrimitive(
        IrPrimitiveType primitiveType,
        IrPropertyConstraints? constraints = null,
        string? constraintsRef = null,
        string? description = null)
    {
        var kind = primitiveType.PrimitiveKind;
        if (kind is IrPrimitiveKind.Binary)
            return BuildZodInstanceOf("Blob");
        if (kind is IrPrimitiveKind.ByteArray)
            return BuildZodInstanceOf("Uint8Array");
        if (kind is IrPrimitiveKind.File)
            return BuildZodInstanceOf("File");

        return ZodExpressionBuilder.BuildPrimitiveExpression(kind, _useBigInt, constraints, constraintsRef, _uuidFormat, description);
    }

    /// <summary>Builds an instance validator matching the generated runtime binary type.</summary>
    /// <param name="typeName">The runtime constructor name.</param>
    /// <returns>The instance validation expression.</returns>
    private static ZodExpressionSchemaNode BuildZodInstanceOf(string typeName)
    {
        return new ZodExpressionSchemaNode(new CallExpressionNode(
            new MemberAccessExpressionNode(
                new IdentifierExpressionNode(new IdentifierNode("z")),
                new IdentifierNode("instanceof")),
            ImmutableList.Create<ExpressionNode>(new IdentifierExpressionNode(new IdentifierNode(typeName)))));
    }
    
    /// <summary>
    /// Builds a named reference and defers edges belonging to direct or mutual recursion.
    /// </summary>
    /// <param name="modelReference">The referenced declaration.</param>
    /// <param name="currentModelName">The containing model, if this reference belongs to a model schema.</param>
    /// <returns>A schema reference whose evaluation is lazy only when it closes a dependency cycle.</returns>
    private ZodSchemaNode BuildZodModelReference(IrModelReference modelReference, string? currentModelName = null)
    {
        var schemaName = $"{modelReference.ModelName}Schema";
        var isLazy = !string.IsNullOrEmpty(currentModelName)
            && IsRecursiveReference(currentModelName, modelReference.ModelName);
        return new ZodPrimitiveSchemaNode(ZodSchemaKind.Any, schemaReference: schemaName, isLazy: isLazy);
    }

    /// <summary>Determines whether the referenced declaration can reach the containing model.</summary>
    /// <param name="currentModelName">The source of the reference being rendered.</param>
    /// <param name="referencedModelName">The target declaration whose dependencies are traversed.</param>
    /// <returns>True when this edge belongs to a dependency cycle; otherwise false.</returns>
    /// <remarks>
    /// The finalized IR graph already includes references inside arrays, records, nullable values,
    /// and composition members. Iterative traversal also terminates when an unrelated cycle is reachable.
    /// </remarks>
    private bool IsRecursiveReference(string currentModelName, string referencedModelName)
    {
        var dependencies = DocumentContext.Document.TypeDependencies.Dependencies;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>();
        pending.Push(referencedModelName);

        while (pending.TryPop(out var candidate))
        {
            if (string.Equals(candidate, currentModelName, StringComparison.Ordinal))
                return true;
            if (!visited.Add(candidate) || !dependencies.TryGetValue(candidate, out var referencedTypes))
                continue;
            foreach (var dependency in referencedTypes)
                pending.Push(dependency);
        }

        return false;
    }
    
    /// <summary>
    /// Строит Zod схему из IrArrayType
    /// </summary>
    private ZodSchemaNode BuildZodArray(IrArrayType arrayType, string? currentModelName = null)
    {
        var elementSchema = BuildZodSchemaFromIrType(arrayType.ElementType, currentModelName, null, null);
        return new ZodArrayNode(elementSchema);
    }
    
    /// <summary>Создаёт запись со строковыми ключами, сохраняя схему значений и контекст рекурсивной модели.</summary>
    /// <param name="dictionaryType">Преобразуемый тип словаря.</param>
    /// <param name="currentModelName">Содержащая модель для отложенного разрешения ссылок на себя.</param>
    /// <returns>Схема записи Zod с преобразованным типом значения.</returns>
    private ZodSchemaNode BuildZodDictionary(IrDictionaryType dictionaryType, string? currentModelName)
    {
        var valueSchema = BuildZodSchemaFromIrType(dictionaryType.ValueType, currentModelName);
        return new ZodRecordNode(BuildZodSchemaFromIrType(dictionaryType.KeyType, currentModelName), valueSchema);
    }
    
    /// <summary>
    /// Builds inclusive or exclusive alternatives while retaining value constraints.
    /// </summary>
    /// <param name="unionType">The alternatives and their matching semantics.</param>
    /// <param name="currentModelName">The containing model for recursive references.</param>
    /// <param name="property">The property constraint context.</param>
    /// <param name="constraintsObjectName">An optional exported constraint object.</param>
    /// <param name="constraints">Constraints on the alternatives.</param>
    /// <returns>The cardinality-aware alternative schema.</returns>
    private ZodSchemaNode BuildZodUnion(
        IrUnionType unionType,
        string? currentModelName = null,
        IrProperty? property = null,
        string? constraintsObjectName = null,
        IrPropertyConstraints? constraints = null)
    {
        var variants = unionType.Types
            .Select(t => BuildZodSchemaFromIrType(t, currentModelName, property, constraintsObjectName, constraints))
            .ToImmutableList();
        if (variants.Count == 0)
            return BuildZodAny();
        if (variants.Count == 1)
            return variants[0];
        return new ZodUnionNode(variants, isExclusive: unionType.Kind == IrUnionKind.OneOf);
    }
    
    /// <summary>
    /// Builds the intersecting schemas, preserving value constraints on each member.
    /// </summary>
    /// <param name="intersectionType">The intersecting types.</param>
    /// <param name="currentModelName">The containing model.</param>
    /// <param name="property">The optional property context.</param>
    /// <param name="constraintsObjectName">The optional exported constraint object.</param>
    /// <param name="constraints">Constraints applying to the value.</param>
    /// <returns>The intersection schema.</returns>
    private ZodSchemaNode BuildZodIntersection(
        IrIntersectionType intersectionType,
        string? currentModelName = null,
        IrProperty? property = null,
        string? constraintsObjectName = null,
        IrPropertyConstraints? constraints = null)
    {
        var schemas = intersectionType.Types
            .Select(t => BuildZodSchemaFromIrType(t, currentModelName, property, constraintsObjectName, constraints))
            .ToImmutableList();
        if (schemas.Count == 0)
            return BuildZodAny();
        if (schemas.Count == 1)
            return schemas[0];
        return new ZodIntersectionNode(schemas);
    }
    
    /// <summary>
    /// Applies value constraints before allowing explicit null.
    /// </summary>
    /// <param name="nullableType">The nullable type.</param>
    /// <param name="currentModelName">The containing model.</param>
    /// <param name="property">The optional property context.</param>
    /// <param name="constraintsObjectName">The optional exported constraint object.</param>
    /// <param name="constraints">Constraints applying to the inner value.</param>
    /// <returns>The nullable schema.</returns>
    private ZodSchemaNode BuildZodNullable(
        IrNullableType nullableType,
        string? currentModelName = null,
        IrProperty? property = null,
        string? constraintsObjectName = null,
        IrPropertyConstraints? constraints = null)
    {
        var innerSchema = BuildZodSchemaFromIrType(nullableType.InnerType, currentModelName, property, constraintsObjectName, constraints);
        return new ZodEffectsNode(innerSchema, ZodEffectKind.Nullable);
    }
    
    /// <summary>
    /// Applies value constraints before allowing undefined.
    /// </summary>
    /// <param name="optionalType">The optional type.</param>
    /// <param name="currentModelName">The containing model.</param>
    /// <param name="property">The optional property context.</param>
    /// <param name="constraintsObjectName">The optional exported constraint object.</param>
    /// <param name="constraints">Constraints applying to the inner value.</param>
    /// <returns>The optional schema.</returns>
    private ZodSchemaNode BuildZodOptional(
        IrOptionalType optionalType,
        string? currentModelName = null,
        IrProperty? property = null,
        string? constraintsObjectName = null,
        IrPropertyConstraints? constraints = null)
    {
        var innerSchema = BuildZodSchemaFromIrType(optionalType.InnerType, currentModelName, property, constraintsObjectName, constraints);
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
