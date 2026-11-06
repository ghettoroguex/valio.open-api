using ValIO.Core.Ast.Builders;
using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Core.Interfaces;
using ValIO.Core.IR;
using ValIO.Core.IR.Types;
using ValIO.Generators.Kotlin.Ast.Nodes;

namespace ValIO.Generators.Kotlin.Ast.Builders;

/// <summary>
/// Builds a Kotlin <c>data class</c> + <c>@Serializable</c> for an IR model.
/// Emits one <see cref="KotlinFileNode"/> per model.
/// </summary>
public sealed class KotlinDataClassAstBuilder : AstBuilderBase
{
    /// <summary>Converts source property identifiers into Kotlin names.</summary>
    private readonly INamingConverter _namingConverter;

    /// <summary>Maps shared schema types into Kotlin type annotations.</summary>
    private readonly KotlinIrTypeMapper _typeMapper;

    /// <summary>Initializes model generation with the document's shared declaration indexes.</summary>
    /// <param name="documentContext">The finalized document context shared by the generation request.</param>
    /// <param name="namingConverter">The converter for generated property identifiers.</param>
    /// <param name="typeMapper">The mapper for Kotlin type annotations.</param>
    public KotlinDataClassAstBuilder(IrDocumentContext documentContext, INamingConverter namingConverter, KotlinIrTypeMapper typeMapper)
        : base(documentContext)
    {
        _namingConverter = namingConverter;
        _typeMapper = typeMapper;
    }

    /// <summary>Builds a serializable data class with imports resolved against the shared document indexes.</summary>
    /// <param name="model">The model whose inherited properties have already been resolved.</param>
    /// <param name="modelsPackage">The Kotlin package containing all generated model declarations.</param>
    /// <returns>The Kotlin file declaring the requested model.</returns>
    public KotlinFileNode BuildFile(IrModel model, string modelsPackage)
    {
        var importScan = new KotlinTypeImportScanner.Result();
        var properties = new List<KotlinPropertyNode>(model.Properties.Count);

        foreach (var prop in model.Properties)
        {
            KotlinTypeImportScanner.Walk(prop.Type, importScan);

            var camelName = _namingConverter.ToCamelCase(prop.Name);
            var safeName = KotlinReservedNames.Avoid(camelName);
            var mappedType = _typeMapper.MapType(prop.Type);

            var annotations = new List<KotlinAnnotationNode>();
            // Emit @SerialName only when the wire name differs from the Kotlin identifier.
            if (!string.Equals(prop.OriginalName, safeName, StringComparison.Ordinal))
            {
                annotations.Add(new KotlinAnnotationNode(
                    "SerialName",
                    [$"\"{EscapeStringLiteral(prop.OriginalName)}\""]));
            }

            var isOptional = _typeMapper.IsOptionalInLanguage(prop);
            var finalType = isOptional ? EnsureNullable(mappedType) : mappedType;
            var defaultValue = isOptional ? "null" : null;

            properties.Add(new KotlinPropertyNode(
                new IdentifierNode(safeName),
                finalType,
                defaultValue,
                isReadOnly: true,
                isConstructorParameter: true,
                visibility: null,
                annotations: annotations,
                documentation: !string.IsNullOrWhiteSpace(prop.Description)
                    ? new CommentNode(prop.Description, CommentKind.MultiLine)
                    : null));
        }

        // Class-level annotations.
        var classAnnotations = new List<KotlinAnnotationNode>
        {
            new KotlinAnnotationNode("Serializable"),
        };

        // Standard imports: serialization + collected primitives.
        var imports = new List<KotlinImportNode>
        {
            new KotlinImportNode("kotlinx.serialization.Serializable"),
        };
        if (properties.Any(p => p.KotlinAnnotations.Any(a => a.Name == "SerialName")))
            imports.Add(new KotlinImportNode("kotlinx.serialization.SerialName"));
        foreach (var stdImport in importScan.StandardImports)
            imports.Add(new KotlinImportNode(stdImport));
        foreach (var modelName in importScan.ModelReferences)
        {
            // modelName carries the affix and is matched against the affixed self/known names.
            if (string.Equals(modelName, model.Name, StringComparison.Ordinal)) continue;
            if (!DocumentContext.KnownTypeNames.Contains(modelName)) continue;
            // Kotlin imports resolve by fully-qualified CLASS name (package + affixed type name),
            // independent of the .kt file name — so the import uses the affixed identifier.
            imports.Add(new KotlinImportNode($"{modelsPackage}.{modelName}"));
        }

        var classNode = new KotlinDataClassNode(
            new IdentifierNode(_namingConverter.ToPascalCase(model.Name)),
            properties,
            classAnnotations,
            documentation: !string.IsNullOrWhiteSpace(model.Description)
                ? new CommentNode(model.Description, CommentKind.MultiLine)
                : null);

        return new KotlinFileNode(
            modelsPackage,
            declarations: [classNode],
            imports: imports);
    }

    /// <summary>Adds optional-field nullability without duplicating an existing nullable annotation.</summary>
    /// <param name="type">The original property annotation.</param>
    /// <returns>The original nullable type or a new nullable wrapper.</returns>
    private static TypeNode EnsureNullable(TypeNode type) =>
        type is NullableTypeNode ? type : new NullableTypeNode(type);

    /// <summary>Escapes source wire names for Kotlin string literal arguments.</summary>
    /// <param name="s">The source wire name.</param>
    /// <returns>The escaped string literal contents.</returns>
    private static string EscapeStringLiteral(string s) =>
        s.Replace("\\", "\\\\", StringComparison.Ordinal)
         .Replace("\"", "\\\"", StringComparison.Ordinal)
         .Replace("\n", "\\n", StringComparison.Ordinal)
         .Replace("\r", "\\r", StringComparison.Ordinal);
}
