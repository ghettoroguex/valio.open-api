using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.IR;
using ValIO.Generators.TypeScript.Options;
using ValIO.Generators.TypeScript.Validation;

namespace ValIO.Generators.TypeScript.Ast.Builders;

/// <summary>Builds the shared UUID constant import required by inline request body validation.</summary>
internal static class TypeScriptInlineUuidImport
{
    /// <summary>Creates an import when an inline body schema references the configured UUID constant.</summary>
    /// <param name="operations">The operations generated in the source file.</param>
    /// <param name="validation">The selected client validation strategy.</param>
    /// <param name="uuidFormat">The configured UUID validation mode.</param>
    /// <param name="modelsPath">The configured models directory.</param>
    /// <param name="relativePrefix">The relative path from the generated file to the output root.</param>
    /// <returns>The required runtime import, or null when no inline body needs it.</returns>
    public static ImportNode? Create(
        IEnumerable<IrOperation> operations,
        IClientValidationStrategy validation,
        string? uuidFormat,
        string? modelsPath,
        string relativePrefix)
    {
        if (!validation.IsEnabled || validation.UuidConstName is not { } uuidConst
            || TypeScriptOptionIds.UuidFormat.Resolve(uuidFormat) != TypeScriptOptionIds.UuidFormat.Regex
            || !operations.Any(operation => operation.RequestBody is { Type: var bodyType }
                && ValidationTypes.NeedsConstructedSchema(bodyType)
                && UuidFormatSupport.ContainsUuid(bodyType)))
            return null;

        return new ImportNode(
            items: [new IdentifierNode(uuidConst)],
            from: $"{relativePrefix}/{modelsPath ?? "models"}/{TypeScriptOptionIds.UuidFormat.ConstFileName}",
            kind: ImportKind.Named);
    }
}
