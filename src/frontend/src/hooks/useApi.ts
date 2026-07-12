import { useCallback, useEffect, useRef, useState } from 'react'

interface State<T> {
  data?: T
  loading: boolean
  error?: string
}

/**
 * Загружает данные и опционально перезапрашивает с интервалом (polling).
 * immediate=false — не грузить при монтировании (ждём ручной refresh); polling
 * при этом не запускается до первого вызова.
 */
export function useApi<T>(fetcher: () => Promise<T>, pollMs?: number, immediate = true) {
  const [state, setState] = useState<State<T>>({ loading: immediate })
  const fetcherRef = useRef(fetcher)
  fetcherRef.current = fetcher

  const refresh = useCallback(async (silent = false) => {
    if (!silent) setState((s) => ({ ...s, loading: true }))
    try {
      const data = await fetcherRef.current()
      setState({ data, loading: false })
    } catch (e) {
      const msg = (e as { message?: string })?.message ?? 'Ошибка запроса'
      setState((s) => ({ ...s, loading: false, error: msg }))
    }
  }, [])

  useEffect(() => {
    if (!immediate) return
    refresh()
    if (!pollMs) return
    const id = setInterval(() => refresh(true), pollMs)
    return () => clearInterval(id)
  }, [refresh, pollMs, immediate])

  return { ...state, refresh }
}
