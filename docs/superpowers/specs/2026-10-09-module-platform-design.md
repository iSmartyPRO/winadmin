# Платформа модулей и прав (часть 1 из 4: WinAdmin + Active Directory)

**Дата:** 2026-10-09
**Статус:** на ревью
**Основа:** ветка `feat/network-settings-hardening` (сетевые настройки, ACL, `network.json`)

## Контекст и цель

WinAdmin должен заменить портал Access (`access.gescons.ru`, ASP.NET 4.7.1 на IIS, PostgreSQL) и при этом стать модульным: каждый раздел включается/выключается, настраивается и делегируется конкретным людям с ограничением области.

Развёртывание: WinAdmin как служба Windows на **сервере в домене**, пользователи (администраторы, отдел кадров) заходят **из LAN/VPN** доменными учётками.

### Декомпозиция (каждая часть — своя спецификация, план, реализация)

| # | Часть | Зависит от |
|---|---|---|
| **1** | **Платформа модулей и прав** (этот документ): модули, роли с областью, делегирование, вход через AD + Kerberos, SQLite/PostgreSQL, шифрование секретов | — |
| 2 | HTTPS для режима «Сеть» (сертификат, привязка, редирект) | 1 |
| 3 | Модуль Active Directory: перенос функций Access (пользователи, группы `sg_*`/папки, рассылки, проекты, увольнение/восстановление, фото, пароль, перенос, скрытые OU, OU уволенных, Exchange) | 1, 2 |
| 4 | Импорт данных из PostgreSQL Access и вывод Access из эксплуатации | 3 |

## Решения, принятые на этапе обсуждения

- Модули **встроены в сборку** и включаются в рантайме (не плагины-DLL, не отдельные приложения).
- Права — `модуль.действие`; роль = набор прав + **область** (для AD — OU); делегирование с запретом повышения прав.
- Вход: форма (локальные учётки и учётки домена) + **SSO Kerberos** («Войти как текущий пользователь Windows»).
- Хранилище: **SQLite или PostgreSQL** (настраивается); критичные данные (пароли, ключи) **шифруются**.

## Область этой части

### Входит

| Область | Что делаем |
|---|---|
| Модули | Контракт `IWinAdminModule`, реестр, включение/выключение, настройки по схеме, требования к машине, фильтр выключенных модулей |
| Существующие разделы | Становятся модулями: `system` (дашборд, диски), `services`, `processes`, `printers`, `power`, `eventlogs`, `software` |
| Права | Определения прав в модулях, роли, области, назначения (локальный пользователь, пользователь AD, группа AD, API-ключ), расчёт итоговых прав, кэш |
| Делегирование | `platform.roles.manage` только в пределах собственных прав; встроенная роль «Администратор»; защита от потери доступа |
| Каталог | Подключение к домену в ядре (чтение учёткой компьютера), поиск пользователей/групп AD |
| Вход | AD по паролю, Kerberos (Negotiate), локальные учётки, лимит попыток, одинаковое время ответа |
| Хранилище | Провайдер SQLite/PostgreSQL, `database.json`, миграции для обоих |
| Секреты | `ISecretProtector` (AES-256-GCM, ключ под DPAPI машины), `[Secret]` в настройках, постоянный JWT-секрет, экспорт/импорт ключа |
| Перенос | Scopes → права, существующие пользователи и API-ключи → роли при первом запуске |
| UI | «Администрирование»: Модули, Роли, Назначения; меню из `/me`; страница входа с кнопкой SSO |

### Не входит

- HTTPS (часть 2). **Пока HTTPS нет, вход учёткой домена по паролю разрешён только при обращении через loopback** (127.0.0.1/::1) — иначе доменный пароль пошёл бы по сети открытым текстом. Kerberos (SSO) и локальные учётки работают и по сети.
- Модуль AD и его права (часть 3) — здесь только контракт области и подключение к домену для входа.
- Импорт из Access (часть 4).
- Плагины-DLL, несколько серверов под одной панелью.

## 1. Модули

### Контракт (`WinAdmin.Core/Modules`)

