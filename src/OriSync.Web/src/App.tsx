import { lazy, Suspense, useCallback, useEffect, useState } from 'react'
import './App.css'
import {
  AdminRecoveryForm,
  ChangePasswordForm,
  LoginForm,
  MentorResetForm,
} from './auth/AuthForms'
import { postJson } from './auth/api'
import type { AuthView, Session } from './auth/types'
import { useSessionActivity } from './auth/useSessionActivity'
import { requestRearCamera } from './attendance/camera'
import { AdminDashboard } from './dashboards/AdminDashboard'
import { MentorDashboard } from './dashboards/MentorDashboard'
import { WorkspaceShell, type WorkspaceNavigationItem } from './layout/WorkspaceShell'
import { MentorActivation } from './mentors/MentorActivation'
import { MentorManagement } from './mentors/MentorManagement'
import { OrientationSettings } from './orientation/OrientationSettings'
import { RegisterReports } from './registers/RegisterReports'
import { AdminStudentDirectory } from './students/AdminStudentDirectory'
import { StudentManagement } from './students/StudentManagement'
import { PageSkeleton } from '@/components/ui/page-skeleton'

let attendanceScannerModule: ReturnType<typeof importAttendanceScanner> | null = null

function importAttendanceScanner() {
  return import('./attendance/AttendanceScanner')
}

function preloadAttendanceScanner() {
  attendanceScannerModule ??= importAttendanceScanner()
  return attendanceScannerModule
}

const AttendanceScanner = lazy(async () => {
  const module = await preloadAttendanceScanner()
  return { default: module.AttendanceScanner }
})

function App() {
  const [activation] = useState(() => readActivationLink())
  const [session, setSession] = useState<Session | null>(null)
  const [checkingSession, setCheckingSession] = useState(() => activation === null)
  const [view, setView] = useState<AuthView>('login')

  useEffect(() => {
    if (activation) {
      window.history.replaceState(null, '', '/activate-mentor')
      return
    }

    const controller = new AbortController()

    async function restoreSession() {
      try {
        const response = await fetch('/api/auth/session', {
          credentials: 'same-origin',
          cache: 'no-store',
          signal: controller.signal,
        })

        if (response.ok) {
          const restored = (await response.json()) as Session
          setSession(restored)
          if (restored.mustChangePassword) setView('change-password')
        }
      } catch (error) {
        if (!(error instanceof DOMException && error.name === 'AbortError')) {
          setSession(null)
        }
      } finally {
        if (!controller.signal.aborted) setCheckingSession(false)
      }
    }

    void restoreSession()
    return () => controller.abort()
  }, [activation])

  const handleExpired = useCallback(() => {
    setSession(null)
    setView('login')
  }, [])
  useSessionActivity(session, handleExpired)

  async function logout() {
    await postJson('/api/auth/logout')
    handleExpired()
  }

  function acceptSession(nextSession: Session) {
    setSession(nextSession)
    setView(nextSession.mustChangePassword ? 'change-password' : 'login')
  }

  if (activation) {
    return <div className="app-shell"><Header /><MentorActivation invitationId={activation.invitationId} token={activation.token} /></div>
  }
  if (checkingSession) return <LoadingScreen />

  return (
    <div className="app-shell">
      {session && view !== 'change-password' ? (
        <SignedInPanel
          session={session}
          onChangePassword={() => setView('change-password')}
          onLogout={() => void logout()}
        />
      ) : (
        <><Header /><main className="auth-layout">
          <section className="auth-introduction">
            <p className="eyebrow">Tshwane University of Technology</p>
            <h1>First-year orientation attendance</h1>
            <p>
              Administrators and mentors use one secure portal. Students do not
              sign in to OriSync.
            </p>
          </section>

          {view === 'login' && <LoginForm onSignedIn={acceptSession} onView={setView} />}
          {view === 'mentor-reset' && <MentorResetForm onView={setView} />}
          {view === 'admin-recovery' && <AdminRecoveryForm onView={setView} />}
          {view === 'change-password' && session && (
            <ChangePasswordForm
              forced={session.mustChangePassword}
              onChanged={handleExpired}
              onCancel={session.mustChangePassword ? undefined : () => setView('login')}
            />
          )}
        </main></>
      )}
    </div>
  )
}

function Header() {
  return (
    <header className="top-bar">
      <img className="top-bar-tut-logo" src="/tut-logo.png" alt="Tshwane University of Technology" />
      <span className="top-bar-divider" aria-hidden="true" />
      <span className="brand-mark" aria-hidden="true">O</span>
      <span className="brand-name">OriSync</span>
    </header>
  )
}

function LoadingScreen() {
  return (
    <div className="app-shell">
      <Header />
      <main className="loading-panel"><PageSkeleton label="Checking your session" variant="auth" /></main>
    </div>
  )
}

