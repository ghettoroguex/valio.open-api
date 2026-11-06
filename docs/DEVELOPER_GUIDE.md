# ValIO Developer Guide

A practical guide for **contributors and developers** working on the ValIO code generator:
how the pipeline, IR, AST, builders and renderers fit together, how to write tests, and how to
extend the generator with new options, transports, validation libraries, stacks and languages.

> This guide is written in English first; a translated version will follow. For project conventions
> (branching, commit style, formatting) see [CONTRIBUTING.md](../CONTRIBUTING.md). For the high-level
> overview see the root [README.md](../README.md) and the published docs at <https://valio.dev>.

---

## 1. The big picture

ValIO turns an **OpenAPI 3.x** document into source files for a target language. The flow is the same
for every language:

```
OpenAPI (URL/file)
   │  ValIO.Parsers          IOpenApiDocumentParser
   ▼
Parsed schema
   │  ValIO.Core/IR          IrBuilder + resolver
   ▼
IR  (IrDocument: models, enums, clients/operations — language-agnostic)
   │  ValIO.Application       IGeneratorRegistry.FindPlugin(languageId)
   ▼
IGeneratorPlugin  →  ICodeGenerationPipeline  (an ordered list of ICodeGenerationStep)
   │  each step builds AST nodes from IR and renders them to text
   ▼
GeneratedFile[]  (relative path + content)
   │  ValIO.Output           IGenerationOutputSink
   ▼
Disk / Zip / Stream / InMemory
```

Key idea: **IR is language-agnostic; everything language-specific lives in a generator plugin.**
Within a plugin, code is produced as an **AST** (typed nodes) and then **rendered** to a string —
never by string concatenation in business logic (a few legacy string-based renderers remain, noted below).

The three frontends (`ValIO.Cli`, `ValIO.Web`, `ValIO.Desktop`) all call the same
`ValIO.Application` orchestrator; they never contain generation logic.

---

## 2. The IR (Intermediate Representation)

Namespace: `ValIO.Core.IR` (and `ValIO.Core.IR.Types`, `.Enums`, `.Constraints`). Produced by
`ValIO.Core/IR/Builders/IrBuilder.cs` from the parsed OpenAPI document.

| Type | What it is |
|------|------------|
| `IrDocument` | Root. `Models`, `Enums`, `Clients`, `TypeDependencies` (topological order). |
| `IrModel` | An object schema: `Name`, `Description`, `Properties`, `Composition` (allOf/oneOf/anyOf). |
| `IrProperty` | `Name`, `Type` (an `IrType`), `IsRequired`, `Description`, `Constraints`. |
| `IrEnum` | `Name`, `BaseType` (`String`/`Integer`), `Values`, `Description`. |
| `IrClient` / `IrOperation` | A client groups operations; an operation has `MethodName`, `HttpMethod`, `Path`, `PathParameters`, `QueryParameters`, `HeaderParameters`, `RequestBody`, `Responses`. |
| `IrType` (abstract) | `IrPrimitiveType` (`PrimitiveKind`: `String`, `Number`, `Integer`, `Long`, `Boolean`, `Uuid`, `DateTime`, `Date`, `Time`, `Binary`, …), `IrModelReference`, `IrArrayType`, `IrDictionaryType`, `IrUnionType`, `IrIntersectionType`, `IrNullableType`, `IrOptionalType`. |
| `IrPropertyConstraints` | `Min`, `Max`, `ExclusiveMin`, `ExclusiveMax`, `MinLength`, `MaxLength`, `Pattern`, `DefaultValue`. |

When you walk a type tree (e.g. to detect a feature or extract dependencies), handle every `IrType`
subclass recursively. See `UuidFormatSupport.ContainsUuid` and the `ExtractDependencies*` helpers for
the canonical recursion shape.

