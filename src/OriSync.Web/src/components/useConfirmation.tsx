import { useCallback, useEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { ConfirmationModal, type ConfirmationOptions } from './ConfirmationModal'

interface PendingConfirmation extends ConfirmationOptions {
  resolve: (approved: boolean) => void
}

export function useConfirmation() {
  const [pending, setPending] = useState<PendingConfirmation | null>(null)
  const pendingRef = useRef<PendingConfirmation | null>(null)

  const finish = useCallback((approved: boolean) => {
    const current = pendingRef.current
    pendingRef.current = null
    setPending(null)
    current?.resolve(approved)
  }, [])

  useEffect(() => () => {
    pendingRef.current?.resolve(false)
    pendingRef.current = null
  }, [])

  const confirm = useCallback((options: ConfirmationOptions) => new Promise<boolean>((resolve) => {
    pendingRef.current?.resolve(false)
    const next = { ...options, resolve }
    pendingRef.current = next
    setPending(next)
  }), [])

  return {
    confirm,
    confirmationDialog: pending
      ? createPortal(<ConfirmationModal options={pending} onDecision={finish} />, document.body)
      : null,
  }
}
