using System.Text.Json;
using ValIO.Core.Plugins;

namespace ValIO.Generators.TypeScript.Options;

public sealed class TypeScriptOptionsDescriptor : IGeneratorOptionsDescriptor
{
    private static readonly OptionChoice[] TransportChoices =
    [
        new("fetch",            "Fetch API",            "ts.transports.fetch.desc"),
        new("axios",            "Axios",                "ts.transports.axios.desc"),
        new("ky",               "Ky",                   "ts.transports.ky.desc"),
        new("undici",           "Undici",               "ts.transports.undici.desc"),
        new("ofetch",           "ofetch",               "ts.transports.ofetch.desc"),
        new("got",              "got",                  "ts.transports.got.desc"),
        new("angular-rxjs",     "Angular HttpClient (RxJS)", "ts.transports.angularRxjs.desc"),
        new("angular-promise",  "Angular HttpClient (Promise)", "ts.transports.angularPromise.desc"),
        new("solid",            "SolidStart fetch",     "ts.transports.solid.desc"),
        new("nextjs",           "Next.js fetch",        "ts.transports.nextjs.desc"),
    ];

    private static readonly OptionChoice[] EnumFormatChoices =
    [
        new("union",       "String union",   "ts.enumFormat.union.desc"),
        new("enum",        "TS enum",        "ts.enumFormat.enum.desc"),
        new("constObject", "const object",   "ts.enumFormat.constObject.desc"),
    ];

    private static readonly OptionChoice[] ValidationChoices =
    [
        new("zod3",    "Zod 3",   "ts.validation.zod3.desc"),
        new("zod4",    "Zod 4",   "ts.validation.zod4.desc"),
        new("valibot", "Valibot", "ts.validation.valibot.desc"),
        new("typebox", "TypeBox", "ts.validation.typebox.desc"),
    ];

    private static readonly OptionChoice[] QueryWrapperChoices =
    [
        new("tanstack-react",   "TanStack Query — React",   "ts.queryWrapper.tanstackReact.desc"),
        new("tanstack-vue",     "TanStack Query — Vue",     "ts.queryWrapper.tanstackVue.desc"),
        new("tanstack-angular", "TanStack Query — Angular", "ts.queryWrapper.tanstackAngular.desc"),
        new("tanstack-solid",   "TanStack Query — Solid",   "ts.queryWrapper.tanstackSolid.desc"),
        new("tanstack-svelte",  "TanStack Query — Svelte",  "ts.queryWrapper.tanstackSvelte.desc"),
        new("swr",              "SWR",                      "ts.queryWrapper.swr.desc"),
    ];

    private static readonly OptionChoice[] QueryTransportChoices =
    [
        new("fetch", "Fetch API", "ts.queryTransport.fetch.desc"),
        new("axios", "Axios",     "ts.queryTransport.axios.desc"),
        new("ky",    "Ky",        "ts.queryTransport.ky.desc"),
    ];

    private static readonly OptionChoice[] MockChoices =
    [
        new("msw", "Mock Service Worker", "ts.mocks.msw.desc"),
    ];

    private static readonly OptionChoice[] UuidFormatChoices =
    [
        new("guid",   "Built-in uuid validator", "ts.uuidFormat.guid.desc"),
        new("string", "Plain string",            "ts.uuidFormat.string.desc"),
        new("regex",  "Shared format regex",     "ts.uuidFormat.regex.desc"),
    ];

    private static readonly OptionChoice[] ResultSuffixChoices =
    [
        new("Results",   "Results (default)", "ts.resultTypeSuffix.results.desc"),
        new("Result",    "Result",            "ts.resultTypeSuffix.result.desc"),
        new("Response",  "Response",          "ts.resultTypeSuffix.response.desc"),
        new("Responses", "Responses",         "ts.resultTypeSuffix.responses.desc"),
    ];

    /// <inheritdoc />
    public object? ParseOptions(Dictionary<string, JsonElement> rawOptions, out IReadOnlyList<string> errors)
    {
        var errorList = new List<string>();
        var opts = new TypeScriptGenerationOptions();

        if (rawOptions.TryGetValue("generateDefaultValues", out var defaults))
        {
            if (defaults.ValueKind is JsonValueKind.True or JsonValueKind.False)
                opts.GenerateDefaultValues = defaults.GetBoolean();
            else
                errorList.Add("Option 'generateDefaultValues' must be a boolean");
        }

        if (rawOptions.TryGetValue("transports", out var transports))
        {
            if (transports.ValueKind == JsonValueKind.Array)
                opts.Transports =
                [
                    .. transports.EnumerateArray()
                        .Select(t => NormalizeTransport(t.GetString(), errorList))
                        .Where(t => t != null)
                        .Cast<string>()
                ];
            else if (transports.ValueKind == JsonValueKind.String)
            {
                var transport = NormalizeTransport(transports.GetString(), errorList);
                opts.Transports = transport != null ? [transport] : [];
            }
            else
            {
                errorList.Add("Option 'transports' must be a string or array of strings");
            }
        }

