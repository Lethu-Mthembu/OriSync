import { useCallback, useEffect, useMemo, useState } from 'react'
import { getError } from '../auth/api'
import { downloadResponse } from './download'
import type { RegisterFilter, RegisterGroupSummary, RegisterOrientation, RegisterSummary } from './types'
import { PageSkeleton } from '@/components/ui/page-skeleton'

export function RegisterReports() {
  const [orientations, setOrientations] = useState<RegisterOrientation[]>([])
  const [orientationId, setOrientationId] = useState('')
  const [date, setDate] = useState('')
  const [filter, setFilter] = useState<RegisterFilter>('All')
  const [summary, setSummary] = useState<RegisterSummary | null>(null)
  const [groups, setGroups] = useState<RegisterGroupSummary[]>([])
  const [loading, setLoading] = useState(true)
  const [summaryLoading, setSummaryLoading] = useState(false)
  const [downloading, setDownloading] = useState<'all' | number | null>(null)
  const [error, setError] = useState('')

  const orientation = useMemo(
    () => orientations.find((item) => String(item.id) === orientationId) ?? null,
    [orientationId, orientations],
  )
  const availableDates = useMemo(() => {
    if (!orientation) return []
    return completedDates(orientation)
  }, [orientation])

  useEffect(() => {
    const controller = new AbortController()
    void fetch('/api/admin/orientations', {
      credentials: 'same-origin',
      cache: 'no-store',
      signal: controller.signal,
    }).then(async (response) => {
      if (!response.ok) throw new Error(await getError(response))
      const values = (await response.json()) as RegisterOrientation[]
      const selected = values.find((item) => item.isActive) ?? values[0]
      setOrientations(values)
      setOrientationId(selected ? String(selected.id) : '')
      setDate(selected ? completedDates(selected).at(-1) ?? '' : '')
      setError('')
    }).catch((reason: unknown) => {
      if (!(reason instanceof DOMException && reason.name === 'AbortError')) {
        setError(reason instanceof Error ? reason.message : 'Orientations could not be loaded.')
      }
    }).finally(() => {
      if (!controller.signal.aborted) setLoading(false)
    })
    return () => controller.abort()
  }, [])

  const loadSummary = useCallback(async () => {
    if (!orientationId || !date) {
      setSummary(null)
      setGroups([])
      setSummaryLoading(false)
      return
    }

    setSummaryLoading(true)
    const values = new URLSearchParams({
      orientationId,
      date,
      attendanceStatus: filter,
    })
    try {
      const [summaryResponse, groupsResponse] = await Promise.all([
        fetch(`/api/admin/registers/summary?${values}`, { credentials: 'same-origin', cache: 'no-store' }),
        fetch(`/api/admin/registers/groups?${values}`, { credentials: 'same-origin', cache: 'no-store' }),
      ])
      if (!summaryResponse.ok) throw new Error(await getError(summaryResponse))
      if (!groupsResponse.ok) throw new Error(await getError(groupsResponse))
      setSummary((await summaryResponse.json()) as RegisterSummary)
      setGroups((await groupsResponse.json()) as RegisterGroupSummary[])
      setError('')
    } catch (reason) {
      setSummary(null)
      setGroups([])
      setError(reason instanceof Error ? reason.message : 'Register availability could not be loaded.')
    } finally {
      setSummaryLoading(false)
    }
  }, [date, filter, orientationId])

  useEffect(() => {
    const task = window.setTimeout(() => void loadSummary(), 0)
    return () => window.clearTimeout(task)
  }, [loadSummary])

  async function download(groupId: number | 'all') {
    const group = groupId === 'all' ? null : groups.find((item) => item.groupId === groupId)
    if (groupId === 'all' ? !summary || summary.rowCount === 0 : !group || group.matchingStudents === 0) return
    setDownloading(groupId)
    setError('')
    const values = new URLSearchParams({ orientationId, date, attendanceStatus: filter })
    const path = groupId === 'all'
      ? `/api/admin/registers/zip?${values}`
      : `/api/admin/registers/pdf?${values}&groupId=${encodeURIComponent(String(groupId))}`
    try {
      const response = await fetch(path, { credentials: 'same-origin', cache: 'no-store' })
      if (!response.ok) throw new Error(await getError(response))
      await downloadResponse(response, groupId === 'all' ? 'registers.zip' : 'register.pdf')
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'The register could not be downloaded.')
    } finally {
      setDownloading(null)
    }
  }

  function changeOrientation(value: string) {
    const selected = orientations.find((item) => String(item.id) === value)
    setOrientationId(value)
    setDate(selected ? completedDates(selected).at(-1) ?? '' : '')
    setSummary(null)
    setGroups([])
  }

  return <main className="settings-workspace"><header className="settings-heading"><div><p className="eyebrow">Admin reports</p><h1>Attendance registers</h1><p>Select one attendance day, review every group, then download one PDF or a combined ZIP.</p></div></header>{error && <p className="error-message" role="alert">{error}</p>}<section className="settings-card report-card"><div className="report-toolbar"><div className="report-filters"><label className="field">Orientation<select disabled={loading || orientations.length === 0} value={orientationId} onChange={(event) => changeOrientation(event.target.value)}>{orientations.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label><label className="field">Date<select disabled={loading || availableDates.length === 0} value={date} onChange={(event) => { setDate(event.target.value); setSummary(null); setGroups([]) }}>{availableDates.map((item) => <option key={item} value={item}>{formatDate(item)}</option>)}</select></label><label className="field">Attendance<select disabled={loading} value={filter} onChange={(event) => { setFilter(event.target.value as RegisterFilter); setSummary(null); setGroups([]) }}><option value="All">All</option><option value="Present">Present</option><option value="Absent">Absent</option></select></label></div><button className="primary-button zip-download-button" type="button" disabled={loading || summaryLoading || downloading !== null || !summary || summary.rowCount === 0} onClick={() => void download('all')}>{downloading === 'all' ? 'Preparing ZIP…' : 'Download all as ZIP'}</button></div>{loading ? <PageSkeleton label="Loading attendance registers" variant="table" rows={5} /> : orientations.length === 0 ? <p className="empty-state">No orientations are available.</p> : availableDates.length === 0 ? <p className="empty-state">This orientation has no current or historical operating dates.</p> : summaryLoading ? <PageSkeleton label="Checking register availability" variant="table" rows={5} /> : groups.length === 0 && summary === null ? null : <><div className="register-summary-line"><strong>{summary?.rowCount.toLocaleString() ?? 0}</strong> matching {filter.toLowerCase()} rows across <strong>{groups.length}</strong> groups</div><div className="table-scroll"><table className="directory-table register-table"><thead><tr><th>Group</th><th>Total</th><th>Present</th><th>Absent</th><th>{filter} rows</th><th><span className="sr-only">Actions</span></th></tr></thead><tbody>{groups.map((group) => <tr key={group.groupId}><td><span className="group-badge" style={{ backgroundColor: group.badgeColor }}>{group.groupName}</span></td><td>{group.totalStudents.toLocaleString()}</td><td>{group.presentStudents.toLocaleString()}</td><td>{group.absentStudents.toLocaleString()}</td><td><span className={`status-badge attendance-${filter === 'Present' ? 'present' : filter === 'Absent' ? 'absent' : 'pending'}`}>{group.matchingStudents.toLocaleString()}</span></td><td className="register-action-cell"><button className="secondary-button" type="button" disabled={downloading !== null || group.matchingStudents === 0} aria-label={`Download ${group.groupName} PDF`} onClick={() => void download(group.groupId)}>{downloading === group.groupId ? 'Preparing…' : 'Download PDF'}</button></td></tr>)}</tbody></table></div></>}</section></main>
}

function localIsoDate() {
  const now = new Date()
  const year = now.getFullYear()
  const month = String(now.getMonth() + 1).padStart(2, '0')
  const day = String(now.getDate()).padStart(2, '0')
  return `${year}-${month}-${day}`
}

function completedDates(orientation: RegisterOrientation) {
  const today = localIsoDate()
  return orientation.operatingDates.filter((item) => item <= today)
}

function formatDate(value: string) {
  return new Intl.DateTimeFormat('en-ZA', { dateStyle: 'long', timeZone: 'UTC' })
    .format(new Date(`${value}T00:00:00Z`))
}
