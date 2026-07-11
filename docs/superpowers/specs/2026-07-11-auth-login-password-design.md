# Дизайн: Авторизация по логину/паролю + CLI управления пользователями

**Дата:** 2026-07-11  
**Проект:** WinAdmin v1.0.0  
**Статус:** Утверждён

---

## Контекст

WinAdmin использует аутентификацию по X-API-Key для всего — веб-интерфейса и REST API.
Цель: добавить логин/пароль для веб-интерфейса, не ломая существующий API-key механизм.
CLI нужен для управления пользователями без запущенного сервера (начальная настройка, скрипты).

---

## Решения

| Вопрос | Решение |
|---|---|
| Область логин/пароля | Только веб-интерфейс; REST API (X-API-Key) не меняется |
| Размещение CLI | Команды в основном `WinAdmin.Api.exe` |
| Права пользователей | Те же scopes что у API-ключей |
| Время сессии | 30 дней с автообновлением (JWT + Refresh Token) |
| Механизм | JWT (1ч) + Refresh Token в httpOnly cookie (30д) |

---

## Архитектура

### Два параллельных authentication scheme

```
HTTP Request
  ├── X-API-Key header  →  ApiKeyAuthenticationHandler (существующий, не меняется)
  └── Authorization: Bearer <jwt>  →  JwtBearerHandler (новый)
```

Контроллеры принимают обе схемы через `[Authorize(AuthenticationSchemes = "ApiKey,Bearer")]`.
Контроллер `/auth` — анонимный (`[AllowAnonymous]`).

---

## Backend

### Новые таблицы БД

```sql
Users
  Id          TEXT PRIMARY KEY  -- GUID
  Login       TEXT NOT NULL UNIQUE
  PasswordHash TEXT NOT NULL    -- ASP.NET Core PasswordHasher<T>
  Scopes      TEXT NOT NULL     -- "admin,system.read,..." (через запятую)
  CreatedAt   INTEGER NOT NULL
  IsActive    INTEGER NOT NULL DEFAULT 1

RefreshTokens
  Id          TEXT PRIMARY KEY  -- GUID
  UserId      TEXT NOT NULL REFERENCES Users(Id)
  TokenHash   TEXT NOT NULL     -- SHA256 от raw token
  ExpiresAt   INTEGER NOT NULL
  RevokedAt   INTEGER           -- NULL = активен
  CreatedAt   INTEGER NOT NULL
```

### Новые файлы backend

```
WinAdmin.Core/
  Models/
    UserModels.cs          -- UserDto, CreateUserRequest, LoginRequest, TokenResponse
  Abstractions/
    IUserService.cs        -- интерфейс сервиса пользователей
    ITokenService.cs       -- интерфейс генерации/валидации JWT

WinAdmin.Infrastructure/
  Security/
    UserService.cs         -- CRUD пользователей + валидация пароля
    TokenService.cs        -- генерация JWT, управление refresh tokens
  Storage/
    Migrations/            -- новая миграция EF Core

WinAdmin.Api/
  Controllers/
    AuthController.cs      -- POST login, refresh, logout; GET me
    UsersController.cs     -- CRUD пользователей (scope: admin)
```

### Эндпоинты

```
POST /api/v1/auth/login
  Body: { "login": "admin", "password": "Pa$$word" }
  Response 200: { "accessToken": "eyJ...", "expiresIn": 3600 }
  Cookie: wa_refresh=<token>; HttpOnly; SameSite=Strict; Max-Age=2592000

POST /api/v1/auth/refresh
  Cookie: wa_refresh=<token>
  Response 200: { "accessToken": "eyJ...", "expiresIn": 3600 }

POST /api/v1/auth/logout
  Cookie: wa_refresh=<token>
  Response 204 — отзывает refresh token в БД, очищает cookie

GET /api/v1/auth/me
  Header: Authorization: Bearer <jwt>
  Response 200: { "login": "admin", "scopes": ["admin"] }

GET    /api/v1/users          -- scope: admin
POST   /api/v1/users          -- scope: admin
PUT    /api/v1/users/{id}/scopes   -- scope: admin
PUT    /api/v1/users/{id}/password -- scope: admin
PUT    /api/v1/users/{id}/active   -- scope: admin
DELETE /api/v1/users/{id}     -- scope: admin
```

### JWT конфигурация

```json
// appsettings.json
{
  "WinAdmin": {
    "Jwt": {
      "Secret": "",          // генерируется при первом запуске если пусто
      "AccessTokenMinutes": 60,
      "RefreshTokenDays": 30,
      "Issuer": "WinAdmin",
      "Audience": "WinAdmin"
    }
  }
}
```

JWT Secret генерируется автоматически при первом запуске, логируется один раз с инструкцией сохранить его в переменную окружения `WinAdmin__Jwt__Secret`. Без явной установки — при перезапуске генерируется новый (все сессии сбрасываются). Для продакшена рекомендуется задать явно.

### Хеширование паролей

`Microsoft.AspNetCore.Identity.PasswordHasher<T>` из пакета `Microsoft.Extensions.Identity.Core` — лёгкий пакет без полного ASP.NET Core Identity.

---

## Frontend

### Новые файлы

```
src/frontend/src/
  auth/
    LoginForm.tsx       -- форма логин + пароль (заменяет KeyGate)
    AuthProvider.tsx    -- React context: user, scopes, logout, refresh
  api/
    authApi.ts          -- login(), refresh(), logout(), me()
  pages/
    Users.tsx           -- страница управления пользователями (scope: admin)
```

