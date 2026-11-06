namespace ValIO.Generators.TypeScript.Options;

public static class TypeScriptOptionIds
{
    public static class Transport
    {
        public const string Fetch = "fetch";
        public const string Axios = "axios";
        public const string Ky = "ky";
        public const string Undici = "undici";
        public const string OFetch = "ofetch";
        public const string Got = "got";
        public const string AngularRxjs = "angular-rxjs";
        public const string AngularPromise = "angular-promise";
        public const string Solid = "solid";

        /// <summary>Next.js (App Router) server-safe fetch client with cache tags + revalidation.</summary>
        public const string Nextjs = "nextjs";
    }

    public static class Validation
    {
        public const string Zod3 = "zod3";
        public const string Zod4 = "zod4";
        public const string Valibot = "valibot";
        public const string TypeBox = "typebox";
    }

    public static class QueryWrapper
    {
        public const string TanStackReact = "tanstack-react";
        public const string TanStackVue = "tanstack-vue";
        public const string TanStackAngular = "tanstack-angular";
        public const string TanStackSolid = "tanstack-solid";
        public const string TanStackSvelte = "tanstack-svelte";
        public const string Swr = "swr";
    }

    public static class MockLibrary
    {
        public const string Msw = "msw";
    }

    /// <summary>
    /// Suffix appended to the per-operation discriminated-union result type name
    /// (e.g. <c>{Operation}Results</c>). Configurable to avoid clashes with generated
    /// model names. Presets below are recommended; custom strings are also accepted.
    /// </summary>
    public static class ResultSuffix
    {
        public const string Result = "Result";
        public const string Results = "Results";
        public const string Response = "Response";
        public const string Responses = "Responses";

        /// <summary>Default suffix when none is configured.</summary>
        public const string Default = Results;
    }

    /// <summary>
    /// How fields typed as <c>Guid</c>/<c>Uuid</c> (IR <c>IrPrimitiveKind.Uuid</c>) are validated
    /// in generated schemas.
    /// </summary>
    public static class UuidFormat
    {
        /// <summary>Canonical hexadecimal UUID text validated consistently across libraries, including nil and max UUID values.</summary>
        public const string Guid = "guid";

        /// <summary>Plain string, no UUID refinement.</summary>
        public const string String = "string";

        /// <summary>String validated against a shared UUID format regex constant.</summary>
        public const string Regex = "regex";

        /// <summary>Default when none is configured (backward-compatible).</summary>
        public const string Default = Guid;

        /// <summary>Format-only UUID regex source with strict end-of-input matching and no version or variant enforcement.</summary>
        public const string Pattern = @"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}(?![\s\S])";

        /// <summary>Name of the generated constant file (under the schemas folder) and its exports.</summary>
        public const string ConstFileName = "formats";
        public const string RegexConstName = "UUID_REGEX";
        public const string PatternConstName = "UUID_PATTERN";

        /// <summary>Normalises a configured value to a canonical mode; null/unknown → <see cref="Default"/>.</summary>
        public static string Resolve(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return Default;
            return value.Trim().ToLowerInvariant() switch
            {
                "guid" or "uuid" => Guid,
                "string" or "none" or "plain" => String,
                "regex" or "pattern" => Regex,
                _ => Default,
            };
        }
    }
}
