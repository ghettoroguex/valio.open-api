---
title: CLI-Übersicht
description: Die Kommandozeile von valio.
translationKey: cli-overview
---

`valio` ist eine einzelne, eigenständige Binary, gebaut aus `ValIO.Cli`.
Dieselbe Binary parst OpenAPI-Schemas, führt Generator-Plugins aus und
schreibt das Ergebnis entweder als Verzeichnisbaum oder als ZIP-Archiv.

## Unterbefehle

- [`valio generate`](/de/cli/generate/) — Code aus einem OpenAPI-Schema generieren
- [`valio validate`](/de/cli/validate/) — ein Schema parsen und Metadaten ausgeben

## Globale Optionen

- `--lang <Code>` — UI-Kultur für Meldungen. Akzeptiert `en`, `ru`, `de`,
  `zh-CN`. Liest zusätzlich die Umgebungsvariable `VALIO_LANG`.

## Exit-Codes

| Code | Bedeutung                                |
|------|------------------------------------------|
| 0    | Erfolg                                   |
| 1    | Parsing-, Validierungs- oder IO-Fehler   |
