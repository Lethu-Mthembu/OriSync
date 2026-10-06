import '@testing-library/jest-dom/vitest'
import { render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import App from './App'

describe('App', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('shows the application name and reports a connected API', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue({
        ok: true,
        json: async () => ({ service: 'OriSync.Api', status: 'ready' }),
      }),
    )

    render(<App />)

    expect(screen.getByText('OriSync')).toBeInTheDocument()
    expect(await screen.findByText('API connected')).toBeInTheDocument()
  })

  it('reports an unavailable API when the request fails', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new Error('offline')))

    render(<App />)

    expect(await screen.findByText('API unavailable')).toBeInTheDocument()
  })
})
