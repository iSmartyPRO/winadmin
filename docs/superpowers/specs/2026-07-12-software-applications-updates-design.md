# Software: Applications и Updates — дизайн

**Дата:** 2026-07-12  
**Статус:** утверждено

## Цель

Добавить в WinAdmin раздел **Software** для просмотра установленных программ и
обновлений Windows с возможностью удаления (и отката обновлений, где ОС это
поддерживает), с отображением хода выполнения в боковой панели.

## Решения (зафиксированы)

| Вопрос | Выбор |
|---|---|
| Навигация | Пункт меню Software с подпунктами Applications и Updates (как Журналы Windows) |
| Источник приложений | Реестр Uninstall + Microsoft Store / AppX |
| Прогресс операций | Drawer справа, список остаётся видимым |
| Updates | Список + uninstall + rollback, где поддерживается |
| Системные приложения | Скрыты по умолчанию; переключатель «Показать системные» |
| Долгие операции | Фоновые jobs + polling REST (без SignalR) |

## Область действия (v1)

**Входит:**
- Список установленных приложений (Registry + Store) с ключевыми полями
- Удаление приложений, у которых есть рабочий uninstall
- Список установленных обновлений Windows
- Удаление и откат обновлений при поддержке ОС
- Job API + UI drawer с прогрессом/статусом
- Scopes `software.read` / `software.manage`, аудит manage-действий

**Не входит:**
- Установка ПО или поиск/установка доступных (ещё не установленных) обновлений
- SignalR / WebSocket для прогресса
- Автоматическая перезагрузка после uninstall
- Персистентное хранилище jobs (только in-memory на время жизни процесса)
- Экспорт списков

## Меню и маршруты

| Пункт меню | Route | Компонент |
|---|---|---|
| Software → Applications | `/software/apps` | `Applications.tsx` |
| Software → Updates | `/software/updates` | `Updates.tsx` |

Иконка родителя: `CodeOutlined` (не `AppstoreOutlined` — он уже у «Процессы»).

## Scopes

Добавить в `WinAdmin.Core.Security.Scopes` и в UI создания ключей/пользователей:

- `software.read` — списки applications/updates
- `software.manage` — uninstall / rollback и чтение статуса jobs

`admin` проходит любую scope-policy (см. `ScopeAuthorization`) — отдельно
включать `software.*` в admin не нужно; достаточно добавить scopes в `Scopes.All`
для валидации ключей/пользователей.

## Бэкенд

### Модели — `WinAdmin.Core/Models/SoftwareModels.cs`

```csharp
public sealed record InstalledApp(
    string Id,
    string Name,
    string? Version,
    string? Publisher,
    DateTime? InstallDate,
    string? InstallLocation,
    long? SizeBytes,
    string Source,          // "Registry" | "Store"
    bool IsSystem,
    bool CanUninstall,
    string? UninstallString);

public sealed record InstalledUpdate(
    string Id,
    string? KbArticle,
    string Title,
    string? Description,
    DateTime? InstalledOn,
    bool CanUninstall,
    bool CanRollback);

public enum SoftwareJobType { UninstallApp, UninstallUpdate, RollbackUpdate }
public enum SoftwareJobStatus { Queued, Running, Succeeded, Failed }

public sealed record SoftwareJob(
    string Id,
    SoftwareJobType Type,
    string TargetId,
    string TargetName,
    SoftwareJobStatus Status,
    int? ProgressPercent,   // 0–100 или null = indeterminate
    string StatusMessage,
    string? Error,
    DateTime StartedAt,
    DateTime? FinishedAt);
```

### Абстракции

```csharp
public interface ISoftwareCatalogService
{
    IReadOnlyList<InstalledApp> GetApplications();
    IReadOnlyList<InstalledUpdate> GetUpdates();
}

public interface ISoftwareJobService
{
    SoftwareJob StartUninstallApp(string appId);
    SoftwareJob StartUninstallUpdate(string updateId);
    SoftwareJob StartRollbackUpdate(string updateId);
    SoftwareJob? GetJob(string jobId);
    SoftwareJob? GetActiveJob(); // текущий Running/Queued или null
}
```

Реализации в `WinAdmin.Infrastructure/Software/`.

### Каталог приложений

1. **Registry:** `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall`,
   `HKLM\SOFTWARE\WOW6432Node\...`, `HKCU\...\Uninstall`. Поля DisplayName,
   DisplayVersion, Publisher, InstallDate, InstallLocation, EstimatedSize,
   UninstallString, QuietUninstallString, SystemComponent, WindowsInstaller и т.п.
2. **Store / AppX:** enumeration установленных пакетов (PackageManager / PowerShell
   Get-AppxPackage-эквивалент через WinRT/API, доступный серверному процессу).

**IsSystem:** `SystemComponent=1`, известные Microsoft системные пакеты / framework
AppX, пакеты без пользовательского uninstall — помечать `IsSystem=true`. Точные
эвристики зафиксировать в реализации и покрыть unit-тестами.

**CanUninstall:** есть QuietUninstallString или UninstallString (с тихим флагом где
возможно) для Registry; для Store — пакет допускает remove и не является
provisioned system package, который нельзя снять.

