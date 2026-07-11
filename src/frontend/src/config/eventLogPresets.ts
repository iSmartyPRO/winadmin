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
