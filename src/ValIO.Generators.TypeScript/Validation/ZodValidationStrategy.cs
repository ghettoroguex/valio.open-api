using System.Collections.Immutable;
using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Expressions;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Core.IR;
using ValIO.Core.IR.Types;
using static ValIO.Generators.TypeScript.Validation.ValidationAst;

namespace ValIO.Generators.TypeScript.Validation;

/// <summary>Валидация через Zod: <c>Schema.parse(input)</c> / <c>z.union([...]).parse(input)</c>.</summary>
internal sealed class ZodValidationStrategy : IClientValidationStrategy
{
    private readonly ZodSchemaExpressionBuilder _schema;
    private readonly string _importPath;

    public ZodValidationStrategy(ZodSchemaExpressionBuilder schema, string importPath)
    {
        _schema = schema;
        _importPath = importPath;
    }

    public bool IsEnabled => true;

    /// <summary>Zod references the exported <c>…Constraints</c> constants for rule values.</summary>
    public bool UsesConstraintConstants => true;

    public string? UuidConstName => Options.TypeScriptOptionIds.UuidFormat.RegexConstName;

    public ExpressionNode Validate(IrType type, ExpressionNode input) =>
        Call(Member(_schema.Build(type), "parse"), input);

    public ExpressionNode BuildSchema(IrType type) => _schema.Build(type);

    public ExpressionNode BuildParamsSchema(IReadOnlyList<IrParameter> parameters, string? constraintsRefPrefix = null) =>
        _schema.BuildObjectFromParameters(parameters, constraintsRefPrefix);

    public ExpressionNode ValidateNamed(string schemaName, ExpressionNode input) =>
        Call(Member(Id(schemaName), "parse"), input);

    public IReadOnlyList<ImportNode> RuntimeImports(IReadOnlyCollection<IrType> inlineConstructedTypes, bool hasParams, bool hasNamedSchemaParse) =>
        // `z` нужен только когда строится inline `z.*` (union/array/... в теле запроса);
        // `{Schema}.parse` (модели, параметры zod, вынесенные result-схемы) в `z` не нуждаются.
        inlineConstructedTypes.Any(ValidationTypes.NeedsConstructedSchema)
            ? [ZodImport()]
            : [];

    public IReadOnlyList<ImportNode> SchemaDefinitionImports(IReadOnlyCollection<IrType> definedTypes) =>
        // A schema-declaring file uses `z` (value) to build the schema and `ZodSchema` (type) to annotate the
        // const type (`export const XSchema: ZodSchema<X> = ...`). `ZodSchema` — только тип, поэтому под
        // `verbatimModuleSyntax` его нужно импортировать type-only (иначе TS1485), отдельной строкой от `z`.
        definedTypes.Count > 0 ? [ZodImport(), ZodSchemaTypeImport()] : [];

    /// <inheritdoc/>
    public TypeNode? SchemaTypeAnnotation(string validatedTypeName) =>
        ZodSchemaTypes.Build(new SimpleTypeNode(new IdentifierNode(validatedTypeName)), _importPath);

    private ImportNode ZodImport() =>
        new(ImmutableList.Create(new IdentifierNode("z")), _importPath, ImportKind.Named);

    private ImportNode ZodSchemaTypeImport() =>
        new(ImmutableList.Create(new IdentifierNode("ZodSchema")), _importPath, ImportKind.Named, isTypeOnly: true);
}
