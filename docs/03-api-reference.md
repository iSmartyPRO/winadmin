# Справочник API

Базовый URL: `http(s)://<host>:<port>/api/v1`
Аутентификация: заголовок `X-API-Key: sp_<секрет>` (кроме `/health`).
Интерактивная спецификация: `/swagger`, машиночитаемая: `/swagger/v1/swagger.json`.

Коды ответов: `200` OK · `201` создано · `202` принято (фоновая операция) · `204` нет содержимого ·
`400` неверный запрос · `401` нет/неверный ключ · `403` нет нужного права · `404` не найдено ·
`409` действие не выполнено · `429` лимит запросов.

---

## Вход и домен

### `POST /auth/login` — без ключа
Тело `{ "login": "...", "password": "..." }`. Сначала локальный пользователь WinAdmin, затем учётка
домена (только по HTTPS или с loopback). Ответ `200 { accessToken, expiresIn }` + cookie `wa_refresh`.
Ошибки: `400` пароль домена не по HTTPS · `401` неверный логин или пароль · `403` нет назначений ·
`429` лимит попыток (`Retry-After`) · `503` контроллер домена недоступен.

### `POST /auth/refresh` · `POST /auth/logout` — cookie `wa_refresh`
Учётка домена при обновлении перепроверяется в каталоге.

### `GET /auth/windows` — Negotiate (Kerberos)
SSO текущим пользователем Windows. `404` — вход доменом выключен; `401` — нет Kerberos (NTLM не
принимается); иначе как `/auth/login`.

### `GET /auth/options` — без ключа
`{ "directory": true|false }` — показывать ли кнопку входа Windows.

### `GET /settings/directory` · `PUT /settings/directory` — право `platform.directory.manage`
`{ enabled, domain, server, baseDn, useLdaps }`; пустой `server` — поиск контроллера через DNS,
пустой `baseDn` — весь домен. Включено без домена — `400`.

### `POST /settings/directory/test` — право `platform.directory.manage`
Пошаговая проверка: `[{ name, ok, message }]`.

### `GET /settings/ad` · `PUT /settings/ad` — право `platform.directory.manage`
Структура каталога для модулей AD: `{ rootOu, usersOuName, hiddenOus[], writeMode: ServiceAccount|ProcessAccount, writeLogin, hasWritePassword }`.
В `PUT` пароль передаётся полем `writePassword`: не передан или `null` — не менять, `""` — удалить, иначе — заменить.
`rootOu` — DN вида `OU=…,DC=…`, иначе `400`. Аудит `settings.ad` (без пароля).

### `GET /environment?module=<id>&depth=quick|full` — право `platform.environment.check`
Запускает проверки: без `module` — платформа и включённые модули. Ответ:
`[{ moduleId, at, overall: Ok|Warning|Failed, results: [{ code, title, status: Ok|Warning|Failed|Skipped, message, fix }] }]`.

### `GET /environment/latest` — право `platform.environment.check`
Последние результаты (в том числе фоновой проверки, раз в час).

## Пользователи AD (модуль `ad-users`)

Права `ad-users.read`, `ad-users.edit`, `ad-users.move`, `ad-users.password`, `ad-users.offboard` — с областью «Проекты (OU)».
Выключенный модуль — `404`. Ошибки: `403` вне области/зоны, `404` нет пользователя, `409` не настроено
(корневая OU, группа/OU уволенных, нет OU пользователей в проекте), `422` AD отклонил запись (текст — что делегировать),
`503` контроллер домена недоступен.

| Метод | Путь | Право |
|---|---|---|
| GET | `/ad/projects` | `ad-users.read` (только проекты области) |
| GET | `/ad/users?project=&status=active\|disabled\|terminated\|all&q=` | `ad-users.read` (`terminated` — `ad-users.offboard`) |
| GET | `/ad/users/{sam}` · `/ad/users/{sam}/photo` · `/ad/users/{sam}/history` | `ad-users.read` |
| PUT | `/ad/users/{sam}/attributes` `{ attributes: { имя: значение } }` | `ad-users.edit` |
| PUT · DELETE | `/ad/users/{sam}/photo` (тело — JPEG/PNG) | `ad-users.edit` |
| POST | `/ad/users/{sam}/move` `{ projectDn }` | `ad-users.move` |
| POST | `/ad/users/{sam}/password` `{ password?, generate, mustChange }` → `{ password }` | `ad-users.password` |
| POST | `/ad/users/{sam}/deactivate` → шаги | `ad-users.offboard` |
| POST | `/ad/users/{sam}/activate` `{ projectDn }` → `{ steps, password }` | `ad-users.offboard` |

## Папки (модуль `ad-folders`)

Права `ad-folders.read`, `ad-folders.membership`, `ad-folders.create` — с областью «Проекты (OU)». Папка — путь из
описания групп `sg_*` («`A:\Путь;Full Access`» / «`;Read Only`»), проект папки — проект её групп.

