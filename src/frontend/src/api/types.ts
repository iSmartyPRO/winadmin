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
  scopes: string[]
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
  scopes: string[]
  createdAt: string
  isActive: boolean
}

export interface CreateUserRequest {
  login: string
  password: string
  scopes: string[]
}

export interface MeResponse {
  login: string
  scopes: string[]
}