```csharp
public interface IWinAdminModule
{
    string Id { get; }                         // латиница, kebab: "services", "ad"
    string Title { get; }                      // «Службы»
    string? Description { get; }
    ModuleRequirements Requirements { get; }   // флаги: WindowsServer, DomainJoined
    bool EnabledByDefault { get; }
    IReadOnlyList<PermissionDefinition> Permissions { get; }
    Type? SettingsType { get; }                // POCO, свойства с [Secret] шифруются
    IScopeProvider? Scope { get; }             // null — модуль без областей
    void ConfigureServices(IServiceCollection services, IConfiguration configuration);
}

public sealed record PermissionDefinition(string Id, string Title, string? Description, bool Scopable, bool Dangerous);

[Flags] public enum ModuleRequirements { None = 0, WindowsServer = 1, DomainJoined = 2 }

public interface IScopeProvider
{
    // Нормализует и проверяет описание области (для AD — список DN OU), бросает при ошибке.
    ScopeDefinition Normalize(ScopeDefinition scope);
    // Входит ли a в b (для проверки делегирования).
    bool IsSubsetOf(ScopeDefinition a, ScopeDefinition b);
}

public sealed record ScopeDefinition(IReadOnlyList<string> Items); // пустой список запрещён; «без области» = null
```

- `Id` права обязан начинаться с `"{moduleId}."`; проверяется при старте (ошибка конфигурации → служба не стартует, это ошибка разработчика).
- Модули регистрируются в `Program.cs` списком; в рантайме набор фиксирован.

### Реестр (`IModuleRegistry`)

- Таблица `modules`: `id` (PK), `enabled` (bool), `settings_json` (text), `updated_at`, `updated_by`.
- Записи нет → `EnabledByDefault`.
- `GetState(id)` → `{ Enabled, Available, UnavailableReason, Settings }`.
- `Available = false`, если не выполнены `Requirements` (Windows Server — WMI `ProductType ∈ {2,3}`; домен — `Win32_ComputerSystem.PartOfDomain`). Недоступный модуль нельзя включить; если он был включён, считается выключенным.
- Настройки: сериализация `SettingsType`; свойства с `[Secret]` хранятся как `enc:v1:…`; API отдаёт для них `{ "isSet": true }`, а пустое значение при сохранении означает «не менять».
- Изменения пишутся в аудит (`module.enable`, `module.disable`, `module.settings.update`), без значений секретов.

### Применение

- Контроллеры модуля помечаются `[WinAdminModule("services")]`. Глобальный фильтр: модуль выключен или недоступен → **404** (не раскрываем, что функция есть).
- `ConfigureServices` вызывается для **всех** модулей при старте (включение не требует перезапуска).
- Ядро (`platform`) не является отключаемым модулем: вход, пользователи, роли, назначения, API-ключи, аудит, модули, сеть, подключение к домену.

### Существующие разделы → модули

| Модуль | Разделы | Права |
|---|---|---|
| `system` | Дашборд, Диски | `system.read` |
| `services` | Службы | `services.read`, `services.manage` |
| `processes` | Процессы | `processes.read`, `processes.manage` |
| `printers` | Принтеры | `printers.read`, `printers.manage` |
| `power` | Питание | `power.manage` (Dangerous) |
| `eventlogs` | Журналы Windows | `eventlogs.read` |
| `software` | Приложения, Обновления | `software.read`, `software.manage` |

`disks.read` объединяется с `system.read` (раздел «Диски» — часть модуля `system`). Перенос назначений — см. §7.

Права ядра: `platform.users.manage`, `platform.roles.manage`, `platform.modules.manage`, `platform.apikeys.manage`, `platform.audit.read`, `platform.network.manage`, `platform.directory.manage`.

## 2. Роли, области, назначения

### Данные

- `roles`: `id`, `name` (уникально), `description`, `is_builtin`, `created_at`.
- `role_permissions`: `role_id`, `permission_id`, `scope_json` (null — без области). Область задаётся **на право внутри роли**; редактор UI задаёт её на модуль и применяет ко всем областным правам этого модуля в роли.
- `role_assignments`: `id`, `role_id`, `principal_type` (`LocalUser` | `AdUser` | `AdGroup` | `ApiKey`), `principal_id` (id локального пользователя / **SID** для AD / id ключа), `display_name` (снимок для UI), `created_at`, `created_by`.

### Итоговые права

