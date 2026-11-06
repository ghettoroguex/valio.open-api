---
title: Configuration file
description: Complete reference for valio.config.json and YAML.
translationKey: configuration
---

ValIO is driven by a single configuration file at the root of your
project. By convention it lives at `./valio.config.json` (or
`./valio.config.yaml`), but you can put it anywhere and point the CLI at
it with `--config`. The format is JSON or YAML — ValIO auto-detects
by file extension, so the same structure works in both. One file
describes any number of generation **projects**: a TypeScript app plus
a C# server plus a Kotlin SDK, all from the same OpenAPI document,
all run by one `valio generate` command.

## Minimal example

The smallest valid config is one project that picks a language, a
schema, and an output directory. Every other key has a default.

```json
{
  "projects": {
    "web": {
      "language":   "typescript",
      "schema":     "./openapi.json",
      "outputPath": "./src/api"
    }
  }
}
```

Running `valio generate` against this file emits TypeScript models and
a `fetch`-based client into `./src/api/`.

## Full example

A more realistic configuration mixes multiple projects, per-plugin
`options`, file-organization rules, and naming conventions. Every
top-level key shown here is described in detail below or on its own
page.

```jsonc
{
  "projects": {
    // 1. A TypeScript client for the browser, validated with Zod 4,
    //    wrapped in TanStack Query for React.
    "web": {
      "language":   "typescript",
      "schema":     "https://petstore3.swagger.io/api/v3/openapi.json",
      "outputPath": "./apps/web/src/api",
      "generateModels":  true,
      "generateClients": true,
      "options": {
        "transports":   ["axios"],
        "validation":   "zod4",
        "queryWrapper": "tanstack-react"
      },
      "fileOrganization": {
        "separateModels":  true,
        "separateClients": true,
        "modelsPath":      "models",
        "clientsPath":     "api"
      },
      "namingConvention": {
        "models":     "PascalCase",
        "properties": "CamelCase",
        "methods":    "CamelCase",
        "enums":      "PascalCase"
      }
    },

    // 2. A C# server-side client packaged into a zip for downstream
    //    consumers. Note the `namespace` field, which is only used by
    //    namespaced languages.
    "server": {
      "language":   "csharp",
      "schema":     "./openapi.json",
      "outputPath": "./dist/MyOrg.Api.Client.zip",
      "namespace":  "MyOrg.Api.Client",
      "options": {
        "csharp": {
          "propertyAccessor":             "Init",
          "useRequired":                  true,
          "modelType":                    "Record",
          "generateValidationAttributes": true
        }
      }
    },

    // 3. A Kotlin mobile SDK that only emits models — no clients.
    "mobile": {
      "language":        "kotlin",
      "schema":          "./openapi.json",
      "outputPath":      "./mobile/src/main/kotlin/generated",
      "namespace":       "com.example.api",
      "generateClients": false
    }
  }
}
```

## Top-level keys

The root object currently has exactly one required key. More may be
added in future releases; unknown keys at the root are ignored with a
warning.

| Key        | Type                                          | Required | Description                                   |
|------------|-----------------------------------------------|----------|-----------------------------------------------|
| `projects` | object — string → [project config](#project-keys) | yes  | Map of project name → project configuration. |

## Project keys

Each value under `projects` is a project configuration. The model is
defined by `ProjectConfig` in `src/ValIO.Configuration/Models/ProjectConfig.cs`.

| Key                | Type    | Default | Description                                                                                                                                              |
|--------------------|---------|---------|----------------------------------------------------------------------------------------------------------------------------------------------------------|
| `language`         | string  | —       | **Required.** Generator id — see [Plugin IDs](/reference/plugin-ids/). Valid: `typescript`, `csharp`, `java`, `kotlin`, `python`, `php`, `swift`, `dart`, `fsharp`, `ruby`, `rust`. Case-insensitive. |
| `schema`           | string  | —       | **Required.** Path or URL to an OpenAPI 3.x document (JSON or YAML).                                                                                     |
| `outputPath`       | string  | —       | **Required.** Directory, or a `.zip` file path. Auto-detected by extension.                                                                              |
| `generateModels`   | bool    | `true`  | Emit DTOs/enums.                                                                                                                                         |
| `generateClients`  | bool    | `true`  | Emit HTTP client classes/functions.                                                                                                                      |
| `namespace`        | string? | `null`  | Namespace/package/module prefix. Used by `csharp`, `java`, `kotlin`, `php`, `fsharp`, sometimes `swift`. Ignored by other languages.                     |
| `options`          | object  | `{}`    | Per-plugin options. See the [Options reference](/configuration/options-reference/) for every key by language.                                                         |
| `fileOrganization` | object  | plugin defaults | Controls how generated files are laid out. See [File organization](/configuration/file-organization/).                                                            |
| `namingConvention` | object  | plugin defaults | Per-identifier-kind casing rules. See [Naming conventions](/configuration/naming-conventions/).                                                                   |

See [Projects](/configuration/projects/) for a deep dive into each key with examples.

## Lookup order

When you run `valio generate` without flags, the CLI searches the
current working directory in this order:

1. `--config <path>` (`-c <path>`) flag — if present, the exact file
   is loaded. No fallback.
2. `./valio.config.json`
3. `./valio.config.yaml`
4. `./valio.config.yml`

If none of those exist and you didn't pass `--schema` / `--output`
overrides, the CLI exits with an error explaining what's missing.

```bash
# Use the default file next to the cwd
valio generate

# Use a specific file (extension determines the parser)
valio generate -c ./configs/staging.yaml

# Run only some projects from a config
valio generate -c ./valio.config.json -p web -p mobile
```

## CLI overrides

When `--config` is in play, you can still override certain fields from
the command line. Overrides apply to every selected project.

| Flag                       | Overrides         |
|----------------------------|-------------------|
| `--schema <path>` / `-s`   | `schema`          |
| `--output <path>` / `-o`   | `outputPath`      |
| `--projects <name>` / `-p` | Which projects run (can repeat or use commas) |

```bash
# Same config, but point every selected project at a staging schema
valio generate -c valio.config.json -s ./openapi.staging.json -p web

# Run just the C# project from a multi-project config
valio generate -c valio.config.json -p server
```

This is handy in CI where the schema may live at a different path than
on developer machines.

## Validation

Before generating anything, ValIO validates the configuration:

- **Schema must be reachable.** Local paths must exist; URLs must be
  fetchable. Use `valio validate` to check without generating.
- **`language` must be a registered plugin.** Unknown IDs fail fast
  with the list of registered IDs.
- **`outputPath` parent must be writable.** Missing parent directories
  are created.
- **`options` is parsed by the plugin.** Unknown option keys produce
  warnings; invalid values (e.g. `transports: "unknownLib"`) abort the
  run with an error message naming the offending project and key.

On validation failure, the CLI prints a structured message — project
name, the offending field, and the reason — then exits with code `1`.

## See also

- [Projects](/configuration/projects/) — full reference for every project key.
- [Options reference](/configuration/options-reference/) — per-plugin options table.
- [File organization](/configuration/file-organization/) — folder layout rules.
- [Naming conventions](/configuration/naming-conventions/) — identifier casing.
- [YAML configuration](/configuration/yaml/) — the same shape in YAML, with anchors.
- [Environment variables](/configuration/env-vars/) — overrides via `VALIO_*` vars.
- [Examples](/configuration/examples/web-and-server/) — runnable configs.
- [Plugin IDs](/reference/plugin-ids/) — exhaustive list of `language` values.
