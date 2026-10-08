import '@testing-library/jest-dom/vitest'
import { cleanup, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { OrientationSettings } from './OrientationSettings'
import type { Orientation } from './types'

const orientation: Orientation = {
  id: 12,
  year: 2027,
  name: 'First Year Orientation 2027',
  startDate: '2027-02-01',
  endDate: '2027-02-07',
  attendanceOpensAt: '07:45:00',
  attendanceClosesAt: '15:30:00',
  timeZoneId: 'Africa/Johannesburg',
  isActive: true,
  retentionDueAt: '2027-08-06T22:00:00Z',
  operatingDates: [
    '2027-02-01',
    '2027-02-02',
    '2027-02-03',
    '2027-02-04',
    '2027-02-05',
  ],
  groups: [{
    id: 4,
    name: 'RED',
    badgeColor: '#C62828',
    isActive: true,
    canDelete: true,
  }],
}

describe('OrientationSettings', () => {
  afterEach(() => {
    cleanup()
    vi.unstubAllGlobals()
  })

  it('shows the operating calendar and colour groups', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response([orientation])))

    render(<OrientationSettings />)

    expect(await screen.findByRole('heading', { name: 'First Year Orientation 2027' })).toBeInTheDocument()
    expect(screen.getByText('5')).toBeInTheDocument()
    expect(screen.getByText('RED')).toHaveStyle({ backgroundColor: '#C62828' })
    expect(screen.getByText('Active')).toBeInTheDocument()
  })

  it('creates an inactive orientation through a CSRF-protected request', async () => {
    const created = { ...orientation, id: 13, isActive: false, groups: [] }
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(response([]))
      .mockResolvedValueOnce(response({ requestToken: 'csrf-token' }))
      .mockResolvedValueOnce(response(created, 201))
    vi.stubGlobal('fetch', fetchMock)
    const user = userEvent.setup()

    render(<OrientationSettings />)
    await screen.findByText('No orientations have been created.')
    const year = screen.getByLabelText('Year')
    await user.clear(year)
    await user.type(year, '2027')
    await user.click(screen.getByRole('button', { name: 'Create orientation' }))

    expect(await screen.findByRole('heading', { name: created.name })).toBeInTheDocument()
    expect(fetchMock).toHaveBeenLastCalledWith(
      '/api/admin/orientations',
      expect.objectContaining({
        method: 'POST',
        headers: expect.objectContaining({ 'X-CSRF-TOKEN': 'csrf-token' }),
      }),
    )
  })

  it('requires a selectable badge colour for an unusual group name', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response([orientation])))
    const user = userEvent.setup()

    render(<OrientationSettings />)
    const addGroupButton = await screen.findByRole('button', { name: 'Add group' })
    const addGroupForm = addGroupButton.closest('form')
    expect(addGroupForm).not.toBeNull()
    await user.type(within(addGroupForm!).getByLabelText('Group name'), 'TEAM A')

    expect(within(addGroupForm!).getByLabelText('Badge colour')).toBeInTheDocument()
  })
})

function response(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}
