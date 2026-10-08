import { useCallback, useEffect, useState } from 'react'
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
import { OrientationSettings } from './orientation/OrientationSettings'

function App() {
  const [session, setSession] = useState<Session | null>(null)
  const [checkingSession, setCheckingSession] = useState(true)
  const [view, setView] = useState<AuthView>('login')

  useEffect(() => {
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
  }, [])

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

  if (checkingSession) return <LoadingScreen />

  return (
    <div className="app-shell">
      <Header />
      {session && view !== 'change-password' ? (
        <SignedInPanel
          session={session}
          onChangePassword={() => setView('change-password')}
          onLogout={() => void logout()}
        />
      ) : (
        <main className="auth-layout">
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
        </main>
      )}
    </div>
  )
}

function Header() {
  return (
    <header className="top-bar">
      <span className="brand-mark" aria-hidden="true">O</span>
      <span className="brand-name">OriSync</span>
    </header>
  )
}

function LoadingScreen() {
  return (
    <div className="app-shell">
      <Header />
      <main className="loading-panel" aria-live="polite">Checking your session…</main>
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
    return (
      <>
        <div className="account-toolbar">
          <span>{session.firstName} {session.surname}</span>
          <button className="secondary-button" type="button" onClick={onChangePassword}>Change password</button>
          <button className="primary-button" type="button" onClick={onLogout}>Sign out</button>
        </div>
        <OrientationSettings />
      </>
    )
  }

  return (
    <main className="signed-in-panel">
      <p className="eyebrow">{session.role} account</p>
      <h1>Welcome, {session.firstName}</h1>
      <p>
        Authentication is active. Your operational dashboard will be added in
        its dedicated feature branch.
      </p>
      {session.role === 'Mentor' && session.groupId === null && (
        <p className="account-warning">No group is assigned. Contact the administrator.</p>
      )}
      <div className="button-row">
        <button className="secondary-button" type="button" onClick={onChangePassword}>Change password</button>
        <button className="primary-button" type="button" onClick={onLogout}>Sign out</button>
      </div>
    </main>
  )
}

export default App