Для субъекта собираются все назначения: его самого и (для AD) всех групп из `tokenGroups`. Итог — словарь `permissionId → Scope`, где `Scope` = `Unrestricted` или объединение `ScopeDefinition` всех ролей, дающих это право. Если хотя бы одна роль даёт право без области — `Unrestricted`.

```csharp
public interface IAccessContext
{
    string Actor { get; }
    bool Has(string permissionId);
    EffectiveScope ScopeFor(string permissionId); // Unrestricted | Items | None
}
```

- Атрибут `[RequirePermission("services.manage")]` → 403, если права нет (с любой областью).
- Областные проверки — внутри модуля через `IAccessContext.ScopeFor`.
- Неизвестные права (модуль удалён из сборки) игнорируются при расчёте.

### Кэш

- Итоговые права — `IMemoryCache` по субъекту, TTL 60 с; любое изменение ролей/назначений/модулей сбрасывает весь кэш прав.
- Группы AD субъекта — перечитываются не реже раза в 5 минут (`tokenGroups` через подключение к домену); ошибка чтения → используются последние известные группы до 15 минут, затем вход считается недействительным (401).

### Делегирование (защита от повышения прав)

Субъект с `platform.roles.manage` может создавать, менять, удалять роли и назначения, **только если** каждое право роли есть у него самого, а область каждого права — подмножество его области (`IScopeProvider.IsSubsetOf`; `Unrestricted` ⊇ всё). Иначе — 403 с перечнем лишних прав.

### Защита от потери доступа

- Встроенная роль «Администратор»: все права (включая будущие модули — вычисляется, а не хранится), без областей; нельзя удалить, переименовать, изменить состав.
- Нельзя удалить последнее назначение роли «Администратор» на **активного** субъекта (409).
- Аварийный путь: `WinAdmin.exe user add --login x --password … --role Администратор` и `WinAdmin.exe role assign --role Администратор --local x`.

## 3. Подключение к домену и вход

### Подключение к домену (ядро, `platform.directory.manage`)

Настройки в таблице `platform_settings` (ключ `directory`):

| Поле | По умолчанию |
|---|---|
| `Enabled` | `true`, если машина в домене |
| `Domain` | из `Win32_ComputerSystem.Domain` |
| `Server` | пусто → поиск контроллера через DNS (DC Locator) |
| `BaseDn` | `defaultNamingContext` из RootDSE |
| `UseLdaps` | `false` (Kerberos Sign & Seal на 389), `true` → 636 |

Чтение каталога — **учёткой компьютера** (служба работает как SYSTEM, `AuthenticationTypes.Secure | Sealing | Signing`); пароль не хранится. Служебная учётка для записи — настройка модуля AD (часть 3).

`IDirectoryService` (Infrastructure, `System.DirectoryServices.Protocols`):
- `FindUser(string login)` → `{ Sid, SamAccountName, DisplayName, Upn, Enabled }`
- `GetTokenGroups(Sid)` → SID всех групп (транзитивно)
- `Search(string query, PrincipalKind kind, int limit)` → для назначения ролей
- `ValidateCredentials(login, password)` → bind (только с подписью/шифрованием или LDAPS)
- `TestConnection()` → пошаговая диагностика для UI

### Вход

`POST /api/v1/auth/login { login, password }`:

1. Если `login` совпадает с локальным пользователем WinAdmin → проверка его пароля (как сейчас).
2. Иначе, если подключение к домену включено → **только при loopback-запросе или HTTPS** (иначе 400 «Вход учёткой домена по паролю доступен только по HTTPS или с этого компьютера; используйте вход Windows»): `ValidateCredentials` → `FindUser` → `tokenGroups` → итоговые права.
3. Нет ни одного назначения (ни на пользователя, ни на его группы) → 403 «Нет доступа к WinAdmin».
4. Выдаётся JWT (`sub` = `local:<id>` | `ad:<SID>`, `name`), права в токен **не** кладутся — считаются на сервере.

`GET /api/v1/auth/windows` — схема Negotiate (`Microsoft.AspNetCore.Authentication.Negotiate`, Kerberos; NTLM не принимается). Результат — тот же JWT, шаги 3–4. Работает по HTTP и HTTPS.

Лимит: `/auth/login` — 5 неудачных попыток в минуту на пару (IP, логин) и 20 на IP; превышение → 429 с `Retry-After`. Для несуществующего логина выполняется хеширование-заглушка, чтобы время ответа не отличалось.

