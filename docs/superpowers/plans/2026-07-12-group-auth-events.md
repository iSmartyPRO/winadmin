# Group Related Auth Events Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** On the "Авторизация" Event Log preset, collapse bursts of related raw Security-log records (e.g. the 6-event UAC split-token logon cluster) into a single expandable table row with a human-readable summary ("Вошёл (admin)", "Неудачный вход", etc.), instead of showing every raw record as its own row.

**Architecture:** Purely a frontend presentation change — no backend/API changes. A new pure function groups the already-fetched, already-filtered `EventLogEntryDto[]` into a mixed array of single entries and groups, based on a simple heuristic (same `user`, consecutive, ≤2s gap). `EventLogs.tsx` renders that mixed array with antd Table's built-in `expandable` row feature.

**Tech Stack:** React 19 + TypeScript, Ant Design 6 `Table` (`expandable`), Vite/tsc for type-checking (no frontend test runner exists in this repo — verification is `npm run build` + manual browser check against real Windows Security log data).

## Global Constraints

- No backend/API changes. `EventLogQueryParams`/`EventLogQueryResult`/`maxRecords`/`truncated` and the `data.entries.length` count shown in the page subtitle stay exactly as they are today.
- Grouping applies ONLY when `presetKey === 'auth'`. All other presets keep rendering `data?.entries ?? []` directly, unchanged.
- Group boundary rule: consecutive entries (adjacent in the array as received) group together only if `entry.user === prev.user` AND the time gap between them is ≤ 2000ms. Any user change or larger gap starts a new group.
- A "group" of size 1 is not wrapped — it stays a plain `EventLogEntryDto` and renders exactly as before (no expand arrow, click opens the existing `Modal`).
- Group summary label priority (first match wins): contains `4625` → "Неудачный вход"; contains `4624` and `4672` → "Вошёл (admin)"; contains `4624` (no `4672`) → "Вошёл"; contains `4634` or `4647` and no `4624` → "Вышел"; otherwise → "`N` событий".
- Clicking a group row toggles expand/collapse (not the detail `Modal`); clicking an entry inside the expanded group (or any single, ungrouped row) opens the existing detail `Modal`, unchanged.

---

### Task 1: Pure grouping utility

**Files:**
- Create: `src/frontend/src/utils/authEventGrouping.ts`

**Interfaces:**
- Produces: `groupAuthEvents(entries: EventLogEntryDto[]): AuthEventRow[]`, `isAuthEventGroup(row: AuthEventRow): row is AuthEventGroup`, and the types `AuthEventGroup`, `AuthEventRow`. Task 2 imports all four from this file.

There is no test runner configured for the frontend in this repo (`src/frontend/package.json` has no `test` script, no test framework in `devDependencies`) — this task is verified via `npm --prefix src/frontend run build` (TypeScript type-checks the file) and by hand-tracing the function against the real event cluster documented below, not via an automated test suite.

- [ ] **Step 1: Write the file**

Create `src/frontend/src/utils/authEventGrouping.ts` with exactly this content:

```typescript
// src/frontend/src/utils/authEventGrouping.ts
import type { EventLogEntryDto } from '../api/types'

const GROUP_WINDOW_MS = 2000

export interface AuthEventGroupSummary {
  label: string
  color: string
}

export interface AuthEventGroup {
  isGroup: true
  key: string
  entries: EventLogEntryDto[]
  timeCreated: string
  user?: string
  summary: AuthEventGroupSummary
}

export type AuthEventRow = EventLogEntryDto | AuthEventGroup

export function isAuthEventGroup(row: AuthEventRow): row is AuthEventGroup {
  return (row as AuthEventGroup).isGroup === true
}

function summarize(entries: EventLogEntryDto[]): AuthEventGroupSummary {
  const ids = new Set(entries.map((e) => e.eventId))
  if (ids.has(4625)) return { label: 'Неудачный вход', color: 'red' }
  if (ids.has(4624) && ids.has(4672)) return { label: 'Вошёл (admin)', color: 'green' }
  if (ids.has(4624)) return { label: 'Вошёл', color: 'green' }
  if ((ids.has(4634) || ids.has(4647)) && !ids.has(4624)) return { label: 'Вышел', color: 'blue' }
  return { label: `${entries.length} событий`, color: 'default' }
}

function toRow(run: EventLogEntryDto[]): AuthEventRow {
  if (run.length === 1) return run[0]
  return {
    isGroup: true,
    key: `group-${run[0].id}`,
    entries: run,
    timeCreated: run[0].timeCreated,
    user: run[0].user,
    summary: summarize(run),
  }
}

/**
 * Группирует подряд идущие записи одного пользователя, если разрыв между соседними
 * по времени не превышает GROUP_WINDOW_MS. Порядок entries не меняется — вход
 * ожидается уже отсортированным так, как его нужно отобразить.
 */
export function groupAuthEvents(entries: EventLogEntryDto[]): AuthEventRow[] {
  const rows: AuthEventRow[] = []
  let run: EventLogEntryDto[] = []

  for (const entry of entries) {
    const prev = run[run.length - 1]
    const sameRun = prev !== undefined
      && entry.user === prev.user
      && Math.abs(new Date(entry.timeCreated).getTime() - new Date(prev.timeCreated).getTime()) <= GROUP_WINDOW_MS

    if (sameRun) {
      run.push(entry)
    } else {
      if (run.length > 0) rows.push(toRow(run))
      run = [entry]
    }
  }
  if (run.length > 0) rows.push(toRow(run))

  return rows
}
```

