import type { MeResponse, TokenResponse } from './types'

export const authApi = {
  login: async (login: string, password: string): Promise<TokenResponse> => {
    const res = await fetch('/api/v1/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ login, password }),
      credentials: 'include',
    })
    if (!res.ok) throw new Error((await res.json().catch(() => ({}))).message ?? 'Ошибка входа')
    return res.json()
  },

  options: async (): Promise<{ directory: boolean }> => {
    const res = await fetch('/api/v1/auth/options')
    return res.ok ? res.json() : { directory: false }
  },

  /** SSO: браузер сам отвечает на 401 Negotiate (Kerberos) для узлов зоны «Интрасеть». */
  windows: async (): Promise<TokenResponse> => {
    const res = await fetch('/api/v1/auth/windows', { credentials: 'include' })
    const body = await res.json().catch(() => ({}))
    if (!res.ok) throw new Error(body?.message ?? 'Вход Windows не выполнен')
    return body as TokenResponse
  },

  refresh: async (): Promise<TokenResponse | null> => {
    const res = await fetch('/api/v1/auth/refresh', {
      method: 'POST',
      credentials: 'include',
    })
    if (!res.ok) return null
    return res.json()
  },

  logout: async (): Promise<void> => {
    await fetch('/api/v1/auth/logout', {
      method: 'POST',
      credentials: 'include',
    })
  },

  me: async (token: string): Promise<MeResponse> => {
    const res = await fetch('/api/v1/me', {
      headers: { Authorization: `Bearer ${token}` },
    })
    if (!res.ok) throw new Error('Unauthorized')
    return res.json()
  },
}