**Where formats come from:** OpenAPI `format: uuid|guid` → `IrPrimitiveKind.Uuid` is mapped in
`IrBuilder` (`TryMapSchemaFormatToPrimitiveKind` / `MapPrimitive`). Add new `format` mappings there.

---

## 3. The AST

Two layers:

- **Core AST** (`ValIO.Core/Ast/Nodes`) — language-agnostic base nodes: `AstNode`, `DeclarationNode`
  (carries `Documentation`), `InterfaceDeclarationNode`, `EnumDeclarationNode`, `CommentNode`
  (with `CommentKind`: `JSDoc`, `Xml`, `SingleLine`, `MultiLine`), `IdentifierNode`, expression and
  statement nodes, etc.
- **Language AST** (e.g. `ValIO.Generators.TypeScript/Ast/Nodes`) — TS-specific nodes such as
  `SourceFileNode`, `TypeAliasDeclarationNode`, `PropertySignatureNode`, `VariableDeclarationNode`,
  `FunctionDeclarationNode`, plus per-library subtrees (`Zod/Ast/Nodes`, `TanStackQuery/Ast/Nodes`, …).

Most declaration nodes accept a `documentation: CommentNode?`. The renderer turns that (and the node's
own structure) into JSDoc — see §8. `SourceFileNode` additionally carries `FileDocumentation` for the
file header.

Nodes are immutable; they implement the visitor pattern (`Accept<TResult>(IAstVisitor<TResult>)`) and
`Clone()`.

---

## 4. AST builders

A **builder** maps IR → AST. Example: `Ast/Builders/TypeScriptAstBuilder.cs` builds model interfaces,
enums and endpoint functions; `Ast/Builders/TypeScriptResultsAstBuilder.cs` builds the per-operation
discriminated-union result types; `Zod/Ast/Builders/*`, `Valibot/Ast/Builders/*`,
`TypeBox/Ast/Builders/*` build validation schemas.

Conventions:

- Builders take an `INamingConverter` (PascalCase/camelCase) and usually the plugin options
  (`TypeScriptGenerationOptions`) so behaviour is configurable.
- A builder returns a `SourceFileNode` (imports + declarations + optional `FileDocumentation`).
- Naming helpers live on the builders (e.g. `TypeScriptResultsAstBuilder.ResultTypeName`); reuse them so
  the type a client *imports* matches the type a schema/result *declares*.
- For JSDoc, use the shared `Ast/JsDoc/TypeScriptDoc` helper (`FileHeader`, `Block`, `ConstraintTags`).

---

## 5. AST renderers

A **renderer** turns AST → a code string. `Ast/Renderers/TypeScriptAstRenderer.cs` is the main TS
renderer (a visitor). It auto-assembles rich JSDoc for `FunctionDeclarationNode`
(`@function/@async/@param/@returns`) and `PropertyDeclarationNode` (`@type/@nullish/@minLength/…`) from
the node's structure — so to enrich those you set node fields, not strings.

**Renderer chain (DI):** node renderers implement `ITypeScriptNodeRenderer` and are injected so that
library-specific nodes (Zod/Valibot/TypeBox/TanStack) can be rendered by their own renderer without the
core renderer knowing about them. The plugin wires the chain in `CreatePipeline`.

> **Legacy string-based renderers:** `Zod/Services/ZodSchemaRenderer.cs` builds Zod schema files with a
> `StringBuilder` rather than AST. New code should prefer AST; when editing the Zod renderer, keep using
> `CommentGenerator.GenerateJSDocComment` for comments so headers/JSDoc stay consistent.

---

## 6. Plugins, the pipeline and steps

A language is an `IGeneratorPlugin` (`ValIO.Core/Plugins/IGeneratorPlugin.cs`):

```csharp
public interface IGeneratorPlugin
{
    string LanguageId { get; }                       // e.g. "typescript"
    string DisplayName { get; }
    IReadOnlyList<string> LanguageAliases { get; }    // e.g. ["ts", "typescript"]
    IGeneratorOptionsDescriptor OptionsDescriptor { get; }
    ICodeGenerationPipeline CreatePipeline(IServiceProvider services); // ordered steps
    void ConfigureServices(IServiceCollection services);
}
```

