---
title: Options reference
description: Every per-plugin option, with defaults and choices.
sidebar:
  order: 5
---

The `options` block on a project is an object keyed by option name.
Each plugin declares its own option set in an `OptionsDescriptor.cs`
class — that file is the source of truth for the tables below. This
page collates every option for every registered language plugin, with
defaults, choices, and short descriptions.

Two conventions apply across all plugins:

- **Option keys are camelCase**, e.g. `transports`, `useTanStackQuery`.
  Keys are case-sensitive at parse time.
- **Unknown keys are ignored** with a warning. This keeps configs
  forward-compatible when plugins add new options.

The "Kind" column matches the `OptionKind` enum in
`ValIO.Core/Plugins/OptionDescription.cs`:

- `String` — free-form text.
- `Bool` — `true` / `false`.
- `EnumSingle` — exactly one of the listed `Choices`.
- `EnumMulti` — an array of values from `Choices` (a single string
  is auto-promoted to a one-element array).

## TypeScript

Source: `src/ValIO.Generators.TypeScript/Options/TypeScriptOptionsDescriptor.cs`

| Name           | Kind        | Default | Choices                                                                                              | Description                                                                |
|----------------|-------------|---------|------------------------------------------------------------------------------------------------------|----------------------------------------------------------------------------|
| `transports`   | EnumMulti   | `fetch` | `fetch`, `axios`, `ky`, `undici`, `ofetch`, `got`, `angular-rxjs`, `angular-promise`, `solid`        | HTTP client library/libraries to generate. At least one required.          |
| `enumFormat`   | EnumSingle  | `union` | `union`, `enum`, `constObject`                                                                       | How OpenAPI enums are represented in TS.                                   |
| `validation`   | EnumSingle  | _none_  | `zod3`, `zod4`, `valibot`, `typebox`                                                                 | Validation library to emit runtime schemas for. Omit to skip schemas.      |
| `queryWrapper` | EnumSingle  | _none_  | `tanstack-react`, `tanstack-vue`, `tanstack-angular`, `tanstack-solid`, `tanstack-svelte`, `swr`     | Client wrapper integration. Depends on `transports`.                       |
| `mocks`        | EnumSingle  | _none_  | `msw`                                                                                                | Generate mock handlers alongside clients. Aliased as `mockLibrary`.        |
| `resultTypeSuffix` | EnumSingle | `Results` | `Result`, `Results`, `Response`, `Responses`                                                      | Suffix for the per-operation result-union type name (`{Operation}Results`). Custom strings also accepted. Aliased as `resultSuffix`. |
| `uuidFormat`   | EnumSingle  | `guid`  | `guid`, `string`, `regex`                                                                            | How `Guid`/`Uuid` fields are validated: `guid` = built-in uuid validator (`z.string().uuid()`); `string` = no refinement; `regex` = a shared format-regex constant emitted to `{schemasPath}/formats.ts`. |

Aliases accepted on input: `zod` → `zod3`; `tanstack` or `react-query` →
`tanstack-react`; `angular` → `angular-rxjs`; `angular-async` →
`angular-promise`; `solidstart` / `solid-start` → `solid`;
`@sinclair/typebox` → `typebox`.

`resultTypeSuffix` controls only the **union alias** name —
e.g. `LookupDictionaryItemsByIdResults`. The per-status variant interfaces it
unions keep their fixed `Result` base (`...Result200`, `...ResultOther`). The
four presets are matched case-insensitively and normalised to PascalCase; any
other non-empty value is accepted verbatim (first letter capitalised) so you can
resolve clashes with a generated model that already owns the `...Result` name.

`uuidFormat` picks how `Guid`/`Uuid`-typed fields validate. With `regex`, a single
constant file is emitted to `{schemasPath}/formats.ts` (`UUID_PATTERN` +
`UUID_REGEX`) and imported wherever a uuid field appears — Zod/Valibot use
`UUID_REGEX`, TypeBox uses `UUID_PATTERN` — so the pattern is defined once:

```ts
// schemas/formats.ts
export const UUID_PATTERN = "^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$";
export const UUID_REGEX = new RegExp(UUID_PATTERN);
// → z.string().regex(UUID_REGEX) / v.pipe(v.string(), v.regex(UUID_REGEX)) / Type.String({ pattern: UUID_PATTERN })
```

Validation schemas are written to a root **`schemas/`** folder (sibling of
`models/`, `endpoints/`, `index.ts`). Override the location with
`fileOrganization.schemasPath` (default `"schemas"`).

```json
{
  "language": "typescript",
  "schema":   "./openapi.json",
  "outputPath": "./src/api",
  "options": {
    "transports":      ["axios", "fetch"],
    "validation":      "zod4",
    "queryWrapper":    "tanstack-react",
    "enumFormat":      "enum",
    "mocks":           "msw",
    "resultTypeSuffix": "Results",
    "uuidFormat":      "regex"
  }
}
```

