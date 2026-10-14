import { useCallback, useEffect, useState } from 'react'
import { getError } from '../auth/api'
import { AppIcon } from '../layout/AppIcon'
import { DashboardMetrics } from './DashboardMetrics'
import type { MentorDashboardResponse } from './types'
import { PageSkeleton } from '@/components/ui/page-skeleton'

export function MentorDashboard({ onScan, onWarmScanner }: { onScan: () => void; onWarmScanner: () => void }) {
  const [dashboard, setDashboard] = useState<MentorDashboardResponse | null>(null)
  const [error, setError] = useState('')

  const load = useCallback(async () => {
    try {
      const response = await fetch('/api/mentor/dashboard', { credentials: 'same-origin', cache: 'no-store' })
      if (!response.ok) throw new Error(await getError(response))
      setDashboard((await response.json()) as MentorDashboardResponse)
      setError('')
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'The dashboard could not be loaded.')
    }
  }, [])

  useEffect(() => {
    const initial = window.setTimeout(() => void load(), 0)
    const refresh = window.setInterval(() => void load(), 15000)
    return () => {
      window.clearTimeout(initial)
      window.clearInterval(refresh)
    }
  }, [load])

  return <main className="settings-workspace dashboard-workspace"><header className="settings-heading"><div><p className="eyebrow">Mentor dashboard</p><h1>{dashboard?.groupName ?? 'Group attendance'}</h1><p>{dashboard ? `${dashboard.orientationName} · ${formatDate(dashboard.attendanceDate)}` : 'Loading today’s register summary…'}</p></div>{dashboard && <span className="group-badge dashboard-group-badge" style={{ backgroundColor: dashboard.badgeColor }}>{dashboard.groupName}</span>}</header><section className="scan-cta"><div><p className="form-kicker">Ready for arrivals</p><h2>Mark attendance by QR code</h2><p>Open the rear camera and continue scanning students without leaving the scanner.</p></div><button className="scan-cta-button" type="button" onPointerEnter={onWarmScanner} onTouchStart={onWarmScanner} onFocus={onWarmScanner} onClick={onScan}><AppIcon name="scan" size={28} /><span>Scan attendance</span></button></section>{error && <p className="error-message" role="alert">{error}</p>}{dashboard ? <DashboardMetrics metrics={dashboard.metrics} /> : !error && <PageSkeleton label="Loading mentor dashboard" variant="dashboard" />}<p className="dashboard-note">Attendance updates every 15 seconds. Students whose group assignment starts tomorrow are excluded from today’s register totals.</p></main>
}

function formatDate(value: string) {
  return new Intl.DateTimeFormat('en-ZA', { dateStyle: 'long', timeZone: 'UTC' }).format(new Date(`${value}T00:00:00Z`))
}
