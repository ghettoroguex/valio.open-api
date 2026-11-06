# C++: первая интеграция

Профиль `std`, C++17/20/23. DTO находятся в `generated/Models` и
`generated/Enums`, JSON-кодеки — отдельно в `generated/Serializers/*.h/.cpp`,
HTTP-клиенты — в `generated/Clients`, интерфейсы — в `generated/Interfaces`,
реализации клиентов — в `generated/Clients/Impl`.

Запускайте генератор из этого каталога: относительные пути конфигурации
разрешаются от текущего рабочего каталога.

```powershell
..\cli\valio.exe generate --config .\valio.config.json --projects CppIntegration
.\build.ps1
```

В репозитории вместо `..\cli\valio.exe` можно использовать
`dotnet run --project ../../src/ValIO.Cli --`.
Для собственной схемы добавьте `--schema C:\path\openapi.json` и
`--output C:\path\generated`. Профиль задаётся через `options.runtime: "std"`;
быстрая команда только с `-l cpp` использует профиль Unreal Engine по умолчанию.

Первая конфигурация генерирует все слои. Проект `CppDefinitions` создаёт
только определения, без JSON/HTTP/runtime-файлов:

```powershell
..\cli\valio.exe generate --config .\valio.config.json --projects CppDefinitions
```

CMake-пример использует cpp-httplib 0.18.3 и nlohmann JSON 3.12.0.
В Windows-комплект включены их настоящие header-only пакеты и лицензии в
`third_party`, поэтому для первой сборки сеть не нужна. В checkout репозитория
без этого каталога CMake скачивает закреплённые версии через FetchContent.
Встроенная HTTP-зависимость собрана без TLS; для HTTPS предоставьте зависимости
с поддержкой OpenSSL в своём проекте.
Проверка только моделей не требует загрузки внешних зависимостей:

```powershell
.\build.ps1 -ModelsOnly
```

В приложении связывайте target `valio_models` для DTO, `valio_serializers`
для JSON либо `valio_clients` для HTTP. `valio_generated` объединяет
включённые слои. JSON-кодеки подключаются явно через `Serializers/OrderJson.h`;
сам DTO-заголовок не вводит внешних зависимостей.

В примере явно выбраны `temporalBackend: "string"` и `uuidBackend: "string"`.
Настройки независимы. Для дат/времени доступны `chrono`, `howard-date`,
`boost-date-time`, `valio`; для UUID — `boost`, `stduuid`, `modern-uuid`.
`chrono` и `modern-uuid` требуют `cxxStandard` 20 или 23. Выбранная внешняя
библиотека становится зависимостью DTO-target; строки и `valio` обходятся без неё.
Строковые UUID и temporal values проверяются на JSON-границе без изменения текста.

`model_consumer` проверяет использование DTO, `codec_consumer` — JSON
round trip с required nullable, optional и optional nullable свойствами.
`http_consumer` запускает локальный сервер, внедряет один общий HTTP-клиент
в два generated клиента и показывает before/after hooks, динамический bearer,
один refresh/replay после 401 и отмену до отправки. Внешний сервер не требуется.
В примере авторизация используется последовательно; приложению с параллельными
запросами нужна собственная синхронизация refresh и состояния токена.

Поддержаны обычные объектные `allOf` с именованными базами. Типизированные
`oneOf/anyOf`, discriminator dispatch и вложенные альтернативы в моделях
пока не реализованы C++-генератором: такие составные модели отклоняются.
У свойств union/intersection/any остаётся диагностируемый текстовый fallback;
не используйте его как сериализатор произвольного JSON.
Unreal Engine output требует вашего UE runtime и в этом комплекте нативно не собирался.
