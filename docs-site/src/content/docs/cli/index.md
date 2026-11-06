---
title: CLI overview
description: The valio command-line interface.
translationKey: cli-overview
---

`valio` is a single self-contained executable built from `ValIO.Cli`. The
same binary parses OpenAPI schemas, runs generator plugins, and writes the
result either as a folder tree or as a zip archive.

## Subcommands

- [`valio generate`](/cli/generate/) — generate code from an OpenAPI schema
- [`valio validate`](/cli/validate/) — parse a schema and print its metadata

## Global options

- `--lang <code>` — UI culture for messages. Accepts `en`, `ru`, `de`, `zh-CN`.
  Also reads the `VALIO_LANG` environment variable.

## Exit codes

| Code | Meaning                            |
|------|------------------------------------|
| 0    | Success                            |
| 1    | Parsing / validation / IO failure  |
