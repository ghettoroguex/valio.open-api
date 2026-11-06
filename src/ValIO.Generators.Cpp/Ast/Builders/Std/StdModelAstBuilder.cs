using System.Text.Json;
using ValIO.Core.Ast.Builders;
using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Expressions;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Core.IR;
using ValIO.Core.IR.Types;
using ValIO.Core.IR.Utilities;
using ValIO.Generators.Cpp.Ast.Nodes;
using ValIO.Generators.Cpp.Services;

namespace ValIO.Generators.Cpp.Ast.Builders.Std;

/// <summary>
/// Builds JSON-independent standalone C++ model headers from the shared inheritance plan.
/// </summary>
public sealed class StdModelAstBuilder : AstBuilderBase
{
    /// <summary>Maps IR values to standalone C++ types.</summary>
    private readonly StdCppTypeMapper _typeMapper;
    /// <summary>Names generated declarations and files.</summary>
    private readonly StdNamingConvention _naming;

    /// <summary>Creates a model builder with consistent naming and type mapping.</summary>
    /// <param name="typeMapper">The standalone type mapper.</param>
    /// <param name="naming">The generated naming convention.</param>
    /// <param name="documentContext">The finalized declaration indexes shared by this generation.</param>
    /// <exception cref="ArgumentNullException">A required service is missing.</exception>
    /// <exception cref="ArgumentException">The mapper uses different declaration indexes.</exception>
    public StdModelAstBuilder(StdCppTypeMapper typeMapper, StdNamingConvention naming, IrDocumentContext documentContext)
        : base(documentContext)
    {
        _typeMapper = typeMapper ?? throw new ArgumentNullException(nameof(typeMapper));
        _naming = naming ?? throw new ArgumentNullException(nameof(naming));
        if (!ReferenceEquals(typeMapper.DocumentContext, documentContext))
            throw new ArgumentException("The type mapper and builder must use the same document context.", nameof(documentContext));
    }

    /// <summary>Builds a pure model header or rejects unsupported recursive ownership.</summary>
    /// <param name="model">The model contract.</param>
    /// <param name="rootNamespace">The generated namespace.</param>
    /// <param name="warnings">The optional diagnostic sink.</param>
    /// <returns>The generated definition header.</returns>
    /// <exception cref="NotSupportedException">The model requires unsupported recursive ownership or composition codecs.</exception>
    public CppHeaderFileNode Build(
        IrModel model,
        string rootNamespace,
        ICollection<string>? warnings = null)
    {
        var structName = _naming.StructName(model.Name);
        var enumNames = EnumNames;
        var modelLookup = ModelsByName;
        CppTypeReferenceCollector.EnsureSupportedComposition(model, modelLookup);
        var plan = IrInheritancePlan.Resolve(model, modelLookup);
        CppTypeReferenceCollector.EnsureNonRecursive(model, plan.EffectiveProperties, enumNames);

        var memberNames = plan.EffectiveProperties.Select(property => _naming.PropertyName(property.Name))
            .ToHashSet(StringComparer.Ordinal);
        var fields = new List<FieldDeclarationNode>();
        foreach (var property in plan.EffectiveProperties)
        {
            fields.Add(BuildField(property, rootNamespace, memberNames));
        }

        var structNode = new StructDeclarationNode(
            new IdentifierNode(structName),
            baseTypes: plan.UseInheritance
                ? [new SimpleTypeNode(new IdentifierNode(_naming.StructName(plan.BaseModelName!)))]
                : null,
            members: fields,
            documentation: StdDocumentation.Create($"Data model for {model.OriginalName}.", model.Description));

        var includes = CollectIncludes(model, plan, enumNames);

        return new CppHeaderFileNode(
            fileName: _naming.ModelFileName(model.OriginalName),
            includes: includes,
            declarations: [new NamespaceDeclarationNode(new IdentifierNode(rootNamespace), [structNode])]);
    }

