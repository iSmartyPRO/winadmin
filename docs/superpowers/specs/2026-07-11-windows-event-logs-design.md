# Журналы Windows (Event Logs) — дизайн

Дата: 2026-07-11

## Цель

Добавить в WinAdmin раздел для просмотра и фильтрации журналов событий Windows
(Event Log), чтобы не открывать `eventvwr.msc` на самой машине. Готовые
пресеты для частых сценариев (авторизация, система, приложения, PowerShell,
установка ПО) плюс отдельная опция для произвольного журнала.

## Область действия

Только просмотр и фильтрация. Без экспорта, без очистки журналов (может быть
добавлено отдельным дизайном позже).

## Меню и пресеты

Новый пункт меню **«Журналы Windows»** (иконка `FileTextOutlined`) с
подпунктами. Каждый подпункт открывает один и тот же компонент просмотра с
разной предустановкой:

| Пункт меню | LogName (Windows) | Предфильтр Event ID | Комментарий |
|---|---|---|---|
| Авторизация | `Security` | 4624,4625,4634,4647,4648,4672,4720,4722,4725,4726,4738,4767,4776 | вход/выход, неудачные попытки, изменения учётных записей |
| Security (все события) | `Security` | — | полный журнал безопасности без фильтра по ID |
| Система | `System` | — | драйверы, службы, загрузки/перезагрузки |
| Приложения | `Application` | — | ошибки/предупреждения приложений, включая .NET/IIS |
| PowerShell | `Microsoft-Windows-PowerShell/Operational` | — | выполненные команды/скрипты |
| Установка ПО | `Setup` | — | установка/удаление программ и обновлений Windows |
| — divider — | | | |
| Произвольный журнал | выбирается пользователем | пользователь вводит вручную | список всех журналов машины через API |

Пресеты хранятся как статическая конфигурация во фронтенде
(`src/config/eventLogPresets.ts`): `{ key, label, logName, eventIds?, description }`.
Бэкенду список пресетов не нужен — он получает параметры запроса напрямую.

## Бэкенд

### Модели — `WinAdmin.Core/Models/EventLogModels.cs`

```csharp
public sealed record EventLogEntryDto(
    long Id, DateTime TimeCreated, string LogName, string? ProviderName,
    int EventId, string? Level, string? LevelDisplayName,
    string? User, string? Message, string? MachineName);

public sealed record EventLogQueryRequest(
    string LogName, DateTime StartTime, DateTime EndTime,
    int MaxRecords, IReadOnlyList<int>? EventIds,
    IReadOnlyList<string>? Levels, string? Keyword, string? User);

public sealed record EventLogQueryResult(
    IReadOnlyList<EventLogEntryDto> Entries, bool Truncated, int ScannedCount);
```

`User` — необязательная подстрока имени учётной записи (регистронезависимо,
`Contains`), например `"ivan"` найдёт `ivan.petrov`, `Ivan.Sidorov` и т.д.
Отдельный параметр от `Keyword`, потому что одна запись Security-журнала
часто содержит несколько «имён» (учётная запись, инициировавшая действие, и
целевая учётная запись, над которой совершено действие, плюс имя домена,
имя компьютера) — общий полнотекстовый поиск по всему сообщению цеплял бы
случайные совпадения в этих полях. `User` фильтрует по конкретному
извлечённому имени учётной записи (см. ниже), а не по всему тексту.

`MaxRecords`: по умолчанию 200 на фронтенде, жёсткий cap на бэкенде — 5000
(защита от чрезмерных запросов даже если фронт пришлёт больше).

### Абстракция — `WinAdmin.Core/Abstractions/ISystemServices.cs`

```csharp
public interface IEventLogService
{
    IReadOnlyList<string> GetLogNames();
    EventLogQueryResult Query(EventLogQueryRequest request);
}
```

### Реализация — `WinAdmin.Infrastructure/EventLogs/EventLogService.cs`

Использует `System.Diagnostics.Eventing.Reader` (`EventLogSession`,
`EventLogQuery`, `EventLogReader`):

- `GetLogNames()` → `EventLogSession.GlobalSession.GetLogNames()`, сортировка
  по имени.
