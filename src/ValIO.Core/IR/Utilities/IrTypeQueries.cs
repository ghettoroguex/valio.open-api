using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;

namespace ValIO.Core.IR.Utilities;

/// <summary>
/// Общие запросы над закрытой иерархией IrType (без привязки к языку).
/// </summary>
public static class IrTypeQueries
{
    /// <summary>
    /// Unwraps any alternating sequence of nullable and optional wrappers.
    /// </summary>
    /// <param name="type">The wrapped type to inspect.</param>
    /// <returns>The underlying value type.</returns>
    public static IrType UnwrapNullableOptional(IrType type)
    {
        return type switch
        {
            IrNullableType nullable => UnwrapNullableOptional(nullable.InnerType),
            IrOptionalType optional => UnwrapNullableOptional(optional.InnerType),
            _ => type
        };
    }

    /// <summary>
    /// Является ли тип файловым/бинарным (byte-массивом или их массивом) —
    /// используется для multipart-частей и binary-тел. Единая реализация
    /// для всех генераторов (раньше дублировалась в UETypeMapper).
    /// </summary>
    public static bool IsFileOrBinary(IrType type)
    {
        var unwrapped = UnwrapNullableOptional(type);

        return unwrapped switch
        {
            IrPrimitiveType p => p.PrimitiveKind is IrPrimitiveKind.Binary or IrPrimitiveKind.ByteArray or IrPrimitiveKind.File,
            IrArrayType a when a.ElementType is IrPrimitiveType ep => ep.PrimitiveKind is IrPrimitiveKind.Binary or IrPrimitiveKind.ByteArray or IrPrimitiveKind.File,
            _ => false,
        };
    }
}
