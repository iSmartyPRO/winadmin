# Журналы Windows (Event Logs) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a read-only Windows Event Log viewer to WinAdmin — a "Журналы Windows" menu with six presets (Авторизация, Security, Система, Приложения, PowerShell, Установка ПО) plus a "Произвольный журнал" option that lets the user pick any log on the machine, all sharing one filterable table view.

**Architecture:** Backend adds `IEventLogService` (wraps `System.Diagnostics.Eventing.Reader`) behind a new `EventLogsController`, gated by a new `eventlogs.read` scope. A pure, OS-independent helper class builds the XPath filter and extracts the acting username from each record's XML so it can be unit-tested without touching real Windows event logs. Frontend adds one generic `EventLogs.tsx` page driven by a route param (`/logs/:presetKey`), a static preset config, and a filter panel (time range, level, Event ID, keyword, username substring) that defaults to "last 24h / 200 records" and lets the user widen it manually.

**Tech Stack:** ASP.NET Core 10 (C#) — `System.Diagnostics.Eventing.Reader`; xUnit for backend tests; React 19 + TypeScript + Ant Design 6 (no frontend test framework — verify via `tsc -b`/`vite build`/manual click-through).

## Global Constraints

- Read-only feature only — no export, no clearing/deleting logs (spec: "Область действия").
- Default filter on first load: last 24 hours, max 200 records; user can widen the date range and increase the limit manually (spec: "По умолчанию при первом открытии").
- Backend hard cap on `MaxRecords` is 5000 regardless of what the client requests (spec: "MaxRecords").
- Keyword/User substring matching is scanned in-memory with a hard scan cap of ~5000 records per query, independent of how many matched, to bound worst-case cost (spec: "Защитный лимит").
- `User` is a distinct filter from `Keyword` — it matches only the extracted account name (`TargetUserName` → `SubjectUserName` → `AccountName`, falling back to the translated `record.UserId` SID), not the full message text (spec: "`User` — необязательная подстрока...").
- New scope `eventlogs.read` must be added to `Scopes.All` so it's assignable to API keys/users like every other scope.
- Follow existing codebase conventions exactly: `sealed record` with `{ get; init; }` properties for DTOs (see `ProcessInfo.cs`, `PowerModels.cs`), `[SupportedOSPlatform("windows")]` on OS-facing infrastructure classes (see `DiskService.cs`), controllers inherit `WinAdminControllerBase` and use `[Authorize(Policy = "scope:" + Scopes.X)]` per action (see `ProcessesController.cs`).
- Level filter uses the standard 4 Windows event levels only (Critical/Error/Warning/Information) — "Audit Success/Failure" was dropped from scope because that distinction is a Keywords bitmask, not a Level, and is already covered by the Авторизация preset's specific Event ID list (4624 = success, 4625 = failure). This is a deliberate simplification from the approved spec text; flagged to the user in the plan handoff.

---

## File Structure

**Backend (new files):**
- `src/backend/WinAdmin.Core/Models/EventLogModels.cs` — DTOs (`EventLogEntryDto`, `EventLogQueryRequest`, `EventLogQueryResult`) + two exception types.
- `src/backend/WinAdmin.Infrastructure/EventLogs/EventLogQueryHelpers.cs` — pure, OS-independent static helpers (XPath building, username extraction, substring matching, CSV parsing). Fully unit-tested.
- `src/backend/WinAdmin.Infrastructure/EventLogs/EventLogService.cs` — `IEventLogService` implementation wrapping `EventLogReader`/`EventLogSession`. Not unit-tested (matches existing convention: OS-facing services like `DiskService`, `ProcessService` have no tests either) — verified manually at the end.
- `src/backend/WinAdmin.Api/Controllers/EventLogsController.cs` — `GET lognames`, `GET query`.
- `src/tests/WinAdmin.Tests/EventLogQueryHelpersTests.cs` — unit tests for the pure helpers.

**Backend (modified files):**
- `src/backend/WinAdmin.Core/Security/Scopes.cs` — add `EventLogsRead`.
- `src/backend/WinAdmin.Core/Abstractions/ISystemServices.cs` — add `IEventLogService` interface.
- `src/backend/WinAdmin.Infrastructure/DependencyInjection.cs` — register `IEventLogService`.

**Frontend (new files):**
- `src/frontend/src/config/eventLogPresets.ts` — static preset list.
- `src/frontend/src/pages/EventLogs.tsx` — the single page component for all presets + custom log.

**Frontend (modified files):**
- `src/frontend/src/api/types.ts` — add `EventLogEntryDto`, `EventLogQueryParams`, `EventLogQueryResult`.
- `src/frontend/src/api/client.ts` — add `api.eventLogs.logNames()` / `api.eventLogs.query()`.
- `src/frontend/src/components/AppLayout.tsx` — add "Журналы Windows" submenu.
- `src/frontend/src/App.tsx` — add `/logs/:presetKey` route.

---

### Task 1: Backend models, exceptions, and scope

**Files:**
- Create: `src/backend/WinAdmin.Core/Models/EventLogModels.cs`
- Modify: `src/backend/WinAdmin.Core/Security/Scopes.cs`

**Interfaces:**
- Produces: `EventLogEntryDto`, `EventLogQueryRequest`, `EventLogQueryResult` (records, in `WinAdmin.Core.Models`), `EventLogAccessDeniedException`, `EventLogMissingException` (in `WinAdmin.Core.Models`), `Scopes.EventLogsRead` (`"eventlogs.read"`, in `Scopes.All`).

- [ ] **Step 1: Create the models file**

```csharp
// src/backend/WinAdmin.Core/Models/EventLogModels.cs
namespace WinAdmin.Core.Models;

/// <summary>Запись журнала событий Windows, отформатированная для отображения.</summary>
public sealed record EventLogEntryDto
{
    public long Id { get; init; }
    public DateTime TimeCreated { get; init; }
    public required string LogName { get; init; }
    public string? ProviderName { get; init; }
    public int EventId { get; init; }
    public string? Level { get; init; }
    public string? LevelDisplayName { get; init; }
    public string? User { get; init; }
    public string? Message { get; init; }
    public string? MachineName { get; init; }
}

/// <summary>Параметры запроса журнала событий.</summary>
public sealed record EventLogQueryRequest
{
    public required string LogName { get; init; }
    public DateTime StartTime { get; init; }
    public DateTime EndTime { get; init; }
    public int MaxRecords { get; init; } = 200;
    public IReadOnlyList<int>? EventIds { get; init; }
    public IReadOnlyList<string>? Levels { get; init; }
    public string? Keyword { get; init; }
    public string? User { get; init; }
}

/// <summary>Результат запроса журнала: записи + признак усечения по лимиту/сканированию.</summary>
public sealed record EventLogQueryResult
{
    public required IReadOnlyList<EventLogEntryDto> Entries { get; init; }
    public bool Truncated { get; init; }
    public int ScannedCount { get; init; }
}

/// <summary>Нет прав на чтение журнала (обычно Security без прав администратора).</summary>
public sealed class EventLogAccessDeniedException : Exception
{
    public EventLogAccessDeniedException(string message) : base(message) { }
}

/// <summary>Запрошенный журнал не существует на этой машине.</summary>
public sealed class EventLogMissingException : Exception
{
    public EventLogMissingException(string message) : base(message) { }
}
```

- [ ] **Step 2: Add the scope**

In `src/backend/WinAdmin.Core/Security/Scopes.cs`, add the constant and register it in `All`:

```csharp
public const string PowerManage = "power.manage";
public const string EventLogsRead = "eventlogs.read";
```

```csharp
public static readonly IReadOnlyList<string> All = new[]
{
    SystemRead, DisksRead,
    ServicesRead, ServicesManage,
    ProcessesRead, ProcessesManage,
    PrintersRead, PrintersManage,
    PowerManage, EventLogsRead, Admin,
};
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build src/backend/WinAdmin.Core/WinAdmin.Core.csproj`
Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add src/backend/WinAdmin.Core/Models/EventLogModels.cs src/backend/WinAdmin.Core/Security/Scopes.cs
git commit -m "feat: add Windows Event Log models, exceptions, and eventlogs.read scope"
```

---

### Task 2: Pure query helpers (XPath builder, username extraction, matching) — TDD

**Files:**
- Create: `src/backend/WinAdmin.Infrastructure/EventLogs/EventLogQueryHelpers.cs`
- Test: `src/tests/WinAdmin.Tests/EventLogQueryHelpersTests.cs`

**Interfaces:**
- Consumes: `EventLogQueryRequest` (Task 1).
- Produces (all `public static` on `WinAdmin.Infrastructure.EventLogs.EventLogQueryHelpers`):
  - `string BuildXPathFilter(EventLogQueryRequest request)`
  - `string FormatXPathTime(DateTime dt)`
  - `int LevelToNumber(string level)`
  - `string? ExtractUserNameFromXml(string recordXml)`
  - `bool MatchesSubstring(string? haystack, string? needle)`
  - `IReadOnlyList<int>? ParseIntList(string? csv)`
  - `IReadOnlyList<string>? ParseStringList(string? csv)`

These are consumed by `EventLogService` (Task 3) and `EventLogsController` (Task 4).

- [ ] **Step 1: Write the failing tests**

```csharp
// src/tests/WinAdmin.Tests/EventLogQueryHelpersTests.cs
using WinAdmin.Core.Models;
using WinAdmin.Infrastructure.EventLogs;

namespace WinAdmin.Tests;

public sealed class EventLogQueryHelpersTests
{
    private static readonly DateTime Start = new(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2026, 7, 11, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void BuildXPathFilter_TimeRangeOnly()
    {
        var request = new EventLogQueryRequest { LogName = "Security", StartTime = Start, EndTime = End };

        var xpath = EventLogQueryHelpers.BuildXPathFilter(request);

        Assert.Equal(
            "*[System[TimeCreated[@SystemTime>='2026-07-10T00:00:00.000Z' and @SystemTime<='2026-07-11T00:00:00.000Z']]]",
            xpath);
    }

    [Fact]
    public void BuildXPathFilter_WithEventIds()
    {
        var request = new EventLogQueryRequest
        {
            LogName = "Security", StartTime = Start, EndTime = End,
            EventIds = new[] { 4624, 4625 },
        };

        var xpath = EventLogQueryHelpers.BuildXPathFilter(request);

        Assert.Equal(
            "*[System[TimeCreated[@SystemTime>='2026-07-10T00:00:00.000Z' and @SystemTime<='2026-07-11T00:00:00.000Z'] and (EventID=4624 or EventID=4625)]]",
            xpath);
    }

    [Fact]
    public void BuildXPathFilter_WithLevels()
    {
        var request = new EventLogQueryRequest
        {
            LogName = "System", StartTime = Start, EndTime = End,
            Levels = new[] { "Error", "Warning" },
        };

        var xpath = EventLogQueryHelpers.BuildXPathFilter(request);

        Assert.Equal(
            "*[System[TimeCreated[@SystemTime>='2026-07-10T00:00:00.000Z' and @SystemTime<='2026-07-11T00:00:00.000Z'] and (Level=2 or Level=3)]]",
            xpath);
    }

    [Fact]
    public void BuildXPathFilter_WithEventIdsAndLevels()
    {
        var request = new EventLogQueryRequest
        {
            LogName = "Security", StartTime = Start, EndTime = End,
            EventIds = new[] { 4624 }, Levels = new[] { "Error" },
        };

        var xpath = EventLogQueryHelpers.BuildXPathFilter(request);

        Assert.Equal(
            "*[System[TimeCreated[@SystemTime>='2026-07-10T00:00:00.000Z' and @SystemTime<='2026-07-11T00:00:00.000Z'] and (EventID=4624) and (Level=2)]]",
            xpath);
    }

    [Fact]
    public void ExtractUserNameFromXml_ReturnsTargetUserName()
    {
        const string xml = """
            <Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'>
              <System><EventID>4624</EventID></System>
              <EventData>
                <Data Name='SubjectUserName'>WIN-SRV$</Data>
                <Data Name='TargetUserName'>ivan.petrov</Data>
              </EventData>
            </Event>
            """;

        Assert.Equal("ivan.petrov", EventLogQueryHelpers.ExtractUserNameFromXml(xml));
    }

    [Fact]
    public void ExtractUserNameFromXml_FallsBackToSubjectUserName_WhenTargetIsDash()
    {
        const string xml = """
            <Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'>
              <System><EventID>4625</EventID></System>
              <EventData>
                <Data Name='SubjectUserName'>SYSTEM</Data>
                <Data Name='TargetUserName'>-</Data>
              </EventData>
            </Event>
            """;

        Assert.Equal("SYSTEM", EventLogQueryHelpers.ExtractUserNameFromXml(xml));
    }

    [Fact]
    public void ExtractUserNameFromXml_FallsBackToAccountName()
    {
        const string xml = """
            <Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'>
              <System><EventID>1</EventID></System>
              <EventData>
                <Data Name='AccountName'>admin</Data>
              </EventData>
            </Event>
            """;

        Assert.Equal("admin", EventLogQueryHelpers.ExtractUserNameFromXml(xml));
    }

    [Fact]
    public void ExtractUserNameFromXml_ReturnsNull_WhenNoMatchingFields()
    {
        const string xml = """
            <Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'>
              <System><EventID>7036</EventID></System>
              <EventData>
                <Data Name='param1'>Print Spooler</Data>
                <Data Name='param2'>running</Data>
              </EventData>
            </Event>
            """;

        Assert.Null(EventLogQueryHelpers.ExtractUserNameFromXml(xml));
    }

    [Fact]
    public void ExtractUserNameFromXml_ReturnsNull_OnMalformedXml()
    {
        Assert.Null(EventLogQueryHelpers.ExtractUserNameFromXml("<Event><Unclosed>"));
    }

    [Theory]
    [InlineData("hello world", null, true)]
    [InlineData("hello world", "", true)]
    [InlineData("hello world", "WORLD", true)]
    [InlineData(null, "abc", false)]
    [InlineData("hello", "xyz", false)]
    public void MatchesSubstring_Cases(string? haystack, string? needle, bool expected)
    {
        Assert.Equal(expected, EventLogQueryHelpers.MatchesSubstring(haystack, needle));
    }

    [Fact]
    public void ParseIntList_ParsesCommaSeparated_AndSkipsNonNumeric()
    {
        var result = EventLogQueryHelpers.ParseIntList("4624,4625, 4634, abc");
        Assert.Equal(new[] { 4624, 4625, 4634 }, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseIntList_ReturnsNull_ForEmptyOrWhitespace(string? csv)
    {
        Assert.Null(EventLogQueryHelpers.ParseIntList(csv));
    }

    [Fact]
    public void ParseStringList_ParsesCommaSeparated_TrimsWhitespace()
    {
        var result = EventLogQueryHelpers.ParseStringList("Error, Warning ,Critical");
        Assert.Equal(new[] { "Error", "Warning", "Critical" }, result);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/tests/WinAdmin.Tests --filter EventLogQueryHelpersTests`
Expected: FAIL — `EventLogQueryHelpers` does not exist (compile error).

- [ ] **Step 3: Implement the helpers**

```csharp
// src/backend/WinAdmin.Infrastructure/EventLogs/EventLogQueryHelpers.cs
using System.Xml.Linq;
using WinAdmin.Core.Models;

namespace WinAdmin.Infrastructure.EventLogs;

/// <summary>
/// Логика построения XPath-фильтра и разбора XML записи журнала — без зависимости от
/// реального Event Log API, чтобы её можно было полноценно покрыть unit-тестами.
/// </summary>
public static class EventLogQueryHelpers
{
    private static readonly XNamespace EventNs = "http://schemas.microsoft.com/win/2004/08/events/event";

    private static readonly string[] UserNameFieldPriority =
    {
        "TargetUserName", "SubjectUserName", "AccountName",
    };

    public static string BuildXPathFilter(EventLogQueryRequest request)
    {
        var conditions = new List<string>
        {
            $"TimeCreated[@SystemTime>='{FormatXPathTime(request.StartTime)}' and @SystemTime<='{FormatXPathTime(request.EndTime)}']",
        };

        if (request.EventIds is { Count: > 0 })
        {
            var ids = string.Join(" or ", request.EventIds.Select(id => $"EventID={id}"));
            conditions.Add($"({ids})");
        }

        if (request.Levels is { Count: > 0 })
        {
            var levels = string.Join(" or ", request.Levels.Select(l => $"Level={LevelToNumber(l)}"));
            conditions.Add($"({levels})");
        }

        return $"*[System[{string.Join(" and ", conditions)}]]";
    }

    public static string FormatXPathTime(DateTime dt) =>
        dt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

    public static int LevelToNumber(string level) => level switch
    {
        "Critical" => 1,
        "Error" => 2,
        "Warning" => 3,
        "Information" => 4,
        _ => throw new ArgumentException($"Неизвестный уровень: {level}", nameof(level)),
    };

    /// <summary>
    /// Извлекает имя учётной записи из EventData записи (TargetUserName → SubjectUserName →
    /// AccountName, первое непустое и не "-"). Возвращает null, если запись не парсится или
    /// ни одно из полей не заполнено — вызывающий код сам решает, использовать ли fallback на SID.
    /// </summary>
    public static string? ExtractUserNameFromXml(string recordXml)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(recordXml);
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }

        var dataElements = doc.Descendants(EventNs + "Data").ToList();

        foreach (var fieldName in UserNameFieldPriority)
        {
            var value = dataElements
                .FirstOrDefault(d => (string?)d.Attribute("Name") == fieldName)
                ?.Value;
            if (!string.IsNullOrWhiteSpace(value) && value != "-")
                return value;
        }

        return null;
    }

    public static bool MatchesSubstring(string? haystack, string? needle) =>
        string.IsNullOrEmpty(needle) || (haystack?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false);

    public static IReadOnlyList<int>? ParseIntList(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return null;
        var result = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => int.TryParse(s, out _))
            .Select(int.Parse)
            .ToList();
        return result.Count > 0 ? result : null;
    }

    public static IReadOnlyList<string>? ParseStringList(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return null;
        var result = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        return result.Count > 0 ? result : null;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/tests/WinAdmin.Tests --filter EventLogQueryHelpersTests`
Expected: `Passed! - Failed: 0`, 15 tests passed.

- [ ] **Step 5: Commit**

```bash
git add src/backend/WinAdmin.Infrastructure/EventLogs/EventLogQueryHelpers.cs src/tests/WinAdmin.Tests/EventLogQueryHelpersTests.cs
git commit -m "feat: add pure XPath/username-extraction helpers for event log queries"
```

---

### Task 3: `IEventLogService` abstraction + `EventLogService` implementation + DI

**Files:**
- Modify: `src/backend/WinAdmin.Core/Abstractions/ISystemServices.cs`
- Create: `src/backend/WinAdmin.Infrastructure/EventLogs/EventLogService.cs`
- Modify: `src/backend/WinAdmin.Infrastructure/DependencyInjection.cs`

**Interfaces:**
- Consumes: `EventLogQueryRequest`, `EventLogQueryResult`, `EventLogEntryDto`, `EventLogAccessDeniedException`, `EventLogMissingException` (Task 1); `EventLogQueryHelpers.BuildXPathFilter/MatchesSubstring/ExtractUserNameFromXml` (Task 2).
- Produces: `IEventLogService` with `IReadOnlyList<string> GetLogNames()` and `EventLogQueryResult Query(EventLogQueryRequest request)`, registered in DI as scoped. Consumed by `EventLogsController` (Task 4).

- [ ] **Step 1: Add the abstraction**

In `src/backend/WinAdmin.Core/Abstractions/ISystemServices.cs`, append:

```csharp
public interface IEventLogService
{
    IReadOnlyList<string> GetLogNames();
    EventLogQueryResult Query(EventLogQueryRequest request);
}
```

- [ ] **Step 2: Implement the service**

```csharp
// src/backend/WinAdmin.Infrastructure/EventLogs/EventLogService.cs
using System.Diagnostics.Eventing.Reader;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;

namespace WinAdmin.Infrastructure.EventLogs;

/// <summary>Читает журналы событий Windows через Event Log API с фильтрацией на стороне ОС.</summary>
[SupportedOSPlatform("windows")]
public sealed class EventLogService : IEventLogService
{
    private const int ScanCap = 5000;
    private readonly ILogger<EventLogService> _logger;

    public EventLogService(ILogger<EventLogService> logger) => _logger = logger;

    public IReadOnlyList<string> GetLogNames()
    {
        try
        {
            var session = new EventLogSession();
            return session.GetLogNames().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось перечислить журналы событий");
            return Array.Empty<string>();
        }
    }

    public EventLogQueryResult Query(EventLogQueryRequest request)
    {
        var xpath = EventLogQueryHelpers.BuildXPathFilter(request);
        var entries = new List<EventLogEntryDto>();
        var scanned = 0;
        var truncated = false;

        try
        {
            var query = new EventLogQuery(request.LogName, PathType.LogName, xpath) { ReverseDirection = true };
            using var reader = new EventLogReader(query);

            EventRecord? record;
            while ((record = reader.ReadEvent()) != null)
            {
                using (record)
                {
                    scanned++;
                    if (scanned > ScanCap)
                    {
                        truncated = true;
                        break;
                    }

                    string? message;
                    try { message = record.FormatDescription(); }
                    catch (EventLogException) { message = null; }

                    if (!EventLogQueryHelpers.MatchesSubstring(message, request.Keyword))
                        continue;

                    var user = EventLogQueryHelpers.ExtractUserNameFromXml(record.ToXml())
                        ?? TryTranslateSid(record.UserId);

                    if (!EventLogQueryHelpers.MatchesSubstring(user, request.User))
                        continue;

                    entries.Add(new EventLogEntryDto
                    {
                        Id = record.RecordId ?? 0,
                        TimeCreated = record.TimeCreated ?? DateTime.MinValue,
                        LogName = request.LogName,
                        ProviderName = record.ProviderName,
                        EventId = record.Id,
                        Level = record.Level?.ToString(),
                        LevelDisplayName = SafeLevelDisplayName(record),
                        User = user,
                        Message = message,
                        MachineName = record.MachineName,
                    });

                    if (entries.Count >= request.MaxRecords)
                    {
                        truncated = true;
                        break;
                    }
                }
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Нет доступа к журналу {LogName}", request.LogName);
            throw new EventLogAccessDeniedException(
                $"Нет доступа к журналу «{request.LogName}» — требуются права администратора.");
        }
        catch (EventLogNotFoundException ex)
        {
            _logger.LogWarning(ex, "Журнал {LogName} не найден", request.LogName);
            throw new EventLogMissingException($"Журнал «{request.LogName}» не найден на этой машине.");
        }

        return new EventLogQueryResult { Entries = entries, Truncated = truncated, ScannedCount = scanned };
    }

    private static string? TryTranslateSid(SecurityIdentifier? sid)
    {
        if (sid == null) return null;
        try { return sid.Translate(typeof(NTAccount)).ToString(); }
        catch (IdentityNotMappedException) { return sid.Value; }
    }

    private static string? SafeLevelDisplayName(EventRecord record)
    {
        try { return record.LevelDisplayName; }
        catch (EventLogNotFoundException) { return record.Level?.ToString(); }
    }
}
```

- [ ] **Step 3: Register in DI**

In `src/backend/WinAdmin.Infrastructure/DependencyInjection.cs`, add the using and registration:

```csharp
using WinAdmin.Infrastructure.EventLogs;
```

```csharp
services.AddScoped<IPowerService, PowerService>();
services.AddScoped<IEventLogService, EventLogService>();
```

- [ ] **Step 4: Build the whole solution to verify it compiles**

Run: `dotnet build WinAdmin.slnx`
Expected: `Build succeeded.` (0 errors)

- [ ] **Step 5: Commit**

```bash
git add src/backend/WinAdmin.Core/Abstractions/ISystemServices.cs src/backend/WinAdmin.Infrastructure/EventLogs/EventLogService.cs src/backend/WinAdmin.Infrastructure/DependencyInjection.cs
git commit -m "feat: implement IEventLogService over Windows Event Log API"
```

---

### Task 4: `EventLogsController`

**Files:**
- Create: `src/backend/WinAdmin.Api/Controllers/EventLogsController.cs`

**Interfaces:**
- Consumes: `IEventLogService` (Task 3), `EventLogQueryRequest`/`EventLogQueryResult`/`EventLogAccessDeniedException`/`EventLogMissingException` (Task 1), `EventLogQueryHelpers.ParseIntList/ParseStringList` (Task 2), `Scopes.EventLogsRead` (Task 1).
- Produces: `GET /api/v1/eventlogs/lognames`, `GET /api/v1/eventlogs/query`. Consumed by the frontend API client (Task 5).

- [ ] **Step 1: Create the controller**

```csharp
// src/backend/WinAdmin.Api/Controllers/EventLogsController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Core.Security;
using WinAdmin.Infrastructure.EventLogs;

namespace WinAdmin.Api.Controllers;

/// <summary>Просмотр и фильтрация журналов событий Windows.</summary>
public sealed class EventLogsController : WinAdminControllerBase
{
    private readonly IEventLogService _eventLogs;

    public EventLogsController(IEventLogService eventLogs) => _eventLogs = eventLogs;

    /// <summary>Список всех журналов событий, доступных на этой машине.</summary>
    [HttpGet("lognames")]
    [Authorize(Policy = "scope:" + Scopes.EventLogsRead)]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<string>> GetLogNames() => Ok(_eventLogs.GetLogNames());

    /// <summary>Запрос записей журнала с фильтрами по времени, Event ID, уровню, тексту и учётной записи.</summary>
    [HttpGet("query")]
    [Authorize(Policy = "scope:" + Scopes.EventLogsRead)]
    [ProducesResponseType(typeof(EventLogQueryResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<EventLogQueryResult> Query(
        [FromQuery] string logName,
        [FromQuery] DateTime start,
        [FromQuery] DateTime end,
        [FromQuery] int maxRecords = 200,
        [FromQuery] string? eventIds = null,
        [FromQuery] string? levels = null,
        [FromQuery] string? keyword = null,
        [FromQuery] string? user = null)
    {
        if (string.IsNullOrWhiteSpace(logName))
            return BadRequest(new { message = "Не указан журнал (logName)." });
        if (start > end)
            return BadRequest(new { message = "Начало диапазона не может быть позже конца." });

        var request = new EventLogQueryRequest
        {
            LogName = logName,
            StartTime = start,
            EndTime = end,
            MaxRecords = Math.Clamp(maxRecords, 1, 5000),
            EventIds = EventLogQueryHelpers.ParseIntList(eventIds),
            Levels = EventLogQueryHelpers.ParseStringList(levels),
            Keyword = keyword,
            User = user,
        };

        try
        {
            return Ok(_eventLogs.Query(request));
        }
        catch (EventLogAccessDeniedException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (EventLogMissingException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build WinAdmin.slnx`
Expected: `Build succeeded.` (0 errors)

- [ ] **Step 3: Smoke-test the endpoints manually**

Run: `dotnet run --project src/backend/WinAdmin.Api --urls http://localhost:5099` (leave running)

In another shell, log in and call the new endpoints (replace credentials/token as needed for this environment's auth flow — see `docs/02-security.md` for how to obtain a token):

```bash
curl -s http://localhost:5099/api/v1/eventlogs/lognames -H "Authorization: Bearer <token>"
curl -s "http://localhost:5099/api/v1/eventlogs/query?logName=System&start=2026-07-10T00:00:00Z&end=2026-07-11T00:00:00Z&maxRecords=50" -H "Authorization: Bearer <token>"
```

Expected: first call returns a JSON array of log names including `"System"`, `"Application"`, `"Security"`; second call returns `{ "entries": [...], "truncated": false, "scannedCount": N }` with `entries[].eventId`, `.message`, etc. populated. Stop the server (Ctrl+C) once confirmed.

- [ ] **Step 4: Commit**

```bash
git add src/backend/WinAdmin.Api/Controllers/EventLogsController.cs
git commit -m "feat: add EventLogsController for querying Windows event logs"
```

---

### Task 5: Frontend types + API client + preset config

**Files:**
- Modify: `src/frontend/src/api/types.ts`
- Modify: `src/frontend/src/api/client.ts`
- Create: `src/frontend/src/config/eventLogPresets.ts`

**Interfaces:**
- Consumes: nothing from earlier frontend tasks (this is the frontend's data-layer foundation).
- Produces: `EventLogEntryDto`, `EventLogQueryParams`, `EventLogQueryResult` (types), `api.eventLogs.logNames()`, `api.eventLogs.query(params)`, `EVENT_LOG_PRESETS`, `findPreset(key)`. Consumed by `EventLogs.tsx` (Task 6).

- [ ] **Step 1: Add types**

In `src/frontend/src/api/types.ts`, append:

```typescript
export interface EventLogEntryDto {
  id: number
  timeCreated: string
  logName: string
  providerName?: string
  eventId: number
  level?: string
  levelDisplayName?: string
  user?: string
  message?: string
  machineName?: string
}

export interface EventLogQueryParams {
  logName: string
  start: string
  end: string
  maxRecords?: number
  eventIds?: string
  levels?: string
  keyword?: string
  user?: string
}

export interface EventLogQueryResult {
  entries: EventLogEntryDto[]
  truncated: boolean
  scannedCount: number
}
```

- [ ] **Step 2: Add API client methods**

In `src/frontend/src/api/client.ts`, add the new types to the import block:

```typescript
import type {
  ApiKeyDto, AuditEntryDto, CreatedApiKey, MeResponse, OperationResult,
  PhysicalDisk, PrinterInfo, ProcessInfo, ServiceInfo,
  SystemInfo, SystemMetrics, PowerRequest, UserDto,
  CreateUserRequest, TokenResponse,
  EventLogQueryParams, EventLogQueryResult,
} from './types'
```

Then add to the `api` object (after the `users`/... block, before the closing `}`):

```typescript
  eventLogs: {
    logNames: () => http.get<string[]>('/eventlogs/lognames').then((r) => r.data),
    query: (params: EventLogQueryParams) =>
      http.get<EventLogQueryResult>('/eventlogs/query', { params }).then((r) => r.data),
  },
```

- [ ] **Step 3: Create the preset config**

```typescript
// src/frontend/src/config/eventLogPresets.ts
export interface EventLogPreset {
  key: string
  label: string
  logName: string
  eventIds?: number[]
  description: string
}

export const EVENT_LOG_PRESETS: EventLogPreset[] = [
  {
    key: 'auth',
    label: 'Авторизация',
    logName: 'Security',
    eventIds: [4624, 4625, 4634, 4647, 4648, 4672, 4720, 4722, 4725, 4726, 4738, 4767, 4776],
    description: 'Входы и выходы, неудачные попытки входа, изменения учётных записей',
  },
  {
    key: 'security',
    label: 'Security (все события)',
    logName: 'Security',
    description: 'Полный журнал безопасности без фильтра по Event ID',
  },
  {
    key: 'system',
    label: 'Система',
    logName: 'System',
    description: 'Драйверы, службы, загрузки и перезагрузки, ошибки оборудования',
  },
  {
    key: 'application',
    label: 'Приложения',
    logName: 'Application',
    description: 'Ошибки и предупреждения приложений',
  },
  {
    key: 'powershell',
    label: 'PowerShell',
    logName: 'Microsoft-Windows-PowerShell/Operational',
    description: 'Выполненные команды и скрипты PowerShell',
  },
  {
    key: 'setup',
    label: 'Установка ПО',
    logName: 'Setup',
    description: 'Установка и удаление программ и обновлений Windows',
  },
]

export const findPreset = (key?: string): EventLogPreset | undefined =>
  EVENT_LOG_PRESETS.find((p) => p.key === key)
```

- [ ] **Step 4: Type-check**

Run: `npm --prefix src/frontend run build`
Expected: builds without TypeScript errors (this also runs `tsc -b`, which will fail if a type is misspelled anywhere touched above).

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/api/types.ts src/frontend/src/api/client.ts src/frontend/src/config/eventLogPresets.ts
git commit -m "feat: add event log types, API client methods, and preset config"
```

---

### Task 6: `EventLogs.tsx` page

**Files:**
- Create: `src/frontend/src/pages/EventLogs.tsx`

**Interfaces:**
- Consumes: `api.eventLogs.query`/`logNames` (Task 5), `EventLogEntryDto` (Task 5), `EVENT_LOG_PRESETS`/`findPreset` (Task 5), `useApi` hook (existing, `src/frontend/src/hooks/useApi.ts`), `PageHeader` (existing), `formatDateTime` (existing, `src/frontend/src/utils/format.ts`).
- Produces: default export `EventLogs` React component, reads `presetKey` from the route (`useParams`). Consumed by `App.tsx` routing (Task 7).

- [ ] **Step 1: Create the page component**

```tsx
// src/frontend/src/pages/EventLogs.tsx
import { useEffect, useState } from 'react'
import { useParams } from 'react-router-dom'
import {
  Alert, Button, Card, DatePicker, Input, Modal, Select, Space, Table, Tag, Typography,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import dayjs from 'dayjs'
import type { Dayjs } from 'dayjs'
import { api } from '../api/client'
import { useApi } from '../hooks/useApi'
import type { EventLogEntryDto } from '../api/types'
import PageHeader from '../components/PageHeader'
import { formatDateTime } from '../utils/format'
import { findPreset } from '../config/eventLogPresets'

const { RangePicker } = DatePicker
const { Text, Paragraph } = Typography

const MAX_RECORDS_OPTIONS = [200, 500, 1000, 5000]

const LEVEL_OPTIONS = [
  { label: 'Критическая', value: 'Critical' },
  { label: 'Ошибка', value: 'Error' },
  { label: 'Предупреждение', value: 'Warning' },
  { label: 'Информация', value: 'Information' },
]

const LEVEL_COLORS: Record<string, string> = {
  Critical: 'red', Error: 'volcano', Warning: 'gold', Information: 'blue',
}

function quickRange(hours: number): [Dayjs, Dayjs] {
  return [dayjs().subtract(hours, 'hour'), dayjs()]
}

export default function EventLogs() {
  const { presetKey } = useParams<{ presetKey: string }>()
  const isCustom = presetKey === 'custom'
  const preset = findPreset(presetKey)

  const [logName, setLogName] = useState(preset?.logName ?? '')
  const [logNames, setLogNames] = useState<string[]>([])
  const [range, setRange] = useState<[Dayjs, Dayjs]>(quickRange(24))
  const [maxRecords, setMaxRecords] = useState(200)
  const [levels, setLevels] = useState<string[]>([])
  const [eventIds, setEventIds] = useState(preset?.eventIds?.join(',') ?? '')
  const [keyword, setKeyword] = useState('')
  const [user, setUser] = useState('')
  const [detail, setDetail] = useState<EventLogEntryDto | null>(null)

  // Сброс фильтров при смене пресета/маршрута
  useEffect(() => {
    setLogName(preset?.logName ?? '')
    setEventIds(preset?.eventIds?.join(',') ?? '')
    setRange(quickRange(24))
    setMaxRecords(200)
    setLevels([])
    setKeyword('')
    setUser('')
  }, [presetKey]) // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => {
    if (isCustom) api.eventLogs.logNames().then(setLogNames).catch(() => setLogNames([]))
  }, [isCustom])

  const { data, loading, error, refresh } = useApi(() => api.eventLogs.query({
    logName,
    start: range[0].toISOString(),
    end: range[1].toISOString(),
    maxRecords,
    eventIds: eventIds || undefined,
    levels: levels.length ? levels.join(',') : undefined,
    keyword: keyword || undefined,
    user: user || undefined,
  }))

  // Автозапрос при смене журнала (пресет применился или выбран в кастомном режиме)
  useEffect(() => {
    if (logName) refresh()
  }, [logName]) // eslint-disable-line react-hooks/exhaustive-deps

  const columns: ColumnsType<EventLogEntryDto> = [
    { title: 'Время', dataIndex: 'timeCreated', render: (d: string) => formatDateTime(d), width: 180 },
    {
      title: 'Уровень', dataIndex: 'levelDisplayName', width: 130,
      render: (l: string | undefined, r) => (
        <Tag color={LEVEL_COLORS[r.level ?? ''] ?? 'default'}>{l ?? r.level ?? '—'}</Tag>
      ),
    },
    { title: 'Источник', dataIndex: 'providerName', width: 220, ellipsis: true },
    { title: 'ID события', dataIndex: 'eventId', width: 100 },
    {
      title: 'Пользователь', dataIndex: 'user', width: 160,
      render: (u?: string) => u ?? <Text type="secondary">—</Text>,
    },
    {
      title: 'Сообщение', dataIndex: 'message', ellipsis: true,
      render: (m?: string) => m ?? <Text type="secondary">—</Text>,
    },
  ]

  return (
    <>
      <PageHeader
        title={preset?.label ?? 'Произвольный журнал'}
        subtitle={data ? `${data.entries.length} записей` : undefined}
        onRefresh={refresh}
        loading={loading}
      />
      <Card variant="borderless" className="sp-glass" style={{ marginBottom: 16 }}>
        <Space wrap size="middle">
          {isCustom && (
            <Select
              showSearch
              placeholder="Выберите журнал"
              style={{ width: 280 }}
              value={logName || undefined}
              onChange={setLogName}
              options={logNames.map((n) => ({ label: n, value: n }))}
            />
          )}
          <Space.Compact>
            <Button onClick={() => setRange(quickRange(24))}>24ч</Button>
            <Button onClick={() => setRange(quickRange(24 * 7))}>7д</Button>
            <Button onClick={() => setRange(quickRange(24 * 30))}>30д</Button>
          </Space.Compact>
          <RangePicker showTime value={range} onChange={(v) => v && setRange(v as [Dayjs, Dayjs])} />
          <Select
            style={{ width: 130 }}
            value={maxRecords}
            onChange={setMaxRecords}
            options={MAX_RECORDS_OPTIONS.map((n) => ({ label: `${n} записей`, value: n }))}
          />
          <Select
            mode="multiple"
            allowClear
            placeholder="Уровень"
            style={{ minWidth: 200 }}
            value={levels}
            onChange={setLevels}
            options={LEVEL_OPTIONS}
          />
          <Input
            style={{ width: 170 }}
            placeholder="Event ID (4624,4625)"
            value={eventIds}
            onChange={(e) => setEventIds(e.target.value)}
          />
          <Input.Search
            style={{ width: 200 }}
            placeholder="Поиск по тексту"
            value={keyword}
            onChange={(e) => setKeyword(e.target.value)}
            onSearch={refresh}
          />
          <Input.Search
            style={{ width: 200 }}
            placeholder="Пользователь (часть имени)"
            value={user}
            onChange={(e) => setUser(e.target.value)}
            onSearch={refresh}
          />
          <Button type="primary" onClick={refresh} loading={loading}>Применить</Button>
        </Space>
      </Card>

      {error && <Alert type="error" message={error} style={{ marginBottom: 16 }} />}
      {data?.truncated && (
        <Alert
          type="warning"
          showIcon
          style={{ marginBottom: 16 }}
          message={`Показаны последние ${data.entries.length} записей за выбранный период. Сузьте фильтры или увеличьте лимит, чтобы увидеть больше.`}
        />
      )}

      <Card variant="borderless" className="sp-glass">
        <Table
          rowKey="id"
          size="small"
          columns={columns}
          dataSource={data?.entries ?? []}
          loading={loading}
          pagination={{ pageSize: 25, showSizeChanger: true }}
          onRow={(record) => ({ onClick: () => setDetail(record) })}
        />
      </Card>

      <Modal
        open={!!detail}
        onCancel={() => setDetail(null)}
        footer={null}
        width={640}
        title={detail ? `${detail.providerName ?? ''} · ID ${detail.eventId}` : ''}
      >
        {detail && (
          <>
            <Paragraph><Text strong>Время:</Text> {formatDateTime(detail.timeCreated)}</Paragraph>
            <Paragraph><Text strong>Журнал:</Text> {detail.logName}</Paragraph>
            <Paragraph><Text strong>Уровень:</Text> {detail.levelDisplayName ?? detail.level ?? '—'}</Paragraph>
            <Paragraph><Text strong>Пользователь:</Text> {detail.user ?? '—'}</Paragraph>
            <Paragraph><Text strong>Машина:</Text> {detail.machineName ?? '—'}</Paragraph>
            <Paragraph style={{ whiteSpace: 'pre-wrap' }}>{detail.message ?? '—'}</Paragraph>
          </>
        )}
      </Modal>
    </>
  )
}
```

- [ ] **Step 2: Type-check and lint**

Run: `npm --prefix src/frontend run build && npm --prefix src/frontend run lint`
Expected: both succeed with no errors.

- [ ] **Step 3: Commit**

```bash
git add src/frontend/src/pages/EventLogs.tsx
git commit -m "feat: add EventLogs page with filter panel and detail modal"
```

---

### Task 7: Wire up menu and routes

**Files:**
- Modify: `src/frontend/src/components/AppLayout.tsx`
- Modify: `src/frontend/src/App.tsx`

**Interfaces:**
- Consumes: `EventLogs` component (Task 6).
- Produces: navigable menu entries and route `/logs/:presetKey`.

- [ ] **Step 1: Add the menu icon import and submenu**

In `src/frontend/src/components/AppLayout.tsx`, update the icon import:

```tsx
import {
  DashboardOutlined, HddOutlined, ApiOutlined, AppstoreOutlined,
  PrinterOutlined, PoweroffOutlined, KeyOutlined, FileSearchOutlined,
  BookOutlined, LogoutOutlined, DesktopOutlined, TeamOutlined,
  FileTextOutlined,
} from '@ant-design/icons'
```

Add the submenu to `items`, right after `/power` and before the divider:

```tsx
  const items = [
    { key: '/', icon: <DashboardOutlined />, label: 'Дашборд' },
    { key: '/disks', icon: <HddOutlined />, label: 'Диски' },
    { key: '/services', icon: <ApiOutlined />, label: 'Службы' },
    { key: '/processes', icon: <AppstoreOutlined />, label: 'Процессы' },
    { key: '/printers', icon: <PrinterOutlined />, label: 'Принтеры' },
    { key: '/power', icon: <PoweroffOutlined />, label: 'Питание' },
    {
      key: '/logs', icon: <FileTextOutlined />, label: 'Журналы Windows',
      children: [
        { key: '/logs/auth', label: 'Авторизация' },
        { key: '/logs/security', label: 'Security (все события)' },
        { key: '/logs/system', label: 'Система' },
        { key: '/logs/application', label: 'Приложения' },
        { key: '/logs/powershell', label: 'PowerShell' },
        { key: '/logs/setup', label: 'Установка ПО' },
        { type: 'divider' as const },
        { key: '/logs/custom', label: 'Произвольный журнал' },
      ],
    },
    { type: 'divider' as const },
    ...(isAdmin ? [{ key: '/cp/users', icon: <TeamOutlined />, label: 'Пользователи' }] : []),
    { key: '/cp/apikeys', icon: <KeyOutlined />, label: 'API-ключи' },
    { key: '/cp/audit', icon: <FileSearchOutlined />, label: 'Аудит' },
    { key: '/docs', icon: <BookOutlined />, label: 'API-документация' },
  ]
```

Add `defaultOpenKeys` to the `Menu` so the new submenu is expanded by default (this is the first submenu in the app, so there's no existing open/close state to preserve):

```tsx
        <Menu
          theme="dark"
          mode="inline"
          selectedKeys={[location.pathname]}
          defaultOpenKeys={['/logs']}
          items={items}
          onClick={({ key }) => navigate(key)}
          style={{ background: 'transparent', borderInlineEnd: 'none' }}
        />
```

- [ ] **Step 2: Add the route**

In `src/frontend/src/App.tsx`, add the import:

```tsx
import EventLogs from './pages/EventLogs'
```

Add the route inside `<Routes>`, after `/power`:

```tsx
          <Route path="/power" element={<Power />} />
          <Route path="/logs/:presetKey" element={<EventLogs />} />
```

- [ ] **Step 3: Type-check and lint**

Run: `npm --prefix src/frontend run build && npm --prefix src/frontend run lint`
Expected: both succeed with no errors.

- [ ] **Step 4: Commit**

```bash
git add src/frontend/src/components/AppLayout.tsx src/frontend/src/App.tsx
git commit -m "feat: wire up Windows Event Logs menu and route"
```

---

### Task 8: End-to-end manual verification

**Files:** none (verification only).

**Interfaces:** none — this task exercises the full stack built in Tasks 1–7.

- [ ] **Step 1: Run the backend**

Run: `$env:ASPNETCORE_ENVIRONMENT='Development'; dotnet run --project src/backend/WinAdmin.Api --urls http://localhost:5099`
Expected: server starts, logs "Now listening on: http://localhost:5099".

- [ ] **Step 2: Run the frontend**

Run: `npm --prefix src/frontend run dev`
Expected: Vite dev server starts on `http://localhost:5188`.

- [ ] **Step 3: Log in and grant the new scope**

Log in as the bootstrap admin (see `bootstrap-key.txt` created next to the backend on first run, or the admin user created during setup). If logging in with a non-admin API key/user, add `eventlogs.read` to its scopes via the "API-ключи" or "Пользователи" page first — otherwise every `/logs/*` page will show a 403 error banner.

- [ ] **Step 4: Click through every preset**

In the browser, open the "Журналы Windows" submenu and click each item in turn: Авторизация, Security (все события), Система, Приложения, PowerShell, Установка ПО.

Expected for each: the page loads with "последние 24ч, 200 записей" already applied, the table populates (or shows an empty table if the machine genuinely has no matching events in the last 24h — try widening to 7д if so), and the Event ID field is pre-filled for "Авторизация" only.

- [ ] **Step 5: Verify filtering behavior**

On the "Авторизация" page: type part of a real local username (e.g. the first few letters of the current Windows user) into the "Пользователь" field and press enter — confirm the table narrows to rows where that substring appears in the "Пользователь" column. Clear it and type an unrelated word into "Поиск по тексту" — confirm it filters by message content instead. Widen the date range to 30д and set limit to 1000, click "Применить" — confirm the request re-runs (Network tab shows a new `/api/v1/eventlogs/query` call with updated params).

- [ ] **Step 6: Verify the truncation banner**

On "Security (все события)" (or any log with many entries), set the range to 30д and limit to 200 — if the machine has more than 200 matching Security events in that window, confirm the yellow "Показаны последние N записей..." banner appears above the table.

- [ ] **Step 7: Verify row detail modal**

Click any row in any preset's table — confirm a modal opens showing the full message text and all metadata fields (Время, Журнал, Уровень, Пользователь, Машина).

- [ ] **Step 8: Verify the custom log picker**

Open "Произвольный журнал" — confirm a searchable dropdown appears populated with log names (should include at least `System`, `Application`, `Security`, `Setup`). Pick one, confirm the table loads for it with the same default 24ч/200 filter.

- [ ] **Step 9: Stop both dev servers**

Stop the Vite dev server (Ctrl+C) and the backend (Ctrl+C).

- [ ] **Step 10: Final full test run**

Run: `dotnet test`
Expected: all tests pass, including the 15 new `EventLogQueryHelpersTests`.

No commit for this task — it's verification only. If any step surfaces a bug, fix it in the relevant task's file and commit the fix with a message like `fix: <what was wrong>`.
