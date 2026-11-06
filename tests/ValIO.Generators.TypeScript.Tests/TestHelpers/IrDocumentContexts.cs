using ValIO.Core.IR;

namespace ValIO.Generators.TypeScript.Tests.TestHelpers;

/// <summary>Creates explicit finalized document contexts for component tests that construct IR directly.</summary>
internal static class IrDocumentContexts
{
    /// <summary>Builds a context containing the declarations required by one builder test.</summary>
    /// <param name="models">The models referenced by the test.</param>
    /// <param name="enums">The enums referenced by the test.</param>
    /// <returns>The indexed test document.</returns>
    public static IrDocumentContext Create(IReadOnlyList<IrModel>? models = null, IReadOnlyList<IrEnum>? enums = null) =>
        new(new IrDocument
        {
            Metadata = new IrDocumentMetadata(), Models = models ?? [], Enums = enums ?? [], Clients = [],
            TypeDependencies = new IrTypeDependencyGraph
            {
                Dependencies = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal),
                TopologicalOrder = models?.Select(model => model.Name).ToArray() ?? [],
            },
        });
}
