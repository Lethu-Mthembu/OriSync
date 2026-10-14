import { useEffect, useRef } from 'react'

export interface ConfirmationOptions {
  title: string
  message: string
  confirmLabel?: string
  tone?: 'default' | 'danger'
}

export function ConfirmationModal({
  options,
  onDecision,
}: {
  options: ConfirmationOptions
  onDecision: (approved: boolean) => void
}) {
  const confirmButton = useRef<HTMLButtonElement>(null)

  useEffect(() => {
    confirmButton.current?.focus()
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') onDecision(false)
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [onDecision])

  return (
    <div className="modal-backdrop" role="presentation" onMouseDown={(event) => {
      if (event.target === event.currentTarget) onDecision(false)
    }}>
      <section className="confirmation-modal" role="alertdialog" aria-modal="true" aria-labelledby="confirmation-title" aria-describedby="confirmation-message">
        <p className="form-kicker">Confirm action</p>
        <h2 id="confirmation-title">{options.title}</h2>
        <p id="confirmation-message">{options.message}</p>
        <div className="button-row compact-row">
          <button
            ref={confirmButton}
            className={options.tone === 'danger' ? 'danger-button' : 'primary-button'}
            type="button"
            onClick={() => onDecision(true)}
          >
            {options.confirmLabel ?? 'Confirm'}
          </button>
          <button className="secondary-button" type="button" onClick={() => onDecision(false)}>Cancel</button>
        </div>
      </section>
    </div>
  )
}
