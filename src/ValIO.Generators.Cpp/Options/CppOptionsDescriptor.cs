using System.Text.Json;
using ValIO.Core.Plugins;
using ValIO.Generators.Cpp.Models;

namespace ValIO.Generators.Cpp.Options;

/// <summary>
/// Parses and validates runtime-specific C++ generator options.
/// </summary>
public sealed class CppOptionsDescriptor : IGeneratorOptionsDescriptor
{
    /// <summary>Runtime profiles available to consumers.</summary>
    private static readonly OptionChoice[] RuntimeChoices =
    [
        new("ue",  "Unreal Engine (USTRUCT/UCLASS + UE runtime)", "cpp.runtime.ue.desc"),
        new("std", "Standalone C++ (cpp-httplib + nlohmann_json)", "cpp.runtime.std.desc"),
    ];

    /// <summary>Supported standalone client execution styles.</summary>
    private static readonly OptionChoice[] ApiStyleChoices =
    [
        new("sync",  "Synchronous methods (ValIO::Expected)",  "cpp.apiStyle.sync.desc"),
        new("async", "Asynchronous methods (std::future)",     "cpp.apiStyle.async.desc"),
        new("both",  "Synchronous methods + async wrappers",   "cpp.apiStyle.both.desc"),
    ];

    /// <summary>Supported standalone client declaration shapes.</summary>
    private static readonly OptionChoice[] ClientShapeChoices =
    [
        new("class",          "Single {Client}Client class",                   "cpp.clientShape.class.desc"),
        new("interface+impl", "Interface I{Client} + {Client}Client impl",     "cpp.clientShape.interfaceImpl.desc"),
    ];

    /// <summary>Explicit application temporal representations.</summary>
    private static readonly OptionChoice[] TemporalChoices =
    [
        new("string", "Validated strings", "cpp.temporal.string.desc"),
        new("chrono", "Standard chrono (C++20+)", "cpp.temporal.chrono.desc"),
        new("howard-date", "Howard Hinnant date", "cpp.temporal.howardDate.desc"),
        new("boost-date-time", "Boost.Date_Time", "cpp.temporal.boost.desc"),
        new("valio", "ValIO lightweight values", "cpp.temporal.valio.desc"),
    ];

    /// <summary>Explicit application UUID representations.</summary>
    private static readonly OptionChoice[] UuidChoices =
    [
        new("string", "Validated strings", "cpp.uuid.string.desc"),
        new("boost", "Boost.Uuid", "cpp.uuid.boost.desc"),
        new("stduuid", "stduuid", "cpp.uuid.stduuid.desc"),
        new("modern-uuid", "modern-uuid (C++20+)", "cpp.uuid.modern.desc"),
    ];

    /// <inheritdoc />
    public object? ParseOptions(Dictionary<string, JsonElement> rawOptions, out IReadOnlyList<string> errors)
    {
        var errorList = new List<string>();
        var opts = new CppGenerationOptions();

        if (rawOptions.TryGetValue("runtime", out var runtime) && runtime.ValueKind == JsonValueKind.String)
            opts.Runtime = NormalizeRuntime(runtime.GetString(), errorList);

        if (rawOptions.TryGetValue("cxxStandard", out var standard))
        {
            if (standard.ValueKind == JsonValueKind.Number && standard.TryGetInt32(out var standardNumber))
                opts.CxxStandard = standardNumber;
            else if (standard.ValueKind == JsonValueKind.String && int.TryParse(standard.GetString(), out var standardParsed))
                opts.CxxStandard = standardParsed;
            else
                errorList.Add("Option 'cxxStandard' must be a number (17, 20 or 23)");
        }

        if (rawOptions.TryGetValue("apiStyle", out var apiStyle) && apiStyle.ValueKind == JsonValueKind.String)
            opts.ApiStyle = NormalizeApiStyle(apiStyle.GetString(), errorList);

        if (rawOptions.TryGetValue("clientShape", out var clientShape) && clientShape.ValueKind == JsonValueKind.String)
            opts.ClientShape = NormalizeClientShape(clientShape.GetString(), errorList);

