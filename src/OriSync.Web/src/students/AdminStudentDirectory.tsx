import { useCallback, useEffect, useState } from 'react'
import { getError } from '../auth/api'
import type { OrientationOption, StudentDirectoryResponse } from './types'

const pageSizes = [10, 20, 50, 100]

export function AdminStudentDirectory() {
  const [directory, setDirectory] = useState<StudentDirectoryResponse | null>(null)
  const [groups, setGroups] = useState<Array<{ id: number; name: string }>>([])
  const [search, setSearch] = useState('')
  const [group, setGroup] = useState('all')
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(10)
  const [error, setError] = useState('')

  useEffect(() => {
    void fetch('/api/admin/orientations', { credentials: 'same-origin', cache: 'no-store' })
      .then(async (response) => {
        if (!response.ok) throw new Error(await getError(response))
        const orientations = (await response.json()) as OrientationOption[]
        setGroups(orientations.find((item) => item.isActive)?.groups.filter((item) => item.isActive) ?? [])
      })
      .catch((reason: unknown) => setError(reason instanceof Error ? reason.message : 'Groups could not be loaded.'))
  }, [])

  const load = useCallback(async () => {
    const values = new URLSearchParams({ page: String(page), pageSize: String(pageSize) })
    if (search.trim()) values.set('search', search.trim())
    if (group === 'unassigned') values.set('unassigned', 'true')
    else if (group !== 'all') values.set('groupId', group)
    try {
      const response = await fetch(`/api/admin/students?${values}`, { credentials: 'same-origin', cache: 'no-store' })
      if (!response.ok) throw new Error(await getError(response))
      setDirectory((await response.json()) as StudentDirectoryResponse)
      setError('')
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Students could not be loaded.')
    }
  }, [group, page, pageSize, search])

  useEffect(() => {
    const task = window.setTimeout(() => void load(), 0)
    return () => window.clearTimeout(task)
  }, [load])

  return <main className="settings-workspace"><header className="settings-heading"><div><p className="eyebrow">Admin directory</p><h1>All students</h1><p>This directory is read-only. Mentors control membership and student details.</p></div></header>{error && <p className="error-message" role="alert">{error}</p>}<section className="settings-card directory-card"><div className="directory-filters admin-student-filters"><label className="field">Search<input type="search" placeholder="Student number or name" value={search} onChange={(event) => { setSearch(event.target.value); setPage(1) }} /></label><label className="field">Group<select value={group} onChange={(event) => { setGroup(event.target.value); setPage(1) }}><option value="all">All groups</option><option value="unassigned">UNASSIGNED</option>{groups.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label><label className="field">Rows<select value={pageSize} onChange={(event) => { setPageSize(Number(event.target.value)); setPage(1) }}>{pageSizes.map((size) => <option key={size}>{size}</option>)}</select></label></div>{directory && directory.items.length > 0 ? <div className="table-scroll"><table className="directory-table student-table"><thead><tr><th>Student</th><th>Number</th><th>Group</th><th>Demographics</th><th>Status</th></tr></thead><tbody>{directory.items.map((student) => <tr key={student.enrollmentId}><td><strong>{student.initials} {student.surname}</strong><small>{student.fullNames}</small><small>{student.studentEmail}</small></td><td>{student.studentNumber}</td><td><span className="group-badge" style={{ backgroundColor: student.groupBadgeColor }}>{student.groupName}</span></td><td>{student.ageGroup} · {student.gender}<small>{student.ethnicity}</small></td><td><span className={`status-badge attendance-${student.attendanceStatus === '—' ? 'pending' : student.attendanceStatus.toLowerCase()}`}>{student.attendanceStatus}</span></td></tr>)}</tbody></table></div> : <p className="empty-state">No students match these filters.</p>}<div className="pagination"><button className="secondary-button" type="button" disabled={page <= 1} onClick={() => setPage((value) => value - 1)}>Previous</button><span>Page {directory?.page ?? page} of {Math.max(directory?.totalPages ?? 0, 1)}</span><button className="secondary-button" type="button" disabled={!directory || page >= directory.totalPages} onClick={() => setPage((value) => value + 1)}>Next</button></div></section></main>
}
