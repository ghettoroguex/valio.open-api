---
title: C++ (Unreal Engine / standalone std)
description: C++ generator options and outputs — UE runtime profile and the standalone cpp-httplib + nlohmann_json std profile.
sidebar:
  order: 16
---

## Overview

The C++ plugin (`languageId: "cpp"`) targets **Unreal Engine 5** rather than
generic C++. It emits `USTRUCT`/`UCLASS` types, Blueprint-callable
`UFUNCTION`s, and async actions derived from `UBlueprintAsyncActionBase`,
wired into UE's HTTP module via a small ValIO runtime.

## What gets generated

- **USTRUCT models** — one struct per OpenAPI schema, with `UPROPERTY`
  metadata for the Blueprint editor and Doxygen comments preserved from the
  schema description.
- **UENUM enums** — Blueprint-visible enums with `DisplayName` metadata.
- **Async client actions** — one `UCLASS` per operation, inheriting from
  `UValIOAsyncActionBase`. Exposed as a Blueprint async node returning the
  typed response.
- **Native API functions** — free C++ functions wrapping the async actions
  so the same operations are callable from regular C++ code.
- **Request builders** — helper code that translates path/query parameters
  into `FValIORequestBuilder` calls.

The plugin currently exposes **no user-facing options**; behaviour is
configured in `CppGenerationOptions` defaults inside the assembly.
Notable internal knobs include:

| Internal setting | Default | Purpose |
|------------------|---------|---------|
| `ModuleApiMacro` | `MODULE_API` | Macro inserted on `UCLASS`/`USTRUCT` (e.g. `MYPROJECT_API`). |
| `GeneratedHeaderSuffix` | `.generated.h` | UHT-required suffix for generated headers. |
| `BlueprintCategory` | `ValIO` | Default category in the Blueprint Editor. |
| `BaseAsyncActionClassName` | `UValIOAsyncActionBase` | Base class for async operations. |

## File layout

```
<output>/
  Generated/
    Models/FPet.h, FPet.cpp
    Enums/EStatus.h
    Endpoints/UGetPetsAsync.h, UGetPetsAsync.cpp
    Params/FPetsListParams.h
    Result/FPetsListResult.h
```

## Example

```bash
valio generate -s ./openapi.yaml -o ./Source/MyGame -l cpp
```

## Standalone std runtime profile (`runtime: "std"`)

Besides Unreal Engine, the C++ plugin emits **standalone modern C++** built on
**cpp-httplib** (transport) and **nlohmann_json** (model format). The output is
verified by a CMake compile fixture against exact pins `cpp-httplib v0.18.3` and
`nlohmann_json v3.12.0` on C++ **17/20/23** (MSVC and GCC), including a live
smoke-server round-trip.

### Model/codec separation (no JSON in model headers)

Consumers of generated models depend on **std only**. `Models/*.h` and
`Enums/*.h` contain zero third-party includes; `to_json`/`from_json` are free
ADL functions declared in `Serializers/*Json.h` (forward declarations only)
and defined in `Serializers/*Json.cpp` (the full `<nlohmann/json.hpp>`).
`<httplib.h>` appears in exactly one translation unit (`Runtime/ValIOHttp.cpp`).

### Options

| Option | Default | Purpose |
|--------|---------|---------|
| `runtime` | `ue` | `ue` (Unreal) or `std` (cpp-httplib + nlohmann_json). |
| `cxxStandard` | `17` | Target standard: 17, 20 or 23. `ValIO::Expected` upgrades to `std::expected` at compile time; generated code is identical. |
| `apiStyle` | `sync` | `sync` (`ValIO::Expected<T, ValIO::Error>`), `async` (`std::future`), or `both`. |
| `clientShape` | `class` | `class` (single `{Client}Client`) or `interface+impl` (`I{Client}` pure-virtual interface + implementation — mocks/DI friendly). |
| `namespace` | `Api` | Root namespace for models/enums/clients. |
| `httpInclude` / `jsonInclude` / `jsonFwdInclude` | httplib.h / nlohmann/json.hpp / json_fwd.hpp | Include overrides for consumers with wrapper headers. |
| `errorModelName` | `RestApiErrorResponse` | Error model name (reserved for non-2xx body parsing). |
| `emitCmake` | `false` | Emit a CMakeLists snippet. |

### File layout

```
<output>/
  Models/Order.h              # pure struct, std-only includes
  Enums/OrderStatus.h         # enum class + constexpr ToWire/FromWire (no JSON)
  Serializers/OrderJson.h/.cpp# ADL to_json/from_json — the only place with wire names
  Clients/IStore.h            # (clientShape=interface+impl) pure-virtual interface
  Clients/StoreClient.h/.cpp  # implementation, named local variables per step
  Runtime/ValIO*.h/.cpp       # self-contained ValIO runtime (Expected/Error/Http/Json)
```

### Example

```bash
valio generate -s ./openapi.yaml -o ./generated -l cpp --opt-from-config
```

```jsonc
// valio.config.json
{ "projects": { "store": {
    "language": "cpp",
    "schema": "./openapi.yaml",
    "outputPath": "./generated",
    "options": { "runtime": "std", "cxxStandard": 17, "apiStyle": "both",
                 "clientShape": "interface+impl", "namespace": "Petstore" }
} } }
```
