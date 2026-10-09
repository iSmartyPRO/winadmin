import type { MeResponse, TokenResponse } from './types'

export const authApi = {
  login: async (login: string, password: string): Promise<TokenResponse> => {
    const res = await fetch('/api/v1/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ login, password }),
      credentials: 'include',
    })
    if (!res.ok) throw new Error((await res.json()).message ?? 'Ошибка входа')
    return res.json()
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
