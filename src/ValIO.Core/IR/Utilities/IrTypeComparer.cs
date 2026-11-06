using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;

namespace ValIO.Core.IR.Utilities;

/// <summary>
/// Compares language-independent types without relying on collection identity.
/// </summary>
public static class IrTypeComparer
{
    /// <summary>
    /// Compares types recursively, optionally ignoring nullable and optional wrappers.
    /// </summary>
    /// <remarks>Composition deduplication must preserve presence; collision diagnostics historically ignore it.</remarks>
    /// <param name="a">The first type, or no type.</param>
    /// <param name="b">The second type, or no type.</param>
    /// <param name="ignorePresence">Whether nullable and optional wrappers are ignored recursively.</param>
    /// <returns>Whether the type structures are equivalent under the selected presence policy.</returns>
    public static bool AreSemanticallyEqual(IrType? a, IrType? b, bool ignorePresence = true)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;

        var coreA = ignorePresence ? UnwrapOptionalOrNullable(a) : a;
        var coreB = ignorePresence ? UnwrapOptionalOrNullable(b) : b;

        if (coreA.Constraints != coreB.Constraints) return false;

        return (coreA, coreB) switch
        {
            (IrPrimitiveType pa, IrPrimitiveType pb) => pa.PrimitiveKind == pb.PrimitiveKind &&
                StringComparer.Ordinal.Equals(pa.Format, pb.Format),
            (IrModelReference ma, IrModelReference mb) => StringComparer.Ordinal.Equals(ma.ModelName, mb.ModelName),
            (IrArrayType aa, IrArrayType ab) => AreSemanticallyEqual(aa.ElementType, ab.ElementType, ignorePresence),
            (IrDictionaryType da, IrDictionaryType db) =>
                AreSemanticallyEqual(da.KeyType, db.KeyType, ignorePresence) &&
                AreSemanticallyEqual(da.ValueType, db.ValueType, ignorePresence),
            (IrNullableType na, IrNullableType nb) => AreSemanticallyEqual(na.InnerType, nb.InnerType, ignorePresence),
            (IrOptionalType oa, IrOptionalType ob) => AreSemanticallyEqual(oa.InnerType, ob.InnerType, ignorePresence),
            (IrUnionType ua, IrUnionType ub) => ua.Kind == ub.Kind &&
                HaveSameMembers(ua.Types, ub.Types, ignorePresence, preserveMultiplicity: ua.Kind == IrUnionKind.OneOf),
            (IrIntersectionType ia, IrIntersectionType ib) => HaveSameMembers(ia.Types, ib.Types, ignorePresence),
            _ => false
        };
    }

    /// <summary>Compares unordered composition members without depending on repeated arms.</summary>
    /// <param name="a">The first composition's members.</param>
    /// <param name="b">The second composition's members.</param>
    /// <param name="ignorePresence">The recursive presence comparison policy.</param>
    /// <param name="preserveMultiplicity">Whether repeated alternatives contribute to exclusive match counts.</param>
    /// <returns>Whether every member in either composition has an equivalent in the other.</returns>
    private static bool HaveSameMembers(IReadOnlyList<IrType> a, IReadOnlyList<IrType> b, bool ignorePresence, bool preserveMultiplicity = false)
    {
        if (!preserveMultiplicity)
            return a.All(left => b.Any(right => AreSemanticallyEqual(left, right, ignorePresence))) &&
                   b.All(right => a.Any(left => AreSemanticallyEqual(left, right, ignorePresence)));

        if (a.Count != b.Count) return false;
        var matched = new bool[b.Count];
        foreach (var left in a)
        {
            var index = -1;
            for (var i = 0; i < b.Count; i++)
            {
                if (!matched[i] && AreSemanticallyEqual(left, b[i], ignorePresence))
                {
                    index = i;
                    break;
                }
            }

            if (index < 0) return false;
            matched[index] = true;
        }

        return true;
    }

    /// <summary>Removes outer presence wrappers for parameter collision diagnostics.</summary>
    /// <param name="type">The type whose underlying representation is compared.</param>
    /// <returns>The first type that is neither nullable nor optional.</returns>
    private static IrType UnwrapOptionalOrNullable(IrType type)
    {
        return type switch
        {
            IrNullableType n => UnwrapOptionalOrNullable(n.InnerType),
            IrOptionalType o => UnwrapOptionalOrNullable(o.InnerType),
            _ => type
        };
    }
}
