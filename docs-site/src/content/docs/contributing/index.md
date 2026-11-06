---
title: Contributing
description: Coding rules and plugin authoring guide.
translationKey: contributing
---

ValIO welcomes contributions — new generator plugins, new transports for
existing plugins, bug fixes, and translations.

## Local setup

```bash
git clone https://github.com/valio-projects/valio.open-api
cd valio
dotnet build ValIO.sln
dotnet test  ValIO.sln
```

Requires the .NET 10 SDK (`global.json` pins `10.0.100`).

## Coding rules

The repo enforces a few non-negotiable rules — full text in
`CODING_RULES.md`:

- **No dead code, no `[Obsolete]` corridors.** When a contract changes,
  callers change in the same commit.
- **No catch-all `catch (Exception)`** unless it adds context and
  rethrows. Validators bubble up `ValidationException`.
- **Resx keys instead of hard-coded English** for any UI-bound string.
- **`AddValIO*` extensions are idempotent** — every front-end calls the
  same set in the same order.

## Writing a new generator plugin

A plugin is a class implementing `IGeneratorPlugin`:

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

Then register it in `ValIO.Plugins.Bootstrap.PluginsBootstrapExtensions`
so every front-end picks it up automatically.

## Adding translations

Drop new `*.<culture>.resx` files next to the existing ones under
`src/ValIO.Localization/Resources/`. Missing keys fall back to the
default (English) bundle automatically.

## Pull requests

- Keep one logical change per PR.
- Include the `dotnet test` output in the description for any change
  that touches generators.
- For new transports / validators, add a sample integration test under
  the corresponding `tests/ValIO.Generators.*.Tests/` project.
