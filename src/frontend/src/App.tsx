import { useState, useEffect } from 'react'
import { Routes, Route, Navigate } from 'react-router-dom'
import { api, getStoredToken } from './api/client'
import LoginForm from './auth/LoginForm'
import { AuthProvider } from './auth/AuthProvider'
import AppLayout from './components/AppLayout'
import Guard from './components/Guard'
import Dashboard from './pages/Dashboard'
import Disks from './pages/Disks'
import Services from './pages/Services'
import Processes from './pages/Processes'
import Printers from './pages/Printers'
import Power from './pages/Power'
import ApiKeys from './pages/ApiKeys'
import AuditLog from './pages/AuditLog'
import ApiDocs from './pages/ApiDocs'
import Users from './pages/Users'
import Settings from './pages/Settings'
import EventLogs from './pages/EventLogs'
import Applications from './pages/Applications'
import Updates from './pages/Updates'

export default function App() {
  const [authed, setAuthed] = useState(() => Boolean(getStoredToken()))
  const [machine, setMachine] = useState<string>()

  useEffect(() => {
    if (!authed) return
    api.system().then((s) => setMachine(s.hostname)).catch(() => undefined)
  }, [authed])

  const handleLogout = () => {
    setAuthed(false)
    setMachine(undefined)
  }

  if (!authed) return <LoginForm onAuthed={() => setAuthed(true)} />

  return (
    <AuthProvider onLogout={handleLogout}>
      <Routes>
        <Route element={<AppLayout machine={machine} onLogout={handleLogout} />}>
          <Route path="/" element={<Guard perm="system.read" module="system"><Dashboard /></Guard>} />
          <Route path="/disks" element={<Guard perm="system.read" module="system"><Disks /></Guard>} />
          <Route path="/services" element={<Guard perm="services.read" module="services"><Services /></Guard>} />
          <Route path="/processes" element={<Guard perm="processes.read" module="processes"><Processes /></Guard>} />
          <Route path="/printers" element={<Guard perm="printers.read" module="printers"><Printers /></Guard>} />
          <Route path="/power" element={<Guard perm="power.manage" module="power"><Power /></Guard>} />
          <Route path="/software/apps" element={<Guard perm="software.read" module="software"><Applications /></Guard>} />
          <Route path="/software/updates" element={<Guard perm="software.read" module="software"><Updates /></Guard>} />
          <Route path="/logs/:presetKey" element={<Guard perm="eventlogs.read" module="eventlogs"><EventLogs /></Guard>} />
          <Route path="/cp/apikeys" element={<Guard perm="platform.apikeys.manage"><ApiKeys /></Guard>} />
          <Route path="/cp/audit" element={<Guard perm="platform.audit.read"><AuditLog /></Guard>} />
          <Route path="/cp/users" element={<Guard perm="platform.users.manage"><Users /></Guard>} />
          <Route path="/cp/settings" element={<Guard perm={['platform.network.manage', 'eventlogs.manage']}><Settings /></Guard>} />
          <Route path="/docs" element={<ApiDocs />} />
          <Route path="*" element={<Navigate to="/" replace />} />
        </Route>
      </Routes>
    </AuthProvider>
  )
}
