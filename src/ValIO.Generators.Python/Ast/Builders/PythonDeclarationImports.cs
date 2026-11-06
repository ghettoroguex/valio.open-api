using ValIO.Core.Interfaces;
using ValIO.Core.IR;

namespace ValIO.Generators.Python.Ast.Builders;

/// <summary>Resolves Python import paths from the shared generated-name indexes without rebuilding module maps.</summary>
internal static class PythonDeclarationImports
{
    /// <summary>Finds the source declaration name used for its stable Python module file name.</summary>
    /// <param name="documentContext">The finalized document's shared model and enum indexes.</param>
    /// <param name="namingConverter">The converter for source declaration names into module file names.</param>
    /// <param name="declarationName">The exact generated model or enum name.</param>
    /// <param name="modulePrefix">The relative package prefix preceding the module file name.</param>
    /// <param name="modulePath">The resolved module import path, or an empty string when no declaration exists.</param>
    /// <returns>Whether the generated declaration exists in the document.</returns>
    public static bool TryGetModulePath(
        IrDocumentContext documentContext,
        INamingConverter namingConverter,
        string declarationName,
        string modulePrefix,
        out string modulePath)
    {
        var originalName = documentContext.ModelsByName.TryGetValue(declarationName, out var model)
            ? model.OriginalName
            : documentContext.EnumsByName.TryGetValue(declarationName, out var enumModel)
                ? enumModel.OriginalName
                : null;
        modulePath = originalName is null ? string.Empty : modulePrefix + namingConverter.ToSnakeCase(originalName);
        return originalName is not null;
    }
}
