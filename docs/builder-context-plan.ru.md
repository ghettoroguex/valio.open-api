# Общий контекст IR для генераторов

Статус: первоначальная инвентаризация и план сохранены ниже; после первой C++ поставки реализована отдельная context/runtime волна. Опубликованный historical baseline — e28998750cb31a0a2dbccc80307e8eecd3856562. Приняты общий gate 2134/2134, C++ main native 4/4, scalar providers 7/7, HTTP/TLS object compilation и regenerated consumer examples. Точный r2 source SHA и clean-source provenance фиксируются в BUILD-INFO.json поставки. Ранее собранный C++ архив не меняется.

## Текущее выполнение после первой поставки

| Область | Реализовано или поручено | Статус приёмки |
| --- | --- | --- |
| Core | Добавлены `IrDocumentContext`, frozen ordinal indexes моделей/enum, `KnownTypeNames`, порядок моделей; `GenerationContext.DocumentContext` публикует один ленивый контекст на запрос. Добавлен тонкий `AstBuilderBase`; IR сохраняет parameter Style/Explode. | Последний Core gate: 149 passed / 0 failed / 0 skipped в cpp-r2-general-final.log; 12 новых serialization cases дополняют прежние 137 context/formats/body tests. |
| Девять вторичных генераторов | Model steps переведены на общий контекст; Kotlin использует общий known-types set, Python сохраняет языковые module maps. | Новые suites вошли в общий gate 2134/2134; полная native matrix всех девяти этим не подтверждается. |
| C# | Model/client paths используют context; injection HTTP-клиента и четыре режима tracing проверены владельцем. | 127 passed / 0 failed / 0 skipped в csharp-http-context-fixed.log. |
| TypeScript | Context/HTTP/injection и RFC-format проверены расширенным запуском. | Последний checkpoint: 853 passed / 1 failed / 0 skipped; Ky import snapshot исправлен без production-изменения, финальный повтор ожидается. |
| C++ context, API и runtime | Doxygen, routes, новая структура файлов, HTTP/cancellation, строгие JSON codecs, enum initialization и typed Request методы добавлены владельцами. | Общий/targeted unit 262/262, финальные main native 4/4 и scalar providers 7/7, regenerated preview CMake 3/3 + models-only 1/1; HTTP/TLS object compile с OpenSSL 3.6.1 и MSVC /W4 /WX. HTTPS runtime не доказан. |
| Конкурентная генерация | Добавлены реальные два JSON контракта с одинаковыми именами типов; тесты C#/C++ сравнивают sequential output с 16 параллельными запросами и проверяют fresh renderer/pipeline instances. | Application 80 passed / 0 failed / 0 skipped в cpp-delivery-general-green.log; bounded тест покрывает C#/C++ model-only пути. |

Текущий общий gate 14 test projects: **2134 passed / 0 failed / 0 skipped**,
[cpp-r2-general-final.log](../artifacts/audit/cpp-r2-general-final.log).
Native/Node категории исключены из этого запуска и принимаются отдельно.
C++ r2 CLI preview повторно сгенерирован после query-fix: model/codec/http examples 3/3 в
[cpp-r2-cmake-preview.log](../artifacts/audit/cpp-r2-cmake-preview.log) и models-only
1/1 в [cpp-r2-models-preview.log](../artifacts/audit/cpp-r2-models-preview.log).

Последнее опубликованное состояние отдельно проверено: общий precommit запуск дал
1941 passed / 0 failed / 1 environment skip; дополнительный запуск всего TypeScript
suite из точного committed snapshot дал 814 passed / 0 failed / 0 skipped, включая
50 strict compile случаев и Node/runtime checks. Эти результаты приведены в
[отчёте аудита](open-api-audit-2026-10-11.ru.md); они предшествуют context-миграции.

