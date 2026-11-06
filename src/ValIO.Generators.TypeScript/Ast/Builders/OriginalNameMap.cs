using ValIO.Core.IR;

namespace ValIO.Generators.TypeScript.Ast.Builders;

/// <summary>
/// Resolves source-derived type file names from the shared immutable declaration indexes.
/// Operation file names use their own OriginalMethodName and never enter the type namespace.
/// </summary>
internal static class OriginalNameMap
{
    /// <summary>Resolves a declaration's source name from the canonical document indexes.</summary>
    /// <param name="context">The shared model and enum declaration context.</param>
    /// <param name="generatedTypeName">The generated type identifier to resolve.</param>
    /// <returns>The original model or enum name, or the identifier when it is undeclared.</returns>
    public static string GetTypeOriginalName(IrDocumentContext context, string generatedTypeName)
    {
        if (context.ModelsByName.TryGetValue(generatedTypeName, out var model))
            return model.OriginalName;
        return context.EnumsByName.TryGetValue(generatedTypeName, out var enumModel)
            ? enumModel.OriginalName
            : generatedTypeName;
    }
}