**Id:** стабильный ключ (registry subkey name или PackageFullName). В URL пути
передавать через `Uri.EscapeDataString` / decode на контроллере (PackageFullName
содержит точки и подчёркивания; спецсимволы реестра тоже встречаются).

### Каталог обновлений

Источник: установленные hotfix/updates (WMI `Win32_QuickFixEngineering` и/или
CBS / DISM-совместимый перечень). Для каждого:

- `CanUninstall` — Windows позволяет uninstall (наличие uninstall-пакета / флаг)
- `CanRollback` — отдельный флаг только если API/ОС явно поддерживает rollback
  для данного пакета; иначе `false` и кнопка скрыта. Если под капотом rollback =
  тот же uninstall path — оба действия всё равно остаются отдельными API-вызовами
  с разным аудитом; `CanRollback` выставляется только когда семантика отката
  имеет смысл для пакета.

### Jobs

- In-memory store (singleton), TTL очистки завершённых jobs (например 1 час).
- Не более **одной** активной деструктивной операции (`Queued`/`Running`) за раз:
  вторая попытка → исключение / `409 Conflict` с ссылкой на активный job.
- Фоновый `Task` / `IHostedService`-совместимый запуск: quiet uninstall процесса
  (msiexec / UninstallString с `/quiet`/`/S` где применимо; для Store —
  `RemovePackageAsync`; для updates — `wusa /uninstall` или эквивалент CBS).
- Прогресс: по возможности парсить stdout/exit; иначе indeterminate + текстовые
  этапы («Запуск…», «Удаление…», «Завершение…»).
- Таймаут операции: **30 минут** → `Failed`.
- Reboot required: job `Succeeded`, `StatusMessage` указывает на необходимость
  перезагрузки; авто-reboot не выполняется.
- После рестарта WinAdmin jobs теряются (документировать).

### API — `SoftwareController` (`/api/v1/software`)

| Method | Path | Scope | Ответ |
|---|---|---|---|
| GET | `/applications` | software.read | `InstalledApp[]` |
| POST | `/applications/{id}/uninstall` | software.manage | `202` + `SoftwareJob` |
| GET | `/updates` | software.read | `InstalledUpdate[]` |
| POST | `/updates/{id}/uninstall` | software.manage | `202` + `SoftwareJob` |
| POST | `/updates/{id}/rollback` | software.manage | `202` + `SoftwareJob` |
| GET | `/jobs/{jobId}` | software.manage | `SoftwareJob` |
| GET | `/jobs/active` | software.manage | `SoftwareJob` или `204` |

Ошибки:

- Неизвестный id → `404`
- Нельзя удалить / нет rollback → `400` с понятным `message`
- Уже есть активный job → `409` + тело/заголовок с `jobId` активного
- Job не найден → `404`

### Аудит

Одна запись **при завершении** job (Succeeded или Failed), с `Success`/`Message`
как у Services/Power. При старте отдельный audit не пишем — иначе дубли и
незавершённые операции засоряют лог.

- `software.app.uninstall`
- `software.update.uninstall`
- `software.update.rollback`

Target: отображаемое имя; в details/message — id.

## Frontend

### Applications

AG Grid (как Services): Name, Version, Publisher, InstallDate, Size, Source, Actions.

- `Input.Search` + Switch «Показать системные» (`false` по умолчанию) — фильтр
  клиентский по `isSystem`
- Кнопка Удалить только при `canUninstall`; `Popconfirm`
- После старта — открыть Operation Drawer, polling `GET /jobs/{id}` ~1 с

### Updates

AG Grid: KB, Title, InstalledOn, Actions (Удалить / Откатить).

- Откатить только при `canRollback`
- Тот же drawer + polling

### Operation Drawer

Общий компонент (например `SoftwareOperationDrawer`):

- Target name + тип операции
- Progress (percent или indeterminate)
- Живой `statusMessage`
- Итог Success / Failed + error
- Пока `Running`/`Queued` — предупреждение при закрытии; допускается «Свернуть»
  с продолжением polling (индикатор на странице, что операция идёт)
- По `Succeeded` — refresh списка
- Восстановление: при монтировании страницы вызвать `GET /jobs/active` и при
  наличии job снова открыть drawer

Язык UI: как в текущем приложении (русские строки), пока i18n не внедрён глобально.

## Тестирование

- Unit: эвристики `IsSystem` / `CanUninstall` на фикстурах registry/AppX метаданных
- Unit: job state machine; отказ второго concurrent start
- Unit: контроллер — 409 при активном job, 404 на неизвестный id (по стилю существующих тестов)
- Ручная проверка на Windows: список apps/updates, uninstall тестового пакета,
  drawer прогресс, reboot-required сообщение (если удастся воспроизвести)

## Документация

- Обновить `docs/03-api-reference.md` (endpoints + scopes)
- Упомянуть Software в overview при необходимости
- README feature-list: одна строка про Software

## Вне рамок (явно)

- Установка приложений и Windows Updates
- Параллельные uninstall jobs
- Persistent job history в SQLite
- SignalR
- Авто-reboot
)