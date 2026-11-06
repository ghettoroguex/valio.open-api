using ValIO.Core.Ast.Enums;
using ValIO.Generators.CSharp.Models;

namespace ValIO.Generators.CSharp.Options;

/// <summary>Contains the single set of options consumed by both plugin steps and C# AST builders.</summary>
public sealed class CSharpGenerationOptions
{
    /// <summary>Gets or sets the generated root namespace, falling back to shared configuration when null.</summary>
    /// <value>The optional C# namespace.</value>
    public string? Namespace { get; set; }

    /// <summary>Gets or sets whether generated reference types use nullable annotations.</summary>
    /// <value>True for the supported nullable-aware generation mode.</value>
    public bool NullableReferenceTypes { get; set; } = true;

    /// <summary>Gets or sets whether generated properties use setters or init accessors.</summary>
    /// <value>The emitted property accessor kind.</value>
    public PropertyAccessorType PropertyAccessor { get; set; } = PropertyAccessorType.Set;

    /// <summary>Gets or sets whether schema-required properties use the C# required modifier.</summary>
    /// <value>Whether to enforce required initialization at compile time.</value>
    public bool UseRequired { get; set; }

    /// <summary>Gets or sets whether terminal model classes are sealed.</summary>
    /// <value>Whether to prevent inheritance from models with no generated descendants.</value>
    public bool UseSealed { get; set; }

    /// <summary>Gets or sets the requested model declaration kind.</summary>
    /// <value>The model kind; unsupported kinds are rejected before generation.</value>
    public ModelType ModelType { get; set; } = ModelType.Class;

    /// <summary>Gets or sets whether classes include typed equality and hashing methods.</summary>
    /// <value>Whether to emit equality members.</value>
    public bool GenerateEqualityMembers { get; set; } = true;

    /// <summary>Gets or sets whether schema constraints are exposed as nested constants.</summary>
    /// <value>Whether to emit validation constants.</value>
    public bool GenerateValidationConstants { get; set; }

    /// <summary>Gets or sets whether schema constraints emit data annotation attributes.</summary>
    /// <value>Whether to emit validation attributes.</value>
    public bool GenerateValidationAttributes { get; set; }

    /// <summary>Gets or sets the generated query-parameter calling convention.</summary>
    /// <value>The calling convention; unsupported styles are rejected before generation.</value>
    public QueryParametersStyle QueryParametersStyle { get; set; } = QueryParametersStyle.IndividualParameters;

    /// <summary>Gets or sets whether HTTP clients expose generated interfaces.</summary>
    /// <value>Whether to emit a client interface.</value>
    public bool GenerateInterfaces { get; set; }

    /// <summary>Gets or sets where generated client interfaces are placed.</summary>
    /// <value>The interface file placement strategy.</value>
    public InterfaceFileStrategy InterfaceFileStrategy { get; set; } = InterfaceFileStrategy.SameFile;

    /// <summary>Gets or sets the access modifier of generated clients.</summary>
    /// <value>The client access modifier.</value>
    public AccessModifier ClientAccessModifier { get; set; } = AccessModifier.Public;

    /// <summary>Gets or sets whether HTTP clients receive OpenTelemetry tracing decorators.</summary>
    /// <value>Whether to emit tracing support.</value>
    public bool GenerateTraces { get; set; }

    /// <summary>Gets or sets whether HTTP clients receive OpenTelemetry metrics decorators.</summary>
    /// <value>Whether to emit metrics support.</value>
    public bool GenerateMetrics { get; set; }

    /// <summary>Gets or sets whether ActivitySource results are treated as nullable.</summary>
    /// <value>Whether generated activities require null guards.</value>
    public bool NullableActivity { get; set; } = true;

    /// <summary>Gets or sets the service name used by generated telemetry instruments.</summary>
    /// <value>The optional service name.</value>
    public string? ServiceName { get; set; }

    /// <summary>Reports requested generation modes that have no complete C# implementation.</summary>
    /// <returns>Actionable configuration errors; an empty list means the options are supported.</returns>
    public IReadOnlyList<string> ValidateSupportedFeatures()
    {
        var errors = new List<string>();
        if (ModelType != ModelType.Class)
            errors.Add("C# currently supports modelType=Class only; Record, Struct, and RecordStruct are not implemented.");
        if (QueryParametersStyle != QueryParametersStyle.IndividualParameters)
            errors.Add("C# currently supports queryParametersStyle=IndividualParameters only.");
        if (!NullableReferenceTypes)
            errors.Add("C# currently requires nullableReferenceTypes=true to preserve schema nullability.");
        if (!NullableActivity)
            errors.Add("C# requires nullableActivity=true because ActivitySource.StartActivity may return null when no listener is active.");
        if (ClientAccessModifier is not (AccessModifier.Public or AccessModifier.Internal))
            errors.Add("C# top-level clients support clientAccessModifier=Public or Internal only.");
        return errors;
    }
}
