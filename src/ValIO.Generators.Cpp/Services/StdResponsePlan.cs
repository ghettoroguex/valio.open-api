using ValIO.Core.IR;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Core.IR.Utilities;

namespace ValIO.Generators.Cpp.Services;

/// <summary>Selects a deterministic successful result without confusing default errors and empty responses.</summary>
/// <remarks>HEAD, 204, and 205 never carry a decoded payload. Distinct successful payload schemas require an explicit result variant.</remarks>
internal sealed record StdResponsePlan
{
    /// <summary>Gets the declared payload type before absence is represented.</summary>
    /// <value>The successful payload, or no payload for a bodyless operation.</value>
    public IrType? PayloadType { get; init; }

    /// <summary>Gets whether a successful response may have no payload.</summary>
    /// <value>True when typed and bodyless successes coexist.</value>
    public bool AllowsNoContent { get; init; }

    /// <summary>Gets the successful response contracts used for status-specific decoding.</summary>
    /// <value>The exact and range response declarations, excluding unrelated default errors.</value>
    public IReadOnlyList<IrResponse> Responses { get; init; } = [];

    /// <summary>Gets the result type including the independent absence state.</summary>
    /// <value>An optional payload for mixed successes, or the ordinary payload type.</value>
    public IrType? ResultType => PayloadType is not null && AllowsNoContent
        ? new IrOptionalType { InnerType = PayloadType }
        : PayloadType;

    /// <summary>Resolves successful response declarations independently of their source order.</summary>
    /// <param name="operation">The operation contract to inspect.</param>
    /// <returns>The selected payload and bodyless status policy.</returns>
    /// <exception cref="NotSupportedException">Successful response schemas require unsupported heterogeneous variants.</exception>
    public static StdResponsePlan Create(IrOperation operation)
    {
        if (string.Equals(operation.HttpMethod, "HEAD", StringComparison.OrdinalIgnoreCase))
            return new StdResponsePlan();

        var responses = operation.Responses.Where(response => IsSuccess(response.StatusCode)).ToList();
        if (responses.Count == 0)
            responses.AddRange(operation.Responses.Where(response => response.StatusCode == "default"));

        var payloads = responses.Where(response => !IsBodyless(response)).Select(response => response.Type!).ToList();
        var payload = payloads.FirstOrDefault();
        if (payloads.Any(type => !IrTypeComparer.AreSemanticallyEqual(payload, type, ignorePresence: false)))
            throw new NotSupportedException($"C++ operation '{operation.MethodName}' declares heterogeneous successful response schemas; a typed response variant is required.");

        return new StdResponsePlan
        {
            PayloadType = payload,
            AllowsNoContent = payload is not null && responses.Any(response => IsBodyless(response)
                || string.Equals(response.StatusCode, "2XX", StringComparison.Ordinal)),
            Responses = responses,
        };
    }

    /// <summary>Identifies HTTP success response keys supported by OpenAPI.</summary>
    /// <param name="status">The exact response code or range.</param>
    /// <returns>Whether the key denotes successful responses.</returns>
    private static bool IsSuccess(string status) => status == "2XX"
        || (int.TryParse(status, out var numeric) && numeric is >= 200 and <= 299);

    /// <summary>Identifies responses that cannot produce a payload value.</summary>
    /// <param name="response">The declared response.</param>
    /// <returns>Whether the status or schema declares a bodyless response.</returns>
    internal static bool IsBodyless(IrResponse response) => response.StatusCode is "204" or "205"
        || response.Type is null or IrPrimitiveType { PrimitiveKind: IrPrimitiveKind.Void };
}
