using System.Net;
using System.Net.Http.Headers;

namespace ValIO.Samples.HttpPipeline;

/// <summary>Applies a dynamic bearer token, request hooks, and at most one authorized refresh replay.</summary>
/// <remarks>
/// The handler buffers request content before the first attempt, preserving JSON bytes and multipart boundaries.
/// Each attempt receives a new request message and reads the current token. Hook and token delegates must support
/// concurrent calls when the owning HttpClient is shared. The caller decides whether refreshing authorizes replay.
/// </remarks>
public sealed class BearerReplayHandler : DelegatingHandler
{
    /// <summary>Limits the default buffered request body to sixteen mebibytes.</summary>
    public const long DefaultMaxReplayBodyBytes = 16 * 1024 * 1024;

    /// <summary>Reads the current opaque access token without caching it in the handler.</summary>
    private readonly Func<CancellationToken, ValueTask<string?>> _getAccessToken;

    /// <summary>Refreshes credentials and decides whether one replay is authorized.</summary>
    private readonly Func<CancellationToken, ValueTask<bool>> _refreshAccessToken;

    /// <summary>Runs after authorization is applied and before each HTTP attempt.</summary>
    private readonly Func<HttpRequestMessage, CancellationToken, ValueTask>? _beforeRequest;

    /// <summary>Runs for each received response, including the first unauthorized response.</summary>
    private readonly Func<HttpRequestMessage, HttpResponseMessage, CancellationToken, ValueTask>? _afterResponse;

    /// <summary>Bounds buffered content before any transport attempt.</summary>
    private readonly long _maxReplayBodyBytes;

    /// <summary>Connects application-owned credentials and hooks to an existing HTTP transport.</summary>
    /// <param name="innerHandler">The next handler in the native HttpClient chain.</param>
    /// <param name="getAccessToken">Reads the current JWT or another opaque bearer token for each attempt.</param>
    /// <param name="refreshAccessToken">Refreshes credentials; true authorizes exactly one replay after a 401.</param>
    /// <param name="beforeRequest">An optional per-attempt hook for headers or request logging.</param>
    /// <param name="afterResponse">An optional per-response hook; transport exceptions produce no response hook.</param>
    /// <param name="maxReplayBodyBytes">The positive maximum buffered body size, at most Int32.MaxValue.</param>
    /// <exception cref="ArgumentNullException">A required handler or delegate is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The buffer limit is outside the supported range.</exception>
    public BearerReplayHandler(
        HttpMessageHandler innerHandler,
        Func<CancellationToken, ValueTask<string?>> getAccessToken,
        Func<CancellationToken, ValueTask<bool>> refreshAccessToken,
        Func<HttpRequestMessage, CancellationToken, ValueTask>? beforeRequest = null,
        Func<HttpRequestMessage, HttpResponseMessage, CancellationToken, ValueTask>? afterResponse = null,
        long maxReplayBodyBytes = DefaultMaxReplayBodyBytes)
        : base(innerHandler)
    {
        ArgumentNullException.ThrowIfNull(getAccessToken);
        ArgumentNullException.ThrowIfNull(refreshAccessToken);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxReplayBodyBytes, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxReplayBodyBytes, int.MaxValue);
        _getAccessToken = getAccessToken;
        _refreshAccessToken = refreshAccessToken;
        _beforeRequest = beforeRequest;
        _afterResponse = afterResponse;
        _maxReplayBodyBytes = maxReplayBodyBytes;
    }

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        byte[]? body = null;
        if (request.Content is { } content)
        {
            await content.LoadIntoBufferAsync(_maxReplayBodyBytes, cancellationToken).ConfigureAwait(false);
            body = await content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        }

        using var firstAttempt = CloneRequest(request, body);
        var response = await SendAttempt(firstAttempt, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
            return response;

        try
        {
            if (!await _refreshAccessToken(cancellationToken).ConfigureAwait(false))
                return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }

        response.Dispose();
        using var replay = CloneRequest(request, body);
        return await SendAttempt(replay, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Applies credentials and hooks around a single transport attempt.</summary>
    /// <param name="request">The newly cloned message owned by the enclosing send operation.</param>
    /// <param name="cancellationToken">Cancels token retrieval, hooks, and the transport call.</param>
    /// <returns>The received response, whose ownership transfers to the caller.</returns>
    private async Task<HttpResponseMessage> SendAttempt(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var token = await _getAccessToken(cancellationToken).ConfigureAwait(false);
        request.Headers.Authorization = token is null ? null : new AuthenticationHeaderValue("Bearer", token);
        if (_beforeRequest is not null)
            await _beforeRequest(request, cancellationToken).ConfigureAwait(false);

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        try
        {
            if (_afterResponse is not null)
                await _afterResponse(request, response, cancellationToken).ConfigureAwait(false);
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    /// <summary>Copies request metadata and reuses the exact buffered content for a fresh attempt.</summary>
    /// <param name="source">The original message supplied by the generated client.</param>
    /// <param name="body">The buffered body, or null when the request has no content.</param>
    /// <returns>A new independently disposable request with the original headers and options.</returns>
    private static HttpRequestMessage CloneRequest(HttpRequestMessage source, byte[]? body)
    {
        var clone = new HttpRequestMessage(source.Method, source.RequestUri)
        {
            Version = source.Version,
            VersionPolicy = source.VersionPolicy
        };
        foreach (var header in source.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        foreach (var option in source.Options)
            clone.Options.Set(new HttpRequestOptionsKey<object?>(option.Key), option.Value);

        if (body is not null)
        {
            clone.Content = new ByteArrayContent(body);
            foreach (var header in source.Content!.Headers)
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        return clone;
    }
}
