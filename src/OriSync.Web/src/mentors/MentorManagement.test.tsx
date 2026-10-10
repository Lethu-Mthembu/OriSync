import '@testing-library/jest-dom/vitest'
import { cleanup, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { MentorActivation } from './MentorActivation'
import { MentorManagement } from './MentorManagement'

const group = { id: 4, name: 'RED', badgeColor: '#C62828', isActive: true, canDelete: false }
const orientation = {
  id: 12,
  year: 2027,
  name: 'First Year Orientation 2027',
  startDate: '2027-02-01',
  endDate: '2027-02-12',
  attendanceOpensAt: '07:45:00',
  attendanceClosesAt: '15:30:00',
  timeZoneId: 'Africa/Johannesburg',
  isActive: true,
  retentionDueAt: '2027-08-12T00:00:00Z',
  operatingDates: [],
  groups: [group],
}

describe('Mentor management', () => {
  afterEach(() => {
    cleanup()
    vi.unstubAllGlobals()
  })

  it('shows the active group and queues an email-only invitation', async () => {
    const requests: Array<{ path: string; init?: RequestInit }> = []
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const path = String(input)
      requests.push({ path, init })
      if (path === '/api/admin/orientations') return response([orientation])
      if (path.startsWith('/api/admin/mentors?')) return response({ items: [], page: 1, pageSize: 20, totalItems: 0, totalPages: 0 })
      if (path === '/api/auth/csrf') return response({ requestToken: 'csrf-token' })
      if (path === '/api/admin/mentor-invitations') return response({ id: 7 }, 201)
      throw new Error(`Unexpected request: ${path}`)
    }))
    const user = userEvent.setup()

    render(<MentorManagement />)
    expect(await screen.findAllByRole('option', { name: 'RED' })).toHaveLength(2)
    await user.type(screen.getByLabelText('Email'), 'mentor@orisync.test')
    await user.selectOptions(screen.getByLabelText('Group', { selector: 'select[required]' }), '4')
    await user.click(screen.getByRole('button', { name: 'Send invitation' }))

    expect(await screen.findByText('Invitation queued for mentor@orisync.test.')).toBeInTheDocument()
    const request = requests.find((item) => item.path === '/api/admin/mentor-invitations')
    expect(request?.init).toEqual(expect.objectContaining({ method: 'POST' }))
    expect(request?.init?.body).toBe(JSON.stringify({ email: 'mentor@orisync.test', groupId: 4 }))
  })

  it('activates a mentor with the locked invitation email', async () => {
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
      const path = String(input)
      if (path === '/api/auth/csrf') return response({ requestToken: 'csrf-token' })
      if (path === '/api/auth/mentor-activation/validate') return response({ email: 'mentor@orisync.test', groupId: 4, groupName: 'RED', groupBadgeColor: '#C62828', expiresAt: '2027-01-01T00:00:00Z' })
      if (path === '/api/auth/mentor-activation/accept') return response(null, 204)
      throw new Error(`Unexpected request: ${path}`)
    }))
    const user = userEvent.setup()

    render(<MentorActivation invitationId={7} token="secret-token" />)
    expect(await screen.findByDisplayValue('mentor@orisync.test')).toBeDisabled()
    await user.type(screen.getByLabelText('First name'), 'Jane')
    await user.type(screen.getByLabelText('Surname'), 'Mentor')
    await user.type(screen.getByLabelText('Mentor number'), '223456789')
    await user.type(screen.getByLabelText('Password'), 'Secure!Password')
    await user.type(screen.getByLabelText('Confirm password'), 'Secure!Password')
    await user.click(screen.getByRole('button', { name: 'Activate account' }))

    expect(await screen.findByRole('heading', { name: 'Activation complete' })).toBeInTheDocument()
  })
})

function response(body: unknown, status = 200) {
  return new Response(body === null ? null : JSON.stringify(body), {
    status,
    headers: body === null ? undefined : { 'Content-Type': 'application/json' },
  })
}
