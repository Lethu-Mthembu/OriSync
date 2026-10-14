import { useId, useState } from 'react'
import { AppIcon } from '../layout/AppIcon'

export function PasswordField({
  label,
  value,
  onChange,
  autoComplete,
  minimumLength = 8,
  pattern,
}: {
  label: string
  value: string
  onChange: (value: string) => void
  autoComplete: string
  minimumLength?: number
  pattern?: string
}) {
  const id = useId()
  const [visible, setVisible] = useState(false)

  return (
    <div className="field">
      <label htmlFor={id}>{label}</label>
      <span className="password-input-wrap">
        <input
          id={id}
          required
          type={visible ? 'text' : 'password'}
          value={value}
          minLength={minimumLength}
          pattern={pattern}
          autoComplete={autoComplete}
          onChange={(event) => onChange(event.target.value)}
        />
        <button
          className="password-visibility-button"
          type="button"
          aria-label={`${visible ? 'Hide' : 'Show'} ${label.toLowerCase()}`}
          aria-pressed={visible}
          onClick={() => setVisible((current) => !current)}
        >
          <AppIcon name={visible ? 'eyeOff' : 'eye'} size={19} />
        </button>
      </span>
    </div>
  )
}