Пользователь расширил следующую волну: Doxygen, внедрение общего HTTP-клиента,
статические route constants, обработка ответов 204/205/HEAD и структура
`Interfaces/IStore.h`, `Clients/StoreClient.h`, `Clients/Impl/StoreClient.cpp`;
pre/post hooks, динамическая авторизация и ограниченный повтор после 401;
cancellation для C++17/C#/TypeScript и остальных доступных native targets;
строгий C++ parsing scalar/date/enum/base64/collections. Эти требования являются
объёмом работы, а не заявлением о полной пройденной приёмке. Python context/native
с реальными Pydantic/httpx: 130/130 passed; Java: 133/133 passed. F# принят составным
доказательством: 79 прежних passed плюс исправленный client native 1/1; отдельного
полного повторного 80-test запуска не заявляется. Kotlin context checkpoint был
105/105; последний общий gate принял Kotlin 109/109.
Последующая Java path-segment regression a/b отдельно принята real native gate 1/1,
[java-client-segment-native-final.log](../artifacts/audit/java-client-segment-native-final.log).
Новый Kotlin native gate пока ожидается.
UUID/temporal backends приняты отдельной native matrix семи конкретных сочетаний;
точные cases, логи, настройки и ограничения описаны в
[cpp-scalar-settings.ru.md](cpp-scalar-settings.ru.md). Точный r2 source SHA и clean-source
provenance см. BUILD-INFO.json готовой поставки; первый архив не меняется.

## Текущая pre-r2 доработка: typed Request и wire metadata

Обычные Request methods и scalar overloads объявляются в Runtime/ValIOHttp.h,
а их определения находятся в ValIOHttp.cpp. C++17 template overloads вынесены
в Runtime/Impl/ValIORequest.inl и подключаются из public header: definition
должна быть видна при instantiation. Сам runtime contract не включает JSON types
или cpp-httplib types. Scalar provider/model codecs остаются отдельной границей.

AddQuery/AddHeader/AddFormField принимают string/string_view/C string, bool,
integer/floating values, optional и flat vector. False, zero и empty string
сохраняют presence; отсутствующий optional не создаёт wire field. Nonfinite float,
nonnull pointer violation, present nested null optional и null collection element
отклоняются. Generated client передаёт typed value непосредственно общему runtime,
без собственного форматирования bool/numbers и ветвления для optional. Required
nullable parameter дополнительно проверяется RequireParameterValue. Provider/model
leaf formatter при необходимости передаётся отдельно; это не JSON-зависимость DTO.

Core добавил nullable IrParameter.Style/Explode и IrParameterStyle. Production
builder сохраняет normalized reader flags; legacy вручную созданный IR может
не иметь metadata. 12 новых cases проверяют disk fixtures OpenAPI 3.0/3.1/3.2,
доступные Swagger 2 collection formats, сохранение effective reader flags,
диагностику неизвестного style и совместимость старого manual IR.

| Query/header contract | C++ std encoding |
| --- | --- |
| Query form, explode=true | Повторение wire key для каждого элемента. |
| Query form, explode=false | CSV. |
| Query spaceDelimited/pipeDelimited, explode=false | Space/pipe separators между отдельно percent-encoded элементами. |
| Header simple array | CSV. |
| Nested arrays, unsupported styles, OpenAPI object wire codecs | Явная NotSupportedException; полная parameter serialization не заявляется. |

QueryParameter хранит исходные элементы и CollectionEncoding раздельно, чтобы
literal comma/space/pipe внутри элемента не путался с collection delimiter.
Сериализация form fields по умолчанию CSV; пустая repeated collection опускается,
пустая separated collection сохраняет пустое wire value.

Native проверка обнаружила дополнительную ошибку: cpp-httplib повторно URL-encoded
structural comma, превращая collection delimiter в данные. Runtime теперь отдельно
применяет pinned path codec, затем добавляет query с отдельно encoded elements и
отключает автоматический URL encoding транспорта через set_url_encode(false).
Также исправлен отсутствовавший newline после namespace comment при склейке
generated .cpp: строковый комментарий скрывал первую overload definition.
Source теперь содержит все 12 ordinary definitions: три метода × string,
string_view, C string и bool. Regenerated CMake sample подтвердил native link.

