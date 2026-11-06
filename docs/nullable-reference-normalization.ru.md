# Nullable references: общий IR и влияние на генераторы

Исправление находится в текущей ветке `codex/admin-e2e-multipart`; отдельная ветка или
worktree не создавались. Базовый commit — `56703c3e0e9fae60ef9ff4573a0e7f1389226151`;
исправление пока является локальными изменениями поверх него.

## Причина и решение

OpenAPI `oneOf: [{type: null}, {$ref: ...}]` доходил до языковых генераторов как обычный
union. C++ использовал строковый fallback, C# отклонял неподдерживаемую композицию,
Java терял конкретный тип до `Object`. TypeScript сохранял тип, но генерировал проверку
числа совпадений вместо обычного nullable-validator.

В `IrBuilder` такая композиция теперь становится `IrNullableType` только при доказанной
непересекаемости null с остальными альтернативами. `OpenApiNullability` проверяет
исходные схемы, локальные ссылки и композиционные предикаты с обнаружением циклов.
Тип `object` исключает null; одни `properties` без `type` этого не доказывают.
Неизвестные, внешние и циклические ссылки не считаются доказательством.

Для `oneOf` извлекается ровно одна bare-null ветка, и все остальные ветки должны
доказанно отвергать null. Дубликаты остальных веток, их ограничения и вложенные
exclusive-композиции сохраняются. `oneOf` из nullable-типа и null остаётся исходным
union: иначе null, который должен отвергаться из-за двух совпадений, стал бы допустимым.
Несколько null-веток в `oneOf` тоже сохраняются. Null-only inclusive union и единственная
exclusive null-ветка дают именно null, не произвольное nullable-значение.

Проверка применена на общем этапе построения IR для свойств, элементов коллекций,
словарей, request bodies и responses. Здесь нет имён сущностей GTA MP, special cases
для отдельного языка, изменения OpenAPI или ручной правки сгенерированных файлов.
Произвольные polymorphic unions этим исправлением не превращаются в typed variants.

## Результат по языкам

| Генератор | Nullable reference | Существенная граница |
| --- | --- | --- |
| C++ std | `std::optional<Model>` / `std::optional<Enum>` | Необязательное nullable-свойство сохраняет `optional<optional<T>>`: missing, null и value различаются |
| C# | `Model?` / `Enum?` | Используется существующий System.Text.Json codec; отдельный presence-wrapper не добавлялся |
| Java | Конкретная model/enum reference вместо `Object` | Records остаются без Jackson-зависимости; приложение по существующему контракту предоставляет enum wire codecs в `ObjectMapper` |
| TypeScript | `Model \| null`, `Enum \| null` | Optional остаётся отдельным `?`; validators проверяют required и nullable независимо |
| JavaScript runtime | Скомпилированные TS validators | Реальные Zod 3/4, Valibot и TypeBox принимают null/объект и отвергают malformed значения |
| Dart, F#, Kotlin, PHP, Python, Ruby, Rust, Swift | Существующий mapping `IrNullableType` | Общий IR исправлен без языковых обходов; ниже приведены regression suite результаты |

Java runtime regression явно использует предусмотренные Java API caller-owned enum
codecs. Это не доказательство автоматической генерации Jackson enum codecs: её здесь нет.
Аналогично nullable mapping не означает, что каждый язык автоматически различает
missing и null или валидирует все ограничения JSON Schema.

## Проверки

Общая fixture: `tests/Fixtures/OpenApi/CoreIR/nullable-reference-unions-3.1.json`.
Она проходит настоящий OpenAPI reader, IR builder и генераторы. В native C++ fixture
проверяются реальные model/enum declarations, контейнеры, missing/null/value, roundtrip,
неправильный JSON-тип и неизвестный enum. C# и Java компилируют неизменённые
generated sources и выполняют JSON roundtrip. TS выполняет strict compilation и JS
runtime для четырёх validation backends. Existing exclusive-union tests тоже прошли.

