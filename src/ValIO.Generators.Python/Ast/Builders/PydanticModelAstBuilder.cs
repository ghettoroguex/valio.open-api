using System.Globalization;
using System.Text.Json;
using ValIO.Core.Ast.Builders;
using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Core.Interfaces;
using ValIO.Core.IR;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Generators.Python.Ast.Nodes;

namespace ValIO.Generators.Python.Ast.Builders;

/// <summary>
/// Builds Pydantic v2 declarations with deferred cross-module references and wire aliases.
/// </summary>
public sealed class PydanticModelAstBuilder : AstBuilderBase
{
    /// <summary>Converts property identifiers into safe Python names.</summary>
    private readonly INamingConverter _namingConverter;
    /// <summary>Maps shared schema types into Python type annotations.</summary>
    private readonly PythonIrTypeMapper _typeMapper;

    /// <summary>Initializes the model builder with language naming and type services.</summary>
    /// <param name="documentContext">The finalized document context shared by the generation request.</param>
    /// <param name="namingConverter">The converter for Python property identifiers.</param>
    /// <param name="typeMapper">The mapper for schema type annotations.</param>
    public PydanticModelAstBuilder(IrDocumentContext documentContext, INamingConverter namingConverter, PythonIrTypeMapper typeMapper)
        : base(documentContext)
    {
        _namingConverter = namingConverter;
        _typeMapper = typeMapper;
    }

    /// <summary>Builds a model module whose forward references are completed by the models package.</summary>
    /// <param name="model">The model with any inherited properties already resolved.</param>
    /// <returns>The Python module containing its model class and static import information.</returns>
    public PythonModuleNode BuildModule(IrModel model)
    {
        var datetimeImports = new SortedSet<string>(StringComparer.Ordinal);
        var uuidNeeded = false;
        var anyNeeded = false;

        // Runtime references are resolved after every declaration has been imported by the package.
        var members = new List<DeclarationNode>();
        var typeCheckingImports = new List<PythonImportNode>();
        var typeCheckingTargets = new SortedDictionary<string, string>(StringComparer.Ordinal);

        members.Add(new PythonAttributeAssignmentNode(
            new IdentifierNode("model_config"),
            annotation: null,
            defaultValueLiteral: "ConfigDict(populate_by_name=True)"));

        foreach (var property in model.Properties)
        {
            var mappedType = _typeMapper.MapType(property.Type);
            var isOptional = _typeMapper.IsOptionalInLanguage(property);

            // Keep runtime imports out of individual model modules to support cyclic references.
            CollectModelReferences(property.Type, typeCheckingTargets, model.Name);

            ScanTypeForImports(property.Type, datetimeImports, ref uuidNeeded, ref anyNeeded);

            var hasForwardRef = HasModelReference(property.Type, typeCheckingTargets);
            // A missing value and an explicit null are independent schema states.
            var defaultLiteral = BuildFieldDefault(property, isOptional);

            members.Add(new PythonAttributeAssignmentNode(
                new IdentifierNode(SanitizePropertyName(property.Name)),
                annotation: mappedType,
                defaultValueLiteral: defaultLiteral,
                quoteAnnotation: hasForwardRef,
                documentation: !string.IsNullOrWhiteSpace(property.Description)
                    ? new CommentNode(property.Description, CommentKind.SingleLine)
                    : null));
        }

        var standardImports = new List<PythonImportNode>
        {
            new PythonImportNode("pydantic", new[] { "BaseModel", "ConfigDict" })
        };
        if (model.Properties.Any(RequiresField))
            standardImports[0] = new PythonImportNode("pydantic", new[] { "BaseModel", "ConfigDict", "Field" });

        if (datetimeImports.Count > 0)
            standardImports.Insert(0, new PythonImportNode("datetime", datetimeImports.ToList()));
        if (uuidNeeded)
            standardImports.Insert(0, new PythonImportNode("uuid", new[] { "UUID" }));

        if (anyNeeded)
            standardImports.Insert(0, new PythonImportNode("typing", new[] { "Any" }));

        if (typeCheckingTargets.Count > 0)
        {
            standardImports.Insert(0, new PythonImportNode("typing", new[] { "TYPE_CHECKING" }));
            foreach (var kvp in typeCheckingTargets)
                typeCheckingImports.Add(new PythonImportNode(kvp.Value, new[] { kvp.Key }));
        }

        var classNode = new PythonClassDeclarationNode(
            new IdentifierNode(model.Name),
            baseClasses: new TypeNode[] { new SimpleTypeNode(new IdentifierNode("BaseModel")) },
            members: members,
            documentation: !string.IsNullOrWhiteSpace(model.Description)
                ? new CommentNode(model.Description, CommentKind.MultiLine)
                : null);

        return new PythonModuleNode(
            declarations: new DeclarationNode[] { classNode },
            imports: standardImports,
            typeCheckingImports: typeCheckingImports,
            futureImports: new[] { new PythonImportNode("__future__", new[] { "annotations" }) });
    }