#### Custom axios instance

When `axios` is among the `transports`, an optional `axios` object lets you swap
the global `axios` import for your own pre-configured instance (interceptors,
`baseURL`, auth, …):

```json
{
  "options": {
    "transports": ["axios"],
    "axios": {
      "customInstancePath": "@shared/api/api-mutator.ts",
      "customInstanceName": "apiMutator"
    }
  }
}
```

Each generated `*.axios.ts` then imports the instance as a **named** export and
assigns it to the local `axiosInstance` variable used for every request:

```ts
import { apiMutator } from "@shared/api/api-mutator"; // .ts/.tsx extension is stripped
import type { AxiosRequestConfig, AxiosInstance } from "axios";
// ...
const axiosInstance: AxiosInstance = apiMutator;
```

Both `customInstancePath` and `customInstanceName` are required together —
supplying only one is a configuration error. When the `axios` object is omitted,
clients fall back to `import axios from "axios"`.

When using the **nested** form under `options.typescript` you also have
access to a richer set of fields parsed by `ConfigTypeScriptOptions`:

```json
{
  "options": {
    "typescript": {
      "modelFormat":           "ValidationSchemas",
      "validationLibrary":     "Zod",
      "httpTransports":        "axios",
      "useTanStackQuery":      true,
      "tanStackFramework":     "react",
      "serializeWithSchemas":  true,
      "deserializeWithSchemas": true,
      "enumFormat":            "Enum",
      "useStrictNullChecks":   true,
      "useBigInt":             false,
      "queryParametersStyle":  "ModelClass",
      "axios": {
        "customInstancePath": "@app/valioAxios.ts",
        "customInstanceName": "MyAxiosClient"
      },
      "fetch": {
        "customFetcherPath": "@app/fetcher.ts",
        "customFetcherName": "myFetcher"
      }
    }
  }
}
```

## C#

Source: `src/ValIO.Generators.CSharp/Options/CSharpOptionsDescriptor.cs`

The flat C# descriptor accepts two keys directly:

| Name                     | Kind   | Default       | Description                                                |
|--------------------------|--------|---------------|------------------------------------------------------------|
| `namespace`              | String | `Generated`   | Root namespace for generated C# code.                      |
| `nullableReferenceTypes` | Bool   | `false`       | Emit nullable-reference annotations on generated types.    |

The nested `options.csharp` form (parsed by `ConfigCSharpOptions`)
unlocks the full C# feature set:

| Name                            | Kind        | Default | Choices                                            | Description                                                             |
|---------------------------------|-------------|---------|----------------------------------------------------|-------------------------------------------------------------------------|
| `propertyAccessor`              | EnumSingle  | `Init`  | `Set`, `Init`                                      | Property setter style.                                                  |
| `useRequired`                   | Bool        | `true`  | —                                                  | Use the `required` modifier for required properties.                    |
| `useSealed`                     | Bool        | `false` | —                                                  | Mark generated classes as `sealed`.                                     |
| `modelType`                     | EnumSingle  | `Class` | `Class`, `Record`, `Struct`, `RecordStruct`        | Generated model declaration kind.                                       |
| `generateEqualityMembers`       | Bool        | `false` | —                                                  | Emit `Equals`/`GetHashCode` overrides for records.                      |
| `useNullable`                   | Bool        | `true`  | —                                                  | Emit `?` on nullable reference types.                                   |
| `generateValidationConstants`   | Bool        | `false` | —                                                  | Emit static classes with validation constants (lengths, ranges).        |
| `generateValidationAttributes`  | Bool        | `false` | —                                                  | Emit `[Required]`, `[MinLength]`, `[Range]`, `[RegularExpression]`.     |
| `queryParametersStyle`          | EnumSingle  | `IndividualParameters` | `IndividualParameters`, `ModelClass`    | How query params are passed to client methods.                          |
| `generateInterfaces`            | Bool        | `false` | —                                                  | Emit `IXxxClient` interfaces for clients.                               |
| `interfaceFileStrategy`         | EnumSingle  | `SeparateFile` | `SameFile`, `SeparateFile`                  | Where to put interface declarations.                                    |
| `clientAccessModifier`          | EnumSingle  | `Public`| `Public`, `Internal`                               | Access modifier for client classes.                                     |
| `generateTraces`                | Bool        | `false` | —                                                  | Emit OpenTelemetry traces (`Activity` spans) in clients.                |
| `generateMetrics`               | Bool        | `false` | —                                                  | Emit OpenTelemetry metrics in clients.                                  |