    /// <summary>Builds one field without collapsing missing keys and explicit nulls.</summary>
    /// <param name="property">The property contract.</param>
    /// <param name="rootNamespace">The namespace qualifying enum initializers independently of field names.</param>
    /// <param name="memberNames">All generated field names that can hide model or enum declarations.</param>
    /// <returns>The corresponding C++ field declaration.</returns>
    private FieldDeclarationNode BuildField(IrProperty property, string rootNamespace, IReadOnlySet<string> memberNames)
    {
        var mapped = QualifyHiddenTypes(StdPropertyTypes.MapField(property, _typeMapper), rootNamespace, memberNames);
        var isOptional = StdPropertyTypes.IsOptional(property) || property.Type is IrNullableType;
        var defaultValueStr = isOptional ? null : _typeMapper.GetDefaultValue(property.Type);
        ExpressionNode? defaultValue = defaultValueStr != null
            ? new IdentifierExpressionNode(new IdentifierNode(defaultValueStr))
            : null;
        if (!isOptional && property.Type is IrModelReference reference && EnumsByName.TryGetValue(reference.ModelName, out var declaration))
            defaultValue = BuildEnumDefault(property, reference, declaration, mapped, rootNamespace);

        return new FieldDeclarationNode(
            new IdentifierNode(_naming.PropertyName(property.Name)),
            mapped,
            AccessModifier.Public,
            defaultValue: defaultValue,
            documentation: StdDocumentation.Create($"Value of the '{property.OriginalName}' property.", property.Description,
                property.IsRequired ? "@details The wire property is required." : "@details An empty optional represents a missing wire property."));
    }

    /// <summary>Qualifies declaration references that a field name would hide in the containing C++ class scope.</summary>
    /// <remarks>Qualification also applies inside containers and to references before or after the colliding field.</remarks>
    /// <param name="type">The mapped field type, including presence and nullability wrappers.</param>
    /// <param name="rootNamespace">The namespace containing generated model and enum declarations.</param>
    /// <param name="memberNames">The complete set of generated field names in this model.</param>
    /// <returns>A field type whose declaration references have unambiguous C++ name lookup.</returns>
    private static TypeNode QualifyHiddenTypes(TypeNode type, string rootNamespace, IReadOnlySet<string> memberNames)
        => type switch
        {
            SimpleTypeNode simple when !simple.IsPrimitive && memberNames.Contains(simple.Name.Name)
                => new SimpleTypeNode(new IdentifierNode(string.IsNullOrEmpty(rootNamespace)
                        ? $"::{simple.Name.Name}" : $"::{rootNamespace}::{simple.Name.Name}"),
                    simple.IsPrimitive, simple.IsNullable, simple.IsOptional, simple.LiteralValue, simple.Metadata),
            GenericTypeNode generic => new GenericTypeNode(generic.Name,
                generic.TypeArguments.Select(argument => QualifyHiddenTypes(argument, rootNamespace, memberNames)).ToArray(),
                generic.IsNullable, generic.IsOptional, generic.Metadata),
            _ => type,
        };

    /// <summary>Initializes required enum fields deterministically while retaining an explicit schema wire default.</summary>
    /// <param name="property">The required enum property.</param>
    /// <param name="reference">The declaration reference used by the field.</param>
    /// <param name="declaration">The enum wire values available to the schema default.</param>
    /// <param name="mappedType">The actual application field type.</param>
    /// <param name="rootNamespace">The namespace containing the referenced enum declaration.</param>
    /// <returns>A typed enum value-initializer or a named schema-default member.</returns>
    /// <exception cref="NotSupportedException">The schema default is not a declared enum wire value.</exception>
    private ExpressionNode BuildEnumDefault(IrProperty property, IrModelReference reference, IrEnum declaration, TypeNode mappedType, string rootNamespace)
    {
        var enumType = StdExpressions.Scope(rootNamespace, _naming.EnumName(reference.ModelName));
        if (property.Constraints?.DefaultValue is not { } schemaDefault)
            return new CallExpressionNode(enumType, type: mappedType);

        var member = declaration.Values.FirstOrDefault(value => MatchesEnumDefault(declaration.BaseType, schemaDefault, value.Value));
        if (member is null)
            throw new NotSupportedException($"C++ enum property '{property.Name}' has a schema default outside '{declaration.Name}'.");
        return new MemberAccessExpressionNode(enumType,
            new IdentifierNode(_naming.EnumMemberName(member.Name)), useScopeOperator: true, type: mappedType);
    }

