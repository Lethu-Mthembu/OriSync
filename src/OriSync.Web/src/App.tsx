import { useEffect, useState } from 'react'
import './App.css'

type ConnectionState = 'checking' | 'connected' | 'unavailable'

interface ApiStatus {
  service: string
  status: string
}

function App() {
  const [connection, setConnection] = useState<ConnectionState>('checking')

  useEffect(() => {
    const controller = new AbortController()

    async function checkApi() {
      try {
        const response = await fetch('/api/status', { signal: controller.signal })
        if (!response.ok) {
          throw new Error(`Unexpected status: ${response.status}`)
        }

        const payload = (await response.json()) as ApiStatus
        setConnection(
          payload.service === 'OriSync.Api' && payload.status === 'ready'
            ? 'connected'
            : 'unavailable',
        )
      } catch (error) {
        if (error instanceof DOMException && error.name === 'AbortError') {
          return
        }

        setConnection('unavailable')
      }
    }

    void checkApi()
    return () => controller.abort()
  }, [])

  const statusText = {
    checking: 'Checking API connection',
    connected: 'API connected',
    unavailable: 'API unavailable',
  }[connection]

  return (
    <div className="app-shell">
      <header className="top-bar">
        <span className="brand-mark" aria-hidden="true">
          O
        </span>
        <span className="brand-name">OriSync</span>
      </header>

      <main className="welcome-panel">
        <p className="eyebrow">Tshwane University of Technology</p>
        <h1>First-year orientation attendance</h1>
        <p className="description">
          The application foundation is ready. Authentication, student
          management and attendance features will be added in their dedicated
          feature branches.
        </p>

        <div
          className={`connection-state connection-state--${connection}`}
          aria-live="polite"
        >
          <span className="status-dot" aria-hidden="true" />
          {statusText}
        </div>
      </main>
    </div>
  )
}

export default App
