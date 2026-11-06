using ValIO.Core.IR.Enums;

namespace ValIO.Core.IR.Types;

/// <summary>
/// Represents a scalar schema type and its retained format annotation.
/// </summary>
public sealed record IrPrimitiveType : IrType
{
    /// <summary>
    /// Gets the scalar representation selected independently of language-specific backends.
    /// </summary>
    /// <value>The scalar IR kind used by generator type mappers.</value>
    public required IrPrimitiveKind PrimitiveKind { get; init; }

    /// <summary>Gets the schema's scalar format without replacing its primitive representation.</summary>
    /// <value>The normalized known format, verbatim custom format, or null when no format is present.</value>
    /// <remarks>
    /// Known formats are trimmed and lowercased by the IR producer. Custom format identifiers retain
    /// their exact spelling so case-sensitive validator contracts remain distinct.
    /// </remarks>
    public string? Format { get; init; }
}
