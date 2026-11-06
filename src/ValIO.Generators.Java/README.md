# ValIO.Generators.Java

The **Java** generator plugin (`LanguageId = "java"`). Generates dependency-free record and enum
definitions and an OkHttp client using a caller-supplied Jackson `ObjectMapper`.

- HTTP client: OkHttp. Serialization: Jackson. Options (e.g. `package`, `useHttpClient`,
  `generateBuildFiles`) — see `Options/`.

Pass a shared `OkHttpClient` to the generated constructor. Its application/network interceptors and
`Authenticator` remain active, so the application can log, attach dynamic bearer headers and perform
bounded token refresh. Operations also have an overload accepting `Consumer<Call> callReady`; the
callback receives the actual native call before execution. Retain that call and invoke `cancel()` to
abort an active request, or pass `Call::cancel` when the operation is already canceled. A callback does
not automatically link arbitrary `CompletableFuture` cancellation to the HTTP call.

Success bodies are decoded with their actual generic collection types; non-2xx responses throw
`IOException`. Optional query/header values are omitted when null and source wire names are preserved.
The application owns temporal Jackson modules and enum wire codecs; compiling model declarations does
not prove those runtime conversions. Arbitrary composition fields still use `Object` and do not enforce
exact `oneOf` matching.

Native gates in the test project use `VALIO_JAVAC`; client execution additionally requires
`VALIO_JAVA_CLASSPATH` containing real OkHttp, Okio, Kotlin standard-library and Jackson jars. No jars
are vendored. Missing opt-in variables skip the corresponding gate; invalid configured paths fail it.

Layout: `*GeneratorPlugin.cs`, `Steps/`, `Ast/`, `Options/`.
See the [Developer Guide](../../docs/DEVELOPER_GUIDE.md).
