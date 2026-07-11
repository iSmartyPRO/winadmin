export function formatBytes(bytes: number, digits = 1): string {
  if (!bytes || bytes < 0) return '0 B'
  const units = ['B', 'KB', 'MB', 'GB', 'TB', 'PB']
  const i = Math.floor(Math.log(bytes) / Math.log(1024))
  const idx = Math.min(i, units.length - 1)
  return `${(bytes / Math.pow(1024, idx)).toFixed(idx === 0 ? 0 : digits)} ${units[idx]}`
}

export function formatRate(bytesPerSec: number): string {
  return `${formatBytes(bytesPerSec)}/s`
}

/** Парсит .NET TimeSpan ("d.hh:mm:ss" или "hh:mm:ss") в человекочитаемую строку. */
export function formatUptime(ts?: string): string {
  if (!ts) return '—'
  const dayMatch = ts.match(/^(?:(\d+)\.)?(\d{2}):(\d{2}):(\d{2})/)
  if (!dayMatch) return ts
  const [, d, h, m] = dayMatch
  const days = d ? parseInt(d, 10) : 0
  const hours = parseInt(h, 10)
  const mins = parseInt(m, 10)
  const parts: string[] = []
  if (days) parts.push(`${days} д`)
  if (hours) parts.push(`${hours} ч`)
  parts.push(`${mins} мин`)
  return parts.join(' ')
}

export function formatDateTime(iso?: string): string {
  if (!iso) return '—'
  const d = new Date(iso)
  return d.toLocaleString('ru-RU')
}

/** Цвет полосы заполнения: зелёный → жёлтый → красный. */
export function usageColor(percent: number): string {
  if (percent >= 90) return '#ff4d4f'
  if (percent >= 75) return '#faad14'
  return '#1668dc'
}
