---
title: Участие в разработке
description: Правила кода и руководство по написанию плагинов.
translationKey: contributing
---

ValIO приветствует участие сообщества — новые плагины-генераторы,
новые транспорты для существующих плагинов, исправления ошибок и
переводы.

## Локальная установка

```bash
git clone https://github.com/valio-projects/valio.open-api
cd valio
dotnet build ValIO.sln
dotnet test  ValIO.sln
```

Требуется .NET 10 SDK (`global.json` фиксирует версию `10.0.100`).

## Правила кода

В репозитории действует несколько строгих правил — полный текст в
файле `CODING_RULES.md`:

- **Никакого мёртвого кода и коридоров `[Obsolete]`.** При изменении
  контракта вызывающий код меняется в том же коммите.
- **Никаких всеохватывающих `catch (Exception)`** — кроме случаев,
  когда добавляется контекст и исключение пробрасывается снова.
  Валидаторы поднимают `ValidationException`.
- **Resx-ключи вместо захардкоженного английского** для любой
  привязанной к UI строки.
- **Расширения `AddValIO*` идемпотентны** — каждый фронтенд вызывает
  один и тот же набор в том же порядке.

## Как написать новый плагин-генератор

Плагин — это класс, реализующий `IGeneratorPlugin`:

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

Затем зарегистрируйте его в
`ValIO.Plugins.Bootstrap.PluginsBootstrapExtensions`, чтобы каждый
фронтенд автоматически его подхватил.

## Добавление переводов

Положите новые `*.<culture>.resx`-файлы рядом с существующими в
`src/ValIO.Localization/Resources/`. Отсутствующие ключи автоматически
заменяются значениями из дефолтного (английского) пакета.

## Pull request'ы

- Один логический change на PR.
- Прикладывайте вывод `dotnet test` к описанию для любых изменений,
  затрагивающих генераторы.
- Для новых транспортов / валидаторов добавляйте интеграционный тест
  в соответствующий проект `tests/ValIO.Generators.*.Tests/`.