Выявлен отдельный source-reader gap: Microsoft.OpenApi 3.5.4 нормализует Swagger 2
query collectionFormat=csv, multi и отсутствие collectionFormat одинаково.
IR сохраняет доступное normalized значение, но не восстанавливает потерянный
source distinction. Parser slice поставлен в очередь: сохранить raw metadata
в production JSON/YAML reader boundary для inline и referenced parameters,
не вводя regex-парсер в IrBuilder; затем принять Swagger 2 CSV/multi/default
через реальные parser → IR → generated HTTP tests. Текущий gate это не исправляет.

Unit/Core изменения приняты общим 2134-test прогоном; финальный main native повтор
дал 4/4, [cpp-r2-main-native-final.log](../artifacts/audit/cpp-r2-main-native-final.log).
Provider-native final повтор дал 7/7, EXIT0:
[cpp-r2-provider-native-final.log](../artifacts/audit/cpp-r2-provider-native-final.log).
TLS branch повтор выявил наследование от final
SSLClient; исправление использует composition с genuine SSLClient и public
stop/progress APIs. HTTP cancellation stream adapter остаётся отдельным механизмом.
HTTP/TLS object gate принят с actual OpenSSL 3.6.1 и MSVC C++17 /W4 /WX:
[cpp-r2-tls-syntax-final.log](../artifacts/audit/cpp-r2-tls-syntax-final.log).
Active TLS read cancellation может ждать upstream SSL/OS read timeout; DNS/connect
имеют отдельные ограничения. Immediate TLS cancellation не гарантируется, HTTPS
runtime не доказан. Exact-commit portable package gate принимается отдельно.
Named request containers остаются только планом ниже.

## Проверка владения renderer во всех 12 плагинах

Статический аудит проведён перед финальным gate. Общий orchestrator создаёт новый
pipeline для каждого запроса. Изменяемые C#/C++ renderer не разделяются между
pipeline; singleton renderer остальных языков хранит только constants и локальные
буферы. В TypeScript singleton node handlers stateless, а mutable AST renderer
создаётся отдельно каждым шагом. Новая renderer registration правка не требуется.

| Язык | Construction/lifetime и source evidence | Изменяемое состояние |
| --- | --- | --- |
| C# | CSharpGeneratorPlugin.cs:29,42: resolve transient на каждый CreatePipeline. | CSharpAstRenderer.cs:23–24: _sb/_indentLevel принадлежат pipeline. |
| C++ | CppGeneratorPlugin.cs:27,48: resolve transient на каждый CreatePipeline. | CppAstRenderer.cs:22: _indentLevel принадлежит pipeline. |
| TypeScript | TypeScriptGeneratorPlugin.cs:28–62 создаёт свежие steps; TypeScriptModelsStep.cs:29 и EnumsStep.cs:28 создают renderer. Пять node handlers singleton на :72–76. | TypeScriptAstRenderer.cs:28–30: _sb/_indentLevel локальны шагу; node handlers не имеют mutable fields. ZodSchemaRenderer создаётся внутри ZodSchemasStep.ExecuteAsync. |
| Java | JavaGeneratorPlugin.cs:23: new JavaAstRenderer на pipeline. | JavaAstRenderer.cs:18: const Indent; StringBuilder локальны методам. |
| PHP | PhpGeneratorPlugin.cs:23: new PhpAstRenderer на pipeline. | PhpAstRenderer.cs:18: const Indent; StringBuilder локальны методам. |
| Python | PythonGeneratorPlugin.cs:24,38: singleton renderer. | PythonAstRenderer.cs:18: const IndentSize; StringBuilder локальны методам. |
| Kotlin | KotlinGeneratorPlugin.cs:24,38: singleton renderer. | KotlinAstRenderer.cs:18: const IndentSize; StringBuilder локальны методам. |
| Swift | SwiftGeneratorPlugin.cs:24,38: singleton renderer. | SwiftAstRenderer.cs:18: const IndentSize; StringBuilder локальны методам. |
| Dart | DartGeneratorPlugin.cs:23,37: singleton renderer. | DartAstRenderer.cs:17: const Indent; StringBuilder локальны методам. |
| F# | FSharpGeneratorPlugin.cs:25,39: singleton renderer. | FSharpAstRenderer.cs:18: const IndentSize; StringBuilder локальны методам. |
| Ruby | RubyGeneratorPlugin.cs:24,38: singleton renderer. | RubyAstRenderer.cs:18: const IndentSize; StringBuilder локальны методам. |
| Rust | RustGeneratorPlugin.cs:24,38: singleton renderer. | RustAstRenderer.cs:18: const IndentSize; StringBuilder локальны методам. |