- [ ] **Step 2: Type-check**

Run: `npm --prefix src/frontend run build`
Expected: TypeScript compiles with no errors (this file has no consumers yet, so it only needs to compile standalone-correctly).

- [ ] **Step 3: Hand-trace against the real event cluster**

This is the recorded real data from `docs/superpowers/specs/2026-07-12-group-auth-events-design.md` (Security log, 2026-07-12, `excludeSystemAccounts=true` already applied so only `iNB` events remain):

| time | eventId | user |
|---|---|---|
| 08:57:15.247 | 4648 | iNB |
| 08:57:15.247 | 4624 | iNB |
| 08:57:15.247 | 4624 | iNB |
| 08:57:15.247 | 4672 | iNB |
| 08:57:15.247 | 4634 | iNB |
| 08:57:15.247 | 4634 | iNB |

Confirm by reading the code that `groupAuthEvents` on this input produces exactly one `AuthEventGroup` with `entries.length === 6` and `summary === { label: 'Вошёл (admin)', color: 'green' }` (because the `ids` set is `{4648,4624,4672,4634}`, which contains both `4624` and `4672`, so the second priority rule fires). Note this confirmation in your task report — no code changes needed if it checks out.

- [ ] **Step 4: Commit**

```bash
git add src/frontend/src/utils/authEventGrouping.ts
git commit -m "feat: add pure auth-event grouping utility for the UI"
```

---

### Task 2: Wire grouping into EventLogs.tsx

**Files:**
- Modify: `src/frontend/src/pages/EventLogs.tsx`

**Interfaces:**
- Consumes: `groupAuthEvents`, `isAuthEventGroup`, `AuthEventRow`, `AuthEventGroup` from `../utils/authEventGrouping` (Task 1).
- This is the leaf UI task — nothing downstream consumes new exports from this file.

- [ ] **Step 1: Import the grouping utility**

Add to the top of `src/frontend/src/pages/EventLogs.tsx`, alongside the existing imports:

```typescript
import { groupAuthEvents, isAuthEventGroup } from '../utils/authEventGrouping'
import type { AuthEventRow } from '../utils/authEventGrouping'
```

- [ ] **Step 2: Compute the grouped table data**

Right after the `const isFirstRenderExcludeSystem = useRef(true)` effect block (after its closing `}, [excludeSystem])` line), add:

```typescript
  const tableData: AuthEventRow[] = isAuthPreset
    ? groupAuthEvents(data?.entries ?? [])
    : (data?.entries ?? [])
```

- [ ] **Step 3: Retype and update the columns array**

Replace the existing `const columns: ColumnsType<EventLogEntryDto> = [...]` block with:

