import type { DashboardMetrics as Metrics } from './types'

export function DashboardMetrics({ metrics }: { metrics: Metrics }) {
  const cards = [
    ['Total students', metrics.totalStudents.toLocaleString()],
    ['Present today', metrics.presentToday.toLocaleString()],
    ['Absent today', metrics.absentToday.toLocaleString()],
    ['Attendance rate', `${metrics.attendanceRate.toFixed(1)}%`],
  ]

  return <section className="metric-grid" aria-label="Attendance summary">{cards.map(([label, value]) => <article className="metric-card" key={label}><span>{label}</span><strong>{value}</strong></article>)}</section>
}