Singleton type mappers этих вторичных языков не хранят per-document indexes; часть
из них содержит readonly ссылку на default options. Mutable request options нельзя
изменять одновременно с генерацией — это отдельный контракт владения caller.
В рамках этого аудита нового concrete shared-state race не найдено. Это статическое
доказательство construction/ownership, а не исполненная concurrency matrix всех
12 языков. Существующий bounded test охватывает C#/C++ model-only пути; остальные
не объявляются runtime-проверенными этим тестом.

## Историческая инвентаризация и принятый план

Следующие разделы фиксируют состояние до context-миграции и её принятые границы.
Слова «сейчас», «предлагаемый» и последовательность шагов в этой части относятся
к моменту первоначальной инвентаризации; актуальное выполнение приведено выше.

## Обнаруженные повторения

Сейчас [GenerationContext](../src/ValIO.Core/Plugins/GenerationContext.cs) содержит IR-документ и изменяемые результаты прогона, но не содержит общих индексов документа. Шаги заново строят словари моделей и наборы enum. Это повторение подготовки данных, а не дополнительные узлы IR: словари ссылаются на те же модели.

| Генератор | Повторная подготовка | Файлы миграции |
| --- | --- | --- |
| C++ | `PrepareInputs` каждый раз создаёт `EnumNames` и словарь моделей. std-шаги дополнительно создают enum-набор для builder и ещё один для mapper. UE-шаги повторяют подготовку. | `Cpp/Ast/Builders/CppAstBuilder.cs`, `Cpp/Steps/CppStdStepBase.cs`, `CppStdModelsStep.cs`, `CppStdSerializersStep.cs`, `CppStdClientStep.cs`, `CppUeStepBase.cs`, `CppUeModelsStep.cs`, `CppUeEndpointsStep.cs` |
| C# | `CSharpAstBuilder` имеет три публичных изменяемых свойства `ModelsByName`, `EnumNames`, `EnumsByName`. `Build(document)` заполняет их, затем отдельно создаёт ещё один словарь моделей. `CSharpModelsStep` повторяет настройку этих свойств. | `CSharp/Ast/Builders/CSharpAstBuilder.cs`, `CSharp/Steps/CSharpModelsStep.cs`, `CSharpClientStep.cs` |
| TypeScript | `TypeScriptModelsStep`, `TypeScriptClientStepBase`, Angular и три validator-шагa создают словари моделей. Transport builders получают их через setters. `Build(document)` также создаёт локальный словарь. | `TypeScript/Ast/Builders/TypeScriptAstBuilder.cs`, `TypeScript/Steps/TypeScriptModelsStep.cs`, `TypeScriptClientStepBase.cs`, `AngularRxjsClientStep.cs`, `ZodSchemasStep.cs`, `ValibotSchemasStep.cs`, `TypeBoxSchemasStep.cs` и transport builders |
| Dart | Model-шаг создаёт словарь перед `IrInheritancePlan.FlattenProperties`. | `Dart/Steps/DartModelsStep.cs` |
| F# | То же. | `FSharp/Steps/FSharpModelsStep.cs` |
| Java | То же; собственная обработка порядка моделей. | `Java/Steps/JavaModelsStep.cs` |
| Kotlin | Model-шаг создаёт словарь и набор известных имён. OkHttp-шаг отдельно создаёт такой же набор имён моделей и enum. | `Kotlin/Steps/KotlinModelsStep.cs`, `OkHttpClientStep.cs` |
| PHP | Model-шаг создаёт словарь перед flattening; собственная обработка порядка моделей. | `Php/Steps/PhpModelsStep.cs` |
| Python | Model-шаг создаёт словарь перед flattening; модельный и HTTP-шаги отдельно готовят карты модулей моделей и enum. | `Python/Steps/PythonModelsStep.cs`, `HttpxClientStep.cs`, `PythonStepHelper.cs` |
| Ruby | Model-шаг создаёт словарь перед flattening. | `Ruby/Steps/RubyModelsStep.cs` |
| Rust | То же. | `Rust/Steps/RustModelsStep.cs` |
| Swift | То же. | `Swift/Steps/SwiftModelsStep.cs` |

