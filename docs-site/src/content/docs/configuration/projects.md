---
title: Projects
description: "Multi-project configuration: one config, many outputs."
---

## What is a project

A **project** in ValIO is a single `(schema, language, outputPath)`
tuple. Given an OpenAPI document, a target language, and somewhere to
write files, ValIO produces one set of generated sources. The
`projects` object in your config is a map from a project name to its
configuration — every entry produces one output. A single `valio
generate` invocation can run any subset of them.

The project name is a case-sensitive key (`web`, `server`, `mobile`
below) and doubles as the label used in CLI logs.

## Why multi-project

- **One service, many languages.** Generate a TypeScript client for
  the frontend and a C# client for a console app from the same schema.
- **One language, many services.** Generate a separate TypeScript
  client per microservice, all from one config.
- **A/B comparing options.** Use multiple projects to emit the same
  schema with `transports: ["fetch"]` vs `["axios"]`, or `validation:
  "zod3"` vs `"zod4"`, into separate folders for comparison.
- **CI matrix builds.** Run only the projects affected by a PR
  (`valio generate -p web`) instead of regenerating everything.

## Minimal multi-project example

```json
{
  "projects": {
    "web": {
      "language":   "typescript",
      "schema":     "./openapi.json",
      "outputPath": "./apps/web/src/api"
    },
    "server": {
      "language":   "csharp",
      "schema":     "./openapi.json",
      "outputPath": "./MyOrg.Api.Client/Generated",
      "namespace":  "MyOrg.Api.Client"
    }
  }
}
```

Running `valio generate` against this config generates both projects
sequentially, in the order they appear in the JSON object.

## CLI: project selection

```bash
# All projects (default)
valio generate -c ./valio.config.json

# A single project
valio generate -c ./valio.config.json -p web

# Several projects, repeated flag
valio generate -c ./valio.config.json -p web -p server

# Several projects, comma form
valio generate -c ./valio.config.json -p web,server
```

If you pass a name that isn't in the config, ValIO prints the
available names and exits with code `1`. A failure in one project logs
the error and the run continues with the next, but the CLI still
exits with `1` if any project failed.

## Per-project keys

| Key                | Type    | Default         | Description                                                                       |
|--------------------|---------|-----------------|-----------------------------------------------------------------------------------|
| `language`         | string  | —               | **Required.** Generator id (see below).                                           |
| `schema`           | string  | —               | **Required.** Path or URL to an OpenAPI 3.x document.                             |
| `outputPath`       | string  | —               | **Required.** Folder or `.zip` filename.                                          |
| `generateModels`   | bool    | `true`          | Emit DTOs/enums.                                                                  |
| `generateClients`  | bool    | `true`          | Emit HTTP clients.                                                                |
| `namespace`        | string? | `null`          | Namespace/package; used by C#, Java, Kotlin, PHP, F#, sometimes Swift.            |
| `options`          | object  | `{}`            | Per-plugin options. See [Options reference](/configuration/options-reference/).                |
| `fileOrganization` | object  | plugin defaults | Folder layout. See [File organization](/configuration/file-organization/).                     |
| `namingConvention` | object  | plugin defaults | Identifier casing. See [Naming conventions](/configuration/naming-conventions/).               |

## `language` deep dive

`language` selects which generator plugin runs. The full list of
registered IDs is in [Plugin IDs](/reference/plugin-ids/). At the time
of writing, valid IDs are:

```
typescript   csharp   java     kotlin   python    php
swift        dart     fsharp   ruby     rust
```

Values are case-insensitive on input (`TypeScript`, `typescript`, and
`TYPESCRIPT` all work) and are normalized to lowercase internally.
A C++ generator exists in source but is **not yet registered** in the
plugin bootstrap and cannot be used as a `language` value.

For per-language documentation see [Languages](/languages/).

## `schema` deep dive

ValIO accepts the OpenAPI document as a path or a URL.

```json
// Relative path from the directory containing the config file
{ "schema": "./openapi.json" }

// Absolute path
{ "schema": "C:\\Workspace\\MyProject\\openapi.json" }

// HTTPS URL — fetched at run time
{ "schema": "https://petstore3.swagger.io/api/v3/openapi.json" }

// file:// URL
{ "schema": "file:///srv/specs/openapi.yaml" }
```

YAML schemas (`.yaml`/`.yml`) are accepted alongside JSON; the parser
chooses by extension. Use `valio validate -s <schema>` to check the
document before generating.

## `outputPath` deep dive

`outputPath` accepts either a directory or a `.zip` file path. The
choice is made by extension.

```json
// Directory — created if missing
{ "outputPath": "./src/api" }

// Absolute directory (Windows)
{ "outputPath": "C:\\Workspace\\MyProject\\Generated" }

// .zip file — every emitted file is written into this archive
{ "outputPath": "./dist/MyOrg.Api.Client.zip" }

// Absolute zip path
{ "outputPath": "/var/build/clients/mobile-sdk.zip" }
```

