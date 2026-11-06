using System.Collections.Immutable;
using ValIO.Core.IR.Types;

namespace ValIO.Core.IR.Builders;

/// <summary>
/// Builds declaration dependencies and a dependency-first generation order for every IR producer.
/// Keeping the traversal here ensures <see cref="IrBuilder"/> and direct IR consumers share one implementation.
/// </summary>
public sealed class IrTypeDependencyGraphBuilder
{
    /// <summary>
    /// Builds the graph from declared models and enums; operations introduce no declaration vertices.
    /// </summary>
    /// <param name="models">The model declarations, including inline models.</param>
    /// <param name="enums">The enum declarations.</param>
    /// <returns>The dependency graph and its generation order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="models"/> or <paramref name="enums"/> is null.</exception>
    public IrTypeDependencyGraph Build(
        IReadOnlyList<IrModel> models,
        IReadOnlyList<IrEnum> enums)
    {
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(enums);

        var dependencies = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var allTypeNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var model in models)
        {
            allTypeNames.Add(model.Name);
            dependencies[model.Name] = new HashSet<string>(StringComparer.Ordinal);
        }

        foreach (var enumModel in enums)
        {
            allTypeNames.Add(enumModel.Name);
            dependencies[enumModel.Name] = new HashSet<string>(StringComparer.Ordinal);
        }

        foreach (var model in models)
        {
            dependencies[model.Name] = ExtractDependenciesFromModel(model);
        }

        var topologicalOrder = TopologicalSort(dependencies, allTypeNames);

        var immutableDependencies = dependencies.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<string>)kvp.Value.ToList());

        return new IrTypeDependencyGraph
        {
            Dependencies = immutableDependencies.ToImmutableDictionary(),
            TopologicalOrder = topologicalOrder,
        };
    }

    /// <summary>
    /// Collects referenced declarations from model properties and composition members.
    /// </summary>
    /// <param name="model">The model whose dependencies are required.</param>
    /// <returns>The distinct, case-sensitive names referenced by the model.</returns>
    public HashSet<string> ExtractDependenciesFromModel(IrModel model)
    {
        var dependencies = new HashSet<string>(StringComparer.Ordinal);

        foreach (var property in model.Properties)
            dependencies.UnionWith(ExtractDependenciesFromType(property.Type));

        if (model.Composition != null)
        {
            foreach (var type in model.Composition.Types)
                dependencies.UnionWith(ExtractDependenciesFromType(type));
        }

        return dependencies;
    }

    /// <summary>
    /// Recursively collects declaration references through all IR container and composition wrappers.
    /// </summary>
    /// <param name="type">The type to inspect.</param>
    /// <returns>The distinct declaration names referenced by the type.</returns>
    public HashSet<string> ExtractDependenciesFromType(IrType type)
    {
        var dependencies = new HashSet<string>(StringComparer.Ordinal);
        Walk(type, dependencies);
        return dependencies;
    }

    /// <summary>Visits a type tree without allocating intermediate dependency collections.</summary>
    /// <param name="type">The current type node.</param>
    /// <param name="sink">The shared set receiving referenced declaration names.</param>
    private static void Walk(IrType type, HashSet<string> sink)
    {
        switch (type)
        {
            case IrModelReference modelRef:
                sink.Add(modelRef.ModelName);
                break;
            case IrArrayType arr:
                Walk(arr.ElementType, sink);
                break;
            case IrDictionaryType dict:
                Walk(dict.KeyType, sink);
                Walk(dict.ValueType, sink);
                break;
            case IrUnionType u:
                foreach (var t in u.Types) Walk(t, sink);
                break;
            case IrIntersectionType i:
                foreach (var t in i.Types) Walk(t, sink);
                break;
            case IrNullableType n:
                Walk(n.InnerType, sink);
                break;
            case IrOptionalType o:
                Walk(o.InnerType, sink);
                break;
        }
    }

    /// <summary>
    /// Orders dependencies before their consumers, emitting each declaration once even in recursive cycles.
    /// </summary>
    /// <param name="dependencies">The declaration dependency edges.</param>
    /// <param name="allTypeNames">The declared names in source insertion order.</param>
    /// <returns>A dependency-first order for acyclic edges; recursive edges retain a deterministic traversal order.</returns>
    private static IReadOnlyList<string> TopologicalSort(
        Dictionary<string, HashSet<string>> dependencies,
        HashSet<string> allTypeNames)
    {
        var result = new List<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);

        foreach (var typeName in allTypeNames)
        {
            if (!visited.Contains(typeName))
                Visit(typeName, dependencies, allTypeNames, visited, visiting, result);
        }

        return result;
    }

    /// <summary>Visits one declaration, ignoring active back-edges to terminate recursive cycles.</summary>
    /// <param name="typeName">The declaration name to visit.</param>
    /// <param name="dependencies">The declaration dependency edges.</param>
    /// <param name="allTypeNames">The declared names eligible for ordering.</param>
    /// <param name="visited">The declarations already emitted.</param>
    /// <param name="visiting">The current recursion path.</param>
    /// <param name="result">The output order receiving completed declarations.</param>
    private static void Visit(
        string typeName,
        Dictionary<string, HashSet<string>> dependencies,
        HashSet<string> allTypeNames,
        HashSet<string> visited,
        HashSet<string> visiting,
        List<string> result)
    {
        if (visiting.Contains(typeName) || visited.Contains(typeName))
            return;

        visiting.Add(typeName);
        if (dependencies.TryGetValue(typeName, out var referencedNames))
        {
            foreach (var referencedName in referencedNames)
                if (allTypeNames.Contains(referencedName))
                    Visit(referencedName, dependencies, allTypeNames, visited, visiting, result);
        }

        visiting.Remove(typeName);
        visited.Add(typeName);
        result.Add(typeName);
    }
}
