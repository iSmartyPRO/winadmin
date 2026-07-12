import axios from 'axios'
import { authApi } from './authApi'
import type {
  ApiKeyDto, AuditEntryDto, CreatedApiKey, MeResponse, OperationResult,
  PhysicalDisk, PrinterInfo, ProcessInfo, ServiceInfo,
  SystemInfo, SystemMetrics, PowerRequest, UserDto,
  CreateUserRequest, TokenResponse,
  EventLogQueryParams, EventLogQueryResult,
  ExcludedUserDto,
  InstalledApp, InstalledUpdate, SoftwareJob,
} from './types'

// ── JWT storage (sessionStorage — очищается при закрытии вкладки) ──
const TOKEN_KEY = 'wa_token'
export const getStoredToken = () => sessionStorage.getItem(TOKEN_KEY) ?? ''
export const setStoredToken = (token: string) => sessionStorage.setItem(TOKEN_KEY, token)
export const clearStoredToken = () => sessionStorage.removeItem(TOKEN_KEY)

// ── API Key storage (legacy) ──
const KEY_STORAGE = 'sp_api_key'
export const getStoredKey = () => localStorage.getItem(KEY_STORAGE) ?? ''
export const setStoredKey = (key: string) => localStorage.setItem(KEY_STORAGE, key)
export const clearStoredKey = () => localStorage.removeItem(KEY_STORAGE)

export const http = axios.create({ baseURL: '/api/v1' })

// Request interceptor — добавляет JWT или API key
http.interceptors.request.use((config) => {
  const token = getStoredToken()
  if (token) {
    config.headers['Authorization'] = `Bearer ${token}`
  } else {
    const key = getStoredKey()
    if (key) config.headers['X-API-Key'] = key
  }
  return config
})

// Флаг чтобы не делать несколько refresh одновременно
let refreshPromise: Promise<string | null> | null = null

export const authEvents = new EventTarget()

// Response interceptor — при 401 пробует refresh
http.interceptors.response.use(
  (r) => r,
  async (error) => {
    const originalRequest = error.config
    if (error?.response?.status === 401 && !originalRequest._retry && getStoredToken()) {
      originalRequest._retry = true
      if (!refreshPromise) {
        refreshPromise = authApi.refresh().then((resp) => {
          refreshPromise = null
          if (!resp) { clearStoredToken(); return null }
          setStoredToken(resp.accessToken)
          return resp.accessToken
        })
      }
      const newToken = await refreshPromise
      if (!newToken) {
        authEvents.dispatchEvent(new Event('unauthorized'))
        return Promise.reject(error)
      }
      originalRequest.headers['Authorization'] = `Bearer ${newToken}`
      return http(originalRequest)
    }
    if (error?.response?.status === 401) {
      authEvents.dispatchEvent(new Event('unauthorized'))
    }
    return Promise.reject(error)
  },
)

// ── Эндпоинты ───────────────────────────────────────────────────
export const api = {
  system: () => http.get<SystemInfo>('/system').then((r) => r.data),
  metrics: () => http.get<SystemMetrics>('/system/metrics').then((r) => r.data),
  disks: () => http.get<PhysicalDisk[]>('/disks').then((r) => r.data),

  services: () => http.get<ServiceInfo[]>('/services').then((r) => r.data),
  controlService: (name: string, action: 'start' | 'stop' | 'restart') =>
    http.post<OperationResult>(`/services/${encodeURIComponent(name)}/${action}`).then((r) => r.data),

  processes: () => http.get<ProcessInfo[]>('/processes').then((r) => r.data),
  killProcess: (pid: number) => http.delete<OperationResult>(`/processes/${pid}`).then((r) => r.data),

  printers: () => http.get<PrinterInfo[]>('/printers').then((r) => r.data),
  controlPrinter: (name: string, action: 'pause' | 'resume' | 'purge') =>
    http.post<OperationResult>(`/printers/${encodeURIComponent(name)}/${action}`).then((r) => r.data),

  reboot: (req: PowerRequest) => http.post<OperationResult>('/power/reboot', req).then((r) => r.data),
  shutdown: (req: PowerRequest) => http.post<OperationResult>('/power/shutdown', req).then((r) => r.data),
  cancelPower: () => http.post<OperationResult>('/power/cancel').then((r) => r.data),

  apiKeys: () => http.get<ApiKeyDto[]>('/apikeys').then((r) => r.data),
  availableScopes: () => http.get<string[]>('/apikeys/scopes').then((r) => r.data),
  createKey: (name: string, scopes: string[], expiresAt?: string) =>
    http.post<CreatedApiKey>('/apikeys', { name, scopes, expiresAt }).then((r) => r.data),
  revokeKey: (id: string) => http.delete<OperationResult>(`/apikeys/${id}`).then((r) => r.data),

  audit: (limit = 300) => http.get<AuditEntryDto[]>('/audit', { params: { limit } }).then((r) => r.data),

  users: () => http.get<UserDto[]>('/users').then((r) => r.data),
  createUser: (req: CreateUserRequest) => http.post<UserDto>('/users', req).then((r) => r.data),
  updateUserScopes: (id: string, scopes: string[]) =>
    http.put(`/users/${id}/scopes`, { scopes }),
  changeUserPassword: (id: string, newPassword: string) =>
    http.put(`/users/${id}/password`, { newPassword }),
  setUserActive: (id: string, isActive: boolean) =>
    http.put(`/users/${id}/active`, { isActive }),
  deleteUser: (id: string) => http.delete(`/users/${id}`),

  eventLogs: {
    logNames: () => http.get<string[]>('/eventlogs/lognames').then((r) => r.data),
    query: (params: EventLogQueryParams) =>
      http.get<EventLogQueryResult>('/eventlogs/query', { params }).then((r) => r.data),
  },

  settings: {
    excludedUsers: () =>
      http.get<ExcludedUserDto[]>('/settings/excluded-users').then((r) => r.data),
    addExcludedUser: (userName: string) =>
      http.post<ExcludedUserDto>('/settings/excluded-users', { userName }).then((r) => r.data),
    removeExcludedUser: (id: string) => http.delete(`/settings/excluded-users/${id}`),
  },

  software: {
    applications: () => http.get<InstalledApp[]>('/software/applications').then((r) => r.data),
    uninstallApp: (id: string) =>
      http.post<SoftwareJob>(`/software/applications/${encodeURIComponent(id)}/uninstall`).then((r) => r.data),
    updates: () => http.get<InstalledUpdate[]>('/software/updates').then((r) => r.data),
    uninstallUpdate: (id: string) =>
      http.post<SoftwareJob>(`/software/updates/${encodeURIComponent(id)}/uninstall`).then((r) => r.data),
    rollbackUpdate: (id: string) =>
      http.post<SoftwareJob>(`/software/updates/${encodeURIComponent(id)}/rollback`).then((r) => r.data),
    getJob: (jobId: string) => http.get<SoftwareJob>(`/software/jobs/${jobId}`).then((r) => r.data),
    getActiveJob: () =>
      http.get<SoftwareJob>('/software/jobs/active').then((r) => (r.status === 204 ? null : r.data)),
  },
}

// Re-export MeResponse, TokenResponse for consumers
export type { MeResponse, TokenResponse }
