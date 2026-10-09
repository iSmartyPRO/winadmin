// Типы, зеркалящие DTO бэкенда (WinAdmin.Core.Models).

export interface NetworkAdapterInfo {
  name: string
  description?: string
  macAddress?: string
  ipAddresses: string[]
  isUp: boolean
  speedBitsPerSec: number
}

export interface SystemInfo {
  hostname: string
  domain?: string
  osName: string
  osVersion: string
  osArchitecture: string
  manufacturer?: string
  model?: string
  serialNumber?: string
  biosVersion?: string
  cpuName?: string
  cpuPhysicalCores: number
  cpuLogicalCores: number
  totalMemoryBytes: number
  lastBootTime?: string
  uptime?: string
  networkAdapters: NetworkAdapterInfo[]
}

export interface SystemMetrics {
  cpuUsagePercent: number
  memoryTotalBytes: number
  memoryUsedBytes: number
  memoryUsagePercent: number
  networkBytesSentPerSec: number
  networkBytesReceivedPerSec: number
  timestamp: string
}

export interface DiskVolume {
  drive: string
  label?: string
  fileSystem?: string
  driveType: string
  totalBytes: number
  freeBytes: number
  usedBytes: number
  usedPercent: number
}

export interface PhysicalDisk {
  model: string
  interfaceType?: string
  mediaType?: string
  sizeBytes: number
  partitions: number
  serialNumber?: string
  volumes: DiskVolume[]
}

export interface ServiceInfo {
  name: string
  displayName: string
  status: string
  startType: string
  canStop: boolean
  canPauseAndContinue: boolean
  account?: string
}

export interface ProcessInfo {
  pid: number
  name: string
  mainWindowTitle?: string
  workingSetBytes: number
  threadCount: number
  startTime?: string
  hasWindow: boolean
}

export interface PrinterInfo {
  name: string
  portName?: string
  driverName?: string
  location?: string
  isDefault: boolean
  isShared: boolean
  workOffline: boolean
  status: string
  queuedJobs: number
}

export interface OperationResult {
  success: boolean
  message: string
}

export interface ApiKeyDto {
  id: string
  name: string
  roles: string[]
  createdAt: string
  expiresAt?: string
  lastUsedAt?: string
  isRevoked: boolean
  hint?: string
}

export interface CreatedApiKey {
  key: ApiKeyDto
  plaintextKey: string
}

export interface AuditEntryDto {
  id: number
  timestamp: string
  actor: string
  action: string
  target?: string
  success: boolean
  details?: string
  sourceIp?: string
}

export interface PowerRequest {
  delaySeconds: number
  comment?: string
  force: boolean
}

export interface TokenResponse {
  accessToken: string
  expiresIn: number
}

export interface UserDto {
  id: string
  login: string
  roles: string[]
  createdAt: string
  isActive: boolean
}

export interface CreateUserRequest {
  login: string
  password: string
  roleIds: string[]
}

export interface MeResponse {
  actor: string
  principal: string
  /** право → область (null — без ограничений) */
  permissions: Record<string, string[] | null>
  modules: { id: string; title: string }[]
}

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
  ipAddress?: string
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
  excludeSystemAccounts?: boolean
  excludeLogonTypes?: string
}

export interface EventLogQueryResult {
  entries: EventLogEntryDto[]
  truncated: boolean
  scannedCount: number
}

export interface ExcludedUserDto {
  id: string
  userName: string
  createdAt: string
}

export type NetworkMode = 'Local' | 'Network'

export interface NetworkSettingsDto {
  mode: NetworkMode
  port: number
  allow: string[]
  url: string
  firewallRule: boolean
}

export interface UpdateNetworkSettingsRequest {
  mode: NetworkMode
  port: number
  allow: string[]
}

export type SoftwareJobType = 'UninstallApp' | 'UninstallUpdate' | 'RollbackUpdate'
export type SoftwareJobStatus = 'Queued' | 'Running' | 'Succeeded' | 'Failed'

export interface InstalledApp {
  id: string
  name: string
  version?: string
  publisher?: string
  installDate?: string
  installLocation?: string
  sizeBytes?: number
  source: 'Registry' | 'Store' | string
  isSystem: boolean
  canUninstall: boolean
  uninstallString?: string
}

export interface InstalledUpdate {
  id: string
  kbArticle?: string
  title: string
  description?: string
  installedOn?: string
  canUninstall: boolean
  canRollback: boolean
}

export interface SoftwareJob {
  id: string
  type: SoftwareJobType
  targetId: string
  targetName: string
  status: SoftwareJobStatus
  progressPercent?: number | null
  statusMessage: string
  error?: string
  startedAt: string
  finishedAt?: string
}

export type PrincipalType = 'LocalUser' | 'AdUser' | 'AdGroup' | 'ApiKey'

export interface PermissionDto {
  id: string
  title: string
  description?: string
  scopable: boolean
  dangerous: boolean
}

export interface PermissionGroupDto {
  id: string
  title: string
  scopable: boolean
  scopeTitle?: string
  permissions: PermissionDto[]
}

export interface RoleGrantDto {
  permissionId: string
  scope: string[] | null
}

export interface RoleDto {
  id: string
  name: string
  description?: string
  isBuiltin: boolean
  permissions: RoleGrantDto[]
  assignmentCount: number
}

export interface SaveRoleRequest {
  name: string
  description?: string
  permissions: RoleGrantDto[]
}

export interface RoleAssignmentDto {
  id: string
  roleId: string
  roleName: string
  principalType: PrincipalType
  principalId: string
  displayName: string
  createdAt: string
}

export interface SettingsField {
  name: string
  title: string
  kind: 'string' | 'number' | 'boolean' | 'stringList' | 'secret'
}

export interface ModuleDto {
  id: string
  title: string
  description?: string
  enabled: boolean
  available: boolean
  unavailableReason?: string
  scopable: boolean
  scopeTitle?: string
  permissions: PermissionDto[]
  settingsSchema: SettingsField[]
  settings: Record<string, unknown>
}

export interface DirectorySettings {
  enabled: boolean
  domain: string | null
  server: string | null
  baseDn: string | null
  useLdaps: boolean
}

export interface DirectoryTestStep { name: string; ok: boolean; message: string }

export interface DirectoryEntry {
  sid: string
  kind: 'user' | 'group'
  samAccountName: string
  displayName: string | null
  upn: string | null
  enabled: boolean
}

export type AdWriteMode = 'ServiceAccount' | 'ProcessAccount'

export interface AdStructureSettings {
  rootOu: string | null
  usersOuName: string
  hiddenOus: string[]
  writeMode: AdWriteMode
  writeLogin: string | null
  hasWritePassword: boolean
}

export interface SaveAdStructure extends Omit<AdStructureSettings, 'hasWritePassword'> {
  writePassword?: string | null
}

export type CheckStatus = 'Ok' | 'Warning' | 'Failed' | 'Skipped'

export interface CheckResult { code: string; title: string; status: CheckStatus; message: string; fix: string | null }

export interface EnvironmentReport { moduleId: string; at: string; overall: CheckStatus; results: CheckResult[] }
