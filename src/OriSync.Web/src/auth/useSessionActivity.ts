import { useEffect, useRef } from 'react'
import { postJson } from './api'
import type { Session } from './types'

export function useSessionActivity(session: Session | null, onExpired: () => void) {
  const lastSentAt = useRef(0)

  useEffect(() => {
    if (!session) return

    async function recordActivity() {
      const now = Date.now()
      if (now - lastSentAt.current < 60_000) return
      lastSentAt.current = now

      try {
        const response = await postJson('/api/auth/activity')
        if (response.status === 401) onExpired()
      } catch {
        // Temporary network failure must not manufacture a local logout. The
        // database remains authoritative and will enforce expiry later.
      }
    }

    const options: AddEventListenerOptions = { passive: true }
    const handler = () => void recordActivity()
    window.addEventListener('pointerdown', handler, options)
    window.addEventListener('keydown', handler)
    window.addEventListener('touchstart', handler, options)
    return () => {
      window.removeEventListener('pointerdown', handler)
      window.removeEventListener('keydown', handler)
      window.removeEventListener('touchstart', handler)
    }
  }, [session, onExpired])
}
