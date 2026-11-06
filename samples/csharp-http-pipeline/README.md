# C# HTTP hooks and bearer refresh

Generated C# clients accept an existing `HttpClient` in their constructor. Compose the native
`DelegatingHandler` chain once, then pass that client to the generated API. The supplied
[`BearerReplayHandler`](BearerReplayHandler.cs) reads the current token on every attempt, applies
before/after hooks, and allows one replay when the refresh delegate authorizes it.

```csharp
using ValIO.Samples.HttpPipeline;

var handler = new BearerReplayHandler(
    new SocketsHttpHandler(),
    getAccessToken: ct => credentials.GetAccessToken(ct),
    refreshAccessToken: ct => credentials.Refresh(ct),
    beforeRequest: (request, ct) =>
    {
        request.Headers.TryAddWithoutValidation("X-Correlation-ID", correlationId);
        logger.LogInformation("Sending {Method} {Uri}", request.Method, request.RequestUri);
        return ValueTask.CompletedTask;
    },
    afterResponse: (request, response, ct) =>
    {
        logger.LogInformation("Received {Status}", response.StatusCode);
        return ValueTask.CompletedTask;
    });
using var httpClient = new HttpClient(handler) { BaseAddress = serviceAddress };
var api = new MyGeneratedApi(httpClient);
await api.GetItem(id, cancellationToken: cancellationToken);
```

The application implements the credential methods as `ValueTask<string?>` and `ValueTask<bool>`
and controls token storage and refresh synchronization. JWT values are opaque bearer tokens;
the generated client does not decode them. Returning `false` from refresh returns the original
401 to the generated client's normal `ApiException` path. A second 401 is returned without
another refresh. Authorize replay only for operations your service permits to retry.

Both hooks run for each received attempt: a 401 followed by a successful replay produces two
before calls and two after calls. An exception before receiving a response has no after-response
call. Observe such exceptions around the generated API call or in another handler. Hooks may
inspect headers but should avoid logging credentials. Additional handlers may be placed outside
this handler to observe a whole logical call, or inside it to observe individual attempts.

The sample buffers content before sending, with a configurable default limit of 16 MiB. It copies
the exact JSON bytes, multipart boundary and content headers into each fresh request. File streams
may be non-seekable because replay reads the buffer. Exceeding the limit fails before the first
transport attempt. For larger streaming uploads, provide an application-owned request/content
factory instead of this buffering sample. Dispose the API's `HttpClient` according to your normal
ownership policy; generated clients do not dispose the injected client.

Every generated operation, interface and telemetry decorator exposes a final optional
`CancellationToken`. It is checked before preparing the request and reaches `SendAsync`, response
reads and async JSON deserialization. `JsonContent` serializes JSON during the token-aware HTTP
content write. Pass the token through token retrieval, refresh and hooks as shown above; a replay
uses the same call token. Multipart streams are owned by the generated request and are disposed
after the call. Non-2xx responses retain their raw body in `ApiException`; bodyless success remains
supported.

The generated C# compilation tests compile this exact sample and exercise JSON/multipart replay,
dynamic tokens, hook counts, repeated 401s, buffering limits, errors, bodyless responses and
cancellation before and during transport.