```typescript
  const columns: ColumnsType<AuthEventRow> = [
    {
      title: 'Время', dataIndex: 'timeCreated', width: 180,
      render: (d: string) => formatDateTime(d),
    },
    {
      title: 'Уровень', dataIndex: 'levelDisplayName', width: 130,
      render: (l: string | undefined, r: AuthEventRow) => {
        if (isAuthEventGroup(r)) return <Tag color={r.summary.color}>{r.summary.label}</Tag>
        return <Tag color={LEVEL_COLORS[r.level ?? ''] ?? 'default'}>{l ?? r.level ?? '—'}</Tag>
      },
    },
    {
      title: 'Источник', dataIndex: 'providerName', width: 220, ellipsis: true,
      render: (v: string | undefined, r: AuthEventRow) => {
        if (isAuthEventGroup(r)) return <Text type="secondary">—</Text>
        return v ?? <Text type="secondary">—</Text>
      },
    },
    {
      title: 'ID события', dataIndex: 'eventId', width: 100,
      render: (v: number | undefined, r: AuthEventRow) => (isAuthEventGroup(r) ? `${r.entries.length} событий` : v),
    },
    {
      title: 'Пользователь', dataIndex: 'user', width: 160,
      render: (u?: string) => u ?? <Text type="secondary">—</Text>,
    },
    {
      title: 'Сообщение', dataIndex: 'message', ellipsis: true,
      render: (m: string | undefined, r: AuthEventRow) => {
        if (isAuthEventGroup(r)) return <Text type="secondary">—</Text>
        return m ?? <Text type="secondary">—</Text>
      },
    },
  ]
```

- [ ] **Step 4: Replace the `<Table>` block**

Replace the existing:

```tsx
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
```

with:

```tsx
      <Card variant="borderless" className="sp-glass">
        <Table
          rowKey={(record) => (isAuthEventGroup(record) ? record.key : record.id)}
          size="small"
          columns={columns}
          dataSource={tableData}
          loading={loading}
          pagination={{ pageSize: 25, showSizeChanger: true }}
          onRow={(record) => ({
            onClick: () => {
              if (!isAuthEventGroup(record)) setDetail(record)
            },
          })}
          expandable={{
            expandRowByClick: true,
            rowExpandable: (record) => isAuthEventGroup(record),
            expandedRowRender: (record) => {
              if (!isAuthEventGroup(record)) return null
              return (
                <Table
                  size="small"
                  showHeader={false}
                  pagination={false}
                  rowKey="id"
                  dataSource={record.entries}
                  onRow={(entry) => ({ onClick: () => setDetail(entry) })}
                  columns={[
                    { dataIndex: 'timeCreated', width: 180, render: (d: string) => formatDateTime(d) },
                    {
                      dataIndex: 'levelDisplayName', width: 130,
                      render: (l: string | undefined, r: EventLogEntryDto) => (
                        <Tag color={LEVEL_COLORS[r.level ?? ''] ?? 'default'}>{l ?? r.level ?? '—'}</Tag>
                      ),
                    },
                    { dataIndex: 'eventId', width: 100 },
                    {
                      dataIndex: 'message', ellipsis: true,
                      render: (m?: string) => m ?? <Text type="secondary">—</Text>,
                    },
                  ]}
                />
              )
            },
          }}
        />
      </Card>
```

- [ ] **Step 5: Type-check**

Run: `npm --prefix src/frontend run build`
Expected: TypeScript compiles with no errors.

- [ ] **Step 6: Manual verification against the real event cluster**

With the backend (`http://localhost:5099`) and frontend dev servers running (start them if not: `ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/backend/WinAdmin.Api --urls http://localhost:5099` and `npm --prefix src/frontend run dev`):

1. Open the app, sign in, navigate to Журналы Windows → Авторизация.
2. Set the time range (via the `RangePicker`) to cover `2026-07-12 08:57:00`–`09:00:00`.
3. Confirm the 08:57:15 cluster renders as ONE row with an expand arrow, tag "Вошёл (admin)" (green), and "6 событий" in the ID column.
4. Click the row (not specifically the arrow) — confirm it expands/collapses, and does NOT open the detail `Modal`.
5. Expand it and click one of the inner rows — confirm the detail `Modal` opens with that specific record's data.
6. Confirm a lone, non-clustered event elsewhere in the list (group size 1) still opens the `Modal` directly on row click, with no expand arrow — unchanged from before this feature.
7. Switch to another preset (e.g. «Система») — confirm no expand arrows appear and behavior is exactly as before this feature.

- [ ] **Step 7: Commit**

```bash
git add src/frontend/src/pages/EventLogs.tsx
git commit -m "feat: group related auth events into expandable rows in the UI"
```