        if (rawOptions.TryGetValue("enumFormat", out var enumFmt) && enumFmt.ValueKind == JsonValueKind.String)
            opts.EnumFormat = enumFmt.GetString() ?? "union";

        if (rawOptions.TryGetValue("validation", out var validation) && validation.ValueKind == JsonValueKind.String)
            opts.ValidationLibrary = NormalizeValidation(validation.GetString(), errorList);

        if (rawOptions.TryGetValue("queryWrapper", out var qw) && qw.ValueKind == JsonValueKind.String)
            opts.QueryWrapper = NormalizeQueryWrapper(qw.GetString(), errorList);

        // Which client transport the query hooks wrap (ky/axios/fetch). Accept "queryTransport" (primary) and
        // "hookTransport" (alias). Left null → resolved from the selected transports at generation time.
        if (rawOptions.TryGetValue("queryTransport", out var qt) && qt.ValueKind == JsonValueKind.String)
            opts.QueryTransport = NormalizeQueryTransport(qt.GetString(), errorList);
        else if (rawOptions.TryGetValue("hookTransport", out var ht) && ht.ValueKind == JsonValueKind.String)
            opts.QueryTransport = NormalizeQueryTransport(ht.GetString(), errorList);

        // Accept both "mocks" and "mockLibrary" config keys for symmetry with
        // "validation" / "validationLibrary"-style aliases the user may already use.
        if (rawOptions.TryGetValue("mocks", out var mocks) && mocks.ValueKind == JsonValueKind.String)
            opts.MockLibrary = NormalizeMockLibrary(mocks.GetString(), errorList);
        else if (rawOptions.TryGetValue("mockLibrary", out var mockLibrary) && mockLibrary.ValueKind == JsonValueKind.String)
            opts.MockLibrary = NormalizeMockLibrary(mockLibrary.GetString(), errorList);

        // Suffix for the result-union type name. Accept "resultTypeSuffix" (primary) and
        // "resultSuffix" (alias). Presets are recommended; custom strings pass through.
        if (rawOptions.TryGetValue("uuidFormat", out var uuidFormat) && uuidFormat.ValueKind == JsonValueKind.String)
            opts.UuidFormat = TypeScriptOptionIds.UuidFormat.Resolve(uuidFormat.GetString());

        if (rawOptions.TryGetValue("resultTypeSuffix", out var resultSuffix) && resultSuffix.ValueKind == JsonValueKind.String)
            opts.ResultTypeSuffix = NormalizeResultSuffix(resultSuffix.GetString());
        else if (rawOptions.TryGetValue("resultSuffix", out var resultSuffixAlias) && resultSuffixAlias.ValueKind == JsonValueKind.String)
            opts.ResultTypeSuffix = NormalizeResultSuffix(resultSuffixAlias.GetString());

        // Optional prefix for the result-union type name. Accept "resultTypePrefix" (primary) and
        // "resultPrefix" (alias). Passed through trimmed; empty means no prefix.
        if (rawOptions.TryGetValue("resultTypePrefix", out var resultPrefix) && resultPrefix.ValueKind == JsonValueKind.String)
            opts.ResultTypePrefix = resultPrefix.GetString()?.Trim() ?? string.Empty;
        else if (rawOptions.TryGetValue("resultPrefix", out var resultPrefixAlias) && resultPrefixAlias.ValueKind == JsonValueKind.String)
            opts.ResultTypePrefix = resultPrefixAlias.GetString()?.Trim() ?? string.Empty;

        // Custom axios instance: { "axios": { "customInstancePath": "...", "customInstanceName": "..." } }.
        if (rawOptions.TryGetValue("axios", out var axios) && axios.ValueKind == JsonValueKind.Object)
        {
            var axiosOpts = new TypeScriptAxiosOptions
            {
                CustomInstancePath = GetStringProperty(axios, "customInstancePath"),
                CustomInstanceName = GetStringProperty(axios, "customInstanceName"),
            };

            var hasPath = !string.IsNullOrWhiteSpace(axiosOpts.CustomInstancePath);
            var hasName = !string.IsNullOrWhiteSpace(axiosOpts.CustomInstanceName);
            if (hasPath ^ hasName)
                errorList.Add("Option 'axios' requires both 'customInstancePath' and 'customInstanceName' to use a custom instance");
            if (hasPath || hasName)
                opts.Axios = axiosOpts;
        }

