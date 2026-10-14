import '@testing-library/jest-dom/vitest'
import { cleanup, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { RegisterReports } from './RegisterReports'

describe('Register reports', () => {
  afterEach(() => {
    cleanup()
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
  })

  it('downloads one ZIP for every matching group', async () => {
    const paths: string[] = []
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
      const path = String(input)
      paths.push(path)
      if (path === '/api/admin/orientations') return response([orientation])
      if (path.startsWith('/api/admin/registers/summary?')) {
        return response({ rowCount: 2, groupCount: 2, attendanceStatus: 'All' })
      }
      if (path.startsWith('/api/admin/registers/groups?')) return response(groupSummaries)
      if (path.startsWith('/api/admin/registers/zip?')) {
        return new Response(new Blob(['zip']), {
          headers: {
            'Content-Type': 'application/zip',
            'Content-Disposition': 'attachment; filename="OriSync-2026-10-13-14h30-All.zip"',
          },
        })
      }
      throw new Error(`Unexpected request: ${path}`)
    }))
    vi.stubGlobal('URL', { ...URL, createObjectURL: vi.fn().mockReturnValue('blob:register'), revokeObjectURL: vi.fn() })
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined)
    const user = userEvent.setup()

    render(<RegisterReports />)
    expect(await screen.findByRole('button', { name: 'Download RED PDF' })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Download all as ZIP' }))

    expect(paths.some((path) => path.includes('/api/admin/registers/zip?') &&
      path.includes('orientationId=1') && path.includes('date=2026-10-13') &&
      path.includes('attendanceStatus=All'))).toBe(true)
  })

  it('disables a download whose selected filter has no rows', async () => {
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
      const path = String(input)
      if (path === '/api/admin/orientations') return response([orientation])
      if (path.startsWith('/api/admin/registers/summary?')) {
        return response({ rowCount: 0, groupCount: 0, attendanceStatus: 'All' })
      }
      if (path.startsWith('/api/admin/registers/groups?')) {
        return response(groupSummaries.map((group) => ({ ...group, matchingStudents: 0 })))
      }
      throw new Error(`Unexpected request: ${path}`)
    }))

    render(<RegisterReports />)

    expect(await screen.findByRole('button', { name: 'Download RED PDF' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Download all as ZIP' })).toBeDisabled()
  })
})

const orientation = {
  id: 1,
  year: 2026,
  name: 'First Year Orientation 2026',
  isActive: true,
  operatingDates: ['2026-10-13'],
  groups: [
    { id: 1, name: 'RED', badgeColor: '#C62828', isActive: true },
    { id: 2, name: 'BLUE', badgeColor: '#1565C0', isActive: true },
  ],
}

const groupSummaries = [
  { groupId: 2, groupName: 'BLUE', badgeColor: '#1565C0', totalStudents: 1, presentStudents: 0, absentStudents: 1, matchingStudents: 1 },
  { groupId: 1, groupName: 'RED', badgeColor: '#C62828', totalStudents: 1, presentStudents: 1, absentStudents: 0, matchingStudents: 1 },
]

function response(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}
