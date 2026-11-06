# ValIO

> ValIO — type-safe HTTP clients, models, and validation schemas for 12 languages, generated from any OpenAPI 3.x schema.

[![CI](https://github.com/valio-projects/valio.open-api/actions/workflows/ci.yml/badge.svg)](https://github.com/valio-projects/valio.open-api/actions/workflows/ci.yml)
[![Release](https://github.com/valio-projects/valio.open-api/actions/workflows/release.yml/badge.svg)](https://github.com/valio-projects/valio.open-api/actions/workflows/release.yml)
[![NuGet](https://img.shields.io/nuget/v/ValIO.Cli.svg)](https://www.nuget.org/packages/ValIO.Cli)
[![npm](https://img.shields.io/npm/v/@val.io/valio.svg)](https://www.npmjs.com/package/@val.io/valio)
[![License: LGPL v2.1](https://img.shields.io/badge/License-LGPL_v2.1-blue.svg)](./LICENSE)
[![Docs](https://img.shields.io/badge/docs-GitHub_Pages-blue)](https://valio-projects.github.io/valio.open-api/)

## Quick start

Install as a .NET global tool:

```bash
dotnet tool install --global ValIO.Cli
valio generate -s ./openapi.yaml -o ./out -l typescript
```

Install via npm:

```bash
npm install -g @val.io/valio
valio generate -s ./openapi.yaml -o ./out -l typescript
```

Build from source:

```bash
dotnet build
dotnet run --project src/ValIO.Cli -- generate -s ./openapi.yaml -o ./out -l typescript
```

## Why ValIO

- **12 target languages** — TypeScript, C#, Java, Kotlin, Python, PHP, C++, Swift, Rust, Ruby, F#, Dart.
- **Three frontends, one engine** — `ValIO.Cli` (System.CommandLine), `ValIO.Web` (Blazor Server + MudBlazor), `ValIO.Desktop` (Avalonia 11).
- **Plugin architecture** — every language ships as an `IGeneratorPlugin`; drop in your own without touching the core.
- **Pluggable outputs** — `IGenerationOutputSink` writes to disk, packs a zip file, streams a zip over HTTP, or stays in memory.
- **Localized UI** — English, Русский, Deutsch, 简体中文 across the CLI, web shell, and desktop app.

## Supported languages

| Language   | Default HTTP client                                                       | Validation library            | Notes                                |
|------------|---------------------------------------------------------------------------|-------------------------------|--------------------------------------|
| TypeScript | Fetch / Axios / Ky / Got / OFetch / Undici / Angular / Solid              | Zod 3/4, Valibot, TypeBox     | + TanStack Query, SWR, MSW           |
| C#         | HttpClient                                                                | None (POCOs)                  | System.Text.Json                     |
| Java       | OkHttp                                                                    | Bean Validation               | Jackson                              |
| Kotlin     | OkHttp                                                                    | None                          | Kotlinx.Serialization                |
| Python     | httpx                                                                     | Pydantic                      | async/sync                           |
| PHP        | Guzzle                                                                    | None                          | PSR-7                                |
| C++        | Unreal HTTP / cpp-httplib                                                 | None                          | `runtime: ue` (default) \| `std`: nlohmann_json, JSON-free model headers, C++17/20/23 |
| Swift      | URLSession                                                                | Codable                       | iOS/macOS                            |
| Rust       | Reqwest                                                                   | Serde                         | tokio                                |
| Ruby       | Faraday                                                                   | None                          | Ruby 3+                              |
| F#         | RestSharp                                                                 | None                          | discriminated unions                 |
| Dart       | http                                                                      | None                          | null-safe                            |

## Examples

### TypeScript (Fetch + Zod 4)

```bash
valio generate -s ./petstore.yaml -o ./out/ts -l typescript \
  --opt transports=fetch --opt validation=zod4
```

```ts
import { z } from "zod";
export const Pet = z.object({ id: z.number(), name: z.string() });
export type Pet = z.infer<typeof Pet>;
export async function getPetById(id: number): Promise<Pet> { /* ... */ }
```

Generated TypeScript keeps field presence and JSON nullability independent: required values use
`value: T`, required nullable values use `value: T | null`, optional values use `value?: T`, and
optional nullable values use `value?: T | null`. Zod 3 and 4 follow the same four cases, and string-keyed
records use `z.record(z.string(), valueSchema)`.

For Axios, configure `axios.customInstancePath` and `axios.customInstanceName` to select the default
instance (including its interceptors). Each generated endpoint also accepts an optional native
`AxiosInstance` after `signal`; a supplied client overrides that default for the call. When an operation
has response bodies, documented bodyless 2xx statuses are success variants with `body: undefined`.
Operations with no documented bodies retain `Promise<void>`. The `uuidFormat: regex` option validates
the entire UUID string, including uppercase and all-zero UUIDs.

### C# (HttpClient + System.Text.Json)

```bash
valio generate -s ./petstore.yaml -o ./out/cs -l csharp \
  --opt namespace=PetStore.Generated
```

```csharp
public sealed record Pet(long Id, string Name);
public sealed class PetStoreClient(HttpClient http)
{
    public Task<Pet?> GetPetById(long id, CancellationToken ct = default) =>
        http.GetFromJsonAsync<Pet>($"/pet/{id}", ct);
}
```

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│  Frontends:  ValIO.Cli      ValIO.Web         ValIO.Desktop     │
│              (System.       (Blazor Server +  (Avalonia 11 +    │
│               CommandLine)   minimal API)     CommunityToolkit) │
└────────────────────────────┬────────────────────────────────────┘
                             │
                             ▼   IGenerationOrchestrator
┌─────────────────────────────────────────────────────────────────┐
│                       ValIO.Application                         │
│   1. Parse schema (URL or file) via IOpenApiDocumentParser      │
│   2. Build IR via IrBuilder + schema resolver                   │
│   3. Resolve plugin via IGeneratorRegistry.FindPlugin           │
│   4. Run pipeline (each plugin = ordered ICodeGenerationSteps)  │
│   5. Return GenerationResult { Files, Warnings }                │
└────────────────────────────┬────────────────────────────────────┘
                             │
                             ▼   IGenerationOutputSink
                ┌────────────┴────────────┐
                ▼            ▼            ▼
        DiskOutputSink  ZipFileSink  ZipStreamSink / InMemorySink
```

## Project layout

```
ValIO.sln
├── src/
│   ├── ValIO.Core/                — IR, AST, plugin contracts
│   ├── ValIO.Parsers/             — OpenAPI document parsing
│   ├── ValIO.Configuration/       — valio.config.{json,yaml}
│   ├── ValIO.Application/         — orchestration engine
│   ├── ValIO.Output/              — Disk / Zip / Stream / InMemory sinks
│   ├── ValIO.Localization/        — en/ru/de/zh-CN resources
│   ├── ValIO.Plugins.Bootstrap/   — AddAllGeneratorPlugins() DI helper
│   ├── ValIO.Generators.*/        — 12 language plugins
│   ├── ValIO.Cli/                 — `valio` global tool
│   ├── ValIO.Web/                 — Blazor Server + REST API
│   └── ValIO.Desktop/             — Avalonia 11 app
├── tests/                         — NUnit 4 + FluentAssertions
├── docs/                          — design notes, ADRs
├── docs-site/                     — Starlight (Astro) documentation
├── packaging/npm/                 — npm wrapper + platform packages
└── Examples/                      — sample OpenAPI schemas + outputs
```

## Documentation

Full documentation lives at <https://valio-projects.github.io/valio.open-api/>.

Stable releases from `main` publish the .NET tool and libraries to NuGet, the
CLI to npm, and the web image to `ghcr.io/valio-projects/valio-web`. See
[CI/CD](docs/CI_CD.md) for release setup and
[web deployment](deploy/web/README.md) for the optional Docker Compose host.

To run the docs locally:

```bash
cd docs-site
npm install
npm run dev   # http://localhost:4321
```

## Contributing

Pull requests are welcome — see [CONTRIBUTING.md](./CONTRIBUTING.md) for guidelines, the development loop, and the code-of-conduct.

For the internals — how the IR, AST, builders, renderers, plugins, options and tests fit together, and
how to add a new option, transport, validation library, stack or language — read the
**[Developer Guide](./docs/DEVELOPER_GUIDE.md)**. Each project under `src/` and `tests/` also has its own
`README.md` describing its responsibility.

## License

ValIO is released under the [GNU Lesser General Public License v2.1](./LICENSE).
