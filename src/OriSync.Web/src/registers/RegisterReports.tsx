import { useCallback, useEffect, useMemo, useState } from 'react'
import { getError } from '../auth/api'
import { downloadResponse } from './download'
import type { RegisterFilter, RegisterOrientation, RegisterSummary } from './types'

export function RegisterReports() {
  const [orientations, setOrientations] = useState<RegisterOrientation[]>([])
  const [orientationId, setOrientationId] = useState('')
  const [date, setDate] = useState('')
  const [groupId, setGroupId] = useState('all')
  const [filter, setFilter] = useState<RegisterFilter>('All')
  const [summary, setSummary] = useState<RegisterSummary | null>(null)
  const [loading, setLoading] = useState(true)
  const [downloading, setDownloading] = useState(false)
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
      return
    }

    const values = new URLSearchParams({
      orientationId,
      date,
      attendanceStatus: filter,
    })
    if (groupId !== 'all') values.set('groupId', groupId)
    try {
      const response = await fetch(`/api/admin/registers/summary?${values}`, {
        credentials: 'same-origin',
        cache: 'no-store',
      })
      if (!response.ok) throw new Error(await getError(response))
      setSummary((await response.json()) as RegisterSummary)
      setError('')
    } catch (reason) {
      setSummary(null)
      setError(reason instanceof Error ? reason.message : 'Register availability could not be loaded.')
    }
  }, [date, filter, groupId, orientationId])

  useEffect(() => {
    const task = window.setTimeout(() => void loadSummary(), 0)
    return () => window.clearTimeout(task)
  }, [loadSummary])

  async function download() {
    if (!summary || summary.rowCount === 0) return
    setDownloading(true)
    setError('')
    const values = new URLSearchParams({ orientationId, date, attendanceStatus: filter })
    const path = groupId === 'all'
      ? `/api/admin/registers/zip?${values}`
      : `/api/admin/registers/pdf?${values}&groupId=${encodeURIComponent(groupId)}`
    try {
      const response = await fetch(path, { credentials: 'same-origin', cache: 'no-store' })
      if (!response.ok) throw new Error(await getError(response))
      await downloadResponse(response, groupId === 'all' ? 'registers.zip' : 'register.pdf')
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'The register could not be downloaded.')
    } finally {
      setDownloading(false)
    }
  }

  function changeOrientation(value: string) {
    const selected = orientations.find((item) => String(item.id) === value)
    setOrientationId(value)
    setGroupId('all')
    setDate(selected ? completedDates(selected).at(-1) ?? '' : '')
    setSummary(null)
  }

  return <main className="settings-workspace"><header className="settings-heading"><div><p className="eyebrow">Admin reports</p><h1>Attendance registers</h1><p>Download one historical group PDF or one ZIP containing every group with matching rows.</p></div></header>{error && <p className="error-message" role="alert">{error}</p>}<section className="settings-card report-card"><div className="report-filters"><label className="field">Orientation<select disabled={loading || orientations.length === 0} value={orientationId} onChange={(event) => changeOrientation(event.target.value)}>{orientations.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label><label className="field">Date<select disabled={availableDates.length === 0} value={date} onChange={(event) => { setDate(event.target.value); setSummary(null) }}>{availableDates.map((item) => <option key={item} value={item}>{formatDate(item)}</option>)}</select></label><label className="field">Group<select disabled={!orientation} value={groupId} onChange={(event) => { setGroupId(event.target.value); setSummary(null) }}><option value="all">All groups - ZIP</option>{orientation?.groups.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label><label className="field">Attendance<select value={filter} onChange={(event) => { setFilter(event.target.value as RegisterFilter); setSummary(null) }}><option value="All">All</option><option value="Present">Present</option><option value="Absent">Absent</option></select></label></div>{!loading && orientations.length === 0 ? <p className="empty-state">No orientations are available.</p> : availableDates.length === 0 ? <p className="empty-state">This orientation has no current or historical operating dates.</p> : <div className="report-download-row"><p>{summary ? `${summary.rowCount.toLocaleString()} rows${groupId === 'all' ? ` across ${summary.groupCount} groups` : ''}` : 'Checking register availability…'}</p><button className="primary-button" type="button" disabled={downloading || !summary || summary.rowCount === 0} onClick={() => void download()}>{downloading ? 'Preparing…' : groupId === 'all' ? 'Download ZIP' : 'Download PDF'}</button></div>}</section></main>
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
