---
title: TypeScript
description: TypeScript generator options and outputs.
translationKey: lang-typescript
---

The TypeScript plugin (`languageId: "typescript"`) is the largest and most
configurable of the bundled generators.

## What gets generated

- **Models** — `interface` per OpenAPI schema, plus discriminated unions.
- **Enums** — string-literal unions or `enum`s depending on `EnumFormat`.
- **Params / Results** — one type per operation for `params` and `results`.
- **HTTP clients** — one or more of: `fetch`, `axios`, `ky`, `undici`,
  `ofetch`, `got`, `angular` (HttpClient), `solid` (SolidStart).
- **Validation schemas** — Zod (3 or 4), Valibot, or TypeBox.
- **Query wrappers** — TanStack Query (React / Vue / Angular / Solid / Svelte)
  or SWR.
- **Mocks** — Mock Service Worker handlers.
- **Index files** — one re-export tree.

Every generated file is **fully JSDoc-annotated**: a file header
(`@file` / `@module` / `@description` / `@generated`) plus per-declaration tags —
`@type` / `@nullish` / `@minLength` / `@pattern` … on model properties,
`@param` / `@returns` on client functions, `@enum` + allowed values on enums,
`@typedef` on schema output types, and so on.

## Options

```json
{
  "language": "typescript",
  "options": {
    "transports":         ["fetch", "axios"],
    "validationLibrary":  "zod",
    "queryWrapper":       "tanstack",
    "mockLibrary":        "msw",
    "enumFormat":         "string-union",
    "resultTypeSuffix":   "Results"
  }
}
```

`resultTypeSuffix` sets the suffix on the per-operation result-union type
(`{Operation}Results` by default). Use `Result` / `Response` / `Responses` or a
custom string when the default name clashes with a generated model. See the
[options reference](/configuration/options-reference/) for details.

## File layout

By default models go under `models/<name>/<name>.ts` and clients under
`api/<operation>/<operation>.<transport>.ts`. Override via
`fileOrganization.modelsPath` and `fileOrganization.clientsPath`.