- `Query(...)`:
  - Строит XPath-фильтр из `TimeCreated` (диапазон), `EventID` (если задан) и
    `Level` — фильтрация происходит на стороне Windows, минимизируя объём
    прочитанных данных.
  - `Keyword` (полнотекстовый поиск по отформатированному сообщению) XPath не
    поддерживает — фильтруется в коде уже после `FormatDescription()` каждой
    записи.
  - `User` — тоже фильтруется в коде, но не по всему сообщению, а по имени
    учётной записи, извлечённому отдельной функцией `ExtractUserName(EventRecord)`:
    парсит `record.ToXml()` и ищет первое непустое (и не `"-"`) значение среди
    полей `EventData`, в приоритете `TargetUserName` → `SubjectUserName` →
    `AccountName`; если ни одного из этих полей нет — fallback на
    `record.UserId`, транслированный в имя учётной записи через
    `SecurityIdentifier.Translate(typeof(NTAccount))`. Та же функция
    используется для заполнения поля `User` в `EventLogEntryDto`, которое
    отображается в таблице — так фильтр и колонка всегда согласованы
    (фильтруем по тому же значению, что показываем).
  - Защитный лимит: сканирование обрывается после ~5000 просмотренных
    записей независимо от того, сколько подошло под `Keyword`/`User`-фильтры
    (поле `ScannedCount` в ответе), чтобы широкий диапазон дат с таким
    поиском не перегружал систему.
  - Останавливается, как только набрано `MaxRecords` подходящих записей;
    `Truncated = true`, если лимит был достигнут раньше, чем закончился
    диапазон.
  - Обрабатывает исключения: `UnauthorizedAccessException` /
    `EventLogNotFoundException` → понятная ошибка на русском («нет доступа к
    журналу Security — требуются права администратора», «журнал не найден»).

### Контроллер — `WinAdmin.Api/Controllers/EventLogsController.cs`

- `GET /api/v1/eventlogs/lognames` — `[Authorize(Policy = "scope:" + Scopes.EventLogsRead)]`
- `GET /api/v1/eventlogs/query?logName=&start=&end=&maxRecords=&eventIds=&levels=&keyword=&user=` —
  тот же scope.

### Scope

`Scopes.cs`: добавить `EventLogsRead = "eventlogs.read"` в список `All`.

### DI

`DependencyInjection.cs`: `services.AddScoped<IEventLogService, EventLogService>();`

## Фронтенд

### Конфигурация пресетов — `src/config/eventLogPresets.ts`

Статический массив как описано выше в разделе «Меню и пресеты».

### Страница — `src/pages/EventLogs.tsx`

Один компонент для всех пресетов и для произвольного журнала. Определяется
параметром маршрута:

- `/logs/:presetKey` — пресет из конфигурации задаёт `logName` и
  (опционально) `eventIds`.
- `/logs/custom` — пользователь сам выбирает журнал.

**Панель фильтров:**
- Быстрые диапазоны: 24ч / 7д / 30д / произвольно (`DatePicker.RangePicker`
  для последнего варианта).
- Лимит записей: `Select` — 200 / 500 / 1000 / 5000.
- Уровень: мультиселект — Error / Warning / Information / Audit Success /
  Audit Failure.
- Текстовый поиск по сообщению (`Input.Search`).
- Пользователь: текстовое поле для поиска по подстроке имени учётной записи
  (`Input.Search`, placeholder «часть имени, например ivan»); фильтрует по
  извлечённому имени (см. бэкенд), не по всему тексту сообщения. Показывается
  для всех пресетов — для журналов без полей учётной записи (например
  `System`) просто не даст совпадений сверх fallback на SID-имя.
- Event ID: текстовое поле с числами через запятую — для пресетов
  предзаполнено значением из конфигурации, но редактируемо; для
  «Security (все события)» и «Произвольный журнал» — пусто.
- Только для `/logs/custom`: `Select` с поиском по списку всех журналов
  машины (`api.eventLogs.logNames()`).

**По умолчанию при первом открытии:** последние 24 часа, лимит 200 записей.
Пользователь расширяет диапазон/лимит вручную через фильтры.

**Таблица** (AntD `Table`, по образцу `AuditLog.tsx`): Время, Уровень
(цветной `Tag`), Источник (`ProviderName`), ID события, Пользователь,
Сообщение (обрезано `ellipsis`). Клик по строке → `Modal` с полным текстом
сообщения и всеми полями записи.

**Баннер обрезания:** если `truncated: true` — `Alert` над таблицей:
«Показаны последние N записей за выбранный период. Сузьте фильтры или
увеличьте лимит, чтобы увидеть больше».

### API-клиент

`src/api/client.ts` — добавить `eventLogs: { logNames, query }`.
`src/api/types.ts` — добавить `EventLogEntryDto`, `EventLogQueryParams`,
`EventLogQueryResult`.

### Меню и маршруты

`AppLayout.tsx` — пункт «Журналы Windows» с 7 дочерними пунктами (6 пресетов
+ divider + произвольный журнал), иконка `FileTextOutlined`.

`App.tsx` — `<Route path="/logs/:presetKey" element={<EventLogs />} />` и
`<Route path="/logs/custom" element={<EventLogs />} />`.

## Ошибки и граничные случаи

- Нет прав на чтение журнала (типично для `Security` без прав администратора
  у пула приложения) → 403 с понятным сообщением, фронт показывает `Alert`
  вместо пустой таблицы.
- Журнал не существует на машине (например, `Setup` отсутствует на некоторых
  системах) → 404 с сообщением.
- Диапазон дат некорректен (`start > end`) → 400 валидация на бэкенде.

## Не входит в эту итерацию

- Экспорт (CSV/JSON).
- Очистка журналов (`Clear-EventLog`).
- Подписка на новые события в реальном времени (live tail).
