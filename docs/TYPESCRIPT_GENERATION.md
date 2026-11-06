# TypeScript Code Generation

ValIO поддерживает генерацию TypeScript клиентов, моделей и валидационных схем из OpenAPI спецификаций.

## Возможности

### Форматы моделей

- **Interfaces** - TypeScript интерфейсы (по умолчанию)
- **ValidationSchemas** - Zod/Yup/Valli схемы для валидации

### HTTP клиенты

- **Fetch** - стандартный JavaScript Fetch API (по умолчанию)
- **Axios** - библиотека Axios для HTTP запросов

### Query менеджеры

- **TanStack Query** - React Query для управления состоянием запросов
- Поддержка `useQuery` для GET запросов
- Поддержка `useMutation` для POST/PUT/DELETE запросов

### Валидационные библиотеки

- **Zod** - схема-валидатор с TypeScript типами
- **Yup** - (планируется)
- **Valli** - (планируется)

### Сериализация и десериализация

При использовании валидационных схем (Zod/Yup/Valli) с TanStack Query:
- **Сериализация** - валидация и преобразование данных перед отправкой запроса
- **Десериализация** - валидация и преобразование данных после получения ответа (например, преобразование строк в даты)

## Конфигурация

### Базовый пример

```json
{
  "language": "TypeScript",
  "outputPath": "./generated",
  "generateEnums": true,
  "generateModels": true,
  "generateClients": true,
  "options": {
    "typescript": {
      "modelFormat": "Interfaces",
      "httpClientLibrary": "Fetch",
      "useTanStackQuery": false
    }
  }
}
```

### Полный пример с Zod и TanStack Query

```json
{
  "language": "TypeScript",
  "outputPath": "./generated",
  "generateEnums": true,
  "generateModels": true,
  "generateClients": true,
  "options": {
    "typescript": {
      "modelFormat": "ValidationSchemas",
      "validationLibrary": "Zod",
      "httpClientLibrary": "Axios",
      "useTanStackQuery": true,
      "serializeWithSchemas": true,
      "deserializeWithSchemas": true,
      "enumFormat": "Enum",
      "useStrictNullChecks": true
    }
  },
  "fileOrganization": {
    "groupingStrategy": "ByTag",
    "separateModels": true,
    "separateClients": true,
    "modelsPath": "models",
    "clientsPath": "api"
  },
  "namingConvention": {
    "models": "PascalCase",
    "properties": "CamelCase",
    "methods": "CamelCase",
    "enums": "PascalCase"
  }
}
```

## Опции конфигурации

### TypeScript опции

| Опция | Тип | По умолчанию | Описание |
|-------|-----|--------------|----------|
| `modelFormat` | `"Interfaces"` \| `"ValidationSchemas"` | `"Interfaces"` | Формат генерации моделей |
| `validationLibrary` | `"Zod"` \| `"Yup"` \| `"Valli"` \| `null` | `null` | Библиотека валидации (требуется если `modelFormat = "ValidationSchemas"`) |
| `httpClientLibrary` | `"Fetch"` \| `"Axios"` | `"Fetch"` | HTTP клиент для запросов |
| `useTanStackQuery` | `boolean` | `false` | Использовать TanStack Query для управления запросами |
| `serializeWithSchemas` | `boolean` | `true` | Валидировать и сериализовать данные через схемы перед отправкой |
| `deserializeWithSchemas` | `boolean` | `true` | Валидировать и десериализовать данные через схемы после получения |
| `enumFormat` | `"Enum"` \| `"UnionType"` | `"Enum"` | Формат генерации enum'ов |
| `useStrictNullChecks` | `boolean` | `true` | Использовать strict null checks в TypeScript |

## Примеры использования

### Генерация интерфейсов с Fetch

```json
{
  "language": "TypeScript",
  "outputPath": "./src/generated",
  "options": {
    "typescript": {
      "modelFormat": "Interfaces",
      "httpClientLibrary": "Fetch"
    }
  }
}
```

**Результат:**
- `models/user.model.ts` - интерфейс `User`
- `api/users.client.ts` - клиент с методами для работы с пользователями

### Генерация Zod схем с Axios

```json
{
  "language": "TypeScript",
  "outputPath": "./src/generated",
  "options": {
    "typescript": {
      "modelFormat": "ValidationSchemas",
      "validationLibrary": "Zod",
      "httpClientLibrary": "Axios"
    }
  }
}
```

**Результат:**
- `models/user.schema.ts` - Zod схема `UserSchema`
- `api/users.client.ts` - Axios клиент

### Генерация с TanStack Query

```json
{
  "language": "TypeScript",
  "outputPath": "./src/generated",
  "options": {
    "typescript": {
      "modelFormat": "Interfaces",
      "httpClientLibrary": "Axios",
      "useTanStackQuery": true
    }
  }
}
```

**Результат:**
- `models/user.model.ts` - интерфейс `User`
- `api/users.client.ts` - Axios клиент
- `api/users.queries.ts` - TanStack Query хуки (`useGetUsers`, `useCreateUser`, и т.д.)

### Полная интеграция: Zod + Axios + TanStack Query

