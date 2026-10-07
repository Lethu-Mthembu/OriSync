import { useState } from 'react'
import type { FormEvent } from 'react'
import { getError, postJson } from './api'
import type { AuthView, Session } from './types'

export function LoginForm({
  onSignedIn,
  onView,
}: {
  onSignedIn: (session: Session) => void
  onView: (view: AuthView) => void
}) {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState('')
  const [submitting, setSubmitting] = useState(false)

  async function submit(event: FormEvent) {
    event.preventDefault()
    setError('')
    setSubmitting(true)
    try {
      const response = await postJson('/api/auth/login', { email, password })
      if (!response.ok) {
        setError(await getError(response))
        return
      }
      onSignedIn((await response.json()) as Session)
    } catch {
      setError('OriSync could not reach the server. Try again.')
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <form className="auth-card" onSubmit={(event) => void submit(event)}>
      <div>
        <p className="form-kicker">Secure portal</p>
        <h2>Sign in</h2>
      </div>
      <Field label="Email" type="email" value={email} onChange={setEmail} autoComplete="username" />
      <Field label="Password" type="password" value={password} onChange={setPassword} autoComplete="current-password" minimumLength={8} />
      <ErrorMessage message={error} />
      <button className="primary-button" disabled={submitting} type="submit">
        {submitting ? 'Signing in…' : 'Sign in'}
      </button>
      <div className="auth-links">
        <button type="button" onClick={() => onView('mentor-reset')}>Mentor forgot password</button>
        <button type="button" onClick={() => onView('admin-recovery')}>Admin recovery</button>
      </div>
    </form>
  )
}

export function MentorResetForm({ onView }: { onView: (view: AuthView) => void }) {
  const [mentorNumber, setMentorNumber] = useState('')
  const [password, setPassword] = useState('')
  const [confirmation, setConfirmation] = useState('')
  const [message, setMessage] = useState('')
  const [error, setError] = useState('')
  const [submitting, setSubmitting] = useState(false)

  async function submit(event: FormEvent) {
    event.preventDefault()
    setError('')
    setMessage('')
    if (password !== confirmation) {
      setError('The passwords do not match.')
      return
    }

    setSubmitting(true)
    try {
      const response = await postJson('/api/auth/reset-mentor-password', {
        mentorNumber,
        newPassword: password,
      })
      if (!response.ok) {
        setError(await getError(response))
        return
      }
      setMessage('Password reset. You can now sign in.')
    } catch {
      setError('OriSync could not reach the server. Try again.')
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <form className="auth-card" onSubmit={(event) => void submit(event)}>
      <div>
        <p className="form-kicker">Mentor account</p>
        <h2>Reset password</h2>
        <p className="form-note">Enter the nine-digit mentor number supplied by the administrator.</p>
      </div>
      <Field label="Mentor number" value={mentorNumber} onChange={setMentorNumber} inputMode="numeric" pattern="2[0-9]{8}" />
      <Field label="New password" type="password" value={password} onChange={setPassword} autoComplete="new-password" minimumLength={8} />
      <Field label="Confirm new password" type="password" value={confirmation} onChange={setConfirmation} autoComplete="new-password" minimumLength={8} />
      <ErrorMessage message={error} />
      {message && <p className="success-message" role="status">{message}</p>}
      <button className="primary-button" disabled={submitting} type="submit">
        {submitting ? 'Resetting…' : 'Reset password'}
      </button>
      <button className="secondary-button" type="button" onClick={() => onView('login')}>Back to sign in</button>
    </form>
  )
}

export function AdminRecoveryForm({ onView }: { onView: (view: AuthView) => void }) {
  const [email, setEmail] = useState('')
  const [recoveryCode, setRecoveryCode] = useState('')
  const [password, setPassword] = useState('')
  const [replacementCode, setReplacementCode] = useState('')
  const [error, setError] = useState('')
  const [submitting, setSubmitting] = useState(false)

  async function submit(event: FormEvent) {
    event.preventDefault()
    setError('')
    setSubmitting(true)
    try {
      const response = await postJson('/api/auth/recover-admin', {
        email,
        recoveryCode,
        newPassword: password,
      })
      if (!response.ok) {
        setError(await getError(response))
        return
      }
      const payload = (await response.json()) as { recoveryCode: string }
      setReplacementCode(payload.recoveryCode)
    } catch {
      setError('OriSync could not reach the server. Try again.')
    } finally {
      setSubmitting(false)
    }
  }

  if (replacementCode) {
    return (
      <section className="auth-card">
        <p className="form-kicker">Admin recovery</p>
        <h2>Store the replacement code</h2>
        <p className="form-note">This code is shown once. Store it outside OriSync before leaving this page.</p>
        <code className="recovery-code">{replacementCode}</code>
        <button className="primary-button" type="button" onClick={() => onView('login')}>Return to sign in</button>
      </section>
    )
  }

  return (
    <form className="auth-card" onSubmit={(event) => void submit(event)}>
      <div>
        <p className="form-kicker">Administrator</p>
        <h2>Recover account</h2>
      </div>
      <Field label="Admin email" type="email" value={email} onChange={setEmail} autoComplete="username" />
      <Field label="Recovery code" value={recoveryCode} onChange={setRecoveryCode} autoComplete="off" />
      <Field label="New password" type="password" value={password} onChange={setPassword} autoComplete="new-password" minimumLength={8} />
      <ErrorMessage message={error} />
      <button className="primary-button" disabled={submitting} type="submit">
        {submitting ? 'Recovering…' : 'Recover account'}
      </button>
      <button className="secondary-button" type="button" onClick={() => onView('login')}>Back to sign in</button>
    </form>
  )
}

export function ChangePasswordForm({
  forced,
  onChanged,
  onCancel,
}: {
  forced: boolean
  onChanged: () => void
  onCancel?: () => void
}) {
  const [currentPassword, setCurrentPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [confirmation, setConfirmation] = useState('')
  const [error, setError] = useState('')
  const [submitting, setSubmitting] = useState(false)

  async function submit(event: FormEvent) {
    event.preventDefault()
    setError('')
    if (newPassword !== confirmation) {
      setError('The new passwords do not match.')
      return
    }

    setSubmitting(true)
    try {
      const response = await postJson('/api/auth/change-password', {
        currentPassword,
        newPassword,
      })
      if (!response.ok) {
        setError(await getError(response))
        return
      }
      onChanged()
    } catch {
      setError('OriSync could not reach the server. Try again.')
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <form className="auth-card" onSubmit={(event) => void submit(event)}>
      <div>
        <p className="form-kicker">Account security</p>
        <h2>{forced ? 'Change your temporary password' : 'Change password'}</h2>
        <p className="form-note">Changing your password signs out every active session.</p>
      </div>
      <Field label="Current password" type="password" value={currentPassword} onChange={setCurrentPassword} autoComplete="current-password" minimumLength={8} />
      <Field label="New password" type="password" value={newPassword} onChange={setNewPassword} autoComplete="new-password" minimumLength={8} />
      <Field label="Confirm new password" type="password" value={confirmation} onChange={setConfirmation} autoComplete="new-password" minimumLength={8} />
      <ErrorMessage message={error} />
      <button className="primary-button" disabled={submitting} type="submit">
        {submitting ? 'Changing…' : 'Change password'}
      </button>
      {onCancel && <button className="secondary-button" type="button" onClick={onCancel}>Cancel</button>}
    </form>
  )
}

function Field({
  label,
  value,
  onChange,
  type = 'text',
  minimumLength,
  ...inputProps
}: {
  label: string
  value: string
  onChange: (value: string) => void
  type?: string
  minimumLength?: number
  autoComplete?: string
  inputMode?: 'numeric' | 'text'
  pattern?: string
}) {
  return (
    <label className="field">
      <span>{label}</span>
      <input
        {...inputProps}
        required
        type={type}
        value={value}
        minLength={minimumLength}
        onChange={(event) => onChange(event.target.value)}
      />
    </label>
  )
}

function ErrorMessage({ message }: { message: string }) {
  return message ? <p className="error-message" role="alert">{message}</p> : null
}
