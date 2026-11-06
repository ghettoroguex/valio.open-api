using System.Text.Json;
using System.Text.Json.Serialization;
using ValIO.Core.Plugins;

namespace ValIO.Generators.CSharp.Options;

/// <summary>Parses and validates the canonical options shared by C# plugin steps and AST builders.</summary>
public sealed class CSharpOptionsDescriptor : IGeneratorOptionsDescriptor
{
    /// <summary>Maps JSON option names and textual enum values to strongly typed settings.</summary>
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    /// <inheritdoc/>
    public object? ParseOptions(Dictionary<string, JsonElement> rawOptions, out IReadOnlyList<string> errors)
    {
        var knownNames = typeof(CSharpGenerationOptions).GetProperties()
            .Select(property => property.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var errorList = rawOptions.Keys.Where(name => !knownNames.Contains(name))
            .Select(name => $"Unknown C# option '{name}'.").ToList();
        try
        {
            var options = JsonSerializer.Deserialize<CSharpGenerationOptions>(
                JsonSerializer.Serialize(rawOptions), SerializerOptions) ?? new CSharpGenerationOptions();
            errorList.AddRange(options.ValidateSupportedFeatures());
            errors = errorList;
            return errorList.Count == 0 ? options : null;
        }
        catch (JsonException exception)
        {
            errorList.Add($"Invalid C# options: {exception.Message}");
            errors = errorList;
            return null;
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> ValidateOptions(object options) =>
        options is CSharpGenerationOptions csharpOptions
            ? csharpOptions.ValidateSupportedFeatures()
            : ["C# options must use CSharpGenerationOptions."];

    /// <inheritdoc/>
    public IReadOnlyList<OptionDescription> GetOptionDescriptions() =>
    [
        new("namespace", "Root namespace for generated C# code; otherwise the shared namespace is used.", "Generated", false,
            Kind: OptionKind.String, DescriptionKey: "csharp.namespace.desc", Group: "Project"),
        new("nullableReferenceTypes", "Preserve nullable-reference annotations required by schema nullability.", "true", false,
            Kind: OptionKind.Bool, DescriptionKey: "csharp.nullable.desc", Group: "Language features"),
        new("propertyAccessor", "Property accessor kind: Set or Init.", "Set", false,
            Kind: OptionKind.String, Group: "Models"),
        new("useRequired", "Emit required modifiers on schema-required properties.", "false", false,
            Kind: OptionKind.Bool, Group: "Models"),
        new("useSealed", "Seal terminal model classes while keeping generated base classes open.", "false", false,
            Kind: OptionKind.Bool, Group: "Models"),
        new("modelType", "Model declaration kind; currently only Class is implemented.", "Class", false,
            Kind: OptionKind.String, Group: "Models"),
        new("generateEqualityMembers", "Generate null-safe typed equality and hashing methods.", "true", false,
            Kind: OptionKind.Bool, Group: "Models"),
        new("generateValidationConstants", "Generate nested constants for schema constraints.", "false", false,
            Kind: OptionKind.Bool, Group: "Validation"),
        new("generateValidationAttributes", "Generate data annotations for schema constraints.", "false", false,
            Kind: OptionKind.Bool, Group: "Validation"),
        new("queryParametersStyle", "Query argument convention; currently only IndividualParameters is implemented.", "IndividualParameters", false,
            Kind: OptionKind.String, Group: "Clients"),
        new("generateInterfaces", "Generate interfaces for HTTP clients.", "false", false,
            Kind: OptionKind.Bool, Group: "Clients"),
        new("interfaceFileStrategy", "Place interfaces in SameFile or SeparateFile.", "SameFile", false,
            Kind: OptionKind.String, Group: "Clients"),
        new("clientAccessModifier", "Access modifier for generated HTTP client classes.", "Public", false,
            Kind: OptionKind.String, Group: "Clients"),
        new("generateTraces", "Generate ActivitySource-based HTTP tracing decorators.", "false", false,
            Kind: OptionKind.Bool, Group: "Telemetry"),
        new("generateMetrics", "Generate HTTP metrics instruments and decorators.", "false", false,
            Kind: OptionKind.Bool, Group: "Telemetry"),
        new("nullableActivity", "Guard nullable results returned by ActivitySource.", "true", false,
            Kind: OptionKind.Bool, Group: "Telemetry"),
        new("serviceName", "Optional service name for tracing and metrics.", null, false,
            Kind: OptionKind.String, Group: "Telemetry")
    ];
}