| Метод | Путь | Право |
|---|---|---|
| GET | `/ad/folders?project=&q=` → `{ folders, unparsed }` | `ad-folders.read` |
| GET | `/ad/folders/user/{sam}` → папки пользователя (Full/Read) | `ad-folders.read` |
| POST | `/ad/folders/membership` `{ groupDn, member, add, removeFromOther }` → шаги | `ad-folders.membership` |
| POST | `/ad/folders/preview` `{ projectDn, path, baseName, orgCode?, createDirectory }` → имена групп и UNC | `ad-folders.create` |
| POST | `/ad/folders` (то же тело) → шаги: группа Full, группа Read, папка, права NTFS | `ad-folders.create` |
| GET | `/ad/folders/acl?path=` → `{ exists, missing, warnings, ok }` | `ad-folders.read` |
| POST | `/ad/folders/acl-fix` `{ path }` → шаги | `ad-folders.create` |

### `GET /directory/search?q=<текст>&kind=user|group` — право `platform.roles.manage`
Пользователи и группы AD для назначения ролей: `[{ sid, kind, samAccountName, displayName, upn, enabled }]`.
`503` — контроллер домена недоступен.

---

## Система

### `GET /system` — право `system.read`
Сводная информация о машине.

```json
{
  "hostname": "WS-01", "domain": "CORP", "osName": "Windows 11 Pro",
  "osVersion": "10.0.26200", "osArchitecture": "64-bit",
  "manufacturer": "Dell Inc.", "model": "Latitude", "biosVersion": "1.2.3",
  "cpuName": "Intel Core Ultra 7", "cpuPhysicalCores": 16, "cpuLogicalCores": 22,
  "totalMemoryBytes": 16730009600, "lastBootTime": "2026-06-23T08:00:00+06:00",
  "uptime": "10:02:33", "networkAdapters": [
    { "name": "Ethernet", "macAddress": "AA:BB:...", "ipAddresses": ["192.168.1.5"], "isUp": true, "speedBitsPerSec": 1000000000 }
  ]
}
```

### `GET /system/metrics` — право `system.read`
Мгновенные метрики.

```json
{ "cpuUsagePercent": 7.6, "memoryTotalBytes": 16730009600, "memoryUsedBytes": 14500000000,
  "memoryUsagePercent": 86.8, "networkBytesSentPerSec": 92600, "networkBytesReceivedPerSec": 19000,
  "timestamp": "2026-06-23T12:00:00Z" }
```

## Диски

### `GET /disks` — право `system.read`
Физические диски с томами.

```json
[ { "model": "SSSTC CL4-8D1024", "interfaceType": "SCSI", "mediaType": "SSD",
    "sizeBytes": 1024209543168, "partitions": 3,
    "volumes": [ { "drive": "C:", "label": "iDisk", "fileSystem": "NTFS",
      "driveType": "Fixed", "totalBytes": 1023117619200, "freeBytes": 565251514368,
      "usedBytes": 457866104832, "usedPercent": 44.8 } ] } ]
```

## Службы

### `GET /services` — право `services.read`
```json
[ { "name": "Spooler", "displayName": "Диспетчер печати", "status": "Running",
    "startType": "Auto", "canStop": true, "canPauseAndContinue": false, "account": "LocalSystem" } ]
```

### `POST /services/{name}/{action}` — право `services.manage`
`action` ∈ `start` | `stop` | `restart`. Ответ: `OperationResult` (`200` или `409`).

```json
{ "success": true, "message": "Служба 'Spooler' перезапущена" }
```

## Процессы

### `GET /processes` — право `processes.read`
```json
[ { "pid": 1234, "name": "chrome", "mainWindowTitle": "...", "workingSetBytes": 650000000,
    "threadCount": 42, "startTime": "...", "hasWindow": true } ]
```

### `DELETE /processes/{pid}` — право `processes.manage`
Завершает процесс (с дочерним деревом). Ответ: `OperationResult`.

## Принтеры

### `GET /printers` — право `printers.read`
```json
[ { "name": "HP M428", "portName": "192.168.1.50", "driverName": "HP ...",
    "isDefault": true, "isShared": false, "workOffline": false, "status": "Idle", "queuedJobs": 0 } ]
```

### `POST /printers/{name}/{action}` — право `printers.manage`
`action` ∈ `pause` | `resume` | `purge`. Ответ: `OperationResult`.

## Питание

### `POST /power/reboot` · `POST /power/shutdown` — право `power.manage`
Тело (опционально):

```json
{ "delaySeconds": 30, "comment": "Плановое обслуживание", "force": false }
```

### `POST /power/cancel` — право `power.manage`
Отменяет запланированное действие. Ответ: `OperationResult`.

## Software

