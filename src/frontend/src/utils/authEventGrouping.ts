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
