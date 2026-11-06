using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;

namespace ValIO.Core.IR.Utilities;

/// <summary>
/// Нормализует композицию схем OpenAPI (allOf → intersection, oneOf/anyOf → union)
/// в корректные IR-типы. Единая точка нормализации для всех генераторов:
/// <list type="bullet">
/// <item>схлопывает вырожденные композиции из 0 элементов (→ any) и 1 элемента (→ сам элемент);</item>
/// <item>разворачивает вложенные композиции того же вида (allOf[allOf[X]] → X);</item>
/// <item>удаляет семантические дубликаты членов;</item>
/// <item>идемпотентно оборачивает результат в <see cref="IrNullableType"/>, если требуется.</item>
/// </list>
/// Это гарантирует инвариант «в IR не бывает intersection/union из &lt;2 типов»,
/// от которого зависят все 12 генераторов (single-element allOf из Swashbuckle
/// <c>UseAllOfToExtendReferenceSchemas</c> больше не создаёт вырожденный
/// <see cref="IrIntersectionType"/> и не теряет <c>nullable</c>).
/// </summary>
public static class IrCompositionNormalizer
{
    /// <summary>
    /// Нормализует allOf-композицию в intersection.
    /// 0 элементов → <see cref="IrPrimitiveKind.Any"/>; 1 элемент → сам элемент; ≥2 → <see cref="IrIntersectionType"/>.
    /// </summary>
    /// <param name="members">Уже построенные IR-типы членов allOf.</param>
    /// <param name="nullable">Допускает ли схема JSON null (sibling <c>nullable</c> / <c>x-nullable</c> / type-флаг Null).</param>
    public static IrType NormalizeIntersection(IReadOnlyList<IrType> members, bool nullable = false)
    {
        var flattened = Flatten(members, intersection: true);
        var distinct = Distinct(flattened);

        IrType core = distinct.Count switch
        {
            0 => new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.Any },
            1 => distinct[0],
            _ => new IrIntersectionType { Types = distinct }
        };

        return nullable ? WrapNullable(core) : core;
    }

    /// <summary>
    /// Normalizes alternatives while preserving exclusive match counts and nested exclusive boundaries.
    /// </summary>
    /// <param name="members">Built alternative types; oneOf retains duplicate and null-only alternatives.</param>
    /// <param name="nullable">Whether the containing schema independently permits JSON null.</param>
    /// <param name="kind">The inclusive or exclusive matching rule.</param>
    /// <returns>A normalized type without removing alternatives that affect exclusive matching.</returns>
    public static IrType NormalizeUnion(IReadOnlyList<IrType> members, bool nullable = false, IrUnionKind kind = IrUnionKind.AnyOf)
    {
        var distinct = kind == IrUnionKind.OneOf
            ? members.ToList()
            : Distinct(Flatten(members, intersection: false));

        IrType core = distinct.Count switch
        {
            0 => new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.Any },
            1 => distinct[0],
            _ => new IrUnionType { Types = distinct, Kind = kind }
        };

        return (nullable || core is IrNullableType) ? WrapNullable(core) : core;
    }

    /// <summary>
    /// Идемпотентно оборачивает тип в <see cref="IrNullableType"/> (не создаёт двойную обёртку
    /// <c>IrNullableType{IrNullableType}</c>).
    /// </summary>
    public static IrType WrapNullable(IrType core)
        => core is IrNullableType ? core : new IrNullableType { InnerType = core };

    /// <summary>
    /// Разворачивает вложенные композиции того же вида в плоский список членов.
    /// </summary>
    private static List<IrType> Flatten(IReadOnlyList<IrType> members, bool intersection)
    {
        var result = new List<IrType>(members.Count);
        foreach (var member in members)
        {
            switch (member)
            {
                case IrIntersectionType { Constraints: null } i when intersection:
                    result.AddRange(Flatten(i.Types, intersection));
                    break;
                case IrUnionType { Kind: IrUnionKind.AnyOf, Constraints: null } u when !intersection:
                    result.AddRange(Flatten(u.Types, intersection));
                    break;
                default:
                    result.Add(member);
                    break;
            }
        }

        return result;
    }

    /// <summary>
    /// Removes equivalent inclusive/intersection members while preserving first-seen order and presence.
    /// </summary>
    /// <param name="members">The flattened composition members.</param>
    /// <returns>Members without structurally equivalent duplicates.</returns>
    private static List<IrType> Distinct(IReadOnlyList<IrType> members)
    {
        var result = new List<IrType>(members.Count);
        foreach (var member in members)
        {
            var duplicate = false;
            foreach (var existing in result)
            {
                if (IrTypeComparer.AreSemanticallyEqual(existing, member, ignorePresence: false))
                {
                    duplicate = true;
                    break;
                }
            }

            if (!duplicate)
                result.Add(member);
        }

        return result;
    }
}
