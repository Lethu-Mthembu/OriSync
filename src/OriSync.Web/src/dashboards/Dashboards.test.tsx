import '@testing-library/jest-dom/vitest'
import { cleanup, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { AdminDashboard } from './AdminDashboard'
import { MentorDashboard } from './MentorDashboard'

describe('Role dashboards', () => {
  afterEach(() => {
    cleanup()
    vi.unstubAllGlobals()
  })

  it('shows administrator metrics and ranked groups', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response({
      hasActiveOrientation: true,
      orientationName: 'First Year Orientation 2027',
      attendanceDate: '2027-02-01',
      metrics: { totalStudents: 2, presentToday: 1, absentToday: 1, attendanceRate: 50 },
      groupRanking: [
        { groupId: 2, groupName: 'BLUE', badgeColor: '#1565C0', studentCount: 2, rank: 1 },
        { groupId: 1, groupName: 'RED', badgeColor: '#C62828', studentCount: 1, rank: 2 },
      ],
    })))

    render(<AdminDashboard userName="Lethu Mthembu" />)

    expect(screen.getByText('Lethu Mthembu')).toBeInTheDocument()
    expect(screen.queryByText('Admin dashboard')).not.toBeInTheDocument()
    expect(await screen.findByText('50.0%')).toBeInTheDocument()
    expect(screen.getByText('BLUE')).toBeInTheDocument()
    expect(screen.getByText('2 students')).toBeInTheDocument()
  })

  it('shows only the authenticated mentor group summary', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response({
      orientationName: 'First Year Orientation 2027',
      attendanceDate: '2027-02-01',
      groupId: 1,
      groupName: 'RED',
      badgeColor: '#C62828',
      metrics: { totalStudents: 10, presentToday: 7, absentToday: 3, attendanceRate: 70 },
    })))

    render(<MentorDashboard onScan={vi.fn()} onWarmScanner={vi.fn()} />)

    expect(await screen.findByText('70.0%')).toBeInTheDocument()
    expect(screen.getAllByText('RED')).toHaveLength(2)
    expect(screen.getByRole('button', { name: 'Scan attendance' })).toBeInTheDocument()
    expect(fetch).toHaveBeenCalledWith('/api/mentor/dashboard', expect.any(Object))
  })
})

function response(body: unknown, status = 200) {
  return Promise.resolve(new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  }))
}
