import { createContext, useContext, useEffect, useState, useCallback, type ReactNode } from 'react'
import { authApi } from '../api/authApi'
import { authEvents, clearStoredToken, getStoredToken } from '../api/client'
import type { MeResponse } from '../api/types'

interface AuthState {
  user: MeResponse | null
  token: string
  /** Есть ли право (с любой областью). */
  can: (permission: string) => boolean
  /** Включён ли модуль. */
  moduleOn: (moduleId: string) => boolean
  logout: () => Promise<void>
}

const AuthContext = createContext<AuthState | null>(null)

export function useAuth(): AuthState {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used inside AuthProvider')
  return ctx
}

export function AuthProvider({ children, onLogout }: { children: ReactNode; onLogout: () => void }) {
  const [user, setUser] = useState<MeResponse | null>(null)
  const token = getStoredToken()

  useEffect(() => {
    authApi.me(token).then(setUser).catch(() => setUser(null))
  }, [token])

  useEffect(() => {
    const onUnauthorized = () => {
      clearStoredToken()
      onLogout()
    }
    authEvents.addEventListener('unauthorized', onUnauthorized)
    return () => authEvents.removeEventListener('unauthorized', onUnauthorized)
  }, [onLogout])

  const logout = useCallback(async () => {
    await authApi.logout()
    clearStoredToken()
    onLogout()
  }, [onLogout])

  const can = useCallback((permission: string) => Boolean(user && permission in user.permissions), [user])
  const moduleOn = useCallback((id: string) => Boolean(user?.modules.some((m) => m.id === id)), [user])

  return (
    <AuthContext.Provider value={{ user, token, can, moduleOn, logout }}>
      {children}
    </AuthContext.Provider>
  )
}