В таблице пути генераторов сокращены относительно `src/ValIO.Generators.<Language>/`. При миграции Python карты Python-модулей остаются языковым сервисом: имя модуля зависит от naming convention и структуры пакета.

Дополнительные точки повторения:

- `StdCppTypeMapper` и `UETypeMapper` требуют предварительного вызова `SetEnumNamesForDocument`; `CppAstBuilder` добавляет собственный метод пересылки. Отсутствие вызова меняет классификацию ссылочного типа.
- TypeScript transport builders Ky, Undici, OFetch, Got, Angular, Nextjs и SolidStart, а также основной builder для Fetch/Axios имеют `ModelsByName`. SolidStart пересылает setter вложенному builder. Общий `TypeScriptClientStepBase` тоже хранит изменяемый словарь.
- Zod имеет `ModelsByName` одновременно в AST builder и schema renderer. Valibot и TypeBox создают индексы в своих шагах. Во всех случаях нужен один и тот же поиск модели для текущего документа.
- В TypeScript есть 14 отдельных полей `_orig`: основной builder, builders параметров и результатов, builders их схем, пять transport builders, MSW, Zod renderer, Valibot и TypeBox. Они наполняются через общий `OriginalNameMap`, но остаются изменяемыми накопителями каждого экземпляра.
- `OriginalNameMap.AddOperation` записывает `MethodName` в тот же строковый словарь, что и имена моделей и enum. Области имён операции и типа различны. Общий индекс должен иметь отдельные домены идентичности, а не расширять этот универсальный словарь.

Полезные существующие общие механизмы уже находятся в Core: `IrInheritancePlan`, `IrInheritanceResolver`, `HttpAstBuilder` и граф зависимостей типов. Новая абстракция должна использовать их, а не создавать второй механизм наследования, обхода или нормализации IR.

## Контракт общего контекста

Предлагаемый `IrDocumentContext` создаётся один раз на запрос генерации, после окончательного преобразования имён IR и до первого шага pipeline. Он передаётся через `GenerationContext` всем шагам и их builders.

| Член | Контракт |
| --- | --- |
| `Document` | Единственный исходный `IrDocument` данного прогона. |
| `ModelsByName` | `FrozenDictionary<string, IrModel>`, `StringComparer.Ordinal`, ссылки на модели документа. |
| `EnumsByName` | `FrozenDictionary<string, IrEnum>`, тот же comparer и тот же принцип ссылок. |
| `EnumNames` | `FrozenSet<string>` из ключей `EnumsByName`; производный индекс для существующих membership-проверок. |
| `ModelsInDependencyOrder` | Неизменяемая последовательность моделей по существующему графу с однократным fallback в исходном порядке для отсутствующих в графе моделей. Используется только там, где такой порядок уже нужен. |

