using System.Collections.Frozen;
using System.Collections.Immutable;

namespace ValIO.Core.IR;

/// <summary>Provides shared declaration indexes for one finalized IR document.</summary>
/// <remarks>
/// Index collections are immutable and reference the original declarations. Consumers must not
/// mutate the document or its schema objects after creating this context; the indexes do not
/// recursively freeze the document's object graph.
/// </remarks>
public sealed class IrDocumentContext
{
    /// <summary>Indexes declarations once and preserves the document's existing dependency order.</summary>
    /// <param name="document">The finalized document whose declarations will be indexed.</param>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Model names or enum names are duplicated, or a model and enum share a declaration name.
    /// </exception>
    public IrDocumentContext(IrDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Document = document;

        var models = new Dictionary<string, IrModel>(StringComparer.Ordinal);
        foreach (var model in document.Models)
        {
            if (!models.TryAdd(model.Name, model))
                throw new InvalidOperationException($"Duplicate model name '{model.Name}' in the IR document.");
        }

        var enums = new Dictionary<string, IrEnum>(StringComparer.Ordinal);
        foreach (var enumModel in document.Enums)
        {
            if (!enums.TryAdd(enumModel.Name, enumModel))
                throw new InvalidOperationException($"Duplicate enum name '{enumModel.Name}' in the IR document.");
            if (models.ContainsKey(enumModel.Name))
                throw new InvalidOperationException($"IR declaration name '{enumModel.Name}' is used by both a model and an enum.");
        }

        ModelsByName = models.ToFrozenDictionary(StringComparer.Ordinal);
        EnumsByName = enums.ToFrozenDictionary(StringComparer.Ordinal);
        EnumNames = enums.Keys.ToFrozenSet(StringComparer.Ordinal);
        KnownTypeNames = models.Keys.Concat(enums.Keys).ToFrozenSet(StringComparer.Ordinal);
        ModelsInDependencyOrder = BuildModelOrder(document, ModelsByName);
    }

    /// <summary>Gets the finalized document shared by every index in this context.</summary>
    /// <value>The original document instance.</value>
    public IrDocument Document { get; }

    /// <summary>Gets model declarations indexed by their exact generated names.</summary>
    /// <value>An immutable ordinal dictionary referring to the original models.</value>
    public FrozenDictionary<string, IrModel> ModelsByName { get; }

    /// <summary>Gets enum declarations indexed by their exact generated names.</summary>
    /// <value>An immutable ordinal dictionary referring to the original enums.</value>
    public FrozenDictionary<string, IrEnum> EnumsByName { get; }

    /// <summary>Gets the generated names identifying enum references in this document.</summary>
    /// <value>An immutable ordinal set derived from <see cref="EnumsByName"/>.</value>
    public FrozenSet<string> EnumNames { get; }

    /// <summary>Gets the generated names of all model and enum declarations.</summary>
    /// <value>An immutable ordinal union of model and enum names.</value>
    public FrozenSet<string> KnownTypeNames { get; }

    /// <summary>Gets models in the existing graph order followed by omitted models in source order.</summary>
    /// <value>An immutable sequence containing each declared model exactly once.</value>
    /// <remarks>Enum names, unknown graph vertices, and repeated graph vertices do not add models.</remarks>
    public IReadOnlyList<IrModel> ModelsInDependencyOrder { get; }

    /// <summary>Projects the canonical graph order onto models without recomputing dependencies.</summary>
    /// <param name="document">The document supplying graph order and source-order fallback.</param>
    /// <param name="modelsByName">The already validated ordinal model index.</param>
    /// <returns>An immutable sequence containing each model once.</returns>
    private static IReadOnlyList<IrModel> BuildModelOrder(
        IrDocument document,
        IReadOnlyDictionary<string, IrModel> modelsByName)
    {
        var orderedModels = new List<IrModel>(modelsByName.Count);
        var includedNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in document.TypeDependencies.TopologicalOrder)
        {
            if (modelsByName.TryGetValue(name, out var model) && includedNames.Add(name))
                orderedModels.Add(model);
        }

        foreach (var model in document.Models)
        {
            if (includedNames.Add(model.Name))
                orderedModels.Add(model);
        }

        return orderedModels.ToImmutableArray();
    }
}