function SignedInPanel({
  session,
  onChangePassword,
  onLogout,
}: {
  session: Session
  onChangePassword: () => void
  onLogout: () => void
}) {
  if (session.role === 'Admin') {
    return <AdminWorkspace session={session} onChangePassword={onChangePassword} onLogout={onLogout} />
  }

  return <MentorWorkspace session={session} onChangePassword={onChangePassword} onLogout={onLogout} />
}

function AdminWorkspace({ session, onChangePassword, onLogout }: { session: Session; onChangePassword: () => void; onLogout: () => void }) {
  const [section, setSection] = useState<'dashboard' | 'students' | 'mentors' | 'reports' | 'orientations'>('dashboard')
  type AdminSection = typeof section
  const navigation: WorkspaceNavigationItem<AdminSection>[] = [
    { id: 'dashboard', label: 'Dashboard', icon: 'dashboard' },
    { id: 'students', label: 'Students', icon: 'students' },
    { id: 'mentors', label: 'Mentors', icon: 'mentors' },
    { id: 'reports', label: 'Registers', icon: 'registers' },
    { id: 'orientations', label: 'Orientations and groups', icon: 'settings' },
  ]
  return (
    <WorkspaceShell session={session} activeSection={section} items={navigation} onSelect={setSection} onChangePassword={onChangePassword} onLogout={onLogout}>
      {section === 'dashboard' ? <AdminDashboard /> : section === 'students' ? <AdminStudentDirectory /> : section === 'mentors' ? <MentorManagement /> : section === 'reports' ? <RegisterReports /> : <OrientationSettings />}
    </WorkspaceShell>
  )
}

function MentorWorkspace({ session, onChangePassword, onLogout }: { session: Session; onChangePassword: () => void; onLogout: () => void }) {
  const [section, setSection] = useState<'dashboard' | 'students' | 'scanner'>('dashboard')
  const [initialCameraStream, setInitialCameraStream] = useState<Promise<MediaStream> | null>(null)

  useEffect(() => {
    if (session.groupId === null) return

    const idleWindow = window as unknown as {
      requestIdleCallback?: (callback: IdleRequestCallback) => number
      cancelIdleCallback?: (handle: number) => void
    }
    if (idleWindow.requestIdleCallback && idleWindow.cancelIdleCallback) {
      const task = idleWindow.requestIdleCallback(() => void preloadAttendanceScanner())
      return () => idleWindow.cancelIdleCallback?.(task)
    }

    const task = globalThis.setTimeout(() => void preloadAttendanceScanner(), 1500)
    return () => globalThis.clearTimeout(task)
  }, [session.groupId])

  function openScanner() {
    const camera = requestRearCamera()
    // The scanner renders its own permission error. Attach a handler now so a
    // fast rejection cannot become an unhandled promise while the chunk loads.
    void camera.catch(() => undefined)
    setInitialCameraStream(camera)
    void preloadAttendanceScanner()
    setSection('scanner')
  }

  if (section === 'scanner') {
    return <Suspense fallback={<main className="loading-panel"><PageSkeleton label="Loading scanner" variant="scanner" /></main>}><AttendanceScanner initialCameraStream={initialCameraStream!} onClose={() => { setInitialCameraStream(null); setSection('students') }} /></Suspense>
  }

  type MentorSection = Exclude<typeof section, 'scanner'>
  const navigation: WorkspaceNavigationItem<MentorSection>[] = session.groupId === null
    ? [{ id: 'dashboard', label: 'Dashboard', icon: 'dashboard' }]
    : [
        { id: 'dashboard', label: 'Dashboard', icon: 'dashboard' },
        { id: 'students', label: 'Students', icon: 'students' },
      ]

  return (
    <WorkspaceShell session={session} activeSection={section as MentorSection} items={navigation} onSelect={setSection} onChangePassword={onChangePassword} onLogout={onLogout}>
      {session.groupId === null
        ? <main className="signed-in-panel"><p className="eyebrow">Mentor account</p><h1>Welcome, {session.firstName}</h1><p className="account-warning">No group is assigned. Contact the administrator.</p></main>
        : section === 'dashboard' ? <MentorDashboard onScan={openScanner} onWarmScanner={() => void preloadAttendanceScanner()} /> : <StudentManagement />}
    </WorkspaceShell>
  )
}

function readActivationLink() {
  if (window.location.pathname !== '/activate-mentor') return null
  const values = new URLSearchParams(window.location.hash.slice(1))
  const invitationId = Number(values.get('invitation'))
  const token = values.get('token') ?? ''
  return Number.isSafeInteger(invitationId) && invitationId > 0 && token
    ? { invitationId, token }
    : null
}

export default App