    /// <summary>Matches boxed IR and parsed JSON defaults without conflating string and numeric wire kinds.</summary>
    /// <param name="baseType">The enum's actual JSON wire kind.</param>
    /// <param name="schemaDefault">The CLR primitive or parsed JSON default.</param>
    /// <param name="wireValue">The enum member's original wire value.</param>
    /// <returns>Whether both values represent the same declared wire scalar.</returns>
    private static bool MatchesEnumDefault(IrEnumBaseType baseType, object schemaDefault, object wireValue)
    {
        var source = JsonSerializer.SerializeToElement(schemaDefault);
        var target = JsonSerializer.SerializeToElement(wireValue);
        if (baseType == IrEnumBaseType.String)
            return source.ValueKind == JsonValueKind.String && target.ValueKind == JsonValueKind.String
                && string.Equals(source.GetString(), target.GetString(), StringComparison.Ordinal);
        return source.ValueKind == JsonValueKind.Number && target.ValueKind == JsonValueKind.Number
            && source.TryGetDecimal(out var sourceNumber) && target.TryGetDecimal(out var targetNumber)
            && sourceNumber == decimal.Truncate(sourceNumber) && sourceNumber == targetNumber;
    }

    /// <summary>
    /// Collects explicit standard dependencies and stable model and enum include paths.
    /// </summary>
    /// <param name="model">The model contract.</param>
    /// <param name="plan">The resolved inheritance plan.</param>
    /// <param name="enumNames">The document's enum names.</param>
    /// <returns>The ordered include directives.</returns>
    private List<CppIncludeDirectiveNode> CollectIncludes(IrModel model, IrInheritancePlan plan, IReadOnlySet<string> enumNames)
    {
        var stdIncludes = new SortedSet<string>(StringComparer.Ordinal);
        var providerIncludes = new SortedSet<string>(StringComparer.Ordinal);
        var referencedModels = new HashSet<string>();
        var referencedEnums = new HashSet<string>();
        var orig = CppTypeReferenceCollector.BuildOriginalNameMap(model, plan.EffectiveProperties);

        if (plan.UseInheritance)
            referencedModels.Add(plan.BaseModelName!);

        foreach (var property in plan.EffectiveProperties)
        {
            CppTypeReferenceCollector.Collect(property.Type, referencedModels, referencedEnums, enumNames);
            foreach (var include in StdCppTypeMapper.StdIncludesFor(property.Type))
                stdIncludes.Add(include);
            foreach (var include in _typeMapper.ProviderIncludesFor(property.Type))
                providerIncludes.Add(include);
            // Property presence wrappers are added after mapping the schema value type.
            if (!property.IsRequired)
                stdIncludes.Add("<optional>");
        }

        var includes = new List<CppIncludeDirectiveNode>();
        foreach (var stdInclude in stdIncludes)
            includes.Add(new CppIncludeDirectiveNode(stdInclude.Trim('<', '>'), isSystemInclude: true));
        foreach (var providerInclude in providerIncludes)
            includes.Add(new CppIncludeDirectiveNode(providerInclude));

        foreach (var refModel in referencedModels.Where(m => m != model.Name).OrderBy(x => x, StringComparer.Ordinal))
            includes.Add(new CppIncludeDirectiveNode(_naming.ModelHeaderPath(orig.GetValueOrDefault(refModel, refModel))));

        foreach (var refEnum in referencedEnums.OrderBy(x => x, StringComparer.Ordinal))
            includes.Add(new CppIncludeDirectiveNode(_naming.EnumHeaderPath(orig.GetValueOrDefault(refEnum, refEnum))));

        return includes;
    }

}
