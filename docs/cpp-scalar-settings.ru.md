# C++ std: scalar-типы, UUID и даты

Документ описывает текущую рабочую волну после опубликованного e289987 и отделяет
реализованные настройки и точные принятые provider-native cases. Первый C++ архив
valio-cpp-win-x64-20261011 неизменяем; новые scalar backends в его приёмку не входят.

## Текущий default и разделение зависимостей

По умолчанию в std-профиле string, uuid, date, time и date-time отображаются в std::string.
Binary/byte/file отображаются в byte collections. Изменение representation не должно
подменять wire format: формат сохраняется и проверяется codec на границе JSON.

Models/enum headers предназначены для использования без JSON и HTTP. JSON adapters
размещаются отдельно; DTO не требует пустого .cpp. Выбор внешнего typed provider
может добавить зависимость DTO от этого provider, но не от nlohmann_json/httplib.
Текущие CMake границы: valio_models, valio_serializers и valio_clients.

Минимальная существующая конфигурация definition-only генерации:

```json
{
  "projects": {
    "CppDefinitions": {
      "language": "cpp",
      "schema": "./openapi.json",
      "outputPath": "./definitions",
      "namespace": "Example",
      "generateModels": true,
      "generateClients": false,
      "options": {
        "runtime": "std",
        "cxxStandard": 17,
        "emitSerializers": false,
        "emitCmake": true
      }
    }
  }
}
```

Для JSON round-trip emitSerializers включается. Проверяемый string representation
сохраняет исходный текст даты/времени, включая offset и fractional digits; typed
representation требует отдельно принятой политики точности и диапазона.

## Выбор representation

CppGenerationOptions, CppOptionsDescriptor, mapper и emitted CMake подключены
к двум независимым настройкам:

| Option key | Default | Допустимые значения |
| --- | --- | --- |
| uuidBackend | string | string, boost, stduuid, modern-uuid |
| temporalBackend | string | string, chrono, howard-date, boost-date-time, valio |

Неизвестные значения и неверные JSON-типы дают parse diagnostics. Non-string
backends требуют runtime=std. chrono и modern-uuid требуют cxxStandard 20 или 23;
конфигурация с C++17 отклоняется, а не молча повышает стандарт.

Например, options существующего проекта для typed C++20 representation:

```json
{
  "runtime": "std",
  "cxxStandard": 20,
  "uuidBackend": "modern-uuid",
  "temporalBackend": "chrono",
  "emitSerializers": true,
  "emitCmake": true
}
```

Для C++17 доступны, например, uuidBackend=stduuid и temporalBackend=valio.
Этот выбор входит в прошедшую native provider matrix ниже.

## UUID backend contract

| uuidBackend | Application type | Целевой стандарт | DTO dependency |
| --- | --- | --- | --- |
| string | std::string | C++17 | STL |
| boost | ValIO::Uuid = boost::uuids::uuid | C++17 | Boost.Uuid |
| stduuid | ValIO::Uuid = uuids::uuid | C++17 | stduuid, uuid.h |
| modern-uuid | ValIO::Uuid = muuid::uuid | C++20 или новее | modern-uuid, modern-uuid/uuid.h |

Имя пространства modern-uuid — **muuid**, не modern_uuid. Template разделяет
Runtime/ValIOUuidTypes.h (application alias) и Runtime/ValIOUuidJson.h (JSON adapter).
Внешний parser вызывается после проверки canonical 8-4-4-4-12 строки; nil/max UUID
не запрещаются искусственным ограничением version/variant. Braces, compact form,
whitespace, embedded null и trailing text должны отклоняться.

Native compile/round-trip принят для семи конкретных сочетаний ниже. Результат
не распространяется автоматически на полный декартов продукт всех providers/standards.
Generated CMake ищет Boost, stduuid CONFIG или modern-uuid CONFIG только при выборе
соответствующего provider и добавляет его public dependency к valio_models.
Поддерживаются imported targets Boost::uuid/Boost::headers/Boost::boost/boost::boost,
stduuid::stduuid/stduuid и modern-uuid::modern-uuid/modern-uuid-static/modern-uuid-shared.
Для modern-uuid альтернативные полные target names имеют один и тот же префикс
modern-uuid::; HTTP/JSON targets отдельно не меняются.

## Temporal backend contract

Следующие варианты подключены к mapper и runtime generation. Native acceptance
подтверждён для семи точных сочетаний в таблице ниже; здесь приведён контракт выбора:

| temporalBackend | Целевой стандарт | Provider values |
| --- | --- | --- |
| string | C++17 | std::string, сохраняет wire text; существующий default. |
| chrono | C++20 или новее | std::chrono::year_month_day, hh_mm_ss<nanoseconds>, nanoseconds. |
| howard-date | C++17 | date::year_month_day, date::hh_mm_ss<nanoseconds>, chrono nanoseconds; date::date CMake target. |
| boost-date-time | C++17 | boost::gregorian::date, boost::posix_time::time_duration; Boost headers CMake target. |
| valio | C++17 | Собственные civil date/time/calendar-interval values; fractional text сохраняется отдельно. |

Typed DTO использует ValIO::Date, ValIO::Time, ValIO::DateTime и ValIO::Duration.
Date — provider alias; Time/DateTime сохраняют civil components, UTC offset,
unknown -00:00, Z/z spelling и fractional width. DateTime не сводится к одному
nanosecond epoch count. IR string formats duration/time-span/timespan отображаются
в ValIO::Duration. Runtime/ValIOTemporalTypes.h отделён от ValIOTemporalJson.h.