`CreatePipeline` returns a `CodeGenerationPipeline(steps)`. Each step is an `ICodeGenerationStep`:

```csharp
public interface ICodeGenerationStep
{
    string Name { get; }
    int Order { get; }                                   // pipeline runs steps in Order
    bool ShouldExecute(GenerationContext context);       // gate on options/IR
    Task ExecuteAsync(GenerationContext context, CancellationToken ct = default);
}
```

A step reads `context.IrDocument`, `context.PluginOptions` (cast to your options type),
`context.Configuration.FileOrganization`, builds AST via a builder, renders it, and appends
`context.GeneratedFiles.Add(new GeneratedFile(relativePath, content))`. Order matters only for
readability/dependencies — files are independent strings.

Plugins are registered for all frontends in **one** place:
`src/ValIO.Plugins.Bootstrap/PluginsBootstrapExtensions.cs` (`AddAllGeneratorPlugins`).

---

## 7. Options (settings)

Each plugin exposes an `IGeneratorOptionsDescriptor` that parses the project's `options` block into a
strongly-typed options object. The TypeScript plugin uses a consistent 3-file pattern (mirror it for
new options):

1. **`Options/TypeScriptOptionIds.cs`** — canonical string constants + a `Default` + a `Resolve(string?)`
   normaliser for each enum-like option (e.g. `Transport`, `Validation`, `UuidFormat`, `ResultSuffix`).
2. **`Options/TypeScriptGenerationOptions.cs`** — the typed option properties (with defaults).
3. **`Options/TypeScriptOptionsDescriptor.cs`** — `ParseOptions(rawOptions, out errors)` reads the JSON
   keys, normalises them, and `GetOptionDescriptions()` returns `OptionDescription` rows (used by the
   UI and the docs `options-reference.md`). Add an `OptionChoice[]` for enum options.

Also add a localized description key to `src/ValIO.Localization/Resources/PluginStrings.resx`
(e.g. `ts.uuidFormat.desc`) — missing keys fall back to the inline `Description`.

**Threading an option through builders:** add the value to the options object, then pass it from the
*step* (which has `context.PluginOptions`) into the *builder* constructor, and from the builder into any
nested builder/renderer. `resultTypeSuffix`, `uuidFormat` and the custom-axios-instance options are
worked examples of this end-to-end thread.

`FileOrganization` (`ValIO.Core/Models/FileOrganization.cs`) holds output-folder settings
(`ModelsPath`, `ClientsPath`, `SchemasPath`) and is mapped from config in
`ValIO.Configuration/Services/ConfigurationLoader.cs`.

---

## 8. JSDoc / documentation comments (TypeScript)

- File header: builders set `SourceFileNode.FileDocumentation = TypeScriptDoc.FileHeader(file, module, description)`;
  the renderer emits it at the top (after any `"use server"` directive). String-based renderers
  (`ZodSchemaRenderer`, `TypeScriptIndexStep`) prepend it via `CommentGenerator.GenerateJSDocComment`.
- Multi-tag comments: put newline-separated `@tag value` lines in `CommentNode.Text` (JSDoc). The
  renderer splits them. Use `TypeScriptDoc.Block(description, (tag, value)…)` to assemble them.
- Functions/properties: set the node's `Documentation` + parameter docs; the renderer adds
  `@param/@returns/@type/@nullish/@minLength/…` automatically.

---

## 9. Writing tests

Tests live under `tests/` (one project per source assembly), NUnit 4 + FluentAssertions. Run:

```bash
dotnet test                                                   # everything
dotnet test tests/ValIO.Generators.TypeScript.Tests           # one project
dotnet test --filter "FullyQualifiedName~UuidFormat"          # by name
```

### Unit tests (builder / renderer level)