```json
{
  "language":   "csharp",
  "schema":     "./openapi.json",
  "outputPath": "./MyOrg.Api.Client/Generated",
  "namespace":  "MyOrg.Api.Client",
  "options": {
    "csharp": {
      "propertyAccessor":             "Init",
      "useRequired":                  true,
      "useSealed":                    true,
      "modelType":                    "Record",
      "generateValidationAttributes": true,
      "queryParametersStyle":         "ModelClass",
      "generateTraces":               true,
      "generateMetrics":              true
    }
  }
}
```

## Java

Source: `src/ValIO.Generators.Java/Options/JavaOptionsDescriptor.cs`

| Name                 | Kind        | Default                 | Choices  | Description                                                    |
|----------------------|-------------|-------------------------|----------|----------------------------------------------------------------|
| `package`            | String      | `com.example.generated` | —        | **Required.** Root Java package (e.g. `com.example.api`).      |
| `useHttpClient`      | EnumSingle  | `okhttp`                | `okhttp` | HTTP client implementation. Only `okhttp` is currently wired.  |
| `generateBuildFiles` | Bool        | `false`                 | —        | Also emit `build.gradle.kts` + `gradle.properties`.            |

Aliases accepted: `okhttp3`, `ok-http` → `okhttp`.

```json
{
  "language":   "java",
  "schema":     "./openapi.json",
  "outputPath": "./generated-java",
  "options": {
    "package":            "com.example.api",
    "useHttpClient":      "okhttp",
    "generateBuildFiles": true
  }
}
```

## Kotlin

Source: `src/ValIO.Generators.Kotlin/Options/KotlinOptionsDescriptor.cs`

| Name                 | Kind        | Default                 | Choices                       | Description                                                |
|----------------------|-------------|-------------------------|-------------------------------|------------------------------------------------------------|
| `package`            | String      | `com.example.generated` | —                             | **Required.** Root package name.                           |
| `serialization`      | EnumSingle  | `kotlinx`               | `kotlinx`, `moshi`, `gson`    | Serialization library. Only `kotlinx` is fully supported.  |
| `generateBuildFiles` | Bool        | `false`                 | —                             | Emit `build.gradle.kts` alongside sources.                 |

Aliases accepted: `kotlinx-serialization`, `kotlinx.serialization` →
`kotlinx`.

```json
{
  "language":   "kotlin",
  "schema":     "./openapi.json",
  "outputPath": "./mobile/src/main/kotlin/generated",
  "options": {
    "package":            "com.example.api",
    "serialization":      "kotlinx",
    "generateBuildFiles": true
  }
}
```

## Python

Source: `src/ValIO.Generators.Python/Options/PythonOptionsDescriptor.cs`

| Name            | Kind        | Default     | Choices                                | Description                                                              |
|-----------------|-------------|-------------|----------------------------------------|--------------------------------------------------------------------------|
| `pythonVersion` | EnumSingle  | `3.11`      | `3.10`, `3.11`, `3.12`, `3.13`         | **Required.** Target Python version. Generated syntax assumes 3.10+.     |
| `httpxMode`     | EnumSingle  | `async`     | `async`, `sync`                        | **Required.** Generate `httpx.AsyncClient` or `httpx.Client` clients.    |
| `validator`     | EnumSingle  | `pydantic`  | `pydantic`, `msgspec`                  | **Required.** Model validator library. Only Pydantic v2 is wired today.  |
| `rootModule`    | String      | `generated` | —                                      | **Required.** Top-level Python module/package name.                      |

Aliases accepted: `pydantic2`, `pydantic-v2` → `pydantic`.

```json
{
  "language":   "python",
  "schema":     "./openapi.json",
  "outputPath": "./python_sdk",
  "options": {
    "pythonVersion": "3.12",
    "httpxMode":     "async",
    "validator":     "pydantic",
    "rootModule":    "my_api_client"
  }
}
```

## PHP

Source: `src/ValIO.Generators.Php/Options/PhpOptionsDescriptor.cs`

| Name                   | Kind        | Default        | Choices              | Description                                              |
|------------------------|-------------|----------------|----------------------|----------------------------------------------------------|
| `namespace`            | String      | `App\Generated`| —                    | **Required.** Root PHP namespace.                        |
| `useHttpClient`        | EnumSingle  | `guzzle`       | `guzzle`, `laravel`  | **Required.** HTTP client to wrap. (`laravel` reserved.) |
| `generateComposerJson` | Bool        | `false`        | —                    | Also emit `composer.json` with required dependencies.    |

Aliases accepted: `guzzlehttp` → `guzzle`; `illuminate` → `laravel`.

```json
{
  "language":   "php",
  "schema":     "./openapi.json",
  "outputPath": "./php-client",
  "options": {
    "namespace":            "Acme\\Api\\Client",
    "useHttpClient":        "guzzle",
    "generateComposerJson": true
  }
}
```

