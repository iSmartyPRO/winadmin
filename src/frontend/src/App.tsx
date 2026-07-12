import { useState, useEffect } from 'react'
import { Routes, Route, Navigate } from 'react-router-dom'
import { api, getStoredToken } from './api/client'
import LoginForm from './auth/LoginForm'
import { AuthProvider } from './auth/AuthProvider'
import AppLayout from './components/AppLayout'
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
          <Route path="/" element={<Dashboard />} />
          <Route path="/disks" element={<Disks />} />
          <Route path="/services" element={<Services />} />
          <Route path="/processes" element={<Processes />} />
          <Route path="/printers" element={<Printers />} />
          <Route path="/power" element={<Power />} />
          <Route path="/software/apps" element={<Applications />} />
          <Route path="/software/updates" element={<Updates />} />
          <Route path="/logs/:presetKey" element={<EventLogs />} />
          <Route path="/cp/apikeys" element={<ApiKeys />} />
          <Route path="/cp/audit" element={<AuditLog />} />
          <Route path="/cp/users" element={<Users />} />
          <Route path="/cp/settings" element={<Settings />} />
          <Route path="/docs" element={<ApiDocs />} />
          <Route path="*" element={<Navigate to="/" replace />} />
        </Route>
      </Routes>
    </AuthProvider>
  )
}
