import { useCallback, useEffect, useState } from 'react'
import { getError } from '../auth/api'
import { DashboardMetrics } from './DashboardMetrics'
import type { AdminDashboardResponse } from './types'
import { PageSkeleton } from '@/components/ui/page-skeleton'

export function AdminDashboard({ userName }: { userName: string }) {
  const [dashboard, setDashboard] = useState<AdminDashboardResponse | null>(null)
  const [error, setError] = useState('')

  const load = useCallback(async () => {
    try {
      const response = await fetch('/api/admin/dashboard', { credentials: 'same-origin', cache: 'no-store' })
      if (!response.ok) throw new Error(await getError(response))
      setDashboard((await response.json()) as AdminDashboardResponse)
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

  return <main className="settings-workspace dashboard-workspace"><header className="settings-heading"><div><p className="eyebrow">{userName}</p><h1>Today at a glance</h1><p>{dashboard?.hasActiveOrientation ? `${dashboard.orientationName} · ${formatDate(dashboard.attendanceDate)}` : dashboard ? 'No active orientation is configured.' : 'Loading today’s attendance summary…'}</p></div></header>{error && <p className="error-message" role="alert">{error}</p>}{dashboard === null ? !error && <PageSkeleton label="Loading admin dashboard" variant="dashboard" /> : dashboard.hasActiveOrientation ? <><DashboardMetrics metrics={dashboard.metrics} /><section className="settings-card ranking-card"><div className="card-heading"><div><p className="form-kicker">Current assignments</p><h2>Groups by student count</h2></div></div>{dashboard.groupRanking.length === 0 ? <p className="empty-state">No active groups are configured.</p> : <ol className="group-ranking">{dashboard.groupRanking.map((group) => <li key={group.groupId}><span className="rank-number">{group.rank}</span><span className="colour-dot" style={{ backgroundColor: group.badgeColor }} aria-hidden="true" /><strong>{group.groupName}</strong><span>{group.studentCount.toLocaleString()} students</span></li>)}</ol>}</section></> : <p className="empty-state">Create or activate an orientation to populate this dashboard.</p>}</main>
}

function formatDate(value: string | null) {
  if (!value) return ''
  return new Intl.DateTimeFormat('en-ZA', { dateStyle: 'long', timeZone: 'UTC' }).format(new Date(`${value}T00:00:00Z`))
}