Аудит: `auth.login` / `auth.login.failed` (без пароля), `auth.windows`.

### Существующие механизмы

- Локальные пользователи и refresh-токены — без изменений схемы, кроме удаления поля `Scopes` (переносится в роли, §7).
- API-ключи: поле scopes заменяется назначениями ролей (`principal_type = ApiKey`).

## 4. Хранилище и секреты

### Провайдер

- `ProgramData\WinAdmin\database.json` (рядом с `network.json`, права — только Администраторы/SYSTEM):
  ```json
  { "provider": "sqlite", "connectionString": "Data Source=C:\\ProgramData\\WinAdmin\\WinAdmin.db" }
  ```
  `provider`: `sqlite` | `postgresql`. Пароль в строке PostgreSQL хранится как `Password=enc:v1:…`.
- Нет файла → SQLite по `WinAdmin:DatabasePath` (обратная совместимость), файл создаётся.
- `WinAdmin.exe db show`, `db set --provider postgresql --connection "Host=…;Username=…;Password=…"` (шифрует пароль, проверяет подключение, применяет миграции), `db set --provider sqlite --path …`.
- Один `WinAdminDbContext`; миграции в двух сборках: `WinAdmin.Infrastructure.Migrations.Sqlite` (существующие миграции переезжают сюда) и `WinAdmin.Infrastructure.Migrations.PostgreSql`. Смена провайдера **не переносит данные** (это вне области; при смене CLI предупреждает).

### Секреты

- `ISecretProtector`: `Protect(string) → "enc:v1:" + base64(nonce12 | ciphertext | tag16)`, `Unprotect(string)`; AES-256-GCM, ассоциированные данные — назначение секрета (`"module:ad:ServicePassword"`), чтобы зашифрованное значение нельзя было подставить в другое поле.
- Ключ: 32 случайных байта, `ProgramData\WinAdmin\keys\master.key`, содержимое защищено `ProtectedData.Protect(..., DataProtectionScope.LocalMachine)`, ACL — только Администраторы/SYSTEM (как в `InstallationHardening`). Создаётся при первом запуске.
- Нет ключа, а в БД есть `enc:` значения → секреты считаются незаданными, в лог — ошибка, UI показывает «ключ шифрования не найден».
- `WinAdmin.exe keys export --file key.bin --password …` / `keys import` — для переноса на другой сервер (PostgreSQL) и восстановления. Экспорт — ключ, зашифрованный паролем (PBKDF2-SHA256 600k + AES-GCM).
- JWT-секрет: если `WinAdmin:Jwt:Secret` не задан, генерируется один раз и хранится зашифрованным в `platform_settings` — сессии переживают перезапуск.
- Аудит и логи не содержат секретов и паролей пользователей ни в каком виде.

## 5. API

| Метод | Путь | Право | Ответ / тело |
|---|---|---|---|
| GET | `/api/v1/me` | вход | `{ actor, displayName, permissions: { id: scope|null }, modules: [{ id, title, menu }] }` |
| GET | `/api/v1/modules` | `platform.modules.manage` | все модули: состояние, доступность, причина, права, схема настроек |
| PUT | `/api/v1/modules/{id}` | `platform.modules.manage` | `{ enabled, settings }` |
| GET/POST/PUT/DELETE | `/api/v1/roles[/{id}]` | `platform.roles.manage` (+ делегирование) | |
| GET/POST/DELETE | `/api/v1/role-assignments[/{id}]` | `platform.roles.manage` (+ делегирование) | |
| GET | `/api/v1/permissions` | `platform.roles.manage` | права по модулям, с флагами |
| GET | `/api/v1/directory/search?q=&kind=` | `platform.roles.manage` | пользователи/группы AD |
| GET/PUT | `/api/v1/settings/directory`, POST `/test` | `platform.directory.manage` | |
| POST | `/api/v1/auth/login` | — | §3 |
| GET | `/api/v1/auth/windows` | Negotiate | §3 |

Существующие эндпоинты переходят с `scope:*` политик на `[RequirePermission]` с теми же именами прав (кроме `disks.read` → `system.read`).

## 6. Интерфейс

