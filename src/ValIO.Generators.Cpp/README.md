# ValIO.Generators.Cpp

The C++ plugin (`LanguageId = "cpp"`) supports Unreal Engine (`runtime: "ue"`, the default)
and standalone C++17/20/23 (`runtime: "std"`). Both use the shared IR and language-specific AST builders.

- Unreal Engine output uses the UE HTTP module, reflected types, and async actions. Module API,
  Blueprint, and external UE runtime include options are configured through `CppOptionsDescriptor`.
- Standalone clients use cpp-httplib and nlohmann JSON behind generated runtime and serializer files.
  `apiStyle` selects `sync`, `async`, or `both`; `clientShape` selects `class` or `interface+impl`.

## Standalone dependency boundaries

`Models/*.h` and `Enums/*.h` contain only type definitions and standard library dependencies.
They do not include nlohmann JSON, cpp-httplib, or generated runtime headers. Trivial DTOs and enum
definitions are header-only; empty implementation files provide no useful separation.

`Serializers/*Json.h` declares JSON conversion and enum wire-conversion APIs. These headers include
only nlohmann's forward declaration header. Implementations live in `Serializers/*Json.cpp`, where
the full JSON library is included. Enum parsing uses an enum-specific name such as `StateFromWire`
so multiple enum types with the same wire representation can share a namespace.
Integer enums select a signed 32-bit or 64-bit representation from their declared values; unsupported
integer values fail explicitly rather than being replaced by declaration ordinals.

`Runtime/ValIOJsonTypes.h` supplies nullable JSON adapters for codec implementations, including
nullable elements inside arrays and dictionaries. HTTP client output adds `ValIOHttp`, `ValIOJson`,
`ValIOError`, and `ValIOExpected`; serializer-only output does not emit these unused client layers.

For pure definition output, disable `GenerationConfiguration.GenerateClients` and set
`emitSerializers: false`. The default `emitSerializers: true` emits the independently consumable
codec files. Client generation requires serializers.

With `emitCmake: true`, the generated project exposes:

- `valio_models`: an interface target with generated include paths and the language standard only.
- `valio_serializers`: JSON codec implementations and their explicit JSON dependency.
- `valio_clients`: HTTP client implementations, linking cpp-httplib privately.
- `valio_generated`: the aggregate interface target for the enabled layers.

Set `VALIO_BUILD_SERIALIZERS=OFF` and `VALIO_BUILD_CLIENTS=OFF` when configuring a definition-only
consumer. This path does not search for either third-party package.
Serializer and client builds reuse canonical `nlohmann_json::nlohmann_json` and `httplib::httplib`
targets when the parent project already defines them; otherwise they discover installed CMake packages.

## Contract semantics and validation

Required nullable properties retain their required JSON key. Optional nullable properties use
`std::optional<std::optional<T>>` to distinguish a missing key, explicit null, and a value.
Deserialization resets absent optional fields when reading into an existing object. Empty models
serialize as `{}` and reject non-object input. Async operations capture configuration and arguments
by value and own an independent worker client, so they can outlive the originating client.

The native fixture compiles a model-only translation unit without third-party include paths, compiles
all codec/client implementations, and runs codec and HTTP smoke checks on C++17/20/23. It covers
multiple string enums, integer enum query values, primitive responses, nullable containers, optional
presence, POST/PUT/PATCH multipart data, and async-only output.

The fixture downloads shallow checkouts of cpp-httplib `v0.18.3` and nlohmann JSON `v3.12.0`.
To reuse existing source directories, set `VALIO_CPP_HTTPLIB_SOURCE` and/or `VALIO_CPP_JSON_SOURCE`.
Each directory must contain its `CMakeLists.txt` and public headers; the caller must ensure the
directory contains the exact pinned dependency version. These overrides use real library sources
and keep native build directories separate for every matrix case. Failed cases retain their
generated source, build directory, and configure/build/smoke logs at the reported temporary path.
For offline native verification, `VALIO_CPP_CMAKE_PREFIX_PATH` accepts existing CMake package search
directories separated by semicolons, such as a Conan CMakeDeps output directory. This selects
`VALIO_USE_INSTALLED_DEPENDENCIES=ON` and requires the exact versions `httplib 0.18.3` and
`nlohmann_json 3.12.0` through their installed CMake package configurations, without fetching sources.

Direct self-recursive value properties are rejected with an explicit diagnostic instead of dropping
fields. Mutually recursive value models require an ownership representation and remain unsupported. The
current union/intersection/any mapping still uses a text fallback with generation warnings; arbitrary
structured JSON requires a dedicated value codec. Typed variant validation and discriminator dispatch
are not implemented by that fallback. Composed model alternatives and nested non-reference allOf
members are rejected before emission so that only part of a schema cannot become a published DTO.
Resolved single-base and multiple-base allOf object models retain their inherited or flattened fields.

Layout: `*GeneratorPlugin.cs`, `Steps/`, `Ast/`, `Services/`, `Options/`.
See the [Developer Guide](../../docs/DEVELOPER_GUIDE.md).