```json
{
  "language": "TypeScript",
  "outputPath": "./src/generated",
  "options": {
    "typescript": {
      "modelFormat": "ValidationSchemas",
      "validationLibrary": "Zod",
      "httpClientLibrary": "Axios",
      "useTanStackQuery": true,
      "serializeWithSchemas": true,
      "deserializeWithSchemas": true
    }
  }
}
```

**Результат:**
- `models/user.schema.ts` - Zod схема с валидацией
- `api/users.client.ts` - Axios клиент
- `api/users.queries.ts` - TanStack Query хуки с автоматической валидацией и преобразованием типов

## Использование в коде

### С интерфейсами и Fetch

```typescript
import { UsersClient } from './api/users.client';
import type { User } from './models/user.model';

const client = new UsersClient('https://api.example.com');

// Получение пользователей
const users: User[] = await client.getUsers();

// Создание пользователя
const newUser: User = await client.createUser({
  name: 'John Doe',
  email: 'john@example.com'
});
```

### С Zod схемами и Axios

```typescript
import { UsersClient } from './api/users.client';
import { UserSchema } from './models/user.schema';
import { z } from 'zod';

const client = new UsersClient('https://api.example.com');

// Валидация перед отправкой
const userData = UserSchema.parse({
  name: 'John Doe',
  email: 'john@example.com'
});

const newUser = await client.createUser(userData);
```

### С TanStack Query

```typescript
import { useGetUsers, useCreateUser } from './api/users.queries';
import type { User } from './models/user.model';

function UsersList() {
  const { data: users, isLoading } = useGetUsers();
  const createUserMutation = useCreateUser();

  const handleCreate = () => {
    createUserMutation.mutate({
      name: 'John Doe',
      email: 'john@example.com'
    });
  };

  if (isLoading) return <div>Loading...</div>;

  return (
    <div>
      {users?.map(user => <div key={user.id}>{user.name}</div>)}
      <button onClick={handleCreate}>Create User</button>
    </div>
  );
}
```

### С Zod + TanStack Query (автоматическая валидация)

```typescript
import { useGetUsers, useCreateUser } from './api/users.queries';
import type { User } from './models/user.model';

function UsersList() {
  // useGetUsers автоматически валидирует ответ через UserSchema
  // и преобразует типы (например, строки в даты)
  const { data: users, isLoading } = useGetUsers();
  
  // useCreateUser автоматически валидирует входные данные
  // и преобразует их перед отправкой
  const createUserMutation = useCreateUser();

  const handleCreate = () => {
    // Данные автоматически валидируются через UserSchema
    createUserMutation.mutate({
      name: 'John Doe',
      email: 'john@example.com'
    });
  };

  // users уже валидированы и имеют правильные типы
  return (
    <div>
      {users?.map(user => (
        <div key={user.id}>
          {user.name} - {user.createdAt.toLocaleDateString()}
        </div>
      ))}
    </div>
  );
}
```

## Организация файлов

### Отдельные папки для моделей и клиентов

```json
{
  "fileOrganization": {
    "separateModels": true,
    "separateClients": true,
    "modelsPath": "models",
    "clientsPath": "api"
  }
}
```

**Структура:**
```
generated/
├── models/
│   ├── user.model.ts
│   └── product.model.ts
└── api/
    ├── users.client.ts
    └── products.client.ts
```

### Все в одной папке

```json
{
  "fileOrganization": {
    "separateModels": false,
    "separateClients": false
  }
}
```

**Структура:**
```
generated/
├── user.model.ts
├── product.model.ts
├── users.client.ts
└── products.client.ts
```

## Enum форматы

### TypeScript enum

```json
{
  "options": {
    "typescript": {
      "enumFormat": "Enum"
    }
  }
}
```

**Результат:**
```typescript
export enum Status {
  Active = "active",
  Inactive = "inactive"
}
```

### Union type

```json
{
  "options": {
    "typescript": {
      "enumFormat": "UnionType"
    }
  }
}
```

**Результат:**
```typescript
export type Status = "active" | "inactive";
```

## Зависимости

### Для Fetch клиентов

Никаких дополнительных зависимостей не требуется.

### Для Axios клиентов

```bash
npm install axios
```

### Для TanStack Query

```bash
npm install @tanstack/react-query
```

### Для Zod схем

```bash
npm install zod
```

## Ограничения и известные проблемы

1. **Циклические зависимости**: ValIO автоматически обнаруживает и обрабатывает циклические ссылки в схемах
2. **AllOf/OneOf**: В текущей реализации используется первый тип из списка. Полная поддержка intersection/union типов планируется
3. **Форматы дат**: При использовании Zod схем с `deserializeWithSchemas`, строки автоматически преобразуются в Date объекты согласно схеме

## Примеры конфигураций

См. файлы:
- `valio.config.typescript.example.json` - полный пример конфигурации
- `valio.config.example.json` - пример для C#

### Explicit default values

Set `options.generateDefaultValues` to `true` to export `{Model}DefaultValues` and `{Operation}RouteParamsDefaultValues` / `{Operation}QueryParamsDefaultValues` constants. Only explicitly specified non-null OpenAPI defaults are included; requiredness and nullability of generated types do not change. The option defaults to `false`. Validation constraint constants are generated independently when constraints exist.
