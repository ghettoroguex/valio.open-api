namespace ValIO.Core.IR.Enums;

/// <summary>Describes language-independent HTTP parameter serialization without exposing an OpenAPI SDK type.</summary>
public enum IrParameterStyle
{
    /// <summary>Prefixes path parameter values with a semicolon and the parameter name.</summary>
    Matrix,

    /// <summary>Prefixes path parameter values with a dot.</summary>
    Label,

    /// <summary>Serializes query or cookie values using form-style keys and optional repeated values.</summary>
    Form,

    /// <summary>Serializes path or header collections using comma-separated values.</summary>
    Simple,

    /// <summary>Separates query collection values with spaces.</summary>
    SpaceDelimited,

    /// <summary>Separates query collection values with vertical bars.</summary>
    PipeDelimited,

    /// <summary>Serializes query object properties using bracketed property names.</summary>
    DeepObject,

    /// <summary>Uses the cookie-specific serialization style introduced in OpenAPI 3.2.</summary>
    Cookie,
}