Значения вне диапазона/точности выбранного типа отклоняются: fractional digits
не обрезаются молча. Chrono/Howard/Boost duration отклоняют ненулевые calendar
years/months вместо усреднения в секунды; ValIO сохраняет calendar components.
Leap-second, unknown offset и trailing text покрываются отдельно для каждого backend.
Native acceptance подтверждается конкретными семью provider cases ниже.

## Строгая JSON boundary и текущая приёмка

JsonValidationTemplates уже добавляет проверки типов/range перед scalar conversion,
finite floating-point values, calendar/date-time/UUID strings, canonical padded
base64 и рекурсивных optional/vector/dictionary values. Enum codecs должны отклонять
неизвестные значения вместо silent fallback. Wire binary и base64 сохраняют разные
encoding rules; представление bytes само по себе их не объединяет.

Последний принятый C++ unit gate: **262 passed / 0 failed / 0 skipped** в общем
[cpp-r2-general-final.log](../artifacts/audit/cpp-r2-general-final.log).
Targeted повтор также дал 262/262:
[cpp-r2-unit-final.log](../artifacts/audit/cpp-r2-unit-final.log).
Предыдущий scalar-only checkpoint 237/237 сохранён в cpp-scalar-unit-final.log.
Финальный основной native gate: 4 passed / 0 failed / 0 skipped,
[cpp-r2-main-native-final.log](../artifacts/audit/cpp-r2-main-native-final.log).
Он включает исправленный Windows cancellation с polling 10 ms и optional-body
regressions. Ранний checkpoint выявлял C4702 и active cancellation latency;
эти результаты исправлены. Финальный native повтор после typed Request изменений
принят: main 4/4 и provider 7/7, оба процесса завершились с EXIT0.
Отдельный финальный scalar provider gate: **7 passed / 0 failed / 0 skipped**,
[cpp-r2-provider-native-final.log](../artifacts/audit/cpp-r2-provider-native-final.log).
Ранний cpp-scalar-provider-native.log содержит промежуточную compile error test
fixture и не заменяет успешный final log.

| C++ standard | temporalBackend | uuidBackend | Native результат |
| --- | --- | --- | --- |
| 17 | string | string | Passed |
| 17 | string | boost | Passed |
| 17 | howard-date | stduuid | Passed |
| 17 | boost-date-time | boost | Passed |
| 17 | valio | stduuid | Passed |
| 20 | chrono | modern-uuid | Passed |
| 23 | chrono | string | Passed |

Матрица использует настоящие provider headers/API и проверяет generated native
compile/round-trip. Все заявленные providers встречаются хотя бы в одном принятом
сочетании; прочие сочетания требуют собственного gate при необходимости.

Текущие HTTP и SSL branches успешно скомпилированы с actual OpenSSL 3.6.1,
CPPHTTPLIB_OPENSSL_SUPPORT для SSL, C++17, MSVC 19.51 и /W4 /WX:
[cpp-r2-tls-syntax-final.log](../artifacts/audit/cpp-r2-tls-syntax-final.log).
Это object compilation only: executable/DLL linking, HTTPS handshake,
certificate handling и TLS cancellation runtime не проверены.
Промежуточный TLS gate после HTTP изменений выявил SSLClient final; исправление
вошло в новый compile gate. Решение использует composition с genuine SSLClient
и public stop/progress APIs, без наследования от final type. Active TLS read
cancellation может ждать upstream SSL/OS read timeout; DNS/connect рассматриваются
отдельно. Immediate TLS cancellation не гарантируется.

Preview self-contained Windows CLI повторно опубликован и sample регенерирован
после query-fix. Generated CMake sample прошёл 3/3 CTest: model_consumer, codec_consumer, http_consumer,
[cpp-r2-cmake-preview.log](../artifacts/audit/cpp-r2-cmake-preview.log).
Отдельный models-only build/CTest прошёл 1/1,
[cpp-r2-models-preview.log](../artifacts/audit/cpp-r2-models-preview.log).
Sample подтвердил link всех 12 ordinary AddQuery/AddHeader/AddFormField definitions;
newline при склейке source исправлен, первая definition больше не скрывается комментарием.
Общий прогон 14 test projects дал 2134 passed / 0 failed / 0 skipped,
[cpp-r2-general-final.log](../artifacts/audit/cpp-r2-general-final.log);
native/Node категории в него не включены и принимаются отдельно.

Правка enum initialization вошла в новый unit gate:
required enum без schema default должен value-initialize, а допустимый schema
default — отображаться в типизированный enum member. Native acceptance после
enum/typed Request правок принят: main 4/4 и providers 7/7.

Typed Request API переносит обычные method bodies в Runtime/ValIOHttp.cpp,
а необходимые C++17 templates — в подключаемый Runtime/Impl/ValIORequest.inl.
AddQuery/AddHeader/AddFormField форматируют bool/integer/finite float/string,
optional и flat vector без JSON types в public HTTP contract. False/zero/empty
string остаются present; absent optional пропускается, не представимые wire null
отклоняются. Query collections учитывают сохранённые Style/Explode; header arrays
используют CSV. Nested arrays и неподдержанные object/style codecs отклоняются.
Исправлен повторный URL encoding structural comma в cpp-httplib: runtime применяет
pinned path codec отдельно от query и отключает transport URL encoding через
set_url_encode(false). Literal separators внутри query element кодируются отдельно
от structural collection separators.
Точные поддержанные encodings и Swagger 2 reader gap описаны в
[builder-context-plan.ru.md](builder-context-plan.ru.md#текущая-pre-r2-доработка-typed-request-и-wire-metadata).

Точный r2 source SHA, clean-source provenance и состав готового пакета фиксируются
в его BUILD-INFO.json. Preview gates не заменяют portable gate готового пакета.
Результаты первого
архива не изменены; актуальная поставка и границы доказательств фиксируются в
[аудите](open-api-audit-2026-10-11.ru.md).