- Меню строится из `/me`: показываются включённые модули и только пункты с правом. Маршрут без права → страница «Нет доступа» (не пустая).
- «Администрирование»:
  - **Модули** — карточки: переключатель, статус «недоступен: <причина>», форма настроек (генерируется по схеме: строки, числа, флаги, списки, секреты с «задано/изменить»).
  - **Роли** — список; редактор: права деревом по модулям (опасные помечены), для модулей с областью — редактор области от модуля (в этой части — общий редактор списка строк; OU-пикер появится в части 3).
  - **Назначения** — роль ← субъект: локальный пользователь (список) или поиск в AD (пользователь/группа).
  - **Подключение к домену** — настройки + кнопка «Проверить» с пошаговым результатом.
- Страница входа: форма + «Войти как текущий пользователь Windows» (если подключение к домену включено).
- Существующие «Пользователи» и «API-ключи»: вместо списка scopes — назначенные роли.

## 7. Перенос существующих данных (первый запуск новой версии)

Миграция выполняется в транзакции при старте, идемпотентно (флаг в `platform_settings`):

1. Создаётся роль «Администратор» (`is_builtin`).
2. Пользователи и API-ключи со scope `admin` → назначение «Администратор».
3. Остальные: для каждого уникального набора scopes создаётся роль «Импорт: <scopes через запятую>» с соответствующими правами (`disks.read` → `system.read`), назначается.
4. Колонки scopes удаляются следующей миграцией схемы.
5. Если после переноса нет ни одного назначения «Администратор» — в лог предупреждение и инструкция CLI.

На этой машине: `admin`, `iadmin`, `iadmin1` (scope `admin`) → «Администратор».

## 8. Обработка ошибок

- Контроллер домена недоступен: вход доменом → 503 «Контроллер домена недоступен», локальные учётки работают; для уже вошедших — §2 «Кэш».
- Нет ключа шифрования / повреждён: старт не прерывается, секреты незаданы, ошибка в логе и в «Модули».
- PostgreSQL недоступен при старте: служба не стартует (без БД нет прав), ошибка в журнале событий Windows; `WinAdmin.exe db set --provider sqlite` — аварийный путь.
- Неверная схема настроек модуля в БД (после обновления): поля, которые не удалось прочитать, сбрасываются на значения по умолчанию, предупреждение в лог.

## 9. Порядок реализации

Часть 1 реализуется тремя последовательными планами, каждый даёт работающую систему:

| План | Содержание | Результат |
|---|---|---|
| 1a | Секреты (§4 «Секреты») + провайдер SQLite/PostgreSQL (§4 «Провайдер») | Постоянный JWT-секрет, выбор БД, CLI `db`/`keys` |
| 1b | Модули (§1), роли/области/назначения/делегирование (§2), перенос scopes (§7), API (§5 без directory/auth), UI «Модули/Роли/Назначения» (§6) | Существующие разделы — модули, права через роли для локальных пользователей и ключей |
| 1c | Подключение к домену и вход (§3), поиск AD в назначениях, SSO, лимит входа | Вход доменных учёток и групп AD |

## 10. Тесты

Существующий проект `src/tests/WinAdmin.Tests` (xUnit, Moq, `WebApplicationFactory`).

- Реестр: состояние по умолчанию, включение/выключение, недоступность по требованиям, 404 у выключенного модуля, секретные поля не возвращаются.
- Итоговые права: объединение областей, `Unrestricted` перекрывает, права через группу AD, неизвестные права игнорируются, сброс кэша.
- Делегирование: нельзя выдать право, которого нет; нельзя расширить область; можно выдать подмножество.
- Потеря доступа: нельзя удалить «Администратор» и последнее назначение.
- Вход: локальный; доменный через фейковый `IDirectoryService`; 403 без назначений; запрет пароля домена не по loopback без HTTPS; лимит 429; одинаковое время ответа (порядок величины).
- Секреты: protect/unprotect, неверные ассоциированные данные, отсутствие ключа, экспорт/импорт с паролем.
- Хранилище: миграции SQLite; PostgreSQL — если доступен по переменной `WINADMIN_TEST_POSTGRES`, иначе тесты пропускаются (`Skip`).
- Перенос scopes → роли на копии БД с пользователями `admin` и смешанными scopes.
- Ручная проверка на сервере в домене: вход доменной учёткой с loopback, SSO из браузера в зоне «Интрасеть», назначение роли группе AD.
