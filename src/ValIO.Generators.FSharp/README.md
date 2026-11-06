# ValIO.Generators.FSharp

The **F#** generator plugin (`LanguageId = "fsharp"`). Generates records / discriminated unions and a
RestSharp-based client.

- HTTP client: RestSharp. Idiomatic discriminated unions. Options (e.g. `namespace`) — see `Options/`.

Model files import referenced model/enum modules explicitly and must be compiled in dependency order,
with enum modules before models. Generated API functions return `Task<T>` or `Task<unit>` and require
a `CancellationToken`; cancellation reaches `RestClient.ExecuteAsync`, including active requests.
Pass an application-owned `RestClient` configured with its interceptors/authenticator or an injected
`HttpClient`/`DelegatingHandler` pipeline for logging, dynamic headers and bounded refresh/retry.
Wire header/query names and encoded path parameters are preserved. HTTP failures become
`HttpRequestException` with the original status.

Native model tests use `VALIO_FSHARP_FSI` (an FSI executable or SDK `fsi.dll`). Client execution also
requires `VALIO_RESTSHARP_ASSEMBLY` pointing to a real RestSharp assembly. The integration consumer
checks typed collections, injected refresh/retry handlers and pre-send/in-flight cancellation.
Missing opt-in variables skip the corresponding gate; invalid configured paths fail it.

Discriminated unions provide a model representation; they do not by themselves validate exact `oneOf`
matches. Applications must configure JSON codecs for F# options/unions. Mixed typed/bodyless success
responses and multipart/form serialization still require separate generator work.

Layout: `*GeneratorPlugin.cs`, `Steps/`, `Ast/`, `Options/`.
See the [Developer Guide](../../docs/DEVELOPER_GUIDE.md).
