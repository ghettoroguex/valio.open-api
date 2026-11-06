using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Core.Interfaces;
using ValIO.Core.IR;
using ValIO.Core.IR.Types;
using ValIO.Generators.FSharp.Ast.Nodes;

namespace ValIO.Generators.FSharp.Ast.Builders;

/// <summary>
/// Builds an F# record (or a discriminated union when the model is oneOf/anyOf-composed)
/// for one <see cref="IrModel"/>. Emits a single <see cref="FSharpFileNode"/> per model.
/// </summary>
public sealed class FSharpModelAstBuilder
{
    /// <summary>Converts source names into F# declaration identifiers.</summary>
    private readonly INamingConverter _namingConverter;
    /// <summary>Maps source value types to F# record and union field types.</summary>
    private readonly FSharpIrTypeMapper _typeMapper;

    /// <summary>Creates a model builder using the shared naming and type services.</summary>
    /// <param name="namingConverter">The declaration name converter.</param>
    /// <param name="typeMapper">The F# type mapper.</param>
    public FSharpModelAstBuilder(INamingConverter namingConverter, FSharpIrTypeMapper typeMapper)
    {
        _namingConverter = namingConverter;
        _typeMapper = typeMapper;
    }

    /// <summary>Builds a standalone module and imports every referenced model or enum module.</summary>
    /// <param name="model">The effective model with inherited fields resolved.</param>
    /// <param name="moduleName">The fully qualified generated module name.</param>
    /// <returns>The model declaration and its explicit dependencies.</returns>
    public FSharpFileNode BuildFile(IrModel model, string moduleName)
    {
        var namespaceEnd = moduleName.LastIndexOf('.');
        var modelsNamespace = namespaceEnd < 0 ? "Generated.Models" : moduleName[..namespaceEnd];
        var referencedTypes = model.Properties.Select(property => property.Type)
            .Concat(model.Composition?.Types ?? []);
        var opens = FSharpModelImports.Collect(referencedTypes, modelsNamespace, moduleName).Append("System").ToArray();
        // OneOf/AnyOf compose into a DU; everything else is a record.
        if (model.Composition is { Kind: IrCompositionKind.OneOf or IrCompositionKind.AnyOf } composition)
        {
            var du = BuildDu(model, composition);
            return new FSharpFileNode(
                moduleName: moduleName,
                declarations: [du],
                opens: opens);
        }

        var record = BuildRecord(model);
        return new FSharpFileNode(
            moduleName: moduleName,
            declarations: [record],
            opens: opens);
    }

    /// <summary>Builds record fields with source presence represented through option types.</summary>
    /// <param name="model">The effective object model.</param>
    /// <returns>The record declaration.</returns>
    private FSharpRecordNode BuildRecord(IrModel model)
    {
        var fields = new List<FSharpRecordNode.Field>(model.Properties.Count);
        foreach (var prop in model.Properties)
        {
            var pascalName = _namingConverter.ToPascalCase(prop.Name);
            var safeName = FSharpReservedNames.Avoid(pascalName);
            var mapped = _typeMapper.MapType(prop.Type);
            var isOptional = _typeMapper.IsOptionalInLanguage(prop);
            var finalType = isOptional ? EnsureNullable(mapped) : mapped;
            fields.Add(new FSharpRecordNode.Field(safeName, finalType));
        }

        return new FSharpRecordNode(
            new IdentifierNode(_namingConverter.ToPascalCase(model.Name)),
            fields,
            documentation: !string.IsNullOrWhiteSpace(model.Description)
                ? new CommentNode(model.Description!, CommentKind.MultiLine)
                : null);
    }

    /// <summary>Builds the value representation for a composed model.</summary>
    /// <remarks>This representation alone does not enforce oneOf match counts during deserialization.</remarks>
    /// <param name="model">The source declaration.</param>
    /// <param name="composition">The alternative model types.</param>
    /// <returns>A union declaration containing each declared alternative.</returns>
    private FSharpDuNode BuildDu(IrModel model, IrComposition composition)
    {
        var cases = new List<FSharpDuNode.Case>(composition.Types.Count);
        var index = 0;
        foreach (var t in composition.Types)
        {
            index++;
            var caseName = ChooseCaseName(t, index);
            var payload = _typeMapper.MapType(t);
            cases.Add(new FSharpDuNode.Case(caseName, payload));
        }

        return new FSharpDuNode(
            new IdentifierNode(_namingConverter.ToPascalCase(model.Name)),
            cases,
            documentation: !string.IsNullOrWhiteSpace(model.Description)
                ? new CommentNode(model.Description!, CommentKind.MultiLine)
                : null);
    }

    /// <summary>Uses a referenced model name or a stable positional union case identifier.</summary>
    /// <param name="t">The alternative payload type.</param>
    /// <param name="index">The one-based alternative position.</param>
    /// <returns>The generated case identifier.</returns>
    private string ChooseCaseName(IrType t, int index) => t switch
    {
        IrModelReference r => _namingConverter.ToPascalCase(r.ModelName),
        _ => $"Case{index}",
    };

    /// <summary>Adds an option wrapper only when the mapper has not already supplied one.</summary>
    /// <param name="type">The mapped field type.</param>
    /// <returns>The presence-aware field type.</returns>
    private static TypeNode EnsureNullable(TypeNode type) =>
        type is NullableTypeNode ? type : new NullableTypeNode(type);
}
