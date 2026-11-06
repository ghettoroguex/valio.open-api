using ValIO.Core.IR.Types;

namespace ValIO.Generators.FSharp.Ast.Builders;

/// <summary>Resolves declaration modules required by separately generated F# files.</summary>
internal static class FSharpModelImports
{
    /// <summary>Collects every referenced declaration without importing the current module recursively.</summary>
    /// <param name="types">The model, enum, or operation types to inspect.</param>
    /// <param name="modelsNamespace">The fully qualified namespace containing declaration modules.</param>
    /// <param name="currentModule">The current module to exclude from its own imports.</param>
    /// <returns>Unique module imports in deterministic ordinal order.</returns>
    internal static IReadOnlyList<string> Collect(IEnumerable<IrType> types, string modelsNamespace, string currentModule)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var type in types)
            Visit(type, names);
        return names.Select(name => $"{modelsNamespace}.{name}")
            .Where(name => !string.Equals(name, currentModule, StringComparison.Ordinal)).ToArray();
    }

    /// <summary>Visits the complete container and composition hierarchy for declaration references.</summary>
    /// <param name="type">The type currently inspected.</param>
    /// <param name="names">The declaration names discovered so far.</param>
    private static void Visit(IrType type, ISet<string> names)
    {
        switch (type)
        {
            case IrModelReference reference:
                names.Add(reference.ModelName);
                break;
            case IrNullableType nullable:
                Visit(nullable.InnerType, names);
                break;
            case IrOptionalType optional:
                Visit(optional.InnerType, names);
                break;
            case IrArrayType array:
                Visit(array.ElementType, names);
                break;
            case IrDictionaryType dictionary:
                Visit(dictionary.KeyType, names);
                Visit(dictionary.ValueType, names);
                break;
            case IrUnionType union:
                foreach (var member in union.Types) Visit(member, names);
                break;
            case IrIntersectionType intersection:
                foreach (var member in intersection.Types) Visit(member, names);
                break;
        }
    }
}