`KnownTypeNames` можно добавить как объединение имён моделей и enum при переносе Kotlin, если применение подтверждено. Для него не нужен новый вид IR-узла. Наборы, словари и порядок вычисляются из документа однократно. Новые модели, свойства и операции через контекст не создаются.

Обязательные инварианты:

1. Дубликат имени внутри моделей или enum и пересечение имён модели и enum завершают создание контекста понятной ошибкой. Пустой словарь не служит неявным значением «контекст ещё не настроен».
2. Все индексы одного контекста относятся к одному экземпляру документа и используют ordinal-сравнение. Локальные правила wire-имён, например case-insensitive HTTP-заголовки, к индексам объявлений не применяются.
3. Создание контекста не меняет имена, состав типов, ограничения, nullability, optionality, порядок альтернатив `oneOf` и multiplicity его ветвей.
4. Поле `OriginalName` берётся из модели или enum. Для операции используется её собственный `OriginalMethodName`; при необходимости индекс операций должен иметь составной ключ с областью клиента, а не общий ключ с типами.
5. Неподдерживаемую языком композицию по-прежнему отклоняет соответствующий mapper/builder с диагностикой. Наличие индекса не разрешает молча отбрасывать ветви `allOf`/`anyOf`/`oneOf`.

Текущий IR использует `IReadOnlyList`, но это не доказывает глубокую неизменяемость: под интерфейсом может находиться `List`, а `OriginalSchema` ссылается на OpenAPI-объект. Frozen-индексы защищают их структуру, но не делают весь объектный граф immutable. Для первой миграции нужен явный контракт владения: после создания контекста pipeline не меняет документ и schema. Полное замораживание коллекций IR — отдельная задача, если потребуется внешняя конкурентная передача документа.

В контекст входных данных не входят renderer, mapper, options, активный транспорт, предупреждения, результаты генерации, `StringBuilder` или импорты конкретного выходного файла. `GenerationContext.GeneratedFiles`, `Warnings` и `StepData` остаются результатами одного прогона. Состояние `TypeScriptSourceFileBuilder` с импортами и декларациями остаётся состоянием одного файла.

## Общий builder и границы переиспользования

Первый шаг — внедрить общий контекст через конструктор существующих builders и mappers. Тогда корректная настройка индексов становится обязательной при создании, а публичные setters и последовательность `SetEnumNamesForDocument` исчезают.

Тонкий `AstBuilderBase` оправдан только там, где после этой миграции действительно повторяются поля и конструкторная проверка. Его ответственность — защищённое read-only свойство контекста и доступ к трём общим индексам. Naming и mapper передаются как отдельные зависимости соответствующего языка. Stateless `HttpAstBuilder` и builders, которым не нужен документ, не обязаны наследоваться от него.

В общий базовый класс не переносятся:

- создание language-specific AST-узлов, namespace/import/package и синтаксические особенности C++, C#, TypeScript;
- конкретные типы HTTP-клиентов, transport API, serializer API и validator API;
- выбор scalar-типов, форматов даты/времени, enum representation, правила optional/null и обработка неподдерживаемых конструкций;
- универсальный `BuildModel` с динамическим результатом, набором флагов всех языков или `object` в контракте.

Общий обход ссылок `IrType` может быть чистым сервисом Core с типизированным результатом. Кандидаты на унификацию — обход графа зависимостей, `CppTypeReferenceCollector` и обход `OriginalNameMap`. Перед переносом нужно сопоставить их назначение: сбор только объявлений и обход свойств моделей отличаются от рекурсивного обхода оболочек конкретного типа. Языковые import/module-решения остаются адаптерами результата такого обхода.

