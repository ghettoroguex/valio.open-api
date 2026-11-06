using ValIO.Core.IR;
using ValIO.Core.IR.Builders;

namespace ValIO.Generators.Cpp.Tests.TestHelpers;

/// <summary>Creates explicit partial IR documents for document-aware C++ builder and mapper tests.</summary>
internal static class CppDocumentContextFactory
{
    /// <summary>Builds indexes for only the declarations explicitly supplied by a test.</summary>
    /// <param name="models">The model declarations, or an empty collection.</param>
    /// <param name="enums">The enum declarations, or an empty collection.</param>
    /// <returns>A finalized context with the canonical dependency graph and no unrelated clients.</returns>
    public static IrDocumentContext Create(IReadOnlyList<IrModel>? models = null, IReadOnlyList<IrEnum>? enums = null)
    {
        var documentModels = models ?? [];
        var documentEnums = enums ?? [];
        return new IrDocumentContext(new IrDocument
        {
            Metadata = new IrDocumentMetadata(),
            Models = documentModels,
            Enums = documentEnums,
            Clients = [],
            TypeDependencies = new IrTypeDependencyGraphBuilder().Build(documentModels, documentEnums),
        });
    }
}