Test a single builder or renderer in isolation — fastest, most precise. Construct the builder with a
`NamingConverter` and a type-mapper, feed it a small IR object, and assert on the produced AST or the
rendered string. Example shape (see `tests/.../Builders/TypeBoxAstBuilderTests.cs`,
`Builders/TypeScriptResultsAstBuilderTests.cs`):

```csharp
var sut = new TypeBoxAstBuilder();
var rendered = _renderer.Visit(sut.BuildEnumSchema(enumIr));
rendered.Should().StartWith("Type.Union(");
```

### Integration tests (full pipeline)

Use `PipelineTestHarness` (`tests/.../TestHelpers/`) to run the whole plugin against a fixture and assert
on the generated files:

```csharp
var doc = await PipelineTestHarness.LoadDocumentAsync("simple-schema.json");   // TestData/*.json (embedded)
var ctx = await _harness.RunAsync(doc, new TypeScriptGenerationOptions { Transports = ["ky"], ValidationLibrary = "zod4" });
var file = ctx.GeneratedFiles.First(f => f.RelativePath.EndsWith("getUserById.ky.ts", StringComparison.Ordinal));
file.Content.Should().Contain("z.string().uuid()");
```

Fixtures are JSON files under `tests/.../TestData/` (globbed as embedded resources — just drop a new
`*.json` in). Add a focused fixture when you need a specific shape (e.g. `uuid-schema.json` for uuid
fields) instead of bending an existing one.

### Snapshot ("golden file") tests

`Snapshots/StepSnapshotTests.cs` renders selected outputs and compares against `__snapshots__/*.txt`.
After an **intentional** output change, regenerate and review the diff:

```bash
UPDATE_SNAPSHOTS=1 dotnet test tests/ValIO.Generators.TypeScript.Tests --filter "FullyQualifiedName~StepSnapshotTests"
```

Prefer `.Should().Contain(...)` (substring) assertions over exact matches where you only care about one
fact — they survive unrelated formatting changes (e.g. a new file header).

### Tip: eyeball real output

To sanity-check generated code, temporarily `System.IO.File.WriteAllText(@"...\out\sample.txt", file.Content)`
inside a test, run it, read the file, then revert the line. Keeps you honest about the actual output.

---

## 10. Extending ValIO

### 10a. Add a new option / setting

1. `TypeScriptOptionIds`: constants + `Default` + `Resolve` (if enum-like).
2. `TypeScriptGenerationOptions`: property.
3. `TypeScriptOptionsDescriptor`: parse the key + add an `OptionDescription` (+ `OptionChoice[]`).
4. `PluginStrings.resx`: `ts.<option>.desc`.
5. Thread it from the step → builders → renderers.
6. Tests: a descriptor parse test + an integration test per behaviour; update docs
   `docs-site/.../configuration/options-reference.md` and the example config.

### 10b. Add a new transport / HTTP client (existing language)

Pattern (TypeScript): create `XxxClientAstBuilder` (often mirroring `Ky/Ast/Builders/KyClientAstBuilder.cs`),
add an `XxxClientStep : TypeScriptClientStepBase` with a `FileSuffix` (e.g. `.xxx.ts`), register the step
in `TypeScriptGeneratorPlugin.CreatePipeline`, add the transport id to `TypeScriptOptionIds.Transport`
+ a descriptor choice + normaliser alias, and add tests + a docs/stack page. Reuse the shared
`TypeScriptParamsAstBuilder` / `TypeScriptResultsAstBuilder` for params/results so names line up.

### 10b-cpp. C++ runtime profiles (`ue` | `std`)

The C++ plugin hosts two runtime profiles behind one option (`runtime`); pipeline steps
self-gate on `context.PluginOptions` (`CppUe*Step` run when `Runtime != Std`, `CppStd*Step`
when `Runtime == Std`), so the pipeline always contains both groups — the TS-transport pattern
applied to profiles. Adding a third profile: extend `CppRuntimeProfile` + `CppOptionsDescriptor`,
add a `Cpp<X>Step` group gated on the new value, and builders under `Ast/Builders/<X>/`.

