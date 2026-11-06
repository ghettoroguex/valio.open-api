namespace ValIO.Core.IR.Enums;

/// <summary>Preserves inclusive and exclusive schema alternatives independently of target syntax.</summary>
public enum IrUnionKind
{
    /// <summary>Accepts a value when at least one alternative matches.</summary>
    AnyOf,

    /// <summary>Accepts a value only when exactly one alternative matches.</summary>
    OneOf
}