        // Next.js transport: { "nextjs": { "defaultRevalidate": 60, "generateRevalidateHelpers": true } }.
        if (rawOptions.TryGetValue("nextjs", out var nextjs) && nextjs.ValueKind == JsonValueKind.Object)
        {
            var nextOpts = new TypeScriptNextJsOptions();
            if (nextjs.TryGetProperty("defaultRevalidate", out var dr) && dr.ValueKind == JsonValueKind.Number)
                nextOpts.DefaultRevalidate = dr.GetInt32();
            if (nextjs.TryGetProperty("generateRevalidateHelpers", out var grh)
                && (grh.ValueKind == JsonValueKind.True || grh.ValueKind == JsonValueKind.False))
                nextOpts.GenerateRevalidateHelpers = grh.GetBoolean();
            opts.NextJs = nextOpts;
        }

        errors = errorList.AsReadOnly();
        return opts;
    }

    public IReadOnlyList<string> ValidateOptions(object options)
    {
        if (options is not TypeScriptGenerationOptions opts) return ["Invalid options type"];
        var errors = new List<string>();
        if (opts.Transports.Count == 0) errors.Add("At least one transport must be specified");
        return errors.AsReadOnly();
    }

    /// <inheritdoc />
    public IReadOnlyList<OptionDescription> GetOptionDescriptions() =>
    [
        new(Name: "generateDefaultValues", Description: "Export explicitly specified non-null OpenAPI default values.",
            DefaultValue: "false", IsRequired: false, Kind: OptionKind.Bool, Group: "Models"),
        new(
            Name:           "transports",
            Description:    "HTTP transport library to generate clients for. At least one is required.",
            DefaultValue:   "fetch",
            IsRequired:     true,
            Kind:           OptionKind.EnumMulti,
            Choices:        TransportChoices,
            DescriptionKey: "ts.transports.desc",
            Group:          "Clients"),

        new(
            Name:           "enumFormat",
            Description:    "How OpenAPI enums are represented in TypeScript.",
            DefaultValue:   "union",
            IsRequired:     false,
            Kind:           OptionKind.EnumSingle,
            Choices:        EnumFormatChoices,
            DescriptionKey: "ts.enumFormat.desc",
            Group:          "Models"),

        new(
            Name:           "validation",
            Description:    "Validation library to emit runtime schemas for. Leave empty to skip.",
            DefaultValue:   null,
            IsRequired:     false,
            Kind:           OptionKind.EnumSingle,
            Choices:        ValidationChoices,
            DescriptionKey: "ts.validationLibrary.desc",
            Group:          "Validation"),

        new(
            Name:           "queryWrapper",
            Description:    "Optional client wrapper integration (TanStack Query, SWR, ...).",
            DefaultValue:   null,
            IsRequired:     false,
            Kind:           OptionKind.EnumSingle,
            Choices:        QueryWrapperChoices,
            DescriptionKey: "ts.queryWrapper.desc",
            Group:          "Query wrappers",
            DependsOn:      "transports"),

        new(
            Name:           "queryTransport",
            Description:    "Which client transport the generated query hooks wrap (ky/axios/fetch). Defaults to the first client-capable transport in 'transports'; added automatically if not already selected.",
            DefaultValue:   null,
            IsRequired:     false,
            Kind:           OptionKind.EnumSingle,
            Choices:        QueryTransportChoices,
            DescriptionKey: "ts.queryTransport.desc",
            Group:          "Query wrappers",
            DependsOn:      "queryWrapper"),

        new(
            Name:           "mocks",
            Description:    "Generate mock handlers alongside clients.",
            DefaultValue:   null,
            IsRequired:     false,
            Kind:           OptionKind.EnumSingle,
            Choices:        MockChoices,
            DescriptionKey: "ts.mockLibrary.desc",
            Group:          "Testing"),

        new(
            Name:           "uuidFormat",
            Description:    "How Guid/Uuid fields are validated: 'guid' (built-in uuid validator, default), 'string' (no refinement), or 'regex' (shared format-regex constant).",
            DefaultValue:   TypeScriptOptionIds.UuidFormat.Default,
            IsRequired:     false,
            Kind:           OptionKind.EnumSingle,
            Choices:        UuidFormatChoices,
            DescriptionKey: "ts.uuidFormat.desc",
            Group:          "Validation"),

        new(
            Name:           "resultTypeSuffix",
            Description:    "Suffix for the per-operation result-union type name (e.g. {Operation}Results). Presets listed; any custom string is also accepted. Aliased as 'resultSuffix'.",
            DefaultValue:   TypeScriptOptionIds.ResultSuffix.Default,
            IsRequired:     false,
            Kind:           OptionKind.EnumSingle,
            Choices:        ResultSuffixChoices,
            DescriptionKey: "ts.resultTypeSuffix.desc",
            Group:          "Clients"),

        new(
            Name:           "resultTypePrefix",
            Description:    "Optional prefix for the per-operation result-union type name (e.g. {Prefix}{Operation}{ResultTypeSuffix}). Empty by default. Aliased as 'resultPrefix'.",
            DefaultValue:   null,
            IsRequired:     false,
            Kind:           OptionKind.String,
            Choices:        null,
            DescriptionKey: "ts.resultTypePrefix.desc",
            Group:          "Clients"),
    ];