Relative paths resolve against the **directory containing the config
file**, not the current working directory. This means moving the
config doesn't break the project — the relative tree moves with it.

## `namespace`

`namespace` sets the root namespace, package, or module prefix.
Several languages use it; the rest ignore it.

```jsonc
// C# — dotted namespace
{ "language": "csharp", "namespace": "MyOrg.Api.Client" }

// Java — reverse-DNS package
{ "language": "java",   "namespace": "com.example.api" }

// Kotlin — same as Java
{ "language": "kotlin", "namespace": "com.example.api" }

// PHP — backslash-separated namespace
{ "language": "php",    "namespace": "App\\Generated\\Api" }
```

For some plugins the namespace is also exposed as an `options.<plugin>.namespace`
entry, which takes precedence when both are set. See the
[Options reference](/configuration/options-reference/) for plugin-specific behavior.

## `options`

`options` is a free-form object of per-plugin settings. The object
shape varies by language; see the
[Options reference](/configuration/options-reference/) for every key, default, and
valid value per plugin. The pages under
`/languages/<lang>/` describe what each option does to the generated code.

Two top-level layout styles are supported. Both work; pick one and
stay consistent.

**Flat — keys at the top of `options`:**

```json
{
  "options": {
    "transports":   ["axios"],
    "validation":   "zod4",
    "queryWrapper": "tanstack-react"
  }
}
```

This is the form used by the petstore matrix example
(`Examples/configs/petstore-matrix.json`).

**Nested — keys under a per-language sub-object:**

```json
{
  "options": {
    "typescript": {
      "modelFormat":      "ValidationSchemas",
      "validationLibrary": "Zod",
      "httpTransports":    "axios",
      "useTanStackQuery":  true
    }
  }
}
```

This is the form used by `valio.config.typescript.example.json`. Use
nested form when one project mixes plugin-specific options that share
a name across plugins.

Option keys are **camelCase** by convention. Unknown keys are ignored
with a warning so configs forward-compatible across plugin versions.

## `fileOrganization`

Quick summary of the fields — see [File organization](/configuration/file-organization/) for the full rules:

```json
{
  "fileOrganization": {
    "groupingStrategy": "SingleFile",
    "separateModels":   true,
    "separateClients":  true,
    "modelsPath":       "models",
    "clientsPath":      "api"
  }
}
```

| Key                | Type   | Default        | Description                                            |
|--------------------|--------|----------------|--------------------------------------------------------|
| `groupingStrategy` | string | `"SingleFile"` | `SingleFile`, `byRoute`, `byTag`, etc.                 |
| `separateModels`   | bool   | `true`         | Put models in their own folder.                        |
| `separateClients`  | bool   | `true`         | Put clients in their own folder.                       |
| `modelsPath`       | string | `null`         | Folder name under `outputPath` for models.             |
| `clientsPath`      | string | `null`         | Folder name under `outputPath` for clients.            |

## `namingConvention`

Quick summary — see [Naming conventions](/configuration/naming-conventions/) for
the full rules:

```json
{
  "namingConvention": {
    "models":     "PascalCase",
    "properties": "CamelCase",
    "methods":    "CamelCase",
    "enums":      "PascalCase"
  }
}
```

Each key takes a casing style; the supported values include
`PascalCase`, `camelCase`, `snake_case`, `kebab-case`, and `SCREAMING_SNAKE_CASE`.
Defaults are language-appropriate (e.g. C# defaults to PascalCase for
all four; TypeScript uses camelCase for properties/methods).

## Sharing settings across projects

JSON has no anchors, so repetition is unavoidable in a JSON config.
Two practical workarounds:

**1. Switch to YAML and use anchors.** YAML's `&anchor`/`*alias`
syntax lets you define a base project once and reuse it. Example:

```yaml
projects:
  _shared: &shared
    schema: ./openapi.json
    options:
      transports: ["axios"]
      validation: "zod4"
    fileOrganization:
      separateModels: true
      separateClients: true
      modelsPath: models
      clientsPath: api

  web:
    <<: *shared
    language: typescript
    outputPath: ./apps/web/src/api

  admin:
    <<: *shared
    language: typescript
    outputPath: ./apps/admin/src/api
```

ValIO ignores the `_shared` key at run time because it lacks the
required `language`/`schema`/`outputPath` fields — but a cleaner
pattern is to keep the anchor outside `projects` and reference it
under each real project. See [YAML configuration](/configuration/yaml/).

**2. Generate the JSON config from a script.** A tiny Node or shell
script can stamp out repeated blocks and write the resulting JSON,
then `valio generate -c valio.config.json` picks it up.

## See also

- [Configuration file](/configuration/) — top-level overview.
- [Options reference](/configuration/options-reference/) — every plugin option.
- [File organization](/configuration/file-organization/).
- [Naming conventions](/configuration/naming-conventions/).
- [YAML configuration](/configuration/yaml/) — anchors and aliases.
- [Plugin IDs](/reference/plugin-ids/).
- [Examples](/configuration/examples/web-and-server/).