The **std profile** (cpp-httplib + nlohmann_json) keeps model headers JSON-free
(`Models/*.h` are std-only; `Serializers/*Json.h/.cpp` own the wire names via ADL
`to_json`/`from_json`; `<httplib.h>` lives in one TU — `Runtime/ValIOHttp.cpp`).
Its canonical runtime files are raw-string templates in `Runtime/StdRuntimeTemplates.cs`
(precedent: `CSharpClientStep`'s ApiException/multipart factory) emitted once per project
by `CppStdRuntimeStep`. Compile-verified against exact pins cpp-httplib v0.18.3 +
nlohmann_json v3.12.0 on C++17/20/23 by `tests/ValIO.Generators.Cpp.CompileTests`
(cmake required; `Assert.Ignore` when absent; CI job `cpp-compile-fixture`).

Universal declaration nodes live in Core AST (`NamespaceDeclarationNode`,
`ClassDeclarationNode`, `StructDeclarationNode`, `FieldDeclarationNode` — promoted from
the C# generator; `EnumDeclarationNode.UnderlyingType`, `EnumMemberDeclarationNode.WireValue`,
`MethodDeclarationNode.IsStatic/IsVirtual/IsAbstract/IsConst/Noexcept`). New languages must
reuse them instead of duplicating per-language copies; remaining per-language duplicates
(Java/Kotlin/etc. class nodes) are documented backlog.

### 10c. Add a new validation library

Mirror `Zod` / `Valibot` / `TypeBox`: a schema builder + a source-file builder + a step gated on
`ValidationLibrary == "<id>"`, the id in `TypeScriptOptionIds.Validation` + descriptor, and the
schema-import wiring in the client builders' `BuildSchemaImportPath` / `AddValidationRuntimeImports`.
Honour shared options (e.g. `uuidFormat`) so the new library behaves like the others.

### 10d. Add a new query wrapper / stack

A "stack" is usually a combination of existing transport + validation + query wrapper, exercised by a
docs page under `docs-site/.../stacks/`. A genuinely new query wrapper follows the transport pattern
(builder + step + option id + tests). The five TanStack hook builders
(`TanStackQuery/**/Builders/*HookAstBuilder.cs`) are the reference.

### 10e. Add a new target language

1. New project `src/ValIO.Generators.<Lang>/` + test project `tests/ValIO.Generators.<Lang>.Tests/`.
2. An `IrType → <Lang>AST` mapper, `<Lang>` AST nodes (reuse Core AST where possible), builders and a
   renderer (visitor).
3. Steps (models, enums, clients, …) and a `<Lang>GeneratorPlugin : IGeneratorPlugin` with `LanguageId`,
   aliases, an `IGeneratorOptionsDescriptor`, and `CreatePipeline`.
4. Register it in `PluginsBootstrapExtensions.AddAllGeneratorPlugins`.
5. Add the language row to the root README table and a docs page; add the options table to
   `options-reference.md`.
6. Tests: a `<Lang>PipelineTests` integration suite + builder/renderer unit tests.

Use an existing single-transport language (e.g. `ValIO.Generators.Rust` or `.Swift`) as the smallest
reference; `ValIO.Generators.TypeScript` is the most feature-complete (multiple transports, validation
libraries, query wrappers, mocks).

---

## 11. Build & verify checklist

```bash
dotnet build ValIO.sln          # 0 errors
dotnet test                     # all green (regenerate snapshots for intentional output changes)
dotnet format                   # before committing (see CONTRIBUTING.md)
```

- One coherent change per PR; Conventional Commits (`feat(typescript): …`).
- Update tests, snapshots, the options reference and the example config alongside behaviour changes.
- Per-project responsibilities are summarised in each project's `README.md`.
