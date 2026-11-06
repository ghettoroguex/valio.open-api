# Аудит valio.open-api — 11 октября 2026 года

## Основание и границы проверки

Проверяется ветка codex/admin-e2e-multipart репозитория valio-projects/valio.open-api.
Исходная точка: e2cdf3a93d2101f58f627ca2575067b6e23ab91c.
История ниже — ровно 50 коммитов, достижимых из этой точки, в порядке git log -50;
это не 50 коммитов первой родительской цепочки и не 50 отдельных неповторяющихся изменений.

Исторический анализ основан на родительских связях, именах и изменениях файлов,
first-parent diff merge-коммитов и git patch-id --stable для обычных коммитов.
Проверка архитектуры основана на исходном коде Core и генераторов.
Все номера строк в разделе находок относятся к исходной точке e2cdf3a, до текущих исправлений.
Исторические сообщения о зелёной сборке не считаются доказательством текущей сборки.

Документ различает:

- подтверждённый исходным кодом дефект;
- порученное исправление, результат которого ещё требует итогового gate;
- ограничение модели возможностей, для которого нужна отдельная проработка;
- фактически выполненную локальную проверку, которая записывается в последнем разделе.

Наличие плагина, транспорта или теста не означает проверку всех сочетаний языков,
форматов, протоколов, моделей, enum и валидаторов.

### Принятый порядок поставки

Первая поставка ограничена готовым локальным C++ std артефактом и его проверкой.
После создания этого архива пользователь разрешил commit/push и продолжение работы;
первый commit e28998750cb31a0a2dbccc80307e8eecd3856562 отправлен в
origin/codex/admin-e2e-multipart. Межъязыковые gates этого состояния описаны ниже.
Его результаты не меняют состав и происхождение ранее собранного архива. Исходники
именно этой C++ поставки зафиксированы в её артефакте:
новая миграция ModelsByName/EnumNames и общего builder context в неё не входит.
Её план и текущее выполнение записаны в [builder-context-plan.ru.md](builder-context-plan.ru.md).
Core context и базовый builder добавлены в следующей рабочей волне; приёмка новых
изменений ещё ожидается.

## Уточнённые требования и нормативная матрица

Пользователь расширил приёмку: проверять реальные JSON/YAML документы через production
parser, сохранять семантику композиции и её локальные ограничения, выполнять
сгенерированные валидаторы в Node и отдельно компилировать TypeScript-клиенты.
В C++ DTO должны использоваться без зависимостей от JSON и HTTP.
Эти требования дополняют исходный аудит; их выполнение подтверждается отдельными gates.

Целевые версии фиксированы, без подмены ближайшей старой версией:

| Версия | Официальная норма | Целевой вход | Требуемое доказательство |
| --- | --- | --- | --- |
| Swagger/OpenAPI 2.0 | [OpenAPI 2.0](https://spec.openapis.org/oas/v2.0.html) | JSON и YAML, swagger: 2.0; definitions и версия-специфичные constructs. | Реальный parser → IR → целевые генераторы. |
| OpenAPI 3.0.4 | [OpenAPI 3.0.4](https://spec.openapis.org/oas/v3.0.4.html) | JSON и YAML, openapi: 3.0.4. | Реальный parser → IR; nullable, allOf/oneOf/anyOf и ограничения по правилам этой версии. |
| OpenAPI 3.1.2 | [OpenAPI 3.1.2](https://spec.openapis.org/oas/v3.1.2.html) | JSON и YAML, openapi: 3.1.2. | Реальный parser → IR; JSON null и композиция с учётом диалекта схем. |
| OpenAPI 3.2.0 | [OpenAPI 3.2.0](https://spec.openapis.org/oas/v3.2.0.html) | JSON и YAML, openapi: 3.2.0. | Реальный parser → IR для явно проверяемых возможностей; прочие возможности версии перечисляются отдельно. |

Представление документа в JSON/YAML не требует таких же media types у API.
Это различие установлено в разделах Format
[OpenAPI 2.0](https://spec.openapis.org/oas/v2.0.html#format) и
[OpenAPI 3.2.0](https://spec.openapis.org/oas/v3.2.0.html#format).
Для 3.1 Schema Object опирается на JSON Schema Draft 2020-12; смена одного
значения openapi в старом fixture не доказывает поддержку диалекта.
[OpenAPI 3.1.2, Schema Object](https://spec.openapis.org/oas/v3.1.2.html#schema-object).

Приёмка этой матрицы требует:

- восьми обязательных сочетаний версия × JSON/YAML с проверкой parser diagnostics;
- отсутствия Ignore/skip/catch-and-continue, скрывающих неподдержанный reader или dialect;
- нативных для каждой версии fixtures: oneOf/anyOf не выдаются за стандартные
  constructs Swagger 2.0, а x-nullable явно отмечается как extension;
- проверки IR и наблюдаемого поведения сгенерированного кода, а не только успешного чтения;
- отдельных положительных/отрицательных значений для required/optional/nullable,
  композиции и ограничений внутри каждой ветви.

В текущем diff снят прежний пропуск reader failures в MultiVersionEquivalenceTests.
OpenApiParserMatrixTests уже перечисляет точные версии 2.0/3.0.4/3.1.2/3.2.0 × JSON/YAML
и читает отдельные файлы из tests/Fixtures/OpenApi/Parser. Есть проверки контрактов,
nullable bodies, композиции/вложенной композиции и отрицательные parser-сценарии.
Эта parser/composition matrix входит в подтверждённый Application gate 77/77;
охват конкретных генераторов и runtime остаётся отдельным доказательством.

## Последние 50 коммитов

В выборке 10 merge-коммитов и 40 обычных коммитов. Группы взаимно исключающие;
все 50 записей учтены один раз.

| Группа | Количество | Смысл и результат анализа |
| --- | ---: | --- |
| C++ W1–W8 | 8 | Регистрация профилей, общий AST, std mapper, модели/кодеки/клиенты/runtime, compile fixture и удаление повторов. Перенос общих AST-деклараций выполнен; обнаружены проблемы общего плана allOf. |
| Multipart | 6 | C#/TS form-data и structured multipart. Две пары одинаковых patch-id; поведение конкретных транспортов проверяется отдельно. |
| Интеграционные merges | 4 | EOF/multipart интеграция. Два merge-коммита не меняют дерево относительно первого родителя. |
| Wire names и query transport | 7 | Сохранение исходных имён параметров, построение wire-ключей, Next.js. В Core остались ошибки идентичности и удаления параметров. |
| C#/общее наследование | 6 | EOF, paging/query/defaults/enums/allOf и идентификаторы. Коммит blobs содержит реальные C#/TS исправления. |
| TS schemas/hooks/AST | 8 | Next.js, cache keys, ограничения параметров, inline type literals, discriminator/schema/result и тесты. Обнаружены общие ошибки нормализации и извлечения ограничений. |
| Зависимости | 4 | Два обновления зависимостей и два интегрирующих merge-коммита. |
| Инфраструктура/лицензия | 4 | Trusted publishing, LGPL, переход CI и Release-конфигурация F#. Текущий удалённый CI этим аудитом не подтверждён. |
| Cubemap/документация | 3 | Ветка Cubemap, Hungarian naming, конфигурация и описание отклонения. |

| № | SHA | Исходная тема коммита | Группа |
| ---: | --- | --- | --- |
| 1 | e2cdf3a | Merge remote-tracking branch 'origin/dependabot/npm_and_yarn/packaging/npm/valio/types/node-26.2.0' into codex/admin-e2e-multipart | Зависимости |
| 2 | 3dbd577 | Merge remote-tracking branch 'gitlab/feature/cubemap_generator_mpv1' into codex/admin-e2e-multipart | Cubemap/документация |
| 3 | bed4df5 | Merge remote-tracking branch 'origin/dependabot/nuget/microsoft-extensions-5d9c2f4fa5' into codex/admin-e2e-multipart | Зависимости |
| 4 | 7cf6f1e | Merge remote-tracking branch 'gitlab/fix/ts-nested-query-param-names' into codex/admin-e2e-multipart | Wire names и query transport |
| 5 | 5e66c28 | Merge branch 'codex/publish-valio-eof' into codex/admin-e2e-multipart | Интеграционные merges |
| 6 | aebb663 | Merge branch 'codex/admin-ts-multipart' into codex/admin-e2e-multipart | Интеграционные merges |
| 7 | 24b3b9c | Merge branch 'codex/publish-valio-multipart' into codex/admin-e2e-multipart | Multipart |
| 8 | 0f01358 | refactor(cpp): W8 audit sweep — dedupe collectors/helpers, kill suppressions, fix drain + query-model edge | C++ W1–W8 |
| 9 | 7aeee92 | refactor(cpp): principled literal rendering — kill the TEXT/QuotedString hacks (W7) | C++ W1–W8 |
| 10 | 59aa234 | test(cpp): compile fixture green on 17/20/23 + CMake step + CI + docs (W6) | C++ W1–W8 |
| 11 | b4dfc6a | feat(cpp): std profile end-to-end — clients (interface+impl), runtime, steps (W5) | C++ W1–W8 |
| 12 | 956d698 | feat(cpp): std-profile enum/model/codec builders + Core IrInheritancePlan (W4) | C++ W1–W8 |
| 13 | ee5e602 | feat(cpp): StdCppTypeMapper + StdNamingConvention for the std profile (W3) | C++ W1–W8 |
| 14 | 5b19a51 | refactor(core): promote Namespace/Class/Struct/Field nodes from C# to Core AST (W2) | C++ W1–W8 |
| 15 | c677e73 | feat(cpp): register CppGeneratorPlugin with runtime-profile options (W1) | C++ W1–W8 |
| 16 | 4693852 | merge: integrate C# generator EOF support | Интеграционные merges |
| 17 | c8fa20a | fix(csharp): Normalize generated file endings | C#/общее наследование |
| 18 | 8edabd4 | fix(typescript): support structured multipart in solid clients | Multipart |
| 19 | d48d5ec | fix(typescript): serialize multipart requests as form data | Multipart |
| 20 | 21718f2 | fix(typescript): support structured multipart in solid clients | Multipart |
| 21 | 618eb5a | fix(typescript): serialize multipart requests as form data | Multipart |
| 22 | bede7d0 | fix(csharp): Merge multipart form generation | Интеграционные merges |
| 23 | a83a48a | fix(csharp): Generate multipart form content | Multipart |
| 24 | 54c91ea | fix(nextjs): Avoid nullish coalescing header objects | Wire names и query transport |
| 25 | 32ca588 | fix(typescript): Render wire parameter keys once | Wire names и query transport |
| 26 | 8085bf1 | merge: Align wire serialization with ValIO master | Wire names и query transport |
| 27 | 00d2a0b | fix(typescript): preserve OpenAPI wire parameter names | Wire names и query transport |
| 28 | 4661a0e | blobs | C#/общее наследование |
| 29 | 53cc981 | fix(typescript): точечные «проводные» имена query-параметров в axios/nextjs | Wire names и query transport |
| 30 | 998ec23 | fix(typescript): «проводные» имена query-параметров и заголовков | Wire names и query transport |
| 31 | 153a671 | Bump Microsoft.Extensions.DependencyInjection and 6 others | Зависимости |
| 32 | 1babc46 | chore(deps-dev): bump @types/node in /packaging/npm/valio | Зависимости |
| 33 | fd1513a | ci(nuget): поддержать Trusted Publishing вместо долгоживущего API-ключа | Инфраструктура/лицензия |
| 34 | f34d4c3 | chore(license): перевести проект с MIT на LGPL-2.1 | Инфраструктура/лицензия |
| 35 | 3d1d489 | ci: переезд с GitLab CI на GitHub Actions с публикацией по веткам | Инфраструктура/лицензия |
| 36 | 00c1baf | fix(build): вернуть Release-конфигурацию проектам ValIO.Generators.FSharp | Инфраструктура/лицензия |
| 37 | d82136a | fix(csharp): имена paging-параметров, nullable enum-свойства, пустое тело | C#/общее наследование |
| 38 | 04a3457 | fix(csharp): DefaultValue типизируется по схеме, а не по CLR-типу значения | C#/общее наследование |
| 39 | ed862ba | fix(csharp): рабочая генерация клиентов — ApiException, null/array query, enum-сериализация, allOf | C#/общее наследование |
| 40 | 2568aa1 | fix(codegen): общий allOf-наследование + zod-импорты под verbatimModuleSyntax | C#/общее наследование |
| 41 | a22688f | fix: require Hungarian-notation prefix check to be case-sensitive on the following letter, repoint config to repo-relative output | Cubemap/документация |
| 42 | 1e3ecd9 | feat(typescript): TanStack cache keys, queryTransport, params constraints, fetch query-string | TS schemas/hooks/AST |
| 43 | 7179daf | feat(typescript): add Next.js (App Router) fetch transport | TS schemas/hooks/AST |
| 44 | 030707b | feat(core): add TypeLiteralNode for inline object types | TS schemas/hooks/AST |
| 45 | b8e06f6 | feat(typescript): emit z.discriminatedUnion for oneOf + discriminator | TS schemas/hooks/AST |
| 46 | 0a275b0 | feat(typescript): co-located & typed/coerced schemas + discriminable result union (BUG #6) | TS schemas/hooks/AST |
| 47 | de51382 | tests | TS schemas/hooks/AST |
| 48 | a402079 | split query & mutation imports; fix argument types; | TS schemas/hooks/AST |
| 49 | 9215b37 | nullfix; axios import types fixes | TS schemas/hooks/AST |
| 50 | 5cdb885 | docs: mark branch as CubemapGeneratorMVP1 deviation in README | Cubemap/документация |

### Что доказано о повторах истории

| Пара | Stable patch-id | Значение |
| --- | --- | --- |
| 21718f2 = 8edabd4 | 39b9ab8ee36349ca6b0df04d55c7f9a3624d412b | Одинаковое изменение structured multipart SolidStart в двух цепочках. |
| 618eb5a = d48d5ec | 2d019e399b14ce4829046c7c97ed1505a4980252 | Одинаковое изменение сериализации TypeScript FormData в двух цепочках. |

Коммиты 5e66c28 и aebb663 имеют пустой diff относительно первого родителя:
соответствующее содержимое уже присутствовало. У merge-коммитов 4693852 и bede7d0
есть изменения относительно первого родителя; они интегрируют EOF/multipart из боковых веток.
Эти факты не обосновывают переписывание опубликованной истории и не доказывают наличие
двух действующих реализаций. Исправлять нужно повторяющиеся реализации в текущем коде.

## AST, IR, команды и архитектурные границы

По декларациям исходной точки не обнаружены повторяющиеся точные имена типов,
оканчивающиеся на Node, Type или Command. Это ограниченная инвентаризация деклараций,
а не доказательство отсутствия семантически одинакового кода.

Коммит 5b19a51 действительно перенёс общие NamespaceDeclarationNode,
ClassDeclarationNode, StructDeclarationNode и FieldDeclarationNode в Core.
Параллельных одноимённых C#-деклараций в исходной точке нет.
CLI регистрирует отдельные generate, validate и init; повторение --schema или --language
в разных командах само по себе не является дублированием команды.

Разные уровни и смыслы должны оставаться явными:

- IR описывает входной контракт и семантику типов; AST описывает вывод конкретного языка.
- Class и Struct — разные синтаксические/семантические декларации.
- Optional означает отсутствие значения, Nullable — допустимый JSON null.
- Binary, ByteArray и File нельзя автоматически объединять только потому,
  что конкретный язык отображает их в одинаковый тип; wire-кодирование может различаться.

В проекте уже применяются Builder, Visitor, Strategy и последовательный Pipeline.
Практическое улучшение SOLID/DRY/GRASP — единые операции сравнения, обхода,
выделения имён и разрешения свойств IR с явными границами возможностей плагинов.
Добавление универсальных фабрик поверх существующих классов само по себе не устраняет
обнаруженные ошибки и не является критерием качества.

## Подтверждённые исходным кодом находки

Статус «изменено в текущем diff» означает наличие соответствующего изменения исходников.
Он не означает прохождение общего gate или runtime каждого потребителя.
Финальный Core gate первой C++ поставки подтверждает 108/108 без skips;
он отдельно отмечен в последнем разделе и не заменяет проверку всех языковых toolchains.
Окончательное состояние каждой находки и результаты проверок дополняет координатор.

| ID | Приоритет | Источник в e2cdf3a | Дефект и воспроизводящий сценарий | Направление исправления | Статус |
| --- | --- | --- | --- | --- | --- |
| CORE-01 | P1 | src/ValIO.Core/IR/Builders/IrBuilder.cs:1712; присваивания :547 и :625 | Имя inline-модели строится только из имени свойства. A.details с id и B.details с name перезаписывают один DetailsInlineModel; обе ссылки ведут к последней форме. Возможна коллизия с объявленным компонентом. | Стабильное выделение имён по контексту/пути, резервирование компонентных имён, переиспользование только эквивалентных форм. | Изменено в текущем diff: allocator/reservation и проверки коллизий. |
| CORE-02 | P1 | src/ValIO.Core/IR/Utilities/IrInheritancePlan.cs:58–62 | Flatten обращается к Composition! обычной базовой модели, где Composition=null. Несколько обычных баз allOf вызывают NullReferenceException. Нет защиты от циклов flatten; одиночные циклы могут породить некорректное наследование. | Терминальный случай обычной модели, контроль циклов, детерминированное разрешение diamond/override. | Изменено в текущем diff: общий cycle-safe resolver и FlattenProperties. |
| CORE-03 | P1 | src/ValIO.Core/IR/Utilities/IrTypeComparer.cs:19–27; IrCompositionNormalizer.cs:108 | Дедупликация стирает optional/nullable при сравнении. string перед nullable string способен удалить null из итогового union. Для вложенных композиционных коллекций нет полного структурного сравнения. | Разделить точное структурное равенство и осознанное сравнение без обёрток; нормализовать nullability независимо от порядка. | Изменено в текущем diff: структурное сравнение, presence policy и cardinality oneOf. |
| CORE-04 | P1 | src/ValIO.Core/IR/Builders/IrBuilder.cs:1276, :1323, :1350–1401 | Все имена параметров сравниваются без регистра; dedup зависит от наличия warnings. Удаляется легальный query id при path id. Санитизированные имена смешиваются с wire-идентичностью; порядок {user-id}/{other.id} не совпадает с sanitized lookup. | Идентичность location+OriginalName, case folding только для headers; отдельное разрешение коллизий символов; одинаковая семантика с warnings и без. | Изменено в текущем diff: wire identity, порядок OriginalName и отдельные уникальные символы. |
| CORE-05 | P2 | src/ValIO.Core/IR/Builders/IrBuilder.cs:347–368 | Presence-check не учитывает exclusiveMinimum/Maximum, поэтому exclusive-only ограничение исчезает. decimal.TryParse зависит от текущей культуры. | Проверять exclusive bounds и разбирать числа invariant; регрессии под en-US/ru-RU. | Изменено в текущем diff: exclusive presence и invariant NumberStyles.Float. |
| CORE-06 | P2 | src/ValIO.Core/IR/Builders/IrBuilder.cs:1513–1706; src/ValIO.Core/IR/Builders/IrTypeDependencyGraphBuilder.cs:19–152 | Вынесенный graph builder используется тестами, а production повторяет извлечение зависимостей/топологическую сортировку. Старый обход клиентских зависимостей собирает и выбрасывает результаты. | Production делегирует извлечённому builder; удалить копию/no-op traversal; проверить production parity. | Изменено в текущем diff: production вызывает единственный IrTypeDependencyGraphBuilder. |
| PLUGIN-01 | P2 | src/ValIO.Core/Plugins/GeneratorRegistry.cs:16–18 | Повторный LanguageId/alias другого плагина молча перезаписывает регистрацию. Текущего конфликта между зарегистрированными плагинами не найдено. | Ошибка при конфликте владельцев; разрешить повтор собственного ID в aliases того же плагина. | Изменено в текущем diff: конфликт разных владельцев вызывает InvalidOperationException. |
| LANG-01 | P1 | IrBuilder.cs:148–175 и model builders ниже | Core хранит собственные свойства в Properties, базы allOf — в Composition. Девять генераторов читают только собственные свойства и не добавляют поля баз: Base.id + Child.name теряет id. | Общий cycle-safe flatten resolver на границе генераторов без наследования; сохранить own-only IR для C#/TS/C++. | Изменено в steps девяти генераторов; ограничение unions не закрыто. |

### Межъязыковая проверка allOf

Отсутствие flatten подтверждается прямым обходом model.Properties в:

| Язык | Файл и строка исходной точки |
| --- | --- |
| Java | src/ValIO.Generators.Java/Ast/Builders/JavaRecordModelAstBuilder.cs:33 |
| Kotlin | src/ValIO.Generators.Kotlin/Ast/Builders/KotlinDataClassAstBuilder.cs:31 |
| Python | src/ValIO.Generators.Python/Ast/Builders/PydanticModelAstBuilder.cs:43 |
| Swift | src/ValIO.Generators.Swift/Ast/Builders/SwiftStructAstBuilder.cs:30 |
| Rust | src/ValIO.Generators.Rust/Ast/Builders/RustModelAstBuilder.cs:34 |
| Ruby | src/ValIO.Generators.Ruby/Ast/Builders/RubyModelAstBuilder.cs:29 |
| PHP | src/ValIO.Generators.Php/Ast/Builders/PhpModelAstBuilder.cs:31 |
| Dart | src/ValIO.Generators.Dart/Ast/Builders/DartModelAstBuilder.cs:32 |
| F# | src/ValIO.Generators.FSharp/Ast/Builders/FSharpModelAstBuilder.cs:47 |

Поиск Composition/Flatten/Inherited в исходной точке во всех этих генераторах не обнаружил обработки
AllOf; F# отдельно обрабатывает OneOf/AnyOf как DU.
Изменение только Core inheritance plan не исправляет эти потребители автоматически.
Нужны интеграционные сценарии Base/Child, multiple-base, diamond и повтор свойства.

В текущей рабочей копии steps этих девяти генераторов уже изменены для общего
разрешения свойств allOf; добавлены AllOfModelsStepTests в соответствующих проектах.
Это описание присутствующего кода, а не результат итоговой проверки или компиляции
вывода каждого native toolchain.

### Null и обход типов в остальных языках

TypeScript, Python, PHP, Dart и Ruby явно отображают IrPrimitiveKind.Null.
Java, Kotlin, Rust, Swift и F# в исходной точке попадают в общий fallback.
Kotlin Any без nullable-маркера не представляет required null-only свойство.
Это требует отдельной политики целевого языка; наличие fallback не доказывает
точность сохранения контракта.

Во многих GetImportTarget возвращается только одна ссылка из словаря.
Полный общий обход типов позволил бы собирать все вложенные ссылки единообразно,
вместо повторения switch по array/dictionary/nullable/optional/union/intersection.
Этот вывод является направлением архитектурного улучшения, а не заявлением,
что каждый импорт каждого языка уже проверен компилятором.

### OneOf, AnyOf и ограничения отдельных ветвей

oneOf требует ровно одного успешного совпадения, anyOf — хотя бы одного.
Для пересекающихся ветвей один и тот же payload должен отклоняться oneOf и приниматься
anyOf. Это нормативное различие:
[OpenAPI 3.0.4, Discriminator Object](https://spec.openapis.org/oas/v3.0.4.html#discriminator-object),
[JSON Schema 2020-12, anyOf](https://json-schema.org/draft/2020-12/json-schema-core#section-10.2.1.2),
[JSON Schema 2020-12, oneOf](https://json-schema.org/draft/2020-12/json-schema-core#section-10.2.1.3).

В текущем diff добавлен IrUnionKind с AnyOf/OneOf; IrUnionType хранит исходный
режим совпадения. Дедупликация или flatten не должны превращать oneOf[A,A]
в A: такое изменение меняет число совпавших ветвей.
В IrType.Constraints добавлены локальные ограничения схемы, включая отдельные
ветви композиции; прежних ограничений только на IrProperty для этого недостаточно.
Режим union и ограничения должны сохраняться при rewrite, сравнении и генерации
валидатора. Наличие этих полей в исходниках проверено, общий gate ещё не завершён.

Пример приёмки: anyOf строка minLength=5 либо число minimum=10.
Короткая строка и число 9 отклоняются, строка длины 5 и число 10 принимаются.
Для oneOf с пересекающимися числовыми диапазонами значение в пересечении отклоняется.
Проверяются также sibling properties/required рядом с composition и отсутствие coercion,
которое неожиданно расширяет множество допустимых JSON-значений.

Расширение Core IR не означает автоматически точные unions во всех девяти
альтернативных языках. Java, Kotlin, Swift, Rust, Ruby, Dart и F# type mappers
по-прежнему имеют широкие Object/Any/obj/JSON-value mappings для произвольных unions.
Python/PHP выводят синтаксический union, но он сам по себе не обеспечивает oneOf
exactly-one. F# имеет отдельный DU-путь для модельных OneOf/AnyOf; это не общая
runtime-валидация каждой ветви. Эти ограничения остаются открытыми и не закрываются
исправлением flatten для allOf.

## Текущее состояние изменений по C++, C# и TypeScript

Ниже перечислены присутствующие изменения и ожидаемая проверка. Точный итоговый diff,
добавленные регрессии и результаты общей приёмки дополняются после gate.

| Область | Порученная работа | Ограничение доказательств на момент подготовки раздела |
| --- | --- | --- |
| Core AST/IR | Целостность AST-потомков/родителей, IrUnionKind, локальные IrType.Constraints, общие резолверы и удаление повторов; parameter Style/Explode. | Текущий общий gate: Core 149/149 passed, 0 skipped, включая 12 новых parameter serialization cases; архивный Core108 сохранён отдельно. |
| C++ | Чистые DTO/enum headers, отдельные codecs .h/.cpp, model/serializer/client CMake targets; явный отказ для неподдержанной модельной композиции; typed Request и HTTP/cancellation работа. | Финальные source gates: unit 262/262, main native 4/4, scalar providers 7/7, regenerated preview CMake 3/3 и models-only 1/1, HTTP/TLS object compile. Везде 0 failures / 0 skips. HTTPS runtime не подтверждён. Package provenance см. BUILD-INFO.json готовой поставки. Первый архив ниже неизменен. |
| C# | Nullable reference/enum/default/query и multipart, единые options, согласованность mapper/builders; явный отказ для неподдержанной модельной композиции и неоднородных success results. | Новый context/HTTP gate: 127 passed, 0 failed, 0 skipped, включая четыре native tracing режима. |
| TypeScript | Общая multipart-сериализация, wire-параметры, validators/AST; Node runtime Zod 3/4, Valibot, TypeBox; строгая компиляция transport matrix. | Точный committed snapshot: 814 passed, 0 failed, 0 skipped; включает все 50 strict compile случаев, Node/runtime и прежний Angular тест с реальными зависимостями. Новая context-волна требует отдельного gate. |
| Остальные языки | Совместное подключение flatten-резолвера и проверки allOf-полей. | Unit suites всех девяти генераторов пройдены в общем precommit gate; broad union mappings и отсутствующее полное native coverage остаются ограничениями. |
| SocratiCode | Индексирование valio.open-api запущено по точному пути. | Последний snapshot координатора: 42%, 1959/4616, batch 5/26, persisted 1575 points; watcher inactive, graph pending. Индекс ещё не готов, контрольный поиск не подтверждён. |

### Явные ошибки вместо потери неподдержанной модельной семантики

CSharpAstBuilder.BuildClass теперь отклоняет модельные композиции, которые не являются
allOf ссылками на модели. Разнородные success body types также отклоняются, если
для них нужен ещё не реализованный result union.
В C++ CppTypeReferenceCollector.EnsureSupportedComposition проверяет достижимые
базовые модели, отклоняет OneOf/AnyOf, вложенный non-reference allOf и неразрешённую базу.
Проверка используется UE/std model builders и std JSON builder.

Сообщения требуют composed value representation или typed variant/composition-aware
codec. Это намеренно более строгая граница: такие случаи завершаются
NotSupportedException вместо генерации модели, потерявшей часть исходного контракта.
Исключение не означает реализованную поддержку; точные положительные и отрицательные
fixture-сценарии принимаются только после общего gate.
Обычное allOf модельных ссылок использует общий план наследования/flatten.

### Файловые fixtures и граница доказательства property matrix

Контракты вынесены в отдельные файлы tests/Fixtures/OpenApi:
Parser, CoreIR, Inheritance, Validators, Transport и TypeScriptCompile.
tests/Shared/OpenApiFixture читает скопированные рядом с test assembly документы;
версии/схемы не заменяются текстовыми подстановками и не зависят от CWD.
Это делает конкретный вход теста доступным для самостоятельной проверки.

Добавлен IrBuilderCompositionPresenceTests: required/non-required и nullable/non-nullable
свойства проверяются отдельно для anyOf/oneOf/allOf; дополнительные сценарии проверяют
nullable array/dictionary/object и nullable вложенные значения. Эти Core проверки
входят в 108/108. Они различают отсутствие свойства и допустимый null в IR.
Полная accept/reject matrix сгенерированных моделей/валидаторов каждого языка,
включая унаследованные свойства, остаётся отдельным gate следующих этапов.
Наличие прежнего nullable body теста или одного успешного Core suite её не заменяет.

### Реальные TypeScript validators и transport compile

Проверяются установленные библиотеки Zod 3, Zod 4, Valibot и TypeBox в Node:
сгенерированные schema modules должны принимать/отклонять значения в соответствии
с исходным контрактом. Наличие строк .min(), union или oneOf в snapshot недостаточно.
TypeScript transpileModule без diagnostics также не заменяет tsc type-check.

Первоначальная transport compile матрица: axios, fetch, solid, nextjs, ky, got, ofetch,
undici и angular-rxjs. В текущем precommit запуске добавлен angular-promise:
50 случаев строгой компиляции с реальными зависимостями. Это не покрывает каждую
комбинацию дополнительных query wrappers.
Проверяются required/optional multipart, вложенные metadata и коллекции,
wire query/header names и совместимые типы запросов/ответов.

В текущей копии имеются ValidatorRuntimeIntegrationTests, ValidatorBoundaryPipelineTests
и TransportRequestSerializationRegressionTests; runtime-контракты читаются из файлов.
На последнем статическом просмотре runtime matrix ещё перечисляла 3.0.3/3.1.0,
а общий GeneratedTypeScriptRuntime мог пропускаться без VALIO_VALIDATOR_NODE_MODULES.
Production parser matrix уже использует точные 3.0.4/3.1.2. Нельзя смешивать эти два
набора доказательств. Обязательный запуск с установленными зависимостями, согласование
runtime versions и отсутствие скрытых skips остаются условиями приёмки координатора.
Перед первым коммитом координатор запустил отдельные реальные tsc/Node gates.
Промежуточные ошибки и последующий успешный запуск из точного committed snapshot
приведены в следующем разделе. Этот результат не переносится на новую context-волну.

### Межъязыковая проверка перед первым коммитом

После первой C++ поставки выполнен общий Release gate:
[precommit-general-fixed.log](../artifacts/audit/precommit-general-fixed.log).
Все 14 перечисленных suites завершились без failures: 1941 passed / 0 failed /
1 environment skip. Единственный пропуск этого общего запуска — baseline тест
TypeScript GeneratedAngularOutput_CompilesUnderTscStrict; позднее он выполнен
с реальными зависимостями в committed-snapshot gate ниже. Предупреждения анализаторов в логе
сохраняются; warning-clean сборка не заявляется.

| Suite | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| Core | 108 | 0 | 0 |
| Application | 77 | 0 | 0 |
| C++ | 161 | 0 | 0 |
| C# | 122 | 0 | 0 |
| TypeScript | 706 | 0 | 1 |
| Ruby | 59 | 0 | 0 |
| F# | 59 | 0 | 0 |
| Rust | 62 | 0 | 0 |
| Java | 112 | 0 | 0 |
| Kotlin | 105 | 0 | 0 |
| Python | 123 | 0 | 0 |
| Dart | 68 | 0 | 0 |
| PHP | 89 | 0 | 0 |
| Swift | 90 | 0 | 0 |

Девять альтернативных генераторов теперь имеют свежие unit результаты, включая
подключённые allOf регрессии. Это не означает native/runtime приёмку всех этих
языков и не снимает перечисленные выше ограничения union mappings.

Промежуточные отдельные проверки выявили ошибки, исправленные до окончательного
запуска TypeScript. Эти логи сохранены как история диагностики:

| Gate | Текущий результат | Оставшаяся работа |
| --- | --- | --- |
| TypeScript strict compile, промежуточный запуск | 45 passed / 5 failed / 0 skipped: [typescript-compile-precommit.log](../artifacts/audit/typescript-compile-precommit.log). | Пять angular-promise mode случаев затем исправлены; все 50 compile случаев входят в успешный gate ниже. |
| Node validator/runtime, промежуточный запуск | 66 passed / 4 failed / 0 skipped: [typescript-runtime-precommit.log](../artifacts/audit/typescript-runtime-precommit.log). | Четыре Valibot RFC3339 случая затем исправлены; полный Node/runtime входит в успешный gate ниже. |

Итоговый TypeScript gate выполнен из точного committed snapshot:
**814 passed / 0 failed / 0 skipped**,
[typescript-committed-snapshot.log](../artifacts/audit/typescript-committed-snapshot.log).
Он включает 50 strict compile случаев, все Node/runtime проверки и прежний Angular
тест с установленными реальными зависимостями. Это отдельный, более поздний запуск,
а не изменение исторических счётчиков общего precommit лога.

Опубликован commit
[e28998750cb31a0a2dbccc80307e8eecd3856562](https://github.com/valio-projects/valio.open-api/commit/e28998750cb31a0a2dbccc80307e8eecd3856562)
в origin/codex/admin-e2e-multipart. Успешный удалённый CI этим не подтверждается.

### Новая рабочая волна после опубликованного коммита

Эта волна следует за опубликованным historical baseline и не меняет первый C++ архив.
Точный r2 source SHA и clean-source provenance фиксируются в BUILD-INFO.json поставки.
Добавлены Core `IrDocumentContext`, request-local `GenerationContext.DocumentContext`
и тонкий `AstBuilderBase`; 16 новых context-тестов вошли в Core checkpoint 124/124,
а последующий formats/body gate дал 137/137.
Девять вторичных генераторов переведены на общий контекст. Общий прогон 14 test projects
дал 2134 passed / 0 failed / 0 skipped; native/Node категории исключены из этого
прогона и принимаются отдельно по соответствующим логам ниже.
Подробный статус сохранён в
[builder-context-plan.ru.md](builder-context-plan.ru.md).

Новый объём пользователя: Doxygen; injection общего HTTP-клиента; static route
constants; ответы 204/205/HEAD; структура Interfaces/IStore.h,
Clients/StoreClient.h и Clients/Impl/StoreClient.cpp; pre/post hooks, динамическая
авторизация, ограниченный повтор после 401; cancellation C++17/C#/TypeScript и
других native targets; строгие C++ scalar/date/enum/base64/collection parsers.
Отдельные владельцы выполняют C++ API/runtime/codec изменения. Перечисление
требований не означает, что все они уже реализованы или проверены.

| Новый checkpoint | Результат и граница доказательства |
| --- | --- |
| Общий gate 14 проектов | 2134 passed / 0 failed / 0 skipped: [cpp-r2-general-final.log](../artifacts/audit/cpp-r2-general-final.log). Включает Core 149, Application 80, C++ 262, C# 127, TypeScript 738, Python 130, Kotlin 109 и остальные семь проектов. Native/Node категории этим прогоном не подтверждаются. Предыдущий общий checkpoint 2097 сохранён отдельно. |
| Core context/formats/body/parameters | 149 passed / 0 failed / 0 skipped в cpp-r2-general-final.log. Добавлены 12 parameter Style/Explode cases. Предыдущие 137 context/formats/body tests приняты в [core-context-formats-body.log](../artifacts/audit/core-context-formats-body.log); Core context124 отдельно сохранён в document-context-core.log. |
| C# context/HTTP | 127 passed / 0 failed / 0 skipped, включая четыре native tracing режима: [csharp-http-context-fixed.log](../artifacts/audit/csharp-http-context-fixed.log). |
| C++ текущий unit | 262 passed / 0 failed / 0 skipped в cpp-r2-general-final.log и [targeted повторе](../artifacts/audit/cpp-r2-unit-final.log); включает последнюю form regression. Промежуточный direct unit 261 и scalar checkpoint [237/237](../artifacts/audit/cpp-scalar-unit-final.log) не заменяют эти прогоны. |
| C++ финальный основной native | 4 passed / 0 failed / 0 skipped, EXIT0, 4m12s: [cpp-r2-main-native-final.log](../artifacts/audit/cpp-r2-main-native-final.log). Повтор после enum/typed Request/query-fix принят. Windows cancellation и optional-body regressions сохранены. |
| C++ финальные scalar providers | 7 passed / 0 failed / 0 skipped, EXIT0, 5m29s: [cpp-r2-provider-native-final.log](../artifacts/audit/cpp-r2-provider-native-final.log). Семь точных combinations перечислены в scalar guide; это не весь декартов продукт providers/standards. |
| C++ HTTP/TLS object gate | Passed: [cpp-r2-tls-syntax-final.log](../artifacts/audit/cpp-r2-tls-syntax-final.log), actual OpenSSL 3.6.1, MSVC 19.51, C++17 /W4 /WX. TLS использует composition с genuine final SSLClient и public stop/progress. Это object compilation, не HTTPS handshake/certificates/TLS cancellation runtime acceptance. |
| C++ regenerated r2 preview CMake | 3/3 passed: model_consumer, codec_consumer и http_consumer; [cpp-r2-cmake-preview.log](../artifacts/audit/cpp-r2-cmake-preview.log). Отдельный models-only build/CTest: 1/1 passed, [cpp-r2-models-preview.log](../artifacts/audit/cpp-r2-models-preview.log). Повторно сгенерированный CLI sample после query-fix; package portable gate принимается отдельно. |
| TypeScript context/HTTP | Прежний checkpoint819/0/1 сохранён в typescript-document-context.log. Новый полный запуск: 853 passed / 1 failed / 0 skipped, [typescript-http-formats-harness.log](../artifacts/audit/typescript-http-formats-harness.log); единственный Ky import snapshot исправлен без production-изменения, повтор ожидается. Committed snapshot814/0/0 остаётся отдельным историческим доказательством. |
| Java native | 133 passed / 0 failed / 0 skipped: [java-native-fixed.log](../artifacts/audit/java-native-fixed.log). Client pre-abort regression исправлена. Последующая правка path segment a/b принята отдельным real native gate 1/1: [java-client-segment-native-final.log](../artifacts/audit/java-client-segment-native-final.log). |
| F# native | Составное доказательство всех 80 tests: прежние 79 passed в [fsharp-native.log](../artifacts/audit/fsharp-native.log) плюс исправленный client native 1/1 в [fsharp-client-native-fixed.log](../artifacts/audit/fsharp-client-native-fixed.log). Новый полный 80-test запуск не заявляется. |
| Python real Pydantic/httpx | Новый context/native gate130 passed / 0 failed / 0 skipped: [python-context-native-final.log](../artifacts/audit/python-context-native-final.log). Предшествующий optional-body fixed gate129/129 сохранён в python-native-fixed.log. |
| Kotlin context | Предыдущий checkpoint 105/105 сохранён в [kotlin-context.log](../artifacts/audit/kotlin-context.log). Последний общий gate принял 109 passed / 0 failed / 0 skipped в cpp-delivery-general-green.log. Новый native gate пока ожидается. |
| Application concurrency | Два физических JSON fixture и GenerationConcurrencyTests: 16 параллельных вызовов на C#/C++ с точным сравнением файлов, distinct renderer/pipeline checks. Application 80 passed / 0 failed / 0 skipped в cpp-delivery-general-green.log; это bounded C#/C++ model-only acceptance. |

Правка enum initialization принята новым unit gate: required enum без schema default
value-initialize, допустимый schema default использует типизированный enum member.
Последующие typed Request/form regressions также вошли в 262-test C++ suite.
Финальный native повтор изменённого source принят: main 4/4 и providers 7/7.

Request ordinary methods и scalar overloads имеют определения в Runtime/ValIOHttp.cpp;
C++17 optional/vector/formatter templates вынесены в подключаемый
Runtime/Impl/ValIORequest.inl. Public HTTP contract не включает JSON/cpp-httplib types.
AddQuery/AddHeader/AddFormField принимают typed values; false/zero/empty string
сохраняют presence, absent optional пропускается, nonfinite float и не представимые
wire null отклоняются. Клиенты вызывают общий runtime напрямую. Query form arrays
учитывают Explode (repeated keys либо CSV), space/pipe styles — соответствующий
separator, header simple arrays — CSV. Значения элементов кодируются отдельно от
разделителей; nested arrays и неподдержанные object/style codecs отклоняются.
Подробные границы и parser queue описаны в
[builder-context-plan.ru.md](builder-context-plan.ru.md#текущая-pre-r2-доработка-typed-request-и-wire-metadata).

Исправлена дополнительная query ошибка: cpp-httplib повторно URL-encoded structural
comma, превращая delimiter в данные. Runtime применяет pinned path codec отдельно
от query и отключает transport URL encoding через set_url_encode(false).
Literal separators внутри element кодируются отдельно от collection separators.
Исправлен newline при склейке generated source после namespace comment: все 12
ordinary overload definitions теперь видны компилятору; regenerated native sample
подтвердил link AddQuery/AddHeader/AddFormField для string/string_view/C string/bool.

Pinned cpp-httplib объявляет SSLClient final. TLS использует composition и публичные
stop/progress APIs; HTTP cancellation stream adapter остаётся отдельным механизмом.
Active TLS read cancellation может ждать upstream SSL/OS read timeout; DNS/connect
имеют отдельные ограничения. Гарантированный immediate TLS cancel не заявляется,
HTTP/TLS object compile принят; HTTPS runtime этим не подтверждён.

UUID и temporal scalar backends подключены к options, mapper и generated CMake
отдельным изменением; provider-native matrix семи конкретных cases прошла. String остаётся
default; modern-uuid использует реальный тип muuid::uuid и требует C++20,
stduuid — C++17; предусмотрен Boost.Uuid. Temporal варианты: Chrono20, Howard17,
Boost17 и ValIO17. Не представимые выбранным типом значения должны отклоняться,
а JSON/HTTP зависимости остаются вне DTO headers. Точное состояние настроек и
принятые backend cases и ограничения описаны в
[cpp-scalar-settings.ru.md](cpp-scalar-settings.ru.md).

Новая C++ consumer поставка готовится координатором в
artifacts/releases/valio-cpp-win-x64-20261011-r2. Добавлен CMake http_consumer для
shared transport, hooks, bounded 401 refresh и pre-abort; preview self-contained CLI
повторно опубликован локально после query-fix, CMake examples приняты 3/3 и models-only 1/1.
Точный source SHA, clean-source provenance и hashes новой поставки фиксируются
в её BUILD-INFO.json. Portable gate готового exact-commit пакета принимается отдельно.
Первая поставка и её hash ниже остаются неизменными.

Статический аудит renderer construction всех 12 плагинов не выявил новой общей
mutable renderer state: C#/C++ transient, Java/PHP new per pipeline,
TypeScript mutable renderers per step и stateless singleton node handlers,
остальные singleton renderers имеют constants и локальные StringBuilders.
Подробная source table находится в [builder-context-plan.ru.md](builder-context-plan.ru.md).
Это не concurrency runtime acceptance всех 12 языков; существующий bounded test
проверяет C#/C++ model-only пути.

### Реализованные границы C++ DTO, codecs и CMake targets

Пользователь требует использовать сгенерированные DTO независимо от транспорта
и сериализатора. Конкретный контракт разделения:

1. Model/enum headers содержат DTO и необходимые стандартные C++ типы, а также
   ссылки на другие DTO. Они не включают nlohmann/json, httplib, client/runtime
   или transport headers и не объявляют JSON/HTTP API внутри модели.
2. JSON codecs имеют отдельные headers с объявлениями и отдельные implementation
   .cpp. JSON-зависимости принадлежат codec-границе, а не каждому DTO consumer.
3. CMake предоставляет отдельный model target без find_package/link к JSON/HTTP;
   codec target зависит от models и JSON; client/transport target добавляет HTTP
   и необходимые codecs. Условные зависимости подключаются только для реально
   создаваемого target.
4. Model-only TU включает сгенерированные model/enum headers, создаёт DTO и
   компилируется с доступом только к STL и каталогу models/enums; внешние JSON/HTTP
   headers и библиотеки отсутствуют в его dependency closure.
5. DTO без out-of-line поведения остаются header-only. Пустой DTO.cpp для создания
   вида разделения не генерируется; model target может быть INTERFACE.
6. Отдельный compile/runtime gate проверяет codecs/client, чтобы независимость DTO
   не скрывала несовместимость serialization ADL, wire names или optional/null semantics.

В текущем diff StdModelAstBuilder строит JSON-independent header, а
CppStdSerializersStep создаёт отдельные codecHeader/codecSource для моделей и enum.
StdRuntimeTemplates.CmakeLists теперь создаёт valio_models INTERFACE,
valio_serializers и valio_clients с условными VALIO_BUILD_SERIALIZERS/VALIO_BUILD_CLIENTS;
valio_generated остался агрегирующим INTERFACE target.
find_package JSON/HTTP расположен внутри соответствующих условных ветвей.
CppStdCmakeStep учитывает enum-only output.

Добавлен tests/ValIO.Generators.Cpp.CompileTests/fixtures/model_only.cpp,
включающий только model/enum headers; отдельная граница не требует пустых DTO.cpp.
Targets/fixture проверены не только статически: первая C++ поставка прошла
native 4/4 и отдельный models-only CTest 1/1. Полный generated example прошёл
CTest 2/2. Точные toolchain, варианты и логи приведены в последнем разделе.

## Ограничения media, протоколов и возможностей IR

Следующие ограничения подтверждены исходной точкой и не должны скрываться формулировкой
«поддержаны все языки/стэки/протоколы»:

- IrBuilder.cs:1457 выбирает первый request content type; строка 1499 — первый response
  content/schema. Альтернативные media representations целиком не сохраняются.
- IrResponse не содержит media type. Поэтому кодогенератор не может восстановить
  точную политику Content-Type/Accept/декодирования из одного response type.
- В исходной точке IrParameter не сохранял style/explode/allowReserved. Текущая волна
  добавила Style/Explode и C++ query/header flat-array encodings; allowReserved,
  OpenAPI object codecs и полная сериализация всех locations остаются ограничениями.
  Microsoft.OpenApi 3.5.4 теряет distinction Swagger 2 query CSV/multi/default;
  raw parser metadata и реальные end-to-end regressions поставлены в очередь.
- Cookie-параметры не попадают в отдельную коллекцию IrOperation: switch в
  IrBuilder.cs:1209–1220 учитывает только path/query/header.
- Security requirements/schemes не представлены в IR операции; универсальная
  генерация аутентификации и авторизации этим контрактом не обеспечивается.
- Метаданные сохраняют первый server URL; полноценная семантика server variables
  и operation/path-specific servers этим аудитом не подтверждена.
- IsJson/IsForm в IrRequestBody проверяют ограниченный набор точных строк.
  Media parameters, регистр и vendor +json требуют явной политики.
- ITransportDescriptor в исходной точке не имеет реализаций и потребителей;
  его наличие не является действующей абстракцией протокола.
- IR ориентирован на HTTP/OpenAPI. WebSocket, streaming, event/RPC и другие
  протоколы нельзя объявлять проверенными на основании текущих HTTP-генераторов.

В частности, OpenAPI 3.2.0 описывает itemSchema/itemEncoding для последовательных
media types и streaming multipart. Чтение простого документа с openapi: 3.2.0
не подтверждает реализацию этих механизмов.
[OpenAPI 3.2.0, Media Type Object](https://spec.openapis.org/oas/v3.2.0.html#media-type-object).

Для расширения нужен явный контракт возможностей: что сохраняется в IR,
какой плагин способен вывести эту семантику и какой диагностикой сопровождается
неподдержанный случай. Это отдельная задача проектирования, а не повод молча
заменять неизвестный случай на any/object.

В зависимостях npm исходной точки @types/node обновлён до 26, тогда как CI запускает
Node 22. Это основание проверить совместимость доступных API, но не доказанный
runtime-дефект.

## Следующие этапы после первой C++ поставки

Этот список описывает конкретные следующие gates и открытые возможности. Он не
означает, что текущий diff реализовал всю спецификацию OpenAPI или все протоколы.
Следующие изменения не должны задним числом менять происхождение первого C++
артефакта. Текущая межъязыковая приёмка перед коммитом описана отдельным разделом выше.

| Приоритет | Действие | Проверяемый критерий завершения |
| --- | --- | --- |
| Текущая рабочая волна | Завершить C++ r2 package acceptance. | Общий gate 2134/2134 (Core 149, C++ 262), main native 4/4, providers 7/7, HTTP/TLS object и regenerated CMake 3/3 + DTO 1/1 приняты. Exact-commit portable package gate принимается отдельно; source provenance см. BUILD-INFO.json поставки. TypeScript полный native/Node повтор требует отдельного результата. |
| P0 текущей приёмки | Принять новый C++ API/runtime/codec объём и native проверки других языков. | Проверены Doxygen/папки/HTTP injection/hooks/auth replay/cancellation/no-body responses/строгие codecs; новые compiler/runtime результаты записаны отдельно по каждому языку. |
| P0 следующей приёмки | Распространить presence matrix на сгенерированные модели и валидаторы. | Собственные/унаследованные свойства покрывают required × nullable, absence/null/value и локальные ограничения; Core108 служит исходным IR-доказательством, не заменяя runtime. |
| P0 следующей приёмки | Дополнить прошедшие unit suites девяти альтернативных генераторов доступными native/runtime проверками. | Точные охваченные плагины/сценарии, passed/failed/skipped и отсутствующие toolchains записаны отдельно. |
| P0 следующей приёмки | Сделать обязательные интеграционные проверки воспроизводимыми. | Версии reader/runtime fixtures согласованы с 2.0/3.0.4/3.1.2/3.2.0; зависимости установлены; отсутствие требуемого toolchain не превращается в успех. |
| P1 | Спроектировать typed composed values/codecs для C#/C++ с явной cardinality. | Heterogeneous/nested OneOf/AnyOf имеют сохраняющую контракт representation и accept/reject regressions; до этого остаётся явная NotSupportedException. |
| P1 | Заменить broad union fallbacks в альтернативных языках по отдельным plugin slices. | Для каждой заявленной возможности есть целевой тип, codec/validator, compiler/runtime evidence и diagnostics для ещё неподдержанных случаев. allOf flatten не используется как доказательство union support. |
| P1 | Ввести сохраняемые wire capabilities вместо потери media/style/security/cookie metadata. | IR хранит необходимые media alternatives/encoding/parameter metadata; каждый выбранный transport либо реализует их, либо выдаёт точную диагностику. Нужны version-specific fixtures. |
| P1 parser slice | Сохранить Swagger 2 raw collectionFormat до нормализации reader. | Production JSON/YAML parsing сохраняет различия query CSV/multi/default для inline и referenced parameters; parser → IR → generated HTTP regressions подтверждают wire output. Regex-восстановление внутри IrBuilder не используется. |
| Очередь, plan only | Спроектировать opt-in named C++ std request containers. | Route/query/body DTO переиспользуют IR/models, сохраняют presence/wire semantics и async ownership; cancellation остаётся request control. Default output и прежние сигнатуры совместимы. Конкретный план сохранён в builder-context-plan.ru.md; реализации пока нет. |
| P1 | Отдельно определить поддерживаемый объём 3.2 streaming и иных протоколов. | Список поддержанных constructs и runtime scenarios принят явно; простое чтение openapi: 3.2.0 не считается поддержкой itemSchema/itemEncoding или WebSocket/RPC. |
| P2 | Унифицировать полные IR traversal/import collections и расширить проверки имён. | Вложенные dictionary/union/intersection references собираются без потерь; совпадения wire имён отделены от symbol conflicts; нет независимых копий graph/flatten алгоритма. |
| P2 | Дождаться готовности SocratiCode и подтвердить поиск. | Индекс именно C:\Work\valio\valio.open-api завершён, доступны ожидаемые файлы и контрольный запрос возвращает корректный путь/фрагмент. Текущие 42%, 1959/4616, batch 5/26 и persisted 1575 points ещё не являются search proof; watcher inactive, graph pending. |

## Итоговые проверки первой C++ поставки

Результаты относятся к первой локальной Windows x64 поставке C++ std, собранной
на базе e2cdf3a с локальными source изменениями на момент той сборки. Исполнитель документа прочитал
указанные лог-файлы, но сам не запускал сборки и не менял production-код.
Этот раздел фиксирует происхождение и проверки первого архива; более поздний общий
precommit и committed-snapshot gates описаны выше. Сам архив не является доказательством
приёмки более поздней context-волны или успешного удалённого CI.

Артефакт: [artifacts/releases/valio-cpp-win-x64-20261011](../artifacts/releases/valio-cpp-win-x64-20261011).
Внутри cli — self-contained win-x64 CLI на .NET 10, example — готовый пример и
build.ps1, validation — доказательства проверок, BUILD-INFO.json — версии и hashes.
Готовый архив: [valio-cpp-win-x64-20261011.zip](../artifacts/releases/valio-cpp-win-x64-20261011.zip),
43 125 253 байта. SHA256:
`f378d1c771f7c8339c3a30693c393295d9a45b9d34e09cd7be0f85abc3b951b3`;
[контрольная сумма](../artifacts/releases/valio-cpp-win-x64-20261011.zip.sha256).

| Gate | Статус и доказательство |
| --- | --- |
| История: ровно 50 коммитов, 10 merges / 40 non-merges | Проверено локально относительно e2cdf3a. |
| Повторяющиеся patch-id | Проверены две пары; значения приведены выше. |
| Инвентаризация точных имён AST/IR/Command | В исходной точке повторов не найдено в описанной выборке деклараций. |
| Core | 108 passed / 0 failed / 0 skipped: [core-cpp-release.log](../artifacts/audit/core-cpp-release.log). |
| Application | 77 passed / 0 failed / 0 skipped: [application-cpp-final.log](../artifacts/audit/application-cpp-final.log). Включает production parser matrix 4 версии × JSON/YAML, композиции и три Bootstrap сценария. |
| Production parser versions/formats | 2.0 / 3.0.4 / 3.1.2 / 3.2.0 × JSON/YAML входят в Application gate; это не полная сертификация всех constructs этих версий. |
| IrUnionKind, IrType.Constraints и presence/container IR | Входят в прошедший Core gate; runtime всех языков этим не подтверждён. |
| C++ unit | 161 passed / 0 failed / 0 skipped: [cpp-ready-unit.log](../artifacts/audit/cpp-ready-unit.log). |
| Сгенерированный C++ native | 4 passed / 0 failed / 0 skipped: [cpp-native-offline.log](../artifacts/audit/cpp-native-offline.log). Варианты перечислены ниже. |
| Windows CLI publish | Self-contained win-x64 publish выполнен: [cpp-cli-ready-publish.log](../artifacts/audit/cpp-cli-ready-publish.log). Лог содержит предупреждения; warning-clean сборка не заявляется. |
| Реальная генерация готовым CLI | 22 файла generated и 5 definition-only файлов: [cpp-ready-generation.log](../artifacts/audit/cpp-ready-generation.log). |
| Полный generated CMake/example | Сборка и CTest 2/2: [cpp-example-cmake-build.log](../artifacts/audit/cpp-example-cmake-build.log), [cpp-example-ctest.log](../artifacts/audit/cpp-example-ctest.log). |
| Models-only CMake/example | Сборка и CTest 1/1 без JSON/HTTP dependency linkage: [cpp-example-model-build.log](../artifacts/audit/cpp-example-model-build.log), [cpp-example-model-ctest.log](../artifacts/audit/cpp-example-model-ctest.log). |
| Готовый example/build.ps1 | Проверен исполнением: [cpp-example-script.log](../artifacts/audit/cpp-example-script.log). |
| Переносимость готового архива | Координатор проверил hash, распаковку и CLI/codec из пути с пробелами; CLI generation подтверждён [cpp-archive-portable.log](../artifacts/audit/cpp-archive-portable.log). |
| C#/TypeScript full gates | В первую C++ поставку не входили. Позднее C#122 прошёл общий precommit gate; TypeScript814/0failed/0skipped принят из точного committed snapshot, включая 50 strict compile и Node/runtime. Новая context-волна ожидает своего gate. |
| Девять альтернативных генераторов | В первую C++ поставку не входили. Позднее unit suites всех девяти пройдены в precommit-general-fixed.log; полное native coverage не подтверждено. |
| Общий builder/document context | В первый C++ архив не входил. Новая волна прошла общий gate 2134/2134; отдельные native/runtime результаты и их границы перечислены выше. |
| SocratiCode | Последний snapshot координатора: 42%, 1959/4616, batch 5/26, persisted 1575 points; watcher inactive, graph pending. Индекс ещё не готов; контрольный поиск не подтверждён. |
| git diff --check | Пройден по результату координатора для первой поставки; текущий release gate принимается отдельно. |
| Commit/push/удалённый CI | Historical baseline e28998750cb31a0a2dbccc80307e8eecd3856562 отправлен в origin/codex/admin-e2e-multipart. Точный r2 source SHA и clean-source provenance см. BUILD-INFO.json готовой поставки; успешный удалённый CI не заявляется. |

Native gate выполнен MSVC из Visual Studio 18 2026 Enterprise с CMake 4.4.3,
с точными зависимостями Conan httplib 0.18.3 и nlohmann_json 3.12.0:

| C++ standard | API style | Client shape | Результат |
| --- | --- | --- | --- |
| 17 | both | interface+impl | Native compile и HTTP/codec round-trip passed. |
| 20 | sync | class | Native compile и HTTP/codec round-trip passed. |
| 23 | both | interface+impl | Native compile и HTTP/codec round-trip passed. |
| 17 | async | class | Native compile и HTTP/codec round-trip passed. |

Это четыре конкретных сочетания, а не полный декартов продукт всех options.
По BUILD-INFO.json bundled httplib собран без TLS; локальный HTTP smoke не
подтверждает HTTPS/TLS. C++ UE runtime native gate этой поставкой также не заявляется.
Старые логи не заменяют итоговые: cpp-example-generation.log содержит промежуточный
неуспешный запуск, а успешное доказательство генерации — cpp-ready-generation.log.