        if (rawOptions.TryGetValue("temporalBackend", out var temporal))
        {
            if (temporal.ValueKind != JsonValueKind.String) errorList.Add("Option 'temporalBackend' must be a string");
            else opts.TemporalBackend = temporal.GetString()?.Trim().ToLowerInvariant() switch
            {
                "string" => CppTemporalBackend.String,
                "chrono" => CppTemporalBackend.Chrono,
                "howard-date" => CppTemporalBackend.HowardDate,
                "boost-date-time" => CppTemporalBackend.BoostDateTime,
                "valio" => CppTemporalBackend.Valio,
                var unknown => Unknown(errorList, unknown ?? "", CppTemporalBackend.String, "temporalBackend", "string, chrono, howard-date, boost-date-time, valio"),
            };
        }
        if (rawOptions.TryGetValue("uuidBackend", out var uuid))
        {
            if (uuid.ValueKind != JsonValueKind.String) errorList.Add("Option 'uuidBackend' must be a string");
            else opts.UuidBackend = uuid.GetString()?.Trim().ToLowerInvariant() switch
            {
                "string" => CppUuidBackend.String,
                "boost" => CppUuidBackend.BoostUuid,
                "stduuid" => CppUuidBackend.StdUuid,
                "modern-uuid" => CppUuidBackend.ModernUuid,
                var unknown => Unknown(errorList, unknown ?? "", CppUuidBackend.String, "uuidBackend", "string, boost, stduuid, modern-uuid"),
            };
        }

        if (rawOptions.TryGetValue("namespace", out var ns) && ns.ValueKind == JsonValueKind.String)
            opts.Namespace = ns.GetString()?.Trim();

        if (rawOptions.TryGetValue("httpInclude", out var httpInclude) && httpInclude.ValueKind == JsonValueKind.String)
            opts.HttpInclude = httpInclude.GetString()?.Trim() ?? opts.HttpInclude;

        if (rawOptions.TryGetValue("jsonInclude", out var jsonInclude) && jsonInclude.ValueKind == JsonValueKind.String)
            opts.JsonInclude = jsonInclude.GetString()?.Trim() ?? opts.JsonInclude;

        if (rawOptions.TryGetValue("jsonFwdInclude", out var jsonFwdInclude) && jsonFwdInclude.ValueKind == JsonValueKind.String)
            opts.JsonFwdInclude = jsonFwdInclude.GetString()?.Trim() ?? opts.JsonFwdInclude;

        if (rawOptions.TryGetValue("errorModelName", out var errorModelName) && errorModelName.ValueKind == JsonValueKind.String)
            opts.ErrorModelName = errorModelName.GetString()?.Trim() ?? opts.ErrorModelName;

        if (rawOptions.TryGetValue("emitCmake", out var emitCmake))
        {
            if (emitCmake.ValueKind is JsonValueKind.True or JsonValueKind.False)
                opts.EmitCmake = emitCmake.GetBoolean();
            else
                errorList.Add("Option 'emitCmake' must be a boolean");
        }
        if (rawOptions.TryGetValue("emitSerializers", out var emitSerializers))
        {
            if (emitSerializers.ValueKind is JsonValueKind.True or JsonValueKind.False)
                opts.EmitSerializers = emitSerializers.GetBoolean();
            else
                errorList.Add("Option 'emitSerializers' must be a boolean");
        }

