import { useCallback, useEffect, useRef, useState } from 'react'
import { api } from '../api/client'
import type { SoftwareJob } from '../api/types'

type UseSoftwareJobOptions = {
  onTerminal?: (job: SoftwareJob) => void
}

const terminalStatuses = new Set<SoftwareJob['status']>(['Succeeded', 'Failed'])

export function useSoftwareJob({ onTerminal }: UseSoftwareJobOptions = {}) {
  const [job, setJob] = useState<SoftwareJob>()
  const [drawerOpen, setDrawerOpen] = useState(false)
  const timeoutRef = useRef<number | undefined>(undefined)
  const polledJobIdRef = useRef<string | undefined>(undefined)
  const terminalNotifiedRef = useRef<string | undefined>(undefined)
  const onTerminalRef = useRef(onTerminal)

  useEffect(() => {
    onTerminalRef.current = onTerminal
  }, [onTerminal])

  const clearPolling = useCallback(() => {
    if (timeoutRef.current !== undefined) {
      window.clearTimeout(timeoutRef.current)
      timeoutRef.current = undefined
    }
    polledJobIdRef.current = undefined
  }, [])

  const handleJobUpdate = useCallback((nextJob: SoftwareJob) => {
    setJob(nextJob)

    if (!terminalStatuses.has(nextJob.status)) return false

    clearPolling()
    if (nextJob.status === 'Succeeded' && terminalNotifiedRef.current !== nextJob.id) {
      terminalNotifiedRef.current = nextJob.id
      onTerminalRef.current?.(nextJob)
    }
    return true
  }, [clearPolling])

  const pollRef = useRef<(jobId: string) => Promise<void>>(async () => {})

  pollRef.current = async (jobId: string) => {
    const nextJob = await api.software.getJob(jobId)
    if (polledJobIdRef.current !== jobId) return

    const isTerminal = handleJobUpdate(nextJob)
    if (!isTerminal && polledJobIdRef.current === jobId) {
      timeoutRef.current = window.setTimeout(() => {
        void pollRef.current(jobId)
      }, 1000)
    }
  }

  const poll = useCallback((jobId: string) => pollRef.current(jobId), [])

  const startPolling = useCallback((nextJob: SoftwareJob) => {
    clearPolling()
    terminalNotifiedRef.current = undefined
    setDrawerOpen(true)
    polledJobIdRef.current = nextJob.id

    const isTerminal = handleJobUpdate(nextJob)
    if (!isTerminal) {
      timeoutRef.current = window.setTimeout(() => {
        void poll(nextJob.id)
      }, 1000)
    }
  }, [clearPolling, handleJobUpdate, poll])

  const restoreActive = useCallback(async () => {
    const activeJob = await api.software.getActiveJob()
    if (activeJob) startPolling(activeJob)
  }, [startPolling])

  const minimize = useCallback(() => {
    setDrawerOpen(false)
  }, [])

  const closeDrawer = useCallback(() => {
    setDrawerOpen(false)
    if (job && terminalStatuses.has(job.status)) {
      setJob(undefined)
    }
  }, [job])

  useEffect(() => clearPolling, [clearPolling])

  return {
    job,
    drawerOpen,
    startPolling,
    restoreActive,
    closeDrawer,
    minimize,
  }
}
