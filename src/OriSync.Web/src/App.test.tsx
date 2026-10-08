import '@testing-library/jest-dom/vitest'
import { cleanup, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import App from './App'

const signedInSession = {
  accountId: 1,
  firstName: 'Jane',
  surname: 'Mentor',
  role: 'Mentor',
  groupId: null,
  mustChangePassword: false,
  absoluteExpiresAt: '2026-10-08T08:00:00Z',
}

describe('App authentication', () => {
  afterEach(() => {
    cleanup()
    vi.unstubAllGlobals()
  })

  it('shows the shared sign-in page when no session exists', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response(null, 401)))

    render(<App />)

    expect(await screen.findByRole('heading', { name: 'Sign in' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Mentor forgot password' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Admin recovery' })).toBeInTheDocument()
  })

  it('restores an authenticated session', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response(signedInSession)))

    render(<App />)

    expect(await screen.findByRole('heading', { name: 'Welcome, Jane' })).toBeInTheDocument()
    expect(screen.getByText('No group is assigned. Contact the administrator.')).toBeInTheDocument()
  })

  it('signs in with a CSRF-protected request', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(response(null, 401))
      .mockResolvedValueOnce(response({ requestToken: 'csrf-token' }))
      .mockResolvedValueOnce(response(signedInSession))
    vi.stubGlobal('fetch', fetchMock)
    const user = userEvent.setup()

    render(<App />)
    await user.type(await screen.findByLabelText('Email'), 'mentor@orisync.test')
    await user.type(screen.getByLabelText('Password'), 'password')
    await user.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(await screen.findByRole('heading', { name: 'Welcome, Jane' })).toBeInTheDocument()
    expect(fetchMock).toHaveBeenLastCalledWith(
      '/api/auth/login',
      expect.objectContaining({
        method: 'POST',
        headers: expect.objectContaining({ 'X-CSRF-TOKEN': 'csrf-token' }),
      }),
    )
  })

  it('opens the email OTP reset form', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response(null, 401)))
    const user = userEvent.setup()

    render(<App />)
    await user.click(await screen.findByRole('button', { name: 'Mentor forgot password' }))

    expect(screen.getByRole('heading', { name: 'Reset password' })).toBeInTheDocument()
    expect(screen.getByLabelText('Mentor number')).toBeInTheDocument()
    expect(screen.getByLabelText('Login email')).toBeInTheDocument()
    expect(screen.queryByLabelText('Reset code')).not.toBeInTheDocument()
  })

  it('shows the OTP and new-password fields after a reset code request', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(response(null, 401))
      .mockResolvedValueOnce(response({ requestToken: 'csrf-token' }))
      .mockResolvedValueOnce(response({
        message: 'If the mentor account exists, a password reset code was sent.',
      }, 202))
    vi.stubGlobal('fetch', fetchMock)
    const user = userEvent.setup()

    render(<App />)
    await user.click(await screen.findByRole('button', { name: 'Mentor forgot password' }))
    await user.type(screen.getByLabelText('Mentor number'), '223450002')
    await user.type(screen.getByLabelText('Login email'), 'mentor@orisync.test')
    await user.click(screen.getByRole('button', { name: 'Send reset code' }))

    expect(await screen.findByLabelText('Reset code')).toBeInTheDocument()
    expect(screen.getByLabelText('New password')).toBeInTheDocument()
    expect(fetchMock).toHaveBeenLastCalledWith(
      '/api/auth/mentor-password-reset/request',
      expect.objectContaining({ method: 'POST' }),
    )
  })
})

function response(body: unknown, status = 200) {
  return new Response(body === null ? null : JSON.stringify(body), {
    status,
    headers: body === null ? undefined : { 'Content-Type': 'application/json' },
  })
}
