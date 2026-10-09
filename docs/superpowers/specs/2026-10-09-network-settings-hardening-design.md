# Сетевые настройки и усиление установки (критичные риски аудита)

**Дата:** 2026-10-09
**Статус:** на ревью

## Проблема

Аудит установленной v1.0.5 (`C:\apps\WinAdmin`, служба `WinAdmin`) выявил три критичных риска:

1. **Локальное повышение до SYSTEM.** Служба работает под `LocalSystem`, а папка приложения (и `WinAdmin.exe`) доступна на изменение группе «Прошедшие проверку» (`Authenticated Users: Modify`). Любой локальный пользователь подменяет exe/dll → код выполняется как SYSTEM при перезапуске службы.
2. **Порт открыт всей сети.** `binPath` содержит `--urls http://0.0.0.0:8080`; правило брандмауэра `WinAdmin HTTP 8080` — `RemoteIP: Any`, профили Domain+Private+Public.
3. **HTTP без TLS.** Учётные данные и токены идут открытым текстом.

Дополнительно: БД в `C:\ProgramData\WinAdmin\` читается группой «Пользователи» (хеши паролей, ключи, аудит).

## Цель

- По умолчанию панель доступна **только с этого компьютера** (127.0.0.1), без правила брандмауэра.
- Порт и режим доступа настраиваются **командами CLI** и **в веб-интерфейсе под scope `admin`**, без ручного редактирования службы.
- Папки приложения и данных защищены от записи/чтения непривилегированными пользователями — в том числе на уже установленных копиях после обновления.

Риск 3 закрывается режимом `local`: трафик через loopback не покидает машину. HTTPS для режима `network` — вне области (см. ниже).

## Область

### Входит

| Область | Что делаем |
|---|---|
| Модель и хранение | `network.json` рядом с БД; модель `NetworkSettings` |
| Применение | Kestrel `Endpoints` из `network.json` с перепривязкой на лету; откат при неудаче |
| Брандмауэр | Одно управляемое правило, только для `network`, только перечисленные подсети, без профиля Public |
| API | `GET/PUT /api/v1/settings/network` (scope `admin`), запись в аудит |
| UI | Карточка «Сеть» на странице «Настройки» |
| CLI | `WinAdmin.exe network show` / `network set` |
| ACL | `InstallationHardening` при старте службы; `icacls` в `install-service.ps1` и установщике `winadmin-ctl` |
| Установщики | Убрать `--urls` из `binPath`; `winadmin-ctl` читает/пишет порт через `network.json` |
| Тесты | Новый проект `tests/WinAdmin.Tests` (xUnit) |

### Не входит

- HTTPS/сертификаты (в режиме `network` — только предупреждение в UI).
- Отдельный лимит попыток входа и выравнивание времени ответа `/auth/login`.
- Новые экраны `winadmin-ctl`.
- Локализация (RU/EN i18n ещё не внедрена; новые строки — по-русски, как соседний код).

## Модель настроек

`WinAdmin.Core/Models/NetworkSettings.cs`:

```csharp
public enum NetworkMode { Local, Network }

public sealed record NetworkSettings(NetworkMode Mode, int Port, IReadOnlyList<string> Allow);
```

Файл `network.json` (JSON, camelCase, enum строкой):

```json
{ "mode": "local", "port": 8080, "allow": [] }
```

**Расположение:** каталог `WinAdmin:DatabasePath` (`Path.GetDirectoryName`), иначе `AppContext.BaseDirectory`. Тот же алгоритм в API и CLI — вынести в общий хелпер `WinAdminPaths` (Infrastructure), заменив дублирующийся расчёт `dbPath` в `Program.cs` и `CliRunner.cs`.

**Валидация** (`NetworkSettingsValidator`, чистая функция, возвращает список ошибок):

- `Port` ∈ [1, 65535].
- `Allow`: каждый элемент — IPv4/IPv6-адрес или CIDR (`10.77.77.0/24`, префикс в пределах семейства). Пробелы обрезаются, дубликаты удаляются.
- `Mode = Network` → `Allow` не пуст (никакого «Any»).
- `Mode = Local` → `Allow` сохраняется как есть (чтобы переключение туда-обратно не теряло список), но не применяется.

**Первичное создание.** Если `network.json` нет, при старте службы (и в CLI `network show`) создаётся файл: `Mode = Local`, `Port` = порт из текущего `urls` (ключ конфигурации, куда попадает `--urls`), иначе `8080`, `Allow = []`. Так существующая установка `--urls http://0.0.0.0:8080` автоматически переезжает на `127.0.0.1:8080`.

Битый/невалидный `network.json` при старте: лог-ошибка, используются значения по умолчанию (`Local`, 8080) — служба не должна падать и терять доступ полностью; файл не перезаписывается, чтобы админ видел, что сломано.

