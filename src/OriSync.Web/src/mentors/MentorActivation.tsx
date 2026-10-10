import { useEffect, useState, type FormEvent } from 'react'
import { getError, postJson } from '../auth/api'
import type { MentorActivationDetails } from './types'

const passwordPattern = '(?=.*[a-z])(?=.*[A-Z])(?=.*[^A-Za-z0-9]).{8,}'

export function MentorActivation({ invitationId, token }: { invitationId: number; token: string }) {
  const [details, setDetails] = useState<MentorActivationDetails | null>(null)
  const [firstName, setFirstName] = useState('')
  const [surname, setSurname] = useState('')
  const [mentorNumber, setMentorNumber] = useState('')
  const [password, setPassword] = useState('')
  const [confirmation, setConfirmation] = useState('')
  const [loading, setLoading] = useState(true)
  const [submitting, setSubmitting] = useState(false)
  const [complete, setComplete] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    let current = true
    async function validate() {
      const response = await postJson('/api/auth/mentor-activation/validate', { invitationId, token })
      if (!current) return
      if (response.ok) setDetails((await response.json()) as MentorActivationDetails)
      else setError(await getError(response))
      setLoading(false)
    }
    void validate()
    return () => { current = false }
  }, [invitationId, token])

  async function activate(event: FormEvent) {
    event.preventDefault()
    if (password !== confirmation) {
      setError('The passwords do not match.')
      return
    }
    setSubmitting(true)
    setError('')
    const response = await postJson('/api/auth/mentor-activation/accept', {
      invitationId,
      token,
      firstName,
      surname,
      mentorNumber,
      password,
    })
    if (response.ok) setComplete(true)
    else setError(await getError(response))
    setSubmitting(false)
  }

  if (loading) return <main className="activation-layout" aria-live="polite">Validating activation link…</main>
  if (complete) {
    return (
      <main className="activation-layout">
        <section className="auth-card">
          <p className="form-kicker">Mentor account</p>
          <h1>Activation complete</h1>
          <p className="success-message" role="status">Your account is active. You can now sign in.</p>
          <a className="primary-link" href="/">Go to sign in</a>
        </section>
      </main>
    )
  }
  if (!details) {
    return (
      <main className="activation-layout">
        <section className="auth-card">
          <h1>Activation unavailable</h1>
          <p className="error-message" role="alert">{error || 'The activation link is invalid.'}</p>
        </section>
      </main>
    )
  }

  return (
    <main className="activation-layout">
      <form className="auth-card activation-card" onSubmit={(event) => void activate(event)}>
        <div>
          <p className="form-kicker">Mentor invitation</p>
          <h1>Activate your account</h1>
        </div>
        <div className="activation-group">
          <span className="colour-dot" style={{ backgroundColor: details.groupBadgeColor }} aria-hidden="true" />
          {details.groupName} Group
        </div>
        {error && <p className="error-message" role="alert">{error}</p>}
        <label className="field">Login email<input type="email" value={details.email} disabled /></label>
        <label className="field">First name<input required maxLength={100} autoComplete="given-name" value={firstName} onChange={(event) => setFirstName(event.target.value)} /></label>
        <label className="field">Surname<input required maxLength={100} autoComplete="family-name" value={surname} onChange={(event) => setSurname(event.target.value)} /></label>
        <label className="field">Mentor number<input required inputMode="numeric" pattern="2[0-9]{8}" maxLength={9} value={mentorNumber} onChange={(event) => setMentorNumber(event.target.value.replace(/\D/g, '').slice(0, 9))} /></label>
        <label className="field">Password<input required type="password" minLength={8} pattern={passwordPattern} autoComplete="new-password" value={password} onChange={(event) => setPassword(event.target.value)} /></label>
        <label className="field">Confirm password<input required type="password" minLength={8} pattern={passwordPattern} autoComplete="new-password" value={confirmation} onChange={(event) => setConfirmation(event.target.value)} /></label>
        <p className="form-note">Use at least 8 characters with an uppercase letter, lowercase letter and special character.</p>
        <button className="primary-button" type="submit" disabled={submitting}>{submitting ? 'Activating…' : 'Activate account'}</button>
      </form>
    </main>
  )
}
