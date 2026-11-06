using ValIO.Core.IR.Constraints;

namespace ValIO.Generators.TypeScript.Validation;

/// <summary>Compares validation rules independently from initialization defaults.</summary>
internal static class ValidationConstraints
{
    /// <summary>Determines whether two constraint records impose the same validation rules.</summary>
    /// <param name="first">The first set of value constraints.</param>
    /// <param name="second">The second set of value constraints.</param>
    /// <returns>True when validation metadata is equivalent, excluding defaults.</returns>
    public static bool Equivalent(IrPropertyConstraints first, IrPropertyConstraints second) =>
        (first with { DefaultValue = null }) == (second with { DefaultValue = null });
}