## Применение (Kestrel)

- `NetworkConfigurationSource` / `Provider` (Api): читает `network.json`, отдаёт ключи `Kestrel:Endpoints:Http:Url` = `http://127.0.0.1:{port}` (`Local`) или `http://0.0.0.0:{port}` (`Network`). Следит за файлом (`FileSystemWatcher` / `PhysicalFileProvider` с `ReloadOnChange`) и вызывает `OnReload()`.
- Добавляется в `builder.Configuration` **после** остальных источников. Kestrel по умолчанию загружает секцию `Kestrel` с `reloadOnChange: true` и перепривязывает изменённые endpoints без перезапуска процесса.
- При наличии `Kestrel:Endpoints` Kestrel игнорирует `--urls` (с предупреждением в логе) — поэтому старые `binPath` продолжают работать до переустановки.
- `Program.cs`: убрать `app.UseHttpsRedirection()` (HTTPS-endpoint не настраивается, редирект — no-op/вводит в заблуждение).

## Сохранение и защита от потери доступа

`NetworkSettingsService` (Infrastructure, интерфейс `INetworkSettingsService` в Core):

- `Load()` → `NetworkSettings` (с первичным созданием).
- `ValidateAsync(next, current)` → ошибки валидации + проверка доступности: если `(адрес, порт)` меняется, на целевом адресе открывается и сразу закрывается `TcpListener`; при `SocketException` — ошибка «порт занят».
- `Save(next)` → атомарная запись (`network.json.tmp` + `File.Move(overwrite)`), сохраняет предыдущую версию в памяти для отката.
- `ApplyFirewall(settings)` → см. «Брандмауэр».

**Порядок в API `PUT`:**

1. Валидация → `400 { message, errors[] }`; занят порт → `409 { message }`.
2. Ответ `200 { url }` (новый URL панели: `http://127.0.0.1:{port}` или `http://<имя-машины>:{port}`).
3. В `Response.OnCompleted`: брандмауэр → запись `network.json` → аудит `settings.network` (было → стало).
4. Фоновая проверка через 10 с (`NetworkApplyWatchdog`, hosted service): выбирает адреса из `IServerAddressesFeature` / пробное подключение `TcpClient` к новому endpoint. Если не слушаем — восстановить предыдущий `network.json` и правило брандмауэра, аудит `settings.network.rollback`, лог-ошибка.

**Аварийный путь:** `WinAdmin.exe network set ...` из консоли администратора работает всегда; запущенная служба подхватит файл сама.

## Брандмауэр

- Одно правило `WinAdmin (managed)`: Inbound, TCP, `localport={port}`, `remoteip={allow через запятую}`, `profile=domain,private`, `action=allow`.
- `Mode = Local` → правило удаляется.
- `Mode = Network` → правило удаляется и создаётся заново (идемпотентно).
- Всегда удаляются устаревшие правила `WinAdmin HTTP *` (создавались `install-service.ps1` / `winadmin-ctl`).
- Реализация: `netsh advfirewall firewall delete|add rule ...` через `Process` (без PowerShell). Построение аргументов — чистая функция `FirewallCommands.Build(settings)` → список argv, тестируется отдельно. Выполнение — `IFirewallRunner` (подменяется в тестах).
- Сверка при старте службы: `ApplyFirewall(Load())`. Ошибка (нет прав) — предупреждение в логе, старт не прерывается.

Удаление устаревших правил по шаблону: `netsh` не поддерживает wildcard, поэтому удаляются конкретные имена `WinAdmin HTTP {port}` для текущего порта и для порта из `urls`; плюс `WinAdmin (managed)`.

## API

`NetworkSettingsController` — `[Authorize(Policy = "scope:" + Scopes.Admin)]`, `[Route("api/v1/settings/network")]`, по образцу `ExcludedUsersController`.

- `GET` → `200 NetworkSettingsDto { mode, port, allow[], url, firewallRule: bool }`.
- `PUT` body `UpdateNetworkSettingsRequest { mode, port, allow[] }` → `200 { url }` | `400` | `409`.

## UI

`pages/Settings.tsx` — новая карточка «Сеть» над списком исключений (компонент `components/NetworkSettingsCard.tsx`):

- `Radio.Group`: «Только этот компьютер» / «Сеть».
- `InputNumber` порт (1–65535).
- Режим «Сеть»: `Select mode="tags"` для подсетей + `Alert type="warning"`: «Трафик не шифруется (HTTP). Используйте только в доверенной сети.»
- «Сохранить» → `Modal.confirm`: «Панель переедет на {url}. Если вы подключены не с этого компьютера, доступ может пропасть.» → `PUT` → через 1.5 с `window.location.assign(url + location.pathname)`.
- Клиент: `api.settings.network()` / `api.settings.updateNetwork(body)` в `api/client.ts`, типы в `api/types.ts`.

