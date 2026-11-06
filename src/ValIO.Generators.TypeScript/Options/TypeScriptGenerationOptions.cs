namespace ValIO.Generators.TypeScript.Options;

public sealed class TypeScriptGenerationOptions
{
    /// <summary>Whether to export explicitly specified non-null OpenAPI defaults.</summary>
    /// <value>False unless default-value constants are requested.</value>
    public bool GenerateDefaultValues { get; set; }

    public List<string> Transports { get; set; } = ["fetch"];
    public string EnumFormat { get; set; } = "union";
    public string? ValidationLibrary { get; set; }
    public string? QueryWrapper { get; set; }

    /// <summary>
    /// Which client transport the generated query hooks (TanStack Query / SWR) wrap — one of
    /// <c>ky</c>/<c>axios</c>/<c>fetch</c>. When <see langword="null"/> the transport is resolved from
    /// <see cref="Transports"/> (the first client-capable transport, else <c>fetch</c>). Normalised by
    /// <see cref="TypeScriptOptionsDescriptor"/> from the <c>queryTransport</c> config key. If the chosen
    /// transport is not already in <see cref="Transports"/> it is added automatically so its client module is
    /// emitted (see <c>TypeScriptTransportResolutionStep</c>).
    /// </summary>
    /// <seealso cref="TypeScriptClientTransport.ResolveHookClientSuffix"/>
    public string? QueryTransport { get; set; }

    /// <summary>
    /// Mock library to emit alongside the client. Currently supports <c>msw</c> —
    /// generates a single <c>mocks/handlers.ts</c> file containing an
    /// <c>http.{verb}(...)</c> handler per IR operation. Normalised by
    /// <see cref="TypeScriptOptionsDescriptor"/> from the <c>mocks</c> /
    /// <c>mockLibrary</c> config key.
    /// </summary>
    public string? MockLibrary { get; set; }

    /// <summary>
    /// Suffix for the per-operation discriminated-union result type name
    /// (<c>{Operation}{ResultTypeSuffix}</c>, e.g. <c>GetUserByIdResults</c>). Only the
    /// union alias is affected — the per-status variant interfaces keep their fixed
    /// <c>Result</c> base (<c>...Result200</c>, <c>...ResultOther</c>). Defaults to
    /// <see cref="TypeScriptOptionIds.ResultSuffix.Default"/> (<c>Results</c>). Normalised by
    /// <see cref="TypeScriptOptionsDescriptor"/> from the <c>resultTypeSuffix</c> /
    /// <c>resultSuffix</c> config key.
    /// </summary>
    public string ResultTypeSuffix { get; set; } = TypeScriptOptionIds.ResultSuffix.Default;

    /// <summary>
    /// Optional prefix for the per-operation result-union type name
    /// (<c>{ResultTypePrefix}{Operation}{ResultTypeSuffix}</c>). Empty by default. Normalised from the
    /// <c>resultTypePrefix</c> / <c>resultPrefix</c> config key, or the cross-language
    /// <c>namingConvention.resultTypePrefix</c>.
    /// </summary>
    public string ResultTypePrefix { get; set; } = string.Empty;

    /// <summary>
    /// Optional custom axios instance to use instead of the global <c>axios</c> import.
    /// Parsed by <see cref="TypeScriptOptionsDescriptor"/> from the nested <c>axios</c> config
    /// object. When both path and name are set, generated axios clients import the instance and
    /// assign it to the local <c>axiosInstance</c> variable.
    /// </summary>
    public TypeScriptAxiosOptions? Axios { get; set; }

    /// <summary>
    /// Options for the <c>nextjs</c> transport (Next.js App Router fetch client). Parsed by
    /// <see cref="TypeScriptOptionsDescriptor"/> from the nested <c>nextjs</c> config object. When the
    /// <c>nextjs</c> transport is not selected this is <see langword="null"/> and ignored.
    /// </summary>
    public TypeScriptNextJsOptions? NextJs { get; set; }

    /// <summary>
    /// How <c>Guid</c>/<c>Uuid</c> fields are validated in generated schemas:
    /// <c>guid</c> (default — built-in uuid validator), <c>string</c> (no refinement), or
    /// <c>regex</c> (shared format-regex constant). Normalised by
    /// <see cref="TypeScriptOptionsDescriptor"/> from the <c>uuidFormat</c> config key.
    /// </summary>
    public string UuidFormat { get; set; } = TypeScriptOptionIds.UuidFormat.Default;
}

/// <summary>
/// Axios-transport options. Mirrors the <c>options.axios</c> config object.
/// </summary>
public sealed class TypeScriptAxiosOptions
{
    /// <summary>
    /// Module specifier (or file path) exporting the custom axios instance,
    /// e.g. <c>@shared/api/api-mutator</c>. A trailing <c>.ts</c>/<c>.tsx</c> extension is
    /// stripped when the import is emitted.
    /// </summary>
    public string? CustomInstancePath { get; set; }

    /// <summary>
    /// Name of the exported axios instance, e.g. <c>apiMutator</c>. Imported as a named export.
    /// </summary>
    public string? CustomInstanceName { get; set; }
}

/// <summary>
/// Options for the Next.js (App Router) fetch transport. Mirrors the <c>options.nextjs</c> config object.
/// </summary>
public sealed class TypeScriptNextJsOptions
{
    /// <summary>
    /// Default <c>next.revalidate</c> (seconds) applied to cacheable (GET) requests. When
    /// <see langword="null"/> no default is emitted and Next.js uses its own default caching. Callers can
    /// still override per call via <c>options.next.revalidate</c>.
    /// </summary>
    public int? DefaultRevalidate { get; set; }

    /// <summary>
    /// Whether to emit a per-operation <c>revalidate&lt;Op&gt;()</c> helper (calling
    /// <c>revalidateTag(tag, "max")</c> from <c>next/cache</c>) for cacheable GET operations. Defaults to
    /// <see langword="true"/>.
    /// </summary>
    public bool GenerateRevalidateHelpers { get; set; } = true;
}