        // Unreal Engine options share the descriptor with the standalone profile.
        string? Str(string key) =>
            rawOptions.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        bool? Bool(string key) =>
            rawOptions.TryGetValue(key, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? v.GetBoolean() : null;

        opts.ModuleApiMacro = Str("moduleApiMacro") ?? opts.ModuleApiMacro;
        opts.BlueprintCategory = Str("blueprintCategory") ?? opts.BlueprintCategory;
        opts.Copyright = Str("copyright") ?? opts.Copyright;
        opts.AddUnrealEngineIncludesInHeaders = Bool("addUnrealEngineIncludesInHeaders") ?? opts.AddUnrealEngineIncludesInHeaders;
        opts.BaseAsyncActionClassName = Str("baseAsyncActionClassName") ?? opts.BaseAsyncActionClassName;
        opts.BaseAsyncActionIncludePath = Str("baseAsyncActionIncludePath") ?? opts.BaseAsyncActionIncludePath;
        opts.ValIOClientIncludePath = Str("valIOClientIncludePath") ?? opts.ValIOClientIncludePath;
        opts.ValIORequestBuilderIncludePath = Str("valIORequestBuilderIncludePath") ?? opts.ValIORequestBuilderIncludePath;
        opts.ValIOMultipartFormDataIncludePath = Str("valIOMultipartFormDataIncludePath") ?? opts.ValIOMultipartFormDataIncludePath;
        opts.ValIOTypesIncludePath = Str("valIOTypesIncludePath") ?? opts.ValIOTypesIncludePath;
        opts.ValIOResponseIncludePath = Str("valIOResponseIncludePath") ?? opts.ValIOResponseIncludePath;
        opts.GeneratedHeaderSuffix = Str("generatedHeaderSuffix") ?? opts.GeneratedHeaderSuffix;
        opts.OutputDirectory = Str("outputDirectory") ?? opts.OutputDirectory;

        errors = errorList.AsReadOnly();
        return opts;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ValidateOptions(object options)
    {
        if (options is not CppGenerationOptions opts)
            return ["Invalid options type"];

        var errors = new List<string>();
        if (opts.CxxStandard is not (17 or 20 or 23))
            errors.Add($"Option 'cxxStandard' must be 17, 20 or 23 (got {opts.CxxStandard})");
        if (!Enum.IsDefined(opts.TemporalBackend)) errors.Add("Unknown temporalBackend enum value");
        if (!Enum.IsDefined(opts.UuidBackend)) errors.Add("Unknown uuidBackend enum value");
        if (opts.TemporalBackend == CppTemporalBackend.Chrono && opts.CxxStandard < 20)
            errors.Add("Option 'temporalBackend=chrono' requires cxxStandard 20 or 23");
        if (opts.UuidBackend == CppUuidBackend.ModernUuid && opts.CxxStandard < 20)
            errors.Add("Option 'uuidBackend=modern-uuid' requires cxxStandard 20 or 23");
        if (opts.Runtime != CppRuntimeProfile.Std && (opts.TemporalBackend != CppTemporalBackend.String || opts.UuidBackend != CppUuidBackend.String))
            errors.Add("Native temporal and UUID backend options require runtime=std");
        if (!string.IsNullOrEmpty(opts.Namespace)
            && !System.Text.RegularExpressions.Regex.IsMatch(opts.Namespace, @"^[A-Za-z_][A-Za-z0-9_]*$"))
            errors.Add("Option 'namespace' must be a valid C++ identifier");
        return errors.AsReadOnly();
    }

    /// <inheritdoc />
    public IReadOnlyList<OptionDescription> GetOptionDescriptions() =>
    [
        new(
            Name:         "runtime",
            Description:  "Runtime profile: 'ue' emits Unreal Engine types (USTRUCT/UCLASS + external UE runtime); 'std' emits standalone C++ built on cpp-httplib + nlohmann_json with a self-contained ValIO runtime.",
            DefaultValue: "ue",
            IsRequired:   false,
            Kind:         OptionKind.EnumSingle,
            Choices:      RuntimeChoices,
            Group:        "General"),

        new(
            Name:         "cxxStandard",
            Description:  "Target C++ standard for the std profile: 17, 20 or 23. Generated code is C++17-clean; ValIO::Expected upgrades to std::expected at compile time when available.",
            DefaultValue: "17",
            IsRequired:   false,
            Kind:         OptionKind.Int,
            Choices:      null,
            Group:        "std runtime",
            DependsOn:    "runtime"),

        new(
            Name:         "apiStyle",
            Description:  "API style for the std profile: 'sync' (ValIO::Expected), 'async' (std::future) or 'both'.",
            DefaultValue: "sync",
            IsRequired:   false,
            Kind:         OptionKind.EnumSingle,
            Choices:      ApiStyleChoices,
            Group:        "std runtime",
            DependsOn:    "runtime"),

        new(Name: "temporalBackend", Description: "Application date/time/duration representation. Strings retain valid original text; native providers retain offsets and reject unrepresentable precision or calendar intervals.",
            DefaultValue: "string", IsRequired: false, Kind: OptionKind.EnumSingle, Choices: TemporalChoices, Group: "std runtime", DependsOn: "runtime"),
        new(Name: "uuidBackend", Description: "Application UUID representation, independent of temporal types. Selected provider dependencies are public model dependencies; JSON and HTTP remain separate.",
            DefaultValue: "string", IsRequired: false, Kind: OptionKind.EnumSingle, Choices: UuidChoices, Group: "std runtime", DependsOn: "runtime"),

        new(
            Name:         "clientShape",
            Description:  "Client shape for the std profile: 'class' (single {Client}Client) or 'interface+impl' (I{Client} interface for mocking/DI + {Client}Client implementation).",
            DefaultValue: "class",
            IsRequired:   false,
            Kind:         OptionKind.EnumSingle,
            Choices:      ClientShapeChoices,
            Group:        "std runtime",
            DependsOn:    "runtime"),

        new(
            Name:         "namespace",
            Description:  "Root namespace for generated models/enums/clients (std profile). Defaults to the project namespace.",
            DefaultValue: null,
            IsRequired:   false,
            Kind:         OptionKind.String,
            Choices:      null,
            Group:        "std runtime",
            DependsOn:    "runtime"),

        new(
            Name:         "httpInclude",
            Description:  "Include path of cpp-httplib (override for consumers with wrapper headers).",
            DefaultValue: "httplib.h",
            IsRequired:   false,
            Kind:         OptionKind.String,
            Choices:      null,
            Group:        "std runtime",
            DependsOn:    "runtime"),

        new(
            Name:         "jsonInclude",
            Description:  "Include path of the full nlohmann/json header (used only in generated .cpp files).",
            DefaultValue: "nlohmann/json.hpp",
            IsRequired:   false,
            Kind:         OptionKind.String,
            Choices:      null,
            Group:        "std runtime",
            DependsOn:    "runtime"),

        new(
            Name:         "jsonFwdInclude",
            Description:  "Include path of the nlohmann forward-declaration header (used only in serializer headers).",
            DefaultValue: "nlohmann/json_fwd.hpp",
            IsRequired:   false,
            Kind:         OptionKind.String,
            Choices:      null,
            Group:        "std runtime",
            DependsOn:    "runtime"),

        new(
            Name:         "errorModelName",
            Description:  "Model name used to parse non-2xx response bodies; a minimal model + codec is emitted when the schema has none.",
            DefaultValue: "RestApiErrorResponse",
            IsRequired:   false,
            Kind:         OptionKind.String,
            Choices:      null,
            Group:        "std runtime",
            DependsOn:    "runtime"),

        new(
            Name:         "emitCmake",
            Description:  "Emit a CMakeLists snippet for the std profile output (file list + find_package for httplib and nlohmann_json).",
            DefaultValue: "false",
            IsRequired:   false,
            Kind:         OptionKind.Bool,
            Choices:      null,
            Group:        "std runtime",
            DependsOn:    "runtime"),
        new(
            Name: "emitSerializers",
            Description: "Emit separate JSON codec headers and sources. Disable together with client generation for pure DTO output without runtime or third-party dependencies.",
            DefaultValue: "true", IsRequired: false, Kind: OptionKind.Bool, Choices: null,
            Group: "std runtime", DependsOn: "runtime"),
    ];

    /// <summary>Parses a runtime profile and records unsupported values.</summary>
    /// <param name="value">The supplied option value.</param>
    /// <param name="errors">The parse diagnostics.</param>
    /// <returns>The selected runtime profile.</returns>
    private static CppRuntimeProfile NormalizeRuntime(string? value, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
            return CppRuntimeProfile.Ue;

        return value.Trim().ToLowerInvariant() switch
        {
            "ue" or "unreal" or "unreal-engine" or "unrealengine" => CppRuntimeProfile.Ue,
            "std" or "standard" or "cpp-httplib" or "httplib" => CppRuntimeProfile.Std,
            var unknown => Unknown(errors, unknown, CppRuntimeProfile.Ue, "runtime", "ue, std"),
        };
    }

    /// <summary>Parses a client execution style and records unsupported values.</summary>
    /// <param name="value">The supplied option value.</param>
    /// <param name="errors">The parse diagnostics.</param>
    /// <returns>The selected execution style.</returns>
    private static CppApiStyle NormalizeApiStyle(string? value, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
            return CppApiStyle.Sync;

        return value.Trim().ToLowerInvariant() switch
        {
            "sync" => CppApiStyle.Sync,
            "async" => CppApiStyle.Async,
            "both" => CppApiStyle.Both,
            var unknown => Unknown(errors, unknown, CppApiStyle.Sync, "apiStyle", "sync, async, both"),
        };
    }

    /// <summary>Parses a client declaration shape and records unsupported values.</summary>
    /// <param name="value">The supplied option value.</param>
    /// <param name="errors">The parse diagnostics.</param>
    /// <returns>The selected client shape.</returns>
    private static CppClientShape NormalizeClientShape(string? value, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
            return CppClientShape.Class;

        return value.Trim().ToLowerInvariant() switch
        {
            "class" => CppClientShape.Class,
            "interface+impl" or "interfaceimpl" or "interface" => CppClientShape.InterfaceImpl,
            var unknown => Unknown(errors, unknown, CppClientShape.Class, "clientShape", "class, interface+impl"),
        };
    }

    /// <summary>Records an unknown enum option while retaining a usable fallback.</summary>
    /// <typeparam name="TEnum">The option's enum type.</typeparam>
    /// <param name="errors">The parse diagnostics.</param>
    /// <param name="value">The unsupported supplied value.</param>
    /// <param name="fallback">The default option value.</param>
    /// <param name="optionName">The option's configuration key.</param>
    /// <param name="allowed">The accepted values for diagnostics.</param>
    /// <returns>The supplied fallback value.</returns>
    private static TEnum Unknown<TEnum>(List<string> errors, string value, TEnum fallback, string optionName, string allowed)
        where TEnum : struct, Enum
    {
        errors.Add($"Unknown {optionName}: '{value}'. Must be one of: {allowed}.");
        return fallback;
    }
}