    /// <summary>Converts a property identifier into a non-keyword Python name.</summary>
    /// <param name="name">The generated IR property identifier.</param>
    /// <returns>A safe snake_case identifier.</returns>
    private string SanitizePropertyName(string name) =>
        _namingConverter.ToSnakeCase(name) is var snake && !string.IsNullOrEmpty(snake)
            ? PythonKeywords.Avoid(snake)
            : "_";

    /// <summary>Determines whether a property needs Pydantic wire-name or validation metadata.</summary>
    /// <param name="property">The property to inspect.</param>
    /// <returns>Whether a <c>Field</c> expression is required.</returns>
    private bool RequiresField(IrProperty property)
        => SanitizePropertyName(property.Name) != property.OriginalName || property.Constraints is not null;

    /// <summary>Creates an optional default and schema constraints without making absence imply nullability.</summary>
    /// <param name="property">The source property and its wire constraints.</param>
    /// <param name="isOptional">Whether the property may be omitted.</param>
    /// <returns>A Pydantic field expression, an optional default, or null for an unconstrained required field.</returns>
    private string? BuildFieldDefault(IrProperty property, bool isOptional)
    {
        if (!RequiresField(property)) return isOptional ? "None" : null;
        var arguments = new List<string>();
        if (isOptional) arguments.Add("default=None");
        if (SanitizePropertyName(property.Name) != property.OriginalName)
            arguments.Add($"alias={JsonSerializer.Serialize(property.OriginalName)}");
        if (property.Constraints is { } constraints)
        {
            if (constraints.Min is { } min) arguments.Add($"ge={min.ToString(CultureInfo.InvariantCulture)}");
            if (constraints.Max is { } max) arguments.Add($"le={max.ToString(CultureInfo.InvariantCulture)}");
            if (constraints.ExclusiveMin is { } exclusiveMin) arguments.Add($"gt={exclusiveMin.ToString(CultureInfo.InvariantCulture)}");
            if (constraints.ExclusiveMax is { } exclusiveMax) arguments.Add($"lt={exclusiveMax.ToString(CultureInfo.InvariantCulture)}");
            if (constraints.MinLength is { } minLength) arguments.Add($"min_length={minLength}");
            if (constraints.MaxLength is { } maxLength) arguments.Add($"max_length={maxLength}");
            if (constraints.Pattern is { } pattern) arguments.Add($"pattern={JsonSerializer.Serialize(pattern)}");
        }
        return $"Field({string.Join(", ", arguments)})";
    }

    /// <summary>Collects imports required to resolve non-self declaration references after package initialization.</summary>
    /// <param name="type">The property type graph being traversed.</param>
    /// <param name="targets">The map receiving referenced generated names and module paths.</param>
    /// <param name="selfName">The current model name, which requires no sibling import.</param>
    private void CollectModelReferences(
        IrType type,
        SortedDictionary<string, string> targets,
        string selfName)
    {
        switch (type)
        {
            case IrModelReference r when r.ModelName != selfName:
                if (PythonDeclarationImports.TryGetModulePath(DocumentContext, _namingConverter, r.ModelName, ".", out var module))
                    targets[r.ModelName] = module;
                break;
            case IrArrayType a:
                CollectModelReferences(a.ElementType, targets, selfName);
                break;
            case IrDictionaryType d:
                CollectModelReferences(d.KeyType, targets, selfName);
                CollectModelReferences(d.ValueType, targets, selfName);
                break;
            case IrNullableType n:
                CollectModelReferences(n.InnerType, targets, selfName);
                break;
            case IrOptionalType o:
                CollectModelReferences(o.InnerType, targets, selfName);
                break;
            case IrUnionType u:
                foreach (var t in u.Types) CollectModelReferences(t, targets, selfName);
                break;
            case IrIntersectionType i:
                foreach (var t in i.Types) CollectModelReferences(t, targets, selfName);
                break;
        }
    }

