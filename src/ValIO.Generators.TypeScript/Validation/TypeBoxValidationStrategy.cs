using System.Collections.Immutable;
using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Expressions;
using ValIO.Core.IR;
using ValIO.Core.IR.Types;
using ValIO.Generators.TypeScript.TypeBox.Ast.Builders;
using static ValIO.Generators.TypeScript.Validation.ValidationAst;

namespace ValIO.Generators.TypeScript.Validation;

/// <summary>Validates original values through TypeBox without cloning binary objects or coercing model values.</summary>
internal sealed class TypeBoxValidationStrategy : IClientValidationStrategy
{
    private readonly TypeBoxAstBuilder _builder;

    public TypeBoxValidationStrategy(TypeBoxAstBuilder builder)
    {
        _builder = builder;
    }

    public bool IsEnabled => true;

    /// <summary>TypeBox inlines literal rule values (like its model schemas), so no constants object is imported.</summary>
    public bool UsesConstraintConstants => false;

    public string? UuidConstName => Options.TypeScriptOptionIds.UuidFormat.PatternConstName;

    /// <inheritdoc/>
    public ExpressionNode Validate(IrType type, ExpressionNode input) =>
        Call(Member(Id("Value"), "Decode"), _builder.BuildSchemaFromIrType(type), input);

    public ExpressionNode BuildSchema(IrType type) => _builder.BuildSchemaFromIrType(type);

    public ExpressionNode BuildParamsSchema(IReadOnlyList<IrParameter> parameters, string? constraintsRefPrefix = null) =>
        _builder.BuildObjectFromParameters(parameters);

    /// <inheritdoc/>
    public ExpressionNode ValidateNamed(string schemaName, ExpressionNode input) =>
        Call(Member(Id("Value"), "Decode"), Id(schemaName), input);

    /// <inheritdoc/>
    public IReadOnlyList<ImportNode> RuntimeImports(IReadOnlyCollection<IrType> inlineConstructedTypes, bool hasParams, bool hasNamedSchemaParse)
    {
        var imports = ImmutableList.CreateBuilder<ImportNode>();

        // `Type` нужен только для inline-построения схем тела (Type.Union/Array/...); схемы параметров
        // вынесены в {op}.params.schema.ts, поэтому клиенту `Type` для них не нужен.
        if (inlineConstructedTypes.Any(ValidationTypes.NeedsConstructedSchema))
            imports.Add(TypeImport());
        if (inlineConstructedTypes.Any(TypeBoxInstanceAst.RequiresRegistry))
            imports.Add(RegistryImport());
        if (inlineConstructedTypes.Any(_builder.RequiresFormatRegistry))
            imports.Add(FormatRegistryImport());

        // Decode validates original input without mutating or coercing it, retaining Blob and File prototypes.
        if (inlineConstructedTypes.Count > 0 || hasParams || hasNamedSchemaParse)
            imports.Add(ValueImport());

        return imports.ToImmutable();
    }

    /// <inheritdoc/>
    public IReadOnlyList<ImportNode> SchemaDefinitionImports(IReadOnlyCollection<IrType> definedTypes)
    {
        if (definedTypes.Count == 0)
            return [];
        var imports = new List<ImportNode> { TypeImport() };
        if (definedTypes.Any(TypeBoxInstanceAst.RequiresRegistry))
            imports.Add(RegistryImport());
        if (definedTypes.Any(_builder.RequiresFormatRegistry))
            imports.Add(FormatRegistryImport());
        return imports;
    }

    /// <inheritdoc/>
    /// <remarks>TypeBox derives the static type via <c>Static&lt;typeof Schema&gt;</c>, so the const is not annotated.</remarks>
    public TypeNode? SchemaTypeAnnotation(string validatedTypeName) => null;

    private static ImportNode TypeImport() =>
        new(ImmutableList.Create(new IdentifierNode("Type")), "@sinclair/typebox", ImportKind.Named);

    private static ImportNode ValueImport() =>
        new(ImmutableList.Create(new IdentifierNode("Value")), "@sinclair/typebox/value", ImportKind.Named);

    /// <summary>Imports TypeBox's registered custom-kind support for Blob and File schemas.</summary>
    /// <returns>The custom-kind registry import.</returns>
    private static ImportNode RegistryImport() => new([new IdentifierNode("Kind"), new IdentifierNode("TypeRegistry")],
        "@sinclair/typebox", ImportKind.Named);

    /// <summary>Imports the registry used by standard string format predicates.</summary>
    /// <returns>The standard format registry import.</returns>
    private static ImportNode FormatRegistryImport() => new([new IdentifierNode("FormatRegistry")], "@sinclair/typebox", ImportKind.Named);
}