## Swift

Source: `src/ValIO.Generators.Swift/Options/SwiftOptionsDescriptor.cs`

| Name                 | Kind        | Default      | Choices                  | Description                                                |
|----------------------|-------------|--------------|--------------------------|------------------------------------------------------------|
| `moduleName`         | String      | `Generated`  | —                        | **Required.** Output module/folder name.                   |
| `anyMapping`         | EnumSingle  | `anycodable` | `anycodable`, `string`   | **Required.** How IR `Any` maps onto Swift.                |
| `generateBuildFiles` | Bool        | `false`      | —                        | Emit `Package.swift` alongside sources.                    |

Aliases accepted: `any-codable` → `anycodable`.

```json
{
  "language":   "swift",
  "schema":     "./openapi.json",
  "outputPath": "./Sources/MyApiClient",
  "options": {
    "moduleName":         "MyApiClient",
    "anyMapping":         "anycodable",
    "generateBuildFiles": true
  }
}
```

## Dart

Source: `src/ValIO.Generators.Dart/Options/DartOptionsDescriptor.cs`

| Name          | Kind        | Default     | Choices                       | Description                                                |
|---------------|-------------|-------------|-------------------------------|------------------------------------------------------------|
| `packageName` | String      | `generated` | —                             | Top-level Dart package name.                               |
| `anyMapping`  | EnumSingle  | `dynamic`   | `dynamic`, `object`, `string` | How to map IR `Any`.                                       |

```json
{
  "language":   "dart",
  "schema":     "./openapi.json",
  "outputPath": "./dart_client",
  "options": {
    "packageName": "my_api_client",
    "anyMapping":  "dynamic"
  }
}
```

## F#

Source: `src/ValIO.Generators.FSharp/Options/FSharpOptionsDescriptor.cs`

| Name        | Kind   | Default       | Description                                            |
|-------------|--------|---------------|--------------------------------------------------------|
| `namespace` | String | `Generated`   | Top-level F# namespace/module prefix.                  |

```json
{
  "language":   "fsharp",
  "schema":     "./openapi.json",
  "outputPath": "./MyOrg.Api.Client.FSharp",
  "options": {
    "namespace": "MyOrg.Api.Client"
  }
}
```

## Ruby

Source: `src/ValIO.Generators.Ruby/Options/RubyOptionsDescriptor.cs`

Ruby has no domain-specific options because it is dynamically typed.
Only the output module is configurable.

| Name         | Kind   | Default     | Description                                       |
|--------------|--------|-------------|---------------------------------------------------|
| `moduleName` | String | `generated` | Output module/folder name for generated files.    |

```json
{
  "language":   "ruby",
  "schema":     "./openapi.json",
  "outputPath": "./ruby_client",
  "options": {
    "moduleName": "my_api_client"
  }
}
```

## Rust

Source: `src/ValIO.Generators.Rust/Options/RustOptionsDescriptor.cs`

| Name         | Kind        | Default      | Choices                  | Description                                              |
|--------------|-------------|--------------|--------------------------|----------------------------------------------------------|
| `crateName`  | String      | `generated`  | —                        | Output crate/folder name.                                |
| `anyMapping` | EnumSingle  | `json-value` | `json-value`, `string`   | How to map IR `Any`. Default uses `serde_json::Value`.   |

Aliases accepted: `jsonvalue`, `serde-json-value` → `json-value`.

```json
{
  "language":   "rust",
  "schema":     "./openapi.json",
  "outputPath": "./rust_client",
  "options": {
    "crateName":  "my_api_client",
    "anyMapping": "json-value"
  }
}
```

## Tips

- **Unknown options are ignored** with a warning. This lets configs
  outlive plugin upgrades — keys that newer versions add can sit in
  older configs harmlessly, and keys that newer versions remove won't
  break anything.
- **Option keys are case-sensitive.** `transports` works,
  `Transports` does not.
- **EnumMulti accepts a single string.** `"transports": "axios"` is
  equivalent to `"transports": ["axios"]`. Use the array form when you
  want multiple values: `["axios", "fetch"]`.
- **Aliases ease migration.** Many enum values accept multiple input
  spellings (`zod` ↔ `zod3`, `tanstack` ↔ `tanstack-react`, etc.) but
  the canonical form shown in the tables is what gets stored.
- **Required options without defaults fail validation.** For example,
  TypeScript's `transports` is required — an empty list aborts the run
  with `"At least one transport must be specified"`.
- **Plugin-specific shape inside `options`.** Some configs put options
  directly on `options` (`options.transports`), others nest under a
  per-language key (`options.typescript.httpTransports`). Both styles
  are supported; see [Projects: options](/configuration/projects/#options).