    /// <summary>Determines whether a type contains a cross-module declaration reference.</summary>
    /// <param name="type">The property type graph.</param>
    /// <param name="targets">The collected cross-module declarations.</param>
    /// <returns>Whether the annotation contains an imported declaration reference.</returns>
    private static bool HasModelReference(IrType type, SortedDictionary<string, string> targets) => type switch
    {
        IrModelReference r => targets.ContainsKey(r.ModelName),
        IrArrayType a => HasModelReference(a.ElementType, targets),
        IrDictionaryType d => HasModelReference(d.ValueType, targets) || HasModelReference(d.KeyType, targets),
        IrNullableType n => HasModelReference(n.InnerType, targets),
        IrOptionalType o => HasModelReference(o.InnerType, targets),
        IrUnionType u => u.Types.Any(t => HasModelReference(t, targets)),
        IrIntersectionType i => i.Types.Any(t => HasModelReference(t, targets)),
        _ => false
    };

    /// <summary>Collects standard-library imports required by a schema type graph.</summary>
    /// <param name="type">The type graph to traverse.</param>
    /// <param name="datetimeImports">The date and time class names required by annotations.</param>
    /// <param name="uuidNeeded">Whether UUID annotations require the uuid import.</param>
    /// <param name="anyNeeded">Whether unconstrained annotations require typing.Any.</param>
    private static void ScanTypeForImports(
        IrType type,
        SortedSet<string> datetimeImports,
        ref bool uuidNeeded,
        ref bool anyNeeded)
    {
        switch (type)
        {
            case IrPrimitiveType p:
                switch (p.PrimitiveKind)
                {
                    case IrPrimitiveKind.Date: datetimeImports.Add("date"); break;
                    case IrPrimitiveKind.DateTime: datetimeImports.Add("datetime"); break;
                    case IrPrimitiveKind.Time: datetimeImports.Add("time"); break;
                    case IrPrimitiveKind.Uuid: uuidNeeded = true; break;
                    case IrPrimitiveKind.Any: anyNeeded = true; break;
                }
                break;
            case IrArrayType a:
                ScanTypeForImports(a.ElementType, datetimeImports, ref uuidNeeded, ref anyNeeded);
                break;
            case IrDictionaryType d:
                ScanTypeForImports(d.KeyType, datetimeImports, ref uuidNeeded, ref anyNeeded);
                ScanTypeForImports(d.ValueType, datetimeImports, ref uuidNeeded, ref anyNeeded);
                break;
            case IrNullableType n:
                ScanTypeForImports(n.InnerType, datetimeImports, ref uuidNeeded, ref anyNeeded);
                break;
            case IrOptionalType o:
                ScanTypeForImports(o.InnerType, datetimeImports, ref uuidNeeded, ref anyNeeded);
                break;
            case IrUnionType u:
                foreach (var t in u.Types) ScanTypeForImports(t, datetimeImports, ref uuidNeeded, ref anyNeeded);
                break;
            case IrIntersectionType i:
                foreach (var t in i.Types) ScanTypeForImports(t, datetimeImports, ref uuidNeeded, ref anyNeeded);
                break;
        }
    }
}

/// <summary>
/// Protects generated identifiers from Python's reserved keyword vocabulary.
/// </summary>
internal static class PythonKeywords
{
    /// <summary>Contains reserved identifiers that require a generated suffix.</summary>
    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "False","None","True","and","as","assert","async","await","break","class","continue","def","del","elif",
        "else","except","finally","for","from","global","if","import","in","is","lambda","nonlocal","not","or",
        "pass","raise","return","try","while","with","yield","match","case","type",
    };

    /// <summary>Appends a suffix when a generated identifier is a Python keyword.</summary>
    /// <param name="name">The candidate generated identifier.</param>
    /// <returns>The original name or its safe suffixed form.</returns>
    public static string Avoid(string name) => Reserved.Contains(name) ? name + "_" : name;
}
