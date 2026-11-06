---
title: Mitwirken
description: Coding-Regeln und Leitfaden zur Plugin-Entwicklung.
translationKey: contributing
---

ValIO freut sich über Beiträge — neue Generator-Plugins, neue
Transports für bestehende Plugins, Fehlerbehebungen und Übersetzungen.

## Lokales Setup

```bash
git clone https://github.com/valio-projects/valio.open-api
cd valio
dotnet build ValIO.sln
dotnet test  ValIO.sln
```

Benötigt das .NET-10-SDK (`global.json` pinnt auf `10.0.100`).

## Coding-Regeln

Das Repository erzwingt einige nicht verhandelbare Regeln — Volltext
in `CODING_RULES.md`:

- **Kein toter Code, keine `[Obsolete]`-Korridore.** Ändert sich ein
  Vertrag, ändern sich die Aufrufer im selben Commit.
- **Kein Sammel-`catch (Exception)`** — außer wenn Kontext ergänzt und
  weitergeworfen wird. Validatoren lassen `ValidationException`
  hochlaufen.
- **Resx-Schlüssel statt hartcodiertem Englisch** für jede an die UI
  gebundene Zeichenkette.
- **`AddValIO*`-Erweiterungen sind idempotent** — jedes Frontend
  ruft dieselbe Menge in derselben Reihenfolge auf.

## Ein neues Generator-Plugin schreiben

Ein Plugin ist eine Klasse, die `IGeneratorPlugin` implementiert:

```csharp
public sealed class MyLangGeneratorPlugin : IGeneratorPlugin
{
    public string LanguageId => "mylang";
    public string DisplayName => "MyLang";
    public IReadOnlyList<string> LanguageAliases => ["ml"];
    public IGeneratorOptionsDescriptor OptionsDescriptor { get; } = new MyLangOptionsDescriptor();

    public ICodeGenerationPipeline CreatePipeline(IServiceProvider services)
        => new CodeGenerationPipeline(new ICodeGenerationStep[]
        {
            new MyLangModelsStep(),
            new MyLangClientsStep(),
        });

    public void ConfigureServices(IServiceCollection services) { }
}
```

Anschließend registrieren Sie es in
`ValIO.Plugins.Bootstrap.PluginsBootstrapExtensions`, damit jedes
Frontend es automatisch aufnimmt.

## Übersetzungen hinzufügen

Legen Sie neue `*.<culture>.resx`-Dateien neben den bestehenden in
`src/ValIO.Localization/Resources/` ab. Fehlende Schlüssel fallen
automatisch auf das Standardbündel (Englisch) zurück.

## Pull Requests

- Eine logische Änderung pro PR.
- Hängen Sie die `dotnet test`-Ausgabe an die Beschreibung an, wenn
  Generatoren betroffen sind.
- Für neue Transports/Validatoren bitte einen Beispiel-Integrationstest
  unter dem passenden `tests/ValIO.Generators.*.Tests/`-Projekt ergänzen.