Переиспользуемый контракт свойств уже представлен `IrProperty`, ограничений — `IrPropertyConstraints`, операций — `IrOperation`, типов — иерархией `IrType`. Общий контекст предоставляет доступ к этим объектам без параллельных «common model/property/command» DTO. Новые поведенческие интерфейсы добавляются для проверенного повторения, а не по одному на каждый существующий класс.

## Жизненный цикл и конкурентность

[GenerationOrchestrator](../src/ValIO.Application/Services/GenerationOrchestrator.cs) документирован как stateless singleton, безопасный для конкурентных запросов. Он создаёт новый `GenerationContext` и pipeline для каждого вызова. Поэтому сами setters в builders пока не доказывают существующую гонку: большинство builders создаются внутри своего шага. Но они допускают неполную настройку, повторное использование с прежним документом и будущую ошибочную singleton-регистрацию. Constructor injection read-only контекста устраняет эти риски.

Отдельно обнаружено реально общее изменяемое состояние renderers:

| Renderer | Регистрация | Изменяемое состояние |
| --- | --- | --- |
| `CSharpAstRenderer` | Singleton в `PluginsBootstrapExtensions.AddAllGeneratorPlugins` | `_sb` и `_indentLevel`; `Render` очищает и наполняет общий буфер. |
| `CppAstRenderer` | Singleton в `CppGeneratorPlugin.ConfigureServices` | `_indentLevel`; `Render` сбрасывает поле, visitor-методы увеличивают и уменьшают его. |

Это доказанный по исходникам риск пересечения двух вызовов генерации через общий service provider. Runtime-воспроизведение гонки в рамках этой инвентаризации не выполнялось. Исправление: renderer создаётся на pipeline/прогон либо хранит всё изменяемое состояние в локальном объекте одного вызова `Render`. Простая замена singleton на transient недостаточна при продолжении reuse renderer из долгоживущего pipeline; контракт использования тоже должен быть явным.

Статические naming/mapping-сервисы без изменяемого состояния могут оставаться singleton. Массовое изменение lifetime всех плагинов и сервисов не требуется. `TypeScriptAstBuilder._activeEndpointTransport` тоже следует сделать параметром операции или отдельным неизменяемым transport context при переносе этого builder; текущее изменение поля с восстановлением в `finally` не обеспечивает reentrancy.

## Последовательность миграции

1. Завершить текущую проверку C++ артефакта, зафиксировать локальные результаты native build и codec round-trip. Не менять рабочую генерацию во время этой проверки.
2. Добавить `src/ValIO.Core/IR/IrDocumentContext.cs`, подключить в `Core/Plugins/GenerationContext.cs` и `Application/Services/GenerationOrchestrator.cs`. Проверить уникальность имён, ссылки на исходные объекты, порядок и отсутствие изменения IR.
3. Перенести C++: `PrepareInputs`, std/UE steps, `StdCppTypeMapper`, `UETypeMapper`, фасад и document-aware builders. Сохранить язык, профиль, имена файлов и диагностики. Удалить setters и повторные наборы только после перевода всех callers.
4. Исправить lifetime/state C++ renderer и повторить native gate. Контекст не должен сам по себе менять C++ output.
5. Перенести C# model/client paths на один контракт контекста, удалить три setters, проверить прямой `Build(document)` и pipeline. Отдельно исправить lifetime/state C# renderer.
6. Перенести TypeScript model/client base, Angular, восемь transport steps, Zod/Valibot/TypeBox и maps исходных имён. Удалить domain collision между именами операций и типов. Транспорт остаётся отдельной зависимостью.
7. Перенести девять остальных model steps, затем Kotlin known names и Python language-specific module services. Общий порядок моделей применяется только в шагах с соответствующим текущим контрактом.
8. Добавить базовый builder только по остаточному повторению после миграции. Если constructor injection уже устранил дубли, отдельный base class не обязателен.

## Проверка результата

Нужны проверки поведения, а не тесты, повторяющие `ToDictionary`:

- два разных документа с одинаковым именем, но разными model/enum определениями: генерация через один orchestrator параллельно должна давать результаты своих изолированных прогонов без смешения контента;
- общий контекст действительно один для всех шагов запроса; ссылки lookup равны соответствующим объектам документа;
- отсутствие ручной инициализации builder невозможно либо завершается явной ошибкой; прямой builder и plugin pipeline одинаково разрешают enum/default/inheritance;
- fixtures с вложенными композициями, scoped operation names, affixes, nullable/optional контейнерами и исходными wire-именами сохраняют смысл и имена файлов;
- C++ native compilation и round-trip, C# compilation, TypeScript compilation и существующие Node-тесты validators/transports проходят после своего этапа миграции;
- `git diff --check` и аудит оставшихся setters/локальных document lookups подтверждают завершённость переноса, но сами по себе не заменяют генерацию и исполнение артефактов.

Первоначальная инвентаризация выполнена статически по исходникам всех 12 генераторов.
На этапе инвентаризации production-код, API builders и lifetimes сервисов не менялись;
сборка и конкурентные runtime-тесты не запускались. Последующая реализация и её
принятые gates описаны в актуальном статусе в начале документа.

## Очередь после r2: именованные C++ std request types

Статус: **принятый запрос пользователя, только план; реализация не начата**.
Текущая поставка r2 остаётся первым этапом. Следующий slice должен рассмотреть
именованные class/struct containers для route parameters, query parameters и body
по аналогии с существующим подходом C++ UE Params/Result, сохранив std ownership
и зависимости. Перед реализацией нужно выбрать opt-in option и точную форму API;
прежние сигнатуры и default output сохраняются для обратной совместимости.

| Этап | Конкретная работа и граница |
| --- | --- |
| Контракт API | Для операции определить имена, например GetStoreRouteParams / GetStoreQueryParams, и решить, нужен ли общий GetStoreRequest. Body использует существующий model/enum/scalar type; отдельная оболочка допустима только с самостоятельной семантикой presence. Имена разрешаются общим allocator без конфликтов с models/enums и другими операциями. |
| Семантика полей | Сохранить required/optional/nullable и различия absence/null/value для query и body. Route-поля сохраняют обязательность контракта. Учитывать wire names, formats, enum defaults, arrays, objects и ограничения; grouping не добавляет поддержку ещё неподдержанных style/explode/media constructs. |
| Переиспользование IR | Использовать существующие IrOperation, IrParameter, IrRequestBody, IrType и IrDocumentContext. Не создавать параллельную иерархию model/type nodes или копии схем. Общее разрешение имён и типов остаётся единственным источником для std/UE builders. |
| Ownership и control | Для sync API рассмотреть const reference к borrowed request values; async API должен владеть данными до завершения операции. Cancellation и прочие request controls передаются отдельным аргументом/options и не становятся сериализуемыми полями request DTO. |
| Чистые DTO | Request headers зависят от STL, выбранных scalar providers и существующих DTO. JSON/HTTP headers и сериализация остаются на отдельных codec/client границах. Header-only containers не получают пустые .cpp. |
| Генерация и совместимость | Добавить opt-in generation path в C++ std client builder/step, переиспользовать mapper и общие IR lookup. Проверить старые сигнатуры, overload resolution, namespace/file names, interface+impl и class shapes; default configuration должна сохранять прежний output. |
| Unit и native приёмка | Физические fixtures покрывают route/query/body, wire escaping, enums, arrays/objects, required × nullable и absence/null/value. Native C++17/20/23 cases проверяют sync/async ownership, body omission/null/value, cancellation отдельно от payload, HTTP wire output и model-only TU без JSON/HTTP dependencies. |

Критерий завершения этого будущего slice: явный option contract, совместимый default,
typed named request API, отсутствие дублирующих IR/model definitions и собственные
unit/native gates. Текущие r2 результаты его реализацию или приёмку не подтверждают.
