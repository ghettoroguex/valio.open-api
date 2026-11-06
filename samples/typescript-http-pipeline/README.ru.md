# HTTP-клиенты TypeScript: перехватчики, JWT и отмена

Сгенерированные функции принимают настроенный экземпляр своего HTTP-транспорта
последним необязательным аргументом `client`. При его отсутствии используется
прежний импорт библиотеки или глобальный `fetch`. Типы и поведение нативной
библиотеки сохраняются: Axios interceptors, Ky/Got/OFetch hooks, Undici dispatcher,
Next.js cache/next options и обычный `AbortSignal`.

Если у операции есть `params`, порядок аргументов следующий:

| Транспорт | Вызов |
| --- | --- |
| Fetch, Axios, Ky, Next.js, SolidStart | `operation(params, options, signal, client)` |
| Got, OFetch, Undici | `operation(params, signal, client)` |
| Angular Promise | `service.operation(params, signal)` |
| Angular RxJS | `service.operation(params)`; отмена через `subscription.unsubscribe()` |

У операции без параметров аргумент `params` отсутствует. `signal` из прежних
`options` сохраняется; отдельный аргумент `signal` имеет приоритет, если задан.
Конфигурация Got/OFetch/Undici передаётся через настроенный нативный клиент.
Например, для Undici можно передать функцию `request`, замкнутую на свой dispatcher.

В `authenticated-fetch.ts` и `authenticated-axios.ts` находятся рабочие адаптеры
для JWT с динамическим чтением токена, callback до запроса и после ответа,
одним refresh и одним повтором исходного запроса после 401. Повторный 401 возвращается
вызывающему коду. Хранение токена, URL refresh, проверка его ответа и объединение
одновременных refresh принадлежат приложению и передаются через `session`.

```typescript
const controller = new AbortController();
const client = createAuthenticatedFetch("https://api.example.test", session);
const result = await getItem({ id: "42" }, undefined, controller.signal, client);
// controller.abort(reason) прерывает текущий HTTP-запрос и предотвращает повтор после отмены.
```

Fetch-адаптер клонирует исходный `Request` перед отправкой, чтобы повторить тело.
Callback после ответа должен читать `response.clone()`, если ему нужно тело ответа.
Для потоковых тел приложение обязано отдельно обеспечить возможность повтора.
Axios-адаптер подходит для обычных JSON/FormData запросов; повторяемость потоков Node
также остаётся ответственностью приложения.

Angular получает `HttpClient` через DI, поэтому JWT и перехватчики настраиваются
через стандартный `provideHttpClient(withInterceptors(...))`. Promise-методы
принимают `AbortSignal`: уже отменённый сигнал исключает подписку, активная отмена
отписывает HTTP observable, а promise отклоняется с `signal.reason`. Слушатель
отмены удаляется после успеха, ошибки или отмены. Проверка схемы ответа и обработка
204 выполняются после получения ответа.

В SolidStart экземпляр клиента создаётся и передаётся на стороне сервера. Функция
транспорта и `AbortSignal` не являются сериализуемыми аргументами RPC браузера.
Серверный вызывающий код должен удерживать эту конфигурацию в своей области выполнения.

Проверки `NativeTransportInjectionTests` выполняют Fetch и Axios против локального
HTTP-сервера, проверяют порядок callback, JWT, ограничение повтора и реальную отмену.
Angular проверяется с настоящим RxJS и контролируемой границей `HttpClient`.