### Поток аутентификации

```
1. Пользователь вводит логин + пароль → POST /auth/login
2. Сервер возвращает accessToken (тело) + refreshToken (httpOnly cookie)
3. accessToken сохраняется в sessionStorage
4. Axios interceptor добавляет Authorization: Bearer <token> к каждому запросу
5. При 401 → axios interceptor вызывает POST /auth/refresh автоматически
6. Успех → повторяет исходный запрос; провал → редирект на LoginForm
```

### Страница /cp/users (только для scope: admin)

- AG Grid таблица: логин, scopes, дата создания, статус (активен/нет)
- Кнопка «Создать»: модальное окно с полями логин, пароль, scopes (те же чекбоксы что у API-ключей)
- Inline-действия: изменить scopes, сменить пароль, деактивировать/активировать, удалить

### Меню сайдбара

Пункт **Пользователи** (иконка `TeamOutlined`) добавляется в секцию управления, виден только если в JWT есть scope `admin`.

---

## CLI

### Реализация

`System.CommandLine` (NuGet: `System.CommandLine`). При наличии аргументов — CLI режим (только конфиг + БД, без Kestrel). Без аргументов — обычный веб-сервер.

```csharp
// Program.cs — точка входа
if (args.Length > 0 && args[0] == "user")
    return await CliRunner.RunAsync(args, configuration);
// иначе — запуск веб-сервера как обычно
```

### Команды

```bash
# Список пользователей
WinAdmin.Api.exe user list

# Создать пользователя
WinAdmin.Api.exe user add --login admin --password "Pa$$word" --scopes admin
WinAdmin.Api.exe user add --login viewer --password "Pa$$word" --scopes system.read,disks.read

# Сменить пароль
WinAdmin.Api.exe user password admin "NewPa$$word"

# Изменить scopes
WinAdmin.Api.exe user scopes viewer system.read,disks.read,services.read

# Деактивировать / активировать
WinAdmin.Api.exe user deactivate admin
WinAdmin.Api.exe user activate admin

# Удалить
WinAdmin.Api.exe user delete admin
```

### Вывод `user list`

```
LOGIN       SCOPES                       CREATED      ACTIVE
─────────────────────────────────────────────────────────────
admin       admin                        2026-07-11   yes
viewer      system.read,disks.read       2026-07-11   yes
```

---

## Новые файлы CLI

```
WinAdmin.Api/
  Cli/
    CliRunner.cs         -- точка входа CLI, регистрирует команды
    UserCommands.cs      -- add, list, password, scopes, deactivate, activate, delete
```

---

## Миграция и первый запуск

1. При старте сервера — EF Core миграция создаёт таблицы `Users` и `RefreshTokens`
2. Если таблица `Users` пуста — сервер логирует подсказку: `Создайте первого пользователя: WinAdmin.Api.exe user add --login admin --password ... --scopes admin`
3. Старый bootstrap API-key механизм остаётся без изменений

---

## Безопасность

| Аспект | Решение |
|---|---|
| Пароли | PasswordHasher (PBKDF2, 10000 итераций) |
| JWT Secret | Авто-генерация при первом запуске (256 бит) |
| Refresh Token | SHA256 хеш в БД; raw token только в httpOnly cookie |
| XSS защита | accessToken в sessionStorage (не localStorage); refresh в httpOnly cookie |
| CSRF | SameSite=Strict на refresh cookie |
| Brute force | Существующий Rate Limiter (120 req/min per IP) покрывает /auth/login |

---

## Затронутые файлы

### Новые
- `src/backend/WinAdmin.Core/Models/UserModels.cs`
- `src/backend/WinAdmin.Core/Abstractions/IUserService.cs`
- `src/backend/WinAdmin.Core/Abstractions/ITokenService.cs`
- `src/backend/WinAdmin.Infrastructure/Security/UserService.cs`
- `src/backend/WinAdmin.Infrastructure/Security/TokenService.cs`
- `src/backend/WinAdmin.Infrastructure/Storage/Migrations/<timestamp>_AddUsers.cs`
- `src/backend/WinAdmin.Api/Controllers/AuthController.cs`
- `src/backend/WinAdmin.Api/Controllers/UsersController.cs`
- `src/backend/WinAdmin.Api/Cli/CliRunner.cs`
- `src/backend/WinAdmin.Api/Cli/UserCommands.cs`
- `src/frontend/src/api/authApi.ts`
- `src/frontend/src/auth/LoginForm.tsx`
- `src/frontend/src/auth/AuthProvider.tsx`
- `src/frontend/src/pages/Users.tsx`

### Изменяемые
- `src/backend/WinAdmin.Infrastructure/Storage/WinAdminDbContext.cs` — добавить DbSet<User>, DbSet<RefreshToken>
- `src/backend/WinAdmin.Infrastructure/DependencyInjection.cs` — регистрация UserService, TokenService
- `src/backend/WinAdmin.Api/Program.cs` — JWT scheme, CLI entry point, подсказка при пустой таблице Users
- `src/backend/WinAdmin.Api/appsettings.json` — секция Jwt
- `src/backend/WinAdmin.Api/Controllers/*Controller.cs` — добавить Bearer scheme к [Authorize]
- `src/frontend/src/auth/KeyGate.tsx` — заменить на LoginForm
- `src/frontend/src/components/AppLayout.tsx` — пункт меню Пользователи
- `src/frontend/src/api/client.ts` — axios interceptor для refresh
