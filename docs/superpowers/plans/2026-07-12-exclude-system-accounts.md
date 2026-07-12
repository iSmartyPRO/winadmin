# Hide System Accounts in Auth Event Log Preset Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a "Только пользователи" switch to the "Авторизация" (auth) Event Log preset that hides built-in Windows accounts (SYSTEM, LOCAL SERVICE, NETWORK SERVICE, machine `$` accounts) by default, so real user logins aren't buried in noise.

**Architecture:** Detection happens server-side, before `maxRecords`/`truncated` accounting, using the well-known SID of the account (`S-1-5-18`/`19`/`20`) extracted from the paired `*UserSid` field in the event XML — not the localized `*UserName` string, which is rendered in the Windows display language and would break on non-Russian installs. Machine accounts are recognized by the `$` name suffix, which is a NetBIOS naming convention, not a translation, so it's language-neutral too.

**Tech Stack:** ASP.NET Core 10 (C#) backend, React 19 + TypeScript + Ant Design frontend, xUnit tests.

## Global Constraints

- System-account detection MUST use SID comparison (`S-1-5-18`, `S-1-5-19`, `S-1-5-20`) plus the `$` name-suffix check — never literal name strings like `SYSTEM`/`СИСТЕМА` (spec: `docs/superpowers/specs/2026-07-12-exclude-system-accounts-design.md`).
- The feature applies **only** to the `auth` preset (`presetKey === 'auth'`) — no other Event Log preset changes behavior.
- The "Только пользователи" switch defaults to **checked** (`true`) every time the auth preset is opened (no persistence across sessions, matching how other filters on this page already reset per mount).

---

### Task 1: Add `ExtractUserInfo` and `IsSystemAccount` pure helpers

**Files:**
- Modify: `src/backend/WinAdmin.Infrastructure/EventLogs/EventLogQueryHelpers.cs`
- Modify: `src/tests/WinAdmin.Tests/EventLogQueryHelpersTests.cs`

**Interfaces:**
- Produces: `EventLogQueryHelpers.ExtractUserInfo(string recordXml) -> (string? Name, string? Sid)` and `EventLogQueryHelpers.IsSystemAccount(string? sid, string? name) -> bool`. Task 2 consumes both.
- This task does NOT touch `ExtractUserNameFromXml` or its existing tests — they stay as-is and still compile/pass. Task 2 removes them once `EventLogService` no longer calls them.

- [ ] **Step 1: Write failing tests for `ExtractUserInfo`**

Add to `src/tests/WinAdmin.Tests/EventLogQueryHelpersTests.cs`, after the existing `ExtractUserNameFromXml_ReturnsNull_OnMalformedXml` test:

```csharp
[Fact]
public void ExtractUserInfo_ReturnsTargetUserNameAndItsSid()
{
    const string xml = """
        <Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'>
          <System><EventID>4624</EventID></System>
          <EventData>
            <Data Name='SubjectUserName'>WIN-SRV$</Data>
            <Data Name='SubjectUserSid'>S-1-5-18</Data>
            <Data Name='TargetUserName'>ivan.petrov</Data>
            <Data Name='TargetUserSid'>S-1-5-21-1-2-3-1001</Data>
          </EventData>
        </Event>
        """;

    var (name, sid) = EventLogQueryHelpers.ExtractUserInfo(xml);

    Assert.Equal("ivan.petrov", name);
    Assert.Equal("S-1-5-21-1-2-3-1001", sid);
}

[Fact]
public void ExtractUserInfo_FallsBackToSubjectUserName_WithItsOwnSid_WhenTargetIsDash()
{
    const string xml = """
        <Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'>
          <System><EventID>4625</EventID></System>
          <EventData>
            <Data Name='SubjectUserName'>SYSTEM</Data>
            <Data Name='SubjectUserSid'>S-1-5-18</Data>
            <Data Name='TargetUserName'>-</Data>
          </EventData>
        </Event>
        """;

    var (name, sid) = EventLogQueryHelpers.ExtractUserInfo(xml);

    Assert.Equal("SYSTEM", name);
    Assert.Equal("S-1-5-18", sid);
}

[Fact]
public void ExtractUserInfo_FallsBackToAccountName_WithNullSid()
{
    const string xml = """
        <Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'>
          <System><EventID>1</EventID></System>
          <EventData>
            <Data Name='AccountName'>admin</Data>
          </EventData>
        </Event>
        """;

    var (name, sid) = EventLogQueryHelpers.ExtractUserInfo(xml);

    Assert.Equal("admin", name);
    Assert.Null(sid);
}

[Fact]
public void ExtractUserInfo_ReturnsNulls_WhenNoMatchingFields()
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

    var (name, sid) = EventLogQueryHelpers.ExtractUserInfo(xml);

    Assert.Null(name);
    Assert.Null(sid);
}

[Fact]
public void ExtractUserInfo_ReturnsNulls_OnMalformedXml()
{
    var (name, sid) = EventLogQueryHelpers.ExtractUserInfo("<Event><Unclosed>");

    Assert.Null(name);
    Assert.Null(sid);
}

[Theory]
[InlineData("S-1-5-18", "SYSTEM", true)]
[InlineData("S-1-5-18", "СИСТЕМА", true)]
[InlineData("S-1-5-19", "Local Service", true)]
[InlineData("S-1-5-20", null, true)]
[InlineData(null, "DESKTOP-01$", true)]
[InlineData(null, "DOMAIN\\PC$", true)]
[InlineData(null, null, false)]
[InlineData(null, "", false)]
[InlineData(null, "Administrator", false)]
[InlineData(null, "ivanov", false)]
[InlineData("S-1-5-21-1-2-3-1001", "ivanov", false)]
public void IsSystemAccount_Cases(string? sid, string? name, bool expected)
{
    Assert.Equal(expected, EventLogQueryHelpers.IsSystemAccount(sid, name));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/tests/WinAdmin.Tests/WinAdmin.Tests.csproj --filter "ExtractUserInfo|IsSystemAccount_Cases"`
Expected: build error — `ExtractUserInfo` and `IsSystemAccount` do not exist on `EventLogQueryHelpers` yet.

- [ ] **Step 3: Implement `ExtractUserInfo` and `IsSystemAccount`**

In `src/backend/WinAdmin.Infrastructure/EventLogs/EventLogQueryHelpers.cs`, add these members inside the `EventLogQueryHelpers` class (e.g. right after the existing `ExtractUserNameFromXml` method — do not remove that method, Task 2 handles that):

```csharp
    private static readonly (string NameField, string? SidField)[] UserFieldPriority =
    {
        ("TargetUserName", "TargetUserSid"),
        ("SubjectUserName", "SubjectUserSid"),
        ("AccountName", null),
    };

    private const string SidSystem = "S-1-5-18";
    private const string SidLocalService = "S-1-5-19";
    private const string SidNetworkService = "S-1-5-20";

    /// <summary>
    /// Извлекает (имя, SID) учётной записи из EventData записи, перебирая пары полей
    /// TargetUserName/TargetUserSid → SubjectUserName/SubjectUserSid → AccountName (без SID).
    /// Останавливается на первом непустом и не "-" имени; SID берётся из парного поля той же
    /// записи, если для этого приоритета оно объявлено.
    /// </summary>
    public static (string? Name, string? Sid) ExtractUserInfo(string recordXml)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(recordXml);
        }
        catch (System.Xml.XmlException)
        {
            return (null, null);
        }

        var dataElements = doc.Descendants(EventNs + "Data").ToList();

        string? FieldValue(string fieldName) =>
            dataElements.FirstOrDefault(d => (string?)d.Attribute("Name") == fieldName)?.Value;

        foreach (var (nameField, sidField) in UserFieldPriority)
        {
            var name = FieldValue(nameField);
            if (string.IsNullOrWhiteSpace(name) || name == "-") continue;
            var sid = sidField != null ? FieldValue(sidField) : null;
            return (name, string.IsNullOrWhiteSpace(sid) ? null : sid);
        }

        return (null, null);
    }

    /// <summary>
    /// Определяет встроенную системную учётную запись по SID (языконезависимо — SID не
    /// переводится): SYSTEM/LOCAL SERVICE/NETWORK SERVICE, либо по суффиксу "$" в имени
    /// (машинный аккаунт — соглашение именования NetBIOS, тоже не зависит от языка). Если SID
    /// недоступен и имя не оканчивается на "$", запись не считается системной — по
    /// локализованному имени не гадаем.
    /// </summary>
    public static bool IsSystemAccount(string? sid, string? name)
    {
        if (sid is SidSystem or SidLocalService or SidNetworkService) return true;
        return name is not null && name.EndsWith('$');
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/tests/WinAdmin.Tests/WinAdmin.Tests.csproj --filter "ExtractUserInfo|IsSystemAccount_Cases"`
Expected: all listed tests PASS.

- [ ] **Step 5: Run the full test suite to confirm no regressions**

Run: `dotnet test src/tests/WinAdmin.Tests/WinAdmin.Tests.csproj`
Expected: all tests pass (existing `ExtractUserNameFromXml_*` tests untouched and still green).

- [ ] **Step 6: Commit**

```bash
git add src/backend/WinAdmin.Infrastructure/EventLogs/EventLogQueryHelpers.cs src/tests/WinAdmin.Tests/EventLogQueryHelpersTests.cs
git commit -m "feat: add SID-based ExtractUserInfo and IsSystemAccount helpers"
```

---

### Task 2: Wire `ExcludeSystemAccounts` through request → controller → service, remove dead code

**Files:**
- Modify: `src/backend/WinAdmin.Core/Models/EventLogModels.cs`
- Modify: `src/backend/WinAdmin.Api/Controllers/EventLogsController.cs`
- Modify: `src/backend/WinAdmin.Infrastructure/EventLogs/EventLogService.cs`
- Modify: `src/backend/WinAdmin.Infrastructure/EventLogs/EventLogQueryHelpers.cs` (remove now-dead `ExtractUserNameFromXml` + `UserNameFieldPriority`)
- Modify: `src/tests/WinAdmin.Tests/EventLogQueryHelpersTests.cs` (remove the 5 tests for the removed method)

**Interfaces:**
- Consumes: `EventLogQueryHelpers.ExtractUserInfo` and `EventLogQueryHelpers.IsSystemAccount` from Task 1.
- Produces: `EventLogQueryRequest.ExcludeSystemAccounts` (bool) and the `excludeSystemAccounts` query-string parameter on `GET /api/v1/eventlogs/query`. Task 3 (frontend) consumes this query parameter name.

There is no unit-test coverage for `EventLogService` or `EventLogsController` in this codebase (both depend on the live Windows Event Log API / ASP.NET pipeline) — this task is verified by `dotnet build`, the full `dotnet test` run, and a manual smoke check against the running dev servers, consistent with how the rest of `EventLogService` is covered today.

- [ ] **Step 1: Add the request field**

In `src/backend/WinAdmin.Core/Models/EventLogModels.cs`, add a field to `EventLogQueryRequest`:

```csharp
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
    public bool ExcludeSystemAccounts { get; init; }
}
```

- [ ] **Step 2: Add the controller query parameter**

In `src/backend/WinAdmin.Api/Controllers/EventLogsController.cs`, update the `Query` action signature and request construction:

```csharp
    public ActionResult<EventLogQueryResult> Query(
        [FromQuery] string logName,
        [FromQuery] DateTime start,
        [FromQuery] DateTime end,
        [FromQuery] int maxRecords = 200,
        [FromQuery] string? eventIds = null,
        [FromQuery] string? levels = null,
        [FromQuery] string? keyword = null,
        [FromQuery] string? user = null,
        [FromQuery] bool excludeSystemAccounts = false)
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
            ExcludeSystemAccounts = excludeSystemAccounts,
        };
```

- [ ] **Step 3: Use `ExtractUserInfo` + `IsSystemAccount` in the service**

In `src/backend/WinAdmin.Infrastructure/EventLogs/EventLogService.cs`, replace this block:

```csharp
                    string? xml;
                    try { xml = record.ToXml(); }
                    catch (EventLogException) { xml = null; }

                    var user = (xml != null ? EventLogQueryHelpers.ExtractUserNameFromXml(xml) : null)
                        ?? TryTranslateSid(record.UserId);

                    if (!EventLogQueryHelpers.MatchesSubstring(user, request.User))
                        continue;
```

with:

```csharp
                    string? xml;
                    try { xml = record.ToXml(); }
                    catch (EventLogException) { xml = null; }

                    var (extractedName, sid) = xml != null
                        ? EventLogQueryHelpers.ExtractUserInfo(xml)
                        : ((string?)null, (string?)null);
                    var user = extractedName ?? TryTranslateSid(record.UserId);

                    if (request.ExcludeSystemAccounts && EventLogQueryHelpers.IsSystemAccount(sid, user))
                        continue;

                    if (!EventLogQueryHelpers.MatchesSubstring(user, request.User))
                        continue;
```

- [ ] **Step 4: Remove the now-dead `ExtractUserNameFromXml` and `UserNameFieldPriority`**

In `src/backend/WinAdmin.Infrastructure/EventLogs/EventLogQueryHelpers.cs`, delete the `UserNameFieldPriority` field and the entire `ExtractUserNameFromXml` method (superseded by `ExtractUserInfo` from Task 1; no remaining callers after Step 3 above).

- [ ] **Step 5: Remove the tests for the deleted method**

In `src/tests/WinAdmin.Tests/EventLogQueryHelpersTests.cs`, delete these five test methods (all reference the now-removed `ExtractUserNameFromXml`):
`ExtractUserNameFromXml_ReturnsTargetUserName`, `ExtractUserNameFromXml_FallsBackToSubjectUserName_WhenTargetIsDash`, `ExtractUserNameFromXml_FallsBackToAccountName`, `ExtractUserNameFromXml_ReturnsNull_WhenNoMatchingFields`, `ExtractUserNameFromXml_ReturnsNull_OnMalformedXml`.

- [ ] **Step 6: Build and run the full test suite**

Run: `dotnet build src/backend/WinAdmin.Api/WinAdmin.Api.csproj`
Expected: build succeeds, no references to `ExtractUserNameFromXml` remain.

Run: `dotnet test src/tests/WinAdmin.Tests/WinAdmin.Tests.csproj`
Expected: all tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/backend/WinAdmin.Core/Models/EventLogModels.cs \
        src/backend/WinAdmin.Api/Controllers/EventLogsController.cs \
        src/backend/WinAdmin.Infrastructure/EventLogs/EventLogService.cs \
        src/backend/WinAdmin.Infrastructure/EventLogs/EventLogQueryHelpers.cs \
        src/tests/WinAdmin.Tests/EventLogQueryHelpersTests.cs
git commit -m "feat: filter system accounts by SID in event log query, remove dead name-only extractor"
```

---

### Task 3: Frontend — "Только пользователи" switch on the auth preset

**Files:**
- Modify: `src/frontend/src/api/types.ts`
- Modify: `src/frontend/src/pages/EventLogs.tsx`

**Interfaces:**
- Consumes: `excludeSystemAccounts` query parameter from Task 2 (`GET /api/v1/eventlogs/query`).
- No new exports for other frontend code to consume — this is the leaf UI change.

Note: `src/frontend/src/api/client.ts` does not need changes — `eventLogs.query` already forwards its whole `params` object to axios (`http.get(url, { params })`), so any new field on `EventLogQueryParams` is sent automatically.

- [ ] **Step 1: Add the field to `EventLogQueryParams`**

In `src/frontend/src/api/types.ts`, update:

```typescript
export interface EventLogQueryParams {
  logName: string
  start: string
  end: string
  maxRecords?: number
  eventIds?: string
  levels?: string
  keyword?: string
  user?: string
  excludeSystemAccounts?: boolean
}
```

- [ ] **Step 2: Add state, effect, and the switch to `EventLogs.tsx`**

In `src/frontend/src/pages/EventLogs.tsx`:

1. Add `Switch` to the antd import:

```typescript
import {
  Alert, Button, Card, DatePicker, Input, Modal, Select, Space, Switch, Table, Tag, Typography,
} from 'antd'
```

2. Right after `const isCustom = presetKey === 'custom'`, add:

```typescript
  const isAuthPreset = presetKey === 'auth'
```

3. Add new state next to the other filter state (near `const [user, setUser] = useState('')`):

```typescript
  const [excludeSystem, setExcludeSystem] = useState(true)
```

4. In the `useApi` fetcher, add the new param to the `api.eventLogs.query({...})` call:

```typescript
    return api.eventLogs.query({
      logName,
      start: range[0].toISOString(),
      end: range[1].toISOString(),
      maxRecords,
      eventIds: eventIds || undefined,
      levels: levels.length ? levels.join(',') : undefined,
      keyword: keyword || undefined,
      user: user || undefined,
      excludeSystemAccounts: isAuthPreset ? excludeSystem : undefined,
    })
```

5. Add a dedicated effect to refresh when the switch changes, right after the existing `logName` refresh effect (same mount-skip pattern already used there):

```typescript
  const isFirstRenderExcludeSystem = useRef(true)
  useEffect(() => {
    if (isFirstRenderExcludeSystem.current) {
      isFirstRenderExcludeSystem.current = false
      return
    }
    if (isAuthPreset) refresh()
  }, [excludeSystem]) // eslint-disable-line react-hooks/exhaustive-deps
```

6. Add the switch to the filter toolbar `<Space wrap size="middle">`, right after the "Пользователь" `Input.Search`:

```tsx
          {isAuthPreset && (
            <Switch
              checked={excludeSystem}
              onChange={setExcludeSystem}
              checkedChildren="Только пользователи"
              unCheckedChildren="Все записи"
            />
          )}
```

- [ ] **Step 3: Type-check the frontend**

Run: `npm --prefix src/frontend run build`
Expected: TypeScript compiles with no errors.

- [ ] **Step 4: Manual verification**

With the backend (`http://localhost:5099`) and frontend (`http://localhost:5188`) dev servers running:

1. Open `http://localhost:5188`, sign in, navigate to Журналы Windows → Авторизация.
2. Confirm the "Только пользователи" switch is present and checked by default, and that `СИСТЕМА`/`SYSTEM`/`NT AUTHORITY\...`/`*$` rows are absent from the table.
3. Toggle the switch off — confirm system-account rows (e.g. `СИСТЕМА`) now appear and the record count in the page subtitle increases.
4. Switch to another preset (e.g. «Система») — confirm the switch is not shown there and behavior is unchanged from before this feature.

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/api/types.ts src/frontend/src/pages/EventLogs.tsx
git commit -m "feat: add 'only users' switch to hide system accounts on auth event log preset"
```
