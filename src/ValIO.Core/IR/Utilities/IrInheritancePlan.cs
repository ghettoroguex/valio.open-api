using ValIO.Core.IR.Types;

namespace ValIO.Core.IR.Utilities;

/// <summary>
/// Plans single-base inheritance or flattened allOf properties for target languages.
/// </summary>
public sealed record IrInheritancePlan
{
    /// <summary>
    /// Gets the resolved base model's name, or no base when properties are flattened.
    /// </summary>
    /// <value>The generated identity of the inherited model.</value>
    public string? BaseModelName { get; }

    /// <summary>
    /// Gets own properties for inheritance, or all properties in stable base-first order.
    /// </summary>
    /// <value>The properties that the target model must declare.</value>
    public IReadOnlyList<IrProperty> EffectiveProperties { get; }

    /// <summary>Gets whether the target model should inherit a resolved base.</summary>
    /// <value>Whether a base model identity is available.</value>
    public bool UseInheritance => BaseModelName != null;

    /// <summary>Creates a consistent model declaration plan.</summary>
    /// <param name="baseModelName">The inherited model, or no inherited model.</param>
    /// <param name="effectiveProperties">The properties to declare.</param>
    private IrInheritancePlan(string? baseModelName, IReadOnlyList<IrProperty> effectiveProperties)
    {
        BaseModelName = baseModelName;
        EffectiveProperties = effectiveProperties;
    }

    /// <summary>Resolves allOf without emitting multiple or cyclic class inheritance.</summary>
    /// <param name="model">The model being generated.</param>
    /// <param name="modelLookup">Available models indexed by generated identity.</param>
    /// <returns>A finite declaration plan shared by model and serialization builders.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static IrInheritancePlan Resolve(IrModel model, IReadOnlyDictionary<string, IrModel> modelLookup)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(modelLookup);
        if (model.Composition?.Kind != IR.IrCompositionKind.AllOf || model.Composition.Types.Count == 0)
            return new IrInheritancePlan(null, model.Properties);

        var byName = new Dictionary<string, IrProperty>(StringComparer.Ordinal);
        var order = new List<string>();
        var hasCycle = CollectProperties(model, modelLookup, new HashSet<string>(StringComparer.Ordinal), byName, order);

        // Target class inheritance requires exactly one resolved, acyclic reference.
        var refBases = model.Composition.Types.OfType<IrModelReference>().ToList();
        if (!hasCycle && refBases.Count == 1 && model.Composition.Types.Count == 1 &&
            modelLookup.TryGetValue(refBases[0].ModelName, out var baseModel))
        {
            var ownProperties = IrInheritanceResolver.ExcludeInheritedProperties(model, model.Properties, modelLookup);
            return new IrInheritancePlan(baseModel.Name, ownProperties);
        }

        return new IrInheritancePlan(null, order.Select(name => byName[name]).ToList());
    }

    /// <summary>
    /// Collects allOf base and own properties for targets that do not use class inheritance.
    /// </summary>
    /// <remarks>Wire names identify duplicates; the last declaration wins while first-seen order is preserved.</remarks>
    /// <param name="model">The model whose complete property set is required.</param>
    /// <param name="modelLookup">Available models indexed by generated identity.</param>
    /// <returns>A finite, base-first property list, including ordinary non-composed bases.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static IReadOnlyList<IrProperty> FlattenProperties(IrModel model, IReadOnlyDictionary<string, IrModel> modelLookup)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(modelLookup);
        var byName = new Dictionary<string, IrProperty>(StringComparer.Ordinal);
        var order = new List<string>();
        CollectProperties(model, modelLookup, new HashSet<string>(StringComparer.Ordinal), byName, order);
        return order.Select(name => byName[name]).ToList();
    }

    /// <summary>Collects reachable properties and detects cycles along the active allOf path.</summary>
    /// <param name="model">The current model.</param>
    /// <param name="modelLookup">Available model definitions.</param>
    /// <param name="path">Model identities active in this traversal.</param>
    /// <param name="byName">The latest declaration of each wire property.</param>
    /// <param name="order">Wire names in first-seen order.</param>
    /// <returns>Whether a cycle was encountered in this model's base graph.</returns>
    private static bool CollectProperties(
        IrModel model,
        IReadOnlyDictionary<string, IrModel> modelLookup,
        HashSet<string> path,
        Dictionary<string, IrProperty> byName,
        List<string> order)
    {
        if (!path.Add(model.Name)) return true;
        var hasCycle = false;
        if (model.Composition is { Kind: IrCompositionKind.AllOf } composition)
        {
            foreach (var reference in composition.Types.OfType<IrModelReference>())
            {
                if (modelLookup.TryGetValue(reference.ModelName, out var baseModel))
                    hasCycle |= CollectProperties(baseModel, modelLookup, path, byName, order);
            }
        }

        foreach (var p in model.Properties)
        {
            if (!byName.ContainsKey(p.OriginalName))
                order.Add(p.OriginalName);
            byName[p.OriginalName] = p;
        }

        path.Remove(model.Name);
        return hasCycle;
    }
}