Установленные приложения (реестр Uninstall + Microsoft Store) и обновления Windows.
Идентификаторы имеют **префиксы**: `reg:` (реестр), `store:` (Microsoft Store), `upd:` (KB).
В path-параметрах `{id}` значение должно быть **URL-encoded**
(например `reg:Google.Chrome` → `reg%3AGoogle.Chrome`).

Деструктивные операции (uninstall / rollback) выполняются **фоновыми jobs** с polling
статуса. Jobs хранятся **в памяти процесса** — после перезапуска WinAdmin теряются;
завершённые jobs удаляются по TTL (~1 ч). Job в статусе `Running` дольше **30 минут**
автоматически переводится в `Failed`. Одновременно допускается **не более одной**
активной операции (`Queued`/`Running`); повторный старт → `409`:

```json
{ "message": "Уже выполняется операция удаления", "activeJobId": "a1b2c3" }
```

### `GET /software/applications` — право `software.read`
Список установленных приложений.

```json
[ { "id": "reg:Google.Chrome", "name": "Google Chrome", "version": "126.0.6478.127",
    "publisher": "Google LLC", "installDate": "2025-03-15T00:00:00",
    "installLocation": "C:\\Program Files\\Google\\Chrome\\Application",
    "sizeBytes": 524288000, "source": "Registry", "isSystem": false,
    "canUninstall": true, "uninstallString": "..." },
  { "id": "store:Microsoft.WindowsTerminal_8wekyb3d8bbwe!App", "name": "Terminal",
    "version": "1.20.11281.0", "publisher": "Microsoft Corporation", "installDate": null,
    "installLocation": null, "sizeBytes": null, "source": "Store", "isSystem": false,
    "canUninstall": true, "uninstallString": null } ]
```

`source`: `Registry` | `Store`. Поле `isSystem` — системные/фреймворковые пакеты
(по умолчанию скрываются в UI). `canUninstall` — доступно ли удаление.

### `POST /software/applications/{id}/uninstall` — право `software.manage`
Запускает удаление приложения. `{id}` — prefixed id (`reg:…` или `store:…`), URL-encoded.
Ответ `202` + `SoftwareJob`. Неизвестный id → `404`; нельзя удалить → `400`;
активный job уже есть → `409` (см. тело выше).

### `GET /software/updates` — право `software.read`
Список установленных обновлений Windows.

```json
[ { "id": "upd:KB5039893", "kbArticle": "KB5039893", "title": "2024-06 Cumulative Update",
    "description": "...", "installedOn": "2024-06-12T00:00:00",
    "canUninstall": true, "canRollback": false } ]
```

### `POST /software/updates/{id}/uninstall` — право `software.manage`
Удаление обновления. `{id}` — prefixed id (`upd:KB…`), URL-encoded.
Ответ `202` + `SoftwareJob`. Ошибки — как у applications.

### `POST /software/updates/{id}/rollback` — право `software.manage`
Откат обновления (только если `canRollback=true`). `{id}` — prefixed id (`upd:KB…`), URL-encoded.
Ответ `202` + `SoftwareJob`.

### `GET /software/jobs/{jobId}` — право `software.manage`
Текущий статус job (polling ~1 с из UI).

```json
{ "id": "a1b2c3", "type": "UninstallApp", "targetId": "reg:Google.Chrome",
  "targetName": "Google Chrome", "status": "Running", "progressPercent": 45,
  "statusMessage": "Удаление…", "error": null,
  "startedAt": "2026-07-12T10:00:00Z", "finishedAt": null }
```

`type`: `UninstallApp` | `UninstallUpdate` | `RollbackUpdate`.
`status`: `Queued` | `Running` | `Succeeded` | `Failed`.
`progressPercent` — `0`–`100` или `null` (неопределённый прогресс).
Job не найден → `404`.

### `GET /software/jobs/active` — право `software.manage`
Текущий активный job (`Queued`/`Running`) или `204 No Content`, если операций нет.
Используется UI для восстановления drawer после перезагрузки страницы.

## Администрирование

### `GET /apikeys` · `GET /apikeys/scopes` · `POST /apikeys` · `DELETE /apikeys/{id}` — роль «Администратор» или соответствующее право `platform.*`
Создание:

```json
// Запрос
{ "name": "monitoring", "scopes": ["system.read","disks.read"], "expiresAt": "2027-01-01T00:00:00Z" }
// Ответ 201
{ "key": { "id": "...", "name": "monitoring", "scopes": [...], "hint": "Ab12" },
  "plaintextKey": "sp_..." }   // показывается один раз
```

### `GET /audit?limit=200&actor=<имя>` — роль «Администратор» или соответствующее право `platform.*`
Записи журнала аудита (по убыванию времени).

## Служебное

### `GET /health` — без ключа
```json
{ "status": "ok", "machine": "WS-01", "time": "2026-06-23T12:00:00Z" }
```