## CLI

`Cli/NetworkCommands.cs`, регистрируется в `CliRunner` как `network`; в `Program.cs` CLI-режим срабатывает для `args[0]` ∈ {`user`, `network`}.

```
WinAdmin.exe network show
WinAdmin.exe network set [--mode local|network] [--port N] [--allow "10.77.77.0/24,192.168.88.5"]
```

- `set` объединяет переданные опции с текущими, валидирует (включая проверку порта), применяет брандмауэр, пишет файл, печатает новый URL.
- Без прав администратора — понятная ошибка «Запустите от имени администратора» (код 1), без стектрейса.

## Права на папки

`InstallationHardening` (Infrastructure), вызывается при старте веб-режима:

- **Папка приложения** (`AppContext.BaseDirectory`): защищённый DACL (`SetAccessRuleProtection(true, false)`), правила по SID — `S-1-5-32-544` (Администраторы) и `S-1-5-18` (SYSTEM) FullControl, `S-1-5-32-545` (Пользователи) ReadAndExecute; все с наследованием `ContainerInherit | ObjectInherit`. Для вложенных файлов/папок: удалить явные ACE, включить наследование.
- **Папка данных** (каталог `network.json`/БД), если она отличается от папки приложения: то же, но **без** Пользователей.
- Если папка данных совпадает с папкой приложения (БД рядом с exe) — у Пользователей остаётся ReadAndExecute на всё; в лог предупреждение «вынесите БД в ProgramData».
- Пропускается, если процесс не администратор/SYSTEM (`WindowsPrincipal.IsInRole(Administrator)` или SID SYSTEM) — предупреждение в логе.
- Ошибки на отдельных файлах (занят и т.п.) логируются, старт не прерывается.
- Построение списка правил — чистая функция (тестируется); применение — тонкая обёртка над `DirectorySecurity`.

`releases/install-service.ps1`:

- `binPath` без `--urls`; параметр `-Port` записывает `network.json` (`mode: local`) в `$DataPath`, если файла нет.
- `icacls` на `$InstallPath` и `$DataPath` (по SID: `*S-1-5-32-544`, `*S-1-5-18`, `*S-1-5-32-545`), `/inheritance:r`.
- Не создаёт правило брандмауэра; удаляет `WinAdmin HTTP $Port`, если оно есть.
- Вывод: `Open: http://127.0.0.1:$Port`.

`winadmin-ctl` (`installer.rs`, `service.rs`, `settings.rs`):

- `bin_token` без `--urls`; порт пишется в `C:\ProgramData\WinAdmin\network.json` (сохраняя `mode`/`allow`, если файл есть).
- `ensure_firewall` удаляется; `remove_firewall` удаляет и `WinAdmin (managed)`.
- `installed_port()` читает `network.json`, затем фолбэк на разбор `--urls` из `sc qc`.
- `icacls` как в `install-service.ps1`.

## Тесты

Новый проект `tests/WinAdmin.Tests` (xUnit, `Microsoft.AspNetCore.Mvc.Testing`), добавить в `WinAdmin.slnx`.

- `NetworkSettingsValidator`: порты 0/1/65535/65536; IPv4, IPv6, CIDR, мусор, префикс вне диапазона; `Network` без `Allow`; обрезка и дубликаты.
- Построение endpoint URL для `Local`/`Network`.
- Первичное создание: порт из `urls` (`http://0.0.0.0:8080`, `http://+:9090`, отсутствует).
- `FirewallCommands.Build`: `Local` → только delete; `Network` → delete + add с нужными `remoteip`/`profile`.
- `InstallationHardening` — список правил для папки приложения и данных.
- API (`WebApplicationFactory`, временный каталог данных, фейковый `IFirewallRunner`): `GET` без входа → 401; ключ без `admin` → 403; `PUT` невалидно → 400; занятый порт → 409; валидно → 200 и после завершения ответа `network.json` обновлён.
- Rust: тест `installed_port` чтения из JSON-строки.

**Ручная проверка на этой машине** (после обновления `C:\apps\WinAdmin`, требует запуска от администратора):

1. Служба слушает `127.0.0.1:8080`, не `0.0.0.0`.
2. Правила `WinAdmin HTTP 8080` нет.
3. ACL папки приложения и `ProgramData\WinAdmin` — без «Прошедших проверку»/записи пользователей; БД не читается обычным пользователем.
4. Смена порта из UI → редирект на новый порт, старый не слушается; аудит содержит запись.
5. `WinAdmin.exe network set --port 8080` возвращает обратно.
6. `network set --mode network --allow 10.77.77.0/24` → правило `WinAdmin (managed)` с этой подсетью, профили Domain/Private; `--mode local` → правило удалено.