    private static string? NormalizeTransport(string? value, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add("Transport cannot be empty");
            return null;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "fetch" => TypeScriptOptionIds.Transport.Fetch,
            "axios" => TypeScriptOptionIds.Transport.Axios,
            "ky" => TypeScriptOptionIds.Transport.Ky,
            "undici" => TypeScriptOptionIds.Transport.Undici,
            "ofetch" => TypeScriptOptionIds.Transport.OFetch,
            "got" => TypeScriptOptionIds.Transport.Got,
            "angular" or "angular-rxjs" => TypeScriptOptionIds.Transport.AngularRxjs,
            "angular-promise" or "angular-async" => TypeScriptOptionIds.Transport.AngularPromise,
            "solid" or "solidstart" or "solid-start" => TypeScriptOptionIds.Transport.Solid,
            "nextjs" or "next" or "next.js" => TypeScriptOptionIds.Transport.Nextjs,
            var unknown => AddError(errors, $"Unknown transport: '{unknown}'")
        };
    }

    private static string? NormalizeValidation(string? value, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim().ToLowerInvariant() switch
        {
            "zod" or "zod3" => TypeScriptOptionIds.Validation.Zod3,
            "zod4" => TypeScriptOptionIds.Validation.Zod4,
            "valibot" => TypeScriptOptionIds.Validation.Valibot,
            "typebox" or "@sinclair/typebox" => TypeScriptOptionIds.Validation.TypeBox,
            var unknown => AddError(errors, $"Unknown validation library: '{unknown}'")
        };
    }

    private static string? NormalizeQueryWrapper(string? value, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim().ToLowerInvariant() switch
        {
            "tanstack" or "tanstack-react" or "react-query" => TypeScriptOptionIds.QueryWrapper.TanStackReact,
            "tanstack-vue" => TypeScriptOptionIds.QueryWrapper.TanStackVue,
            "tanstack-angular" => TypeScriptOptionIds.QueryWrapper.TanStackAngular,
            "tanstack-solid" => TypeScriptOptionIds.QueryWrapper.TanStackSolid,
            "tanstack-svelte" => TypeScriptOptionIds.QueryWrapper.TanStackSvelte,
            "swr" => TypeScriptOptionIds.QueryWrapper.Swr,
            var unknown => AddError(errors, $"Unknown queryWrapper: '{unknown}'")
        };
    }

    /// <summary>
    /// Normalizes the <c>queryTransport</c>/<c>hookTransport</c> value. Only client-capable transports
    /// (ky/axios/fetch) can back a query hook; anything else (including server-only or framework transports) is
    /// rejected as an error.
    /// </summary>
    /// <param name="value">The raw option value.</param>
    /// <param name="errors">The accumulating error list.</param>
    /// <returns>The canonical transport id, or <see langword="null"/> when empty/invalid.</returns>
    private static string? NormalizeQueryTransport(string? value, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim().ToLowerInvariant() switch
        {
            "fetch" => TypeScriptOptionIds.Transport.Fetch,
            "axios" => TypeScriptOptionIds.Transport.Axios,
            "ky" => TypeScriptOptionIds.Transport.Ky,
            var unknown => AddError(errors, $"Unknown queryTransport: '{unknown}'. Must be one of: fetch, axios, ky.")
        };
    }

    private static string? NormalizeMockLibrary(string? value, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim().ToLowerInvariant() switch
        {
            "msw" => TypeScriptOptionIds.MockLibrary.Msw,
            var unknown => AddError(errors, $"Unknown mock library: '{unknown}'")
        };
    }

    /// <summary>
    /// Normalises the result-type suffix. The four presets (case-insensitive) map to
    /// canonical PascalCase; any other non-empty value is treated as a custom suffix with
    /// its first character capitalised. Empty/whitespace falls back to the default.
    /// </summary>
    private static string NormalizeResultSuffix(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return TypeScriptOptionIds.ResultSuffix.Default;

        var trimmed = value.Trim();
        return trimmed.ToLowerInvariant() switch
        {
            "result"    => TypeScriptOptionIds.ResultSuffix.Result,
            "results"   => TypeScriptOptionIds.ResultSuffix.Results,
            "response"  => TypeScriptOptionIds.ResultSuffix.Response,
            "responses" => TypeScriptOptionIds.ResultSuffix.Responses,
            _ => char.ToUpperInvariant(trimmed[0]) + trimmed[1..],
        };
    }

    private static string? AddError(List<string> errors, string message)
    {
        errors.Add(message);
        return null;
    }

    private static string? GetStringProperty(JsonElement obj, string propertyName)
        => obj.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
