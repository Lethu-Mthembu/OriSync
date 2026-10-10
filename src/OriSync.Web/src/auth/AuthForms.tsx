import { useState } from 'react'
import type { FormEvent } from 'react'
import { getError, postJson } from './api'
import type { AuthView, Session } from './types'

const passwordPattern = '(?=.*[a-z])(?=.*[A-Z])(?=.*[^A-Za-z0-9]).{8,}'

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
  const [email, setEmail] = useState('')
  const [code, setCode] = useState('')
  const [password, setPassword] = useState('')
  const [confirmation, setConfirmation] = useState('')
  const [codeRequested, setCodeRequested] = useState(false)
  const [message, setMessage] = useState('')
  const [error, setError] = useState('')
  const [submitting, setSubmitting] = useState(false)

  async function requestCode(event: FormEvent) {
    event.preventDefault()
    setError('')
    setMessage('')
    setSubmitting(true)
    try {
      const response = await postJson('/api/auth/mentor-password-reset/request', {
        mentorNumber,
        email,
      })
      if (!response.ok) {
        setError(await getError(response))
        return
      }
      setCodeRequested(true)
      setMessage('If the mentor account exists, a reset code was sent. It expires in 5 minutes.')
    } catch {
      setError('OriSync could not reach the server. Try again.')
    } finally {
      setSubmitting(false)
    }
  }

  async function resetPassword(event: FormEvent) {
    event.preventDefault()
    setError('')
    setMessage('')
    if (password !== confirmation) {
      setError('The passwords do not match.')
      return
    }

    setSubmitting(true)
    try {
      const response = await postJson('/api/auth/mentor-password-reset/complete', {
        mentorNumber,
        email,
        code,
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

  function useDifferentDetails() {
    setCodeRequested(false)
    setCode('')
    setPassword('')
    setConfirmation('')
    setMessage('')
    setError('')
  }

  return (
    <form
      className="auth-card"
      onSubmit={(event) => void (codeRequested ? resetPassword(event) : requestCode(event))}
    >
      <div>
        <p className="form-kicker">Mentor account</p>
        <h2>Reset password</h2>
        <p className="form-note">
          Enter your nine-digit mentor number and login email. OriSync will send a one-time code to that email.
        </p>
      </div>
      <Field label="Mentor number" value={mentorNumber} onChange={setMentorNumber} inputMode="numeric" pattern="2[0-9]{8}" />
      <Field label="Login email" type="email" value={email} onChange={setEmail} autoComplete="username" />
      {codeRequested && (
        <>
          <Field label="Reset code" value={code} onChange={setCode} inputMode="numeric" autoComplete="one-time-code" pattern="[0-9]{6}" maximumLength={6} />
          <Field label="New password" type="password" value={password} onChange={setPassword} autoComplete="new-password" minimumLength={8} pattern={passwordPattern} />
          <Field label="Confirm new password" type="password" value={confirmation} onChange={setConfirmation} autoComplete="new-password" minimumLength={8} pattern={passwordPattern} />
          <p className="form-note">Use at least 8 characters with an uppercase letter, lowercase letter and special character.</p>
        </>
      )}
      <ErrorMessage message={error} />
      {message && <p className="success-message" role="status">{message}</p>}
      <button className="primary-button" disabled={submitting} type="submit">
        {submitting ? (codeRequested ? 'Resetting…' : 'Sending…') : (codeRequested ? 'Reset password' : 'Send reset code')}
      </button>
      {codeRequested && (
        <button className="secondary-button" type="button" onClick={useDifferentDetails}>
          Use different details
        </button>
      )}
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
      <Field label="New password" type="password" value={password} onChange={setPassword} autoComplete="new-password" minimumLength={8} pattern={passwordPattern} />
      <p className="form-note">Use at least 8 characters with an uppercase letter, lowercase letter and special character.</p>
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
      <Field label="New password" type="password" value={newPassword} onChange={setNewPassword} autoComplete="new-password" minimumLength={8} pattern={passwordPattern} />
      <Field label="Confirm new password" type="password" value={confirmation} onChange={setConfirmation} autoComplete="new-password" minimumLength={8} pattern={passwordPattern} />
      <p className="form-note">Use at least 8 characters with an uppercase letter, lowercase letter and special character.</p>
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
  maximumLength,
  ...inputProps
}: {
  label: string
  value: string
  onChange: (value: string) => void
  type?: string
  minimumLength?: number
  maximumLength?: number
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
        maxLength={maximumLength}
        onChange={(event) => onChange(event.target.value)}
      />
    </label>
  )
}

function ErrorMessage({ message }: { message: string }) {
  return message ? <p className="error-message" role="alert">{message}</p> : null
}
