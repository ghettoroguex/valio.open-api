using ValIO.Core.IR.Constraints;

namespace ValIO.Core.IR.Types;

/// <summary>
/// Represents a language-independent value type and its local schema constraints.
/// </summary>
public abstract record IrType
{
    /// <summary>Gets constraints declared on this value schema, including individual composition arms.</summary>
    /// <value>Local validation metadata, or no constraints when the schema declares none.</value>
    public IrPropertyConstraints? Constraints { get; init; }
}
