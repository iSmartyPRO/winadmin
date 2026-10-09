import type { ReactNode } from 'react'
import { Spin } from 'antd'
import { useAuth } from '../auth/AuthProvider'
import NoAccess from './NoAccess'

/** Раздел виден, только если модуль включён и есть хотя бы одно из прав. */
export default function Guard({ perm, module, children }: { perm: string | string[]; module?: string; children: ReactNode }) {
  const { user, can, moduleOn } = useAuth()
  if (!user) return <Spin style={{ display: 'block', margin: '80px auto' }} />
  const perms = Array.isArray(perm) ? perm : [perm]
  if ((module && !moduleOn(module)) || !perms.some(can)) return <NoAccess />
  return <>{children}</>
}
