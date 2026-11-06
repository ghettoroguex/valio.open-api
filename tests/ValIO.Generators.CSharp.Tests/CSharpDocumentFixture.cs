using ValIO.Core.IR;

namespace ValIO.Generators.CSharp.Tests;

/// <summary>Creates explicit, bounded documents for C# syntax-fragment tests.</summary>
internal static class CSharpDocumentFixture
{
    /// <summary>Constructs a context with the declarations required by one fragment scenario.</summary>
    /// <param name="models">The model declarations visible to the fragment.</param>
    /// <param name="enums">The enum declarations visible to the fragment.</param>
    /// <returns>A fully initialized document context with stable source ordering.</returns>
    internal static IrDocumentContext Context(IReadOnlyList<IrModel> models, IReadOnlyList<IrEnum> enums) =>
        new(new IrDocument
        {
            Metadata = new IrDocumentMetadata(),
            Models = models,
            Enums = enums,
            Clients = [],
            TypeDependencies = new IrTypeDependencyGraph
            {
                Dependencies = new Dictionary<string, IReadOnlyList<string>>(),
                TopologicalOrder = models.Select(model => model.Name).ToArray()
            }
        });
}