| Suite | PASS | SKIPPED |
| --- | ---: | ---: |
| Core | 161 | 0 |
| Application | 80 | 0 |
| C++ unit | 262 | 0 |
| C++ native C++17/20/23, sync/async/interface | 4 | 0 |
| C# с generated compilation/runtime | 128 | 0 |
| Java с native compilation/Jackson | 136 | 0 |
| TypeScript с strict compilation/JS runtime | 858 | 0 |
| Dart | 68 | 0 |
| F# | 59 | 0 |
| Kotlin | 109 | 0 |
| PHP | 89 | 0 |
| Python | 124 | 6 |
| Ruby | 59 | 0 |
| Rust | 62 | 0 |
| Swift | 90 | 0 |
| **Всего** | **2289** | **6** |

Для восьми остальных языков выполнены regression suites без категорий
`NativeIntegration`, `CompileFixture`, `NodeIntegration`; native runtime всех этих
языков отдельно не проверялся. Шесть Python opt-in cases не запускались из-за
отсутствующих настроек их окружения. Existing analyzer/NuGet warnings не подавлялись.

Команды основных проверок:

```powershell
dotnet test tests/ValIO.Core.Tests --no-restore
dotnet test tests/ValIO.Application.Tests --no-restore
dotnet test tests/ValIO.Generators.Cpp.Tests --no-restore
$env:VALIO_CPP_CMAKE_PREFIX_PATH = 'C:/Work/valio/valio.open-api/artifacts/audit/cpp-dependencies/conan-debug'
$env:CMAKE_GENERATOR = 'Visual Studio 18 2026'
$env:CMAKE_GENERATOR_PLATFORM = 'x64'
dotnet test tests/ValIO.Generators.Cpp.CompileTests --filter FullyQualifiedName~GeneratedClient_CompilesAndRoundTrips
dotnet test tests/ValIO.Generators.CSharp.Tests --no-restore
$env:VALIO_VALIDATOR_NODE_MODULES = 'C:/Work/valio/valio.open-api/artifacts/audit/node/node_modules'
dotnet test tests/ValIO.Generators.TypeScript.Tests --no-restore
$env:VALIO_JAVAC = (Get-Command javac).Source
$env:VALIO_JAVA_CLASSPATH = (Get-ChildItem artifacts/audit/java-fsharp-native -Filter '*.jar' | ForEach-Object FullName) -join [IO.Path]::PathSeparator
dotnet test tests/ValIO.Generators.Java.Tests --no-restore
```

Логи находятся в ignored `artifacts/*nullable*.log`, `artifacts/core-full.log`.
Native C++ использует exact cpp-httplib 0.18.3 и nlohmann_json 3.12.0.

## Локальный CLI и потребители GTA MP

CLI собран командой:

```powershell
dotnet publish src/ValIO.Cli/ValIO.Cli.csproj -c Release -r win-x64 --self-contained false -o artifacts/releases/valio-nullable-win-x64-20261011/cli
```

Полный путь: `C:\Work\valio\valio.open-api\artifacts\releases\valio-nullable-win-x64-20261011\cli\valio.exe`.
Требуется .NET 10 runtime. Это локальная source-built сборка, не опубликованный immutable
release. `BUILD-INFO.json` рядом с `cli/` фиксирует base commit, dirty source state,
хеши двух production source files и сборок. Старый `valio-cpp-win-x64-20261011-r2`
не изменялся. Apphost executable может иметь тот же hash: исправление доказывают
hash фактического `ValIO.Core.dll` и regression tests, а не только hash `valio.exe`.

В worktree GTA MP regenerated напрямую Game SDK (260 файлов), Server SDK (133 файла)
и Public TS SDK (725 файлов). Исправлены Country, Notification.Server, State,
Vote/Review и EndReason. Backend OpenAPI не менялся. Windows x86 plugin DLL собран;
его codec/cancellation и typed nullable tests — 2/2 PASS. Public frontend typecheck
и production build — PASS. Linux, загрузка plugin в open.mp и сетевой E2E не проверены.
Игровые configure/build/tests/runtime по указанию пользователя не выполнялись.
