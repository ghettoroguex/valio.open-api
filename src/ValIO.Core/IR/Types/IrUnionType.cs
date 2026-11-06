using ValIO.Core.IR.Enums;

namespace ValIO.Core.IR.Types;

/// <summary>
/// Represents schema alternatives with their original matching cardinality.
/// </summary>
public sealed record IrUnionType : IrType
{
    /// <summary>Gets the matching rule for the alternatives.</summary>
    /// <value>Inclusive anyOf by default, or exclusive oneOf when declared by the schema.</value>
    public IrUnionKind Kind { get; init; } = IrUnionKind.AnyOf;

    /// <summary>
    /// Типы в union
    /// </summary>
    public required IReadOnlyList<IrType> Types { get; init; }
}
