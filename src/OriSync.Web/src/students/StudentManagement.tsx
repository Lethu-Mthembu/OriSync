import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { getError, sendJson } from '../auth/api'
import type {
  Ethnicity,
  Gender,
  StudentAgeGroup,
  StudentDirectoryItem,
  StudentDirectoryResponse,
  StudentFormValues,
  StudentLookupResponse,
  StudentMutationResponse,
} from './types'

const pageSizes = [10, 20, 50, 100]
const emptyStudent: StudentFormValues = {
  studentNumber: '',
  fullNames: '',
  surname: '',
  studentEmail: '',
  personalEmail: null,
  ageGroup: 'C1',
  gender: 'Male',
  ethnicity: 'African',
}

export function StudentManagement() {
  const [directory, setDirectory] = useState<StudentDirectoryResponse | null>(null)
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(10)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [adding, setAdding] = useState(false)
  const [editing, setEditing] = useState<StudentDirectoryItem | null>(null)

  const load = useCallback(async () => {
    setLoading(true)
    setError('')
    const values = new URLSearchParams({ page: String(page), pageSize: String(pageSize) })
    if (search.trim()) values.set('search', search.trim())
    try {
      const response = await fetch(`/api/mentor/students?${values}`, {
        credentials: 'same-origin',
        cache: 'no-store',
      })
      if (!response.ok) throw new Error(await getError(response))
      setDirectory((await response.json()) as StudentDirectoryResponse)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Students could not be loaded.')
    } finally {
      setLoading(false)
    }
  }, [page, pageSize, search])

  useEffect(() => {
    const task = window.setTimeout(() => void load(), 0)
    return () => window.clearTimeout(task)
  }, [load])

  async function remove(student: StudentDirectoryItem) {
    if (!window.confirm(`Remove ${student.fullNames} ${student.surname} from ${student.groupName} Group?`)) return
    const response = await sendJson(`/api/mentor/students/${student.enrollmentId}/remove`, 'POST')
    if (!response.ok) {
      setError(await getError(response))
      return
    }
    setNotice(`${student.fullNames} ${student.surname} is now UNASSIGNED.`)
    await load()
  }

  function success(message: string) {
    setAdding(false)
    setEditing(null)
    setNotice(message)
    void load()
  }

  return (
    <main className="settings-workspace student-workspace">
      <header className="settings-heading student-heading">
        <div><p className="eyebrow">Mentor workspace</p><h1>Students</h1><p>Manage only the students assigned to your group.</p></div>
        <button className="primary-button" type="button" onClick={() => { setAdding(true); setEditing(null); setError(''); setNotice('') }}>Add student</button>
      </header>

      {error && <p className="error-message" role="alert">{error}</p>}
      {notice && <p className="success-message" role="status">{notice}</p>}
      {adding && <StudentOnboarding onCancel={() => setAdding(false)} onSuccess={success} onError={setError} />}
      {editing && <StudentForm mode="edit" initial={editing} onCancel={() => setEditing(null)} onSuccess={success} onError={setError} />}

      <section className="settings-card directory-card">
        <div className="directory-filters student-filters">
          <label className="field">Search<input type="search" value={search} placeholder="Student number or name" onChange={(event) => { setSearch(event.target.value); setPage(1) }} /></label>
          <label className="field">Rows<select value={pageSize} onChange={(event) => { setPageSize(Number(event.target.value)); setPage(1) }}>{pageSizes.map((size) => <option key={size}>{size}</option>)}</select></label>
        </div>

        {loading ? <p className="empty-state">Loading students…</p> : directory && directory.items.length > 0 ? (
          <>
            <div className="table-scroll student-desktop-directory"><table className="directory-table student-table"><thead><tr><th>Student</th><th>Number</th><th>Demographics</th><th>Status</th><th>Actions</th></tr></thead><tbody>{directory.items.map((student) => <StudentRow key={student.enrollmentId} student={student} onEdit={setEditing} onRemove={(item) => void remove(item)} />)}</tbody></table></div>
            <div className="student-mobile-directory">{directory.items.map((student) => <StudentCard key={student.enrollmentId} student={student} onEdit={setEditing} onRemove={(item) => void remove(item)} />)}</div>
          </>
        ) : <p className="empty-state">No students match this search.</p>}

        <div className="pagination"><button className="secondary-button" type="button" disabled={page <= 1} onClick={() => setPage((value) => value - 1)}>Previous</button><span>Page {directory?.page ?? page} of {Math.max(directory?.totalPages ?? 0, 1)}</span><button className="secondary-button" type="button" disabled={!directory || page >= directory.totalPages} onClick={() => setPage((value) => value + 1)}>Next</button></div>
      </section>
    </main>
  )
}

function StudentOnboarding({ onCancel, onSuccess, onError }: { onCancel: () => void; onSuccess: (message: string) => void; onError: (message: string) => void }) {
  const [number, setNumber] = useState('')
  const [lookup, setLookup] = useState<StudentLookupResponse | null>(null)
  const [loading, setLoading] = useState(false)

  async function find(event: FormEvent) {
    event.preventDefault()
    setLoading(true)
    onError('')
    const response = await fetch(`/api/mentor/students/lookup/${number}`, { credentials: 'same-origin', cache: 'no-store' })
    if (!response.ok) onError(await getError(response))
    else setLookup((await response.json()) as StudentLookupResponse)
    setLoading(false)
  }

  async function onboard() {
    setLoading(true)
    const response = await sendJson('/api/mentor/students/onboard', 'POST', { studentNumber: number })
    if (!response.ok) onError(await getError(response))
    else {
      const result = (await response.json()) as StudentMutationResponse
      onSuccess(result.outcome === 'MovesTomorrow'
        ? `${result.student.fullNames} ${result.student.surname} will join your group tomorrow.`
        : `${result.student.fullNames} ${result.student.surname} was added and is Present.`)
    }
    setLoading(false)
  }

  return (
    <section className="settings-card onboarding-card">
      <div><p className="form-kicker">Step 1</p><h2>Find the student number</h2><p className="form-note">Exact lookup prevents duplicate student identities.</p></div>
      <form className="lookup-form" onSubmit={(event) => void find(event)}><label className="field">Student number<input required inputMode="numeric" pattern="2[0-9]{8}" maxLength={9} value={number} onChange={(event) => { setNumber(event.target.value.replace(/\D/g, '').slice(0, 9)); setLookup(null) }} /></label><button className="primary-button" disabled={loading} type="submit">{loading ? 'Checking…' : 'Continue'}</button><button className="secondary-button" type="button" onClick={onCancel}>Cancel</button></form>
      {lookup?.match === 'New' && <StudentForm mode="create" initial={{ ...emptyStudent, studentNumber: number }} onCancel={onCancel} onSuccess={onSuccess} onError={onError} />}
      {lookup?.match === 'Unassigned' && <div className="lookup-result"><p>{lookup.message}</p><button className="primary-button" disabled={loading} type="button" onClick={() => void onboard()}>Confirm onboarding</button></div>}
      {(lookup?.match === 'Assigned' || lookup?.match === 'Unavailable') && <p className="account-warning">{lookup.message}</p>}
    </section>
  )
}

function StudentForm({ mode, initial, onCancel, onSuccess, onError }: { mode: 'create' | 'edit'; initial: StudentFormValues & { enrollmentId?: number }; onCancel: () => void; onSuccess: (message: string) => void; onError: (message: string) => void }) {
  const [draft, setDraft] = useState<StudentFormValues>({ ...initial })
  const [saving, setSaving] = useState(false)
  async function submit(event: FormEvent) {
    event.preventDefault()
    setSaving(true)
    onError('')
    const path = mode === 'create' ? '/api/mentor/students' : `/api/mentor/students/${initial.enrollmentId}`
    const response = await sendJson(path, mode === 'create' ? 'POST' : 'PUT', { ...draft, personalEmail: draft.personalEmail || null })
    if (!response.ok) onError(await getError(response))
    else {
      const result = (await response.json()) as StudentMutationResponse
      onSuccess(mode === 'create'
        ? `${result.student.fullNames} ${result.student.surname} was registered as Present.`
        : `${result.student.fullNames} ${result.student.surname} was updated. Download a replacement QR code.`)
    }
    setSaving(false)
  }

  return (
    <form className="student-form" onSubmit={(event) => void submit(event)}>
      <div className="student-form-heading"><div><p className="form-kicker">{mode === 'create' ? 'New student' : 'Edit profile'}</p><h2>{mode === 'create' ? 'Registration details' : `${initial.fullNames} ${initial.surname}`}</h2></div><p className="form-note">Names accept letters, spaces, apostrophes and hyphens only.</p></div>
      <div className="form-grid student-form-grid">
        <label className="field">Student number<input required inputMode="numeric" pattern="2[0-9]{8}" maxLength={9} value={draft.studentNumber} onChange={(event) => setDraft({ ...draft, studentNumber: event.target.value.replace(/\D/g, '').slice(0, 9) })} /></label>
        <label className="field field-wide">Full names<input required maxLength={100} value={draft.fullNames} onChange={(event) => setDraft({ ...draft, fullNames: event.target.value })} /></label>
        <label className="field">Surname<input required maxLength={100} value={draft.surname} onChange={(event) => setDraft({ ...draft, surname: event.target.value })} /></label>
        <label className="field field-wide">Student email<input required type="email" maxLength={254} value={draft.studentEmail} onChange={(event) => setDraft({ ...draft, studentEmail: event.target.value })} /></label>
        <label className="field field-wide">Personal email <span className="optional-label">Optional</span><input type="email" maxLength={254} value={draft.personalEmail ?? ''} onChange={(event) => setDraft({ ...draft, personalEmail: event.target.value || null })} /></label>
        <label className="field">Age group<select value={draft.ageGroup} onChange={(event) => setDraft({ ...draft, ageGroup: event.target.value as StudentAgeGroup })}>{([['C1', '16–18'], ['C2', '19–21'], ['C3', '22–24'], ['C4', '25–27'], ['C5', '28 and above']] as Array<[StudentAgeGroup, string]>).map(([value, label]) => <option key={value} value={value}>{value} ({label})</option>)}</select></label>
        <label className="field">Gender<select value={draft.gender} onChange={(event) => setDraft({ ...draft, gender: event.target.value as Gender })}>{(['Male', 'Female', 'Other'] as Gender[]).map((value) => <option key={value}>{value}</option>)}</select></label>
        <label className="field">Ethnicity<select value={draft.ethnicity} onChange={(event) => setDraft({ ...draft, ethnicity: event.target.value as Ethnicity })}>{(['White', 'African', 'Coloured', 'Indian', 'Other'] as Ethnicity[]).map((value) => <option key={value}>{value}</option>)}</select></label>
      </div>
      <div className="button-row compact-row"><button className="primary-button" disabled={saving} type="submit">{saving ? 'Saving…' : mode === 'create' ? 'Register as Present' : 'Save changes'}</button><button className="secondary-button" type="button" onClick={onCancel}>Cancel</button></div>
    </form>
  )
}

function StudentRow({ student, onEdit, onRemove }: StudentActions) {
  return <tr><td><strong>{student.initials} {student.surname}</strong><small>{student.fullNames}</small><small>{student.studentEmail}</small></td><td>{student.studentNumber}</td><td>{student.ageGroup} · {student.gender}<small>{student.ethnicity}</small></td><td><Attendance student={student} /></td><td><Actions student={student} onEdit={onEdit} onRemove={onRemove} /></td></tr>
}

function StudentCard({ student, onEdit, onRemove }: StudentActions) {
  return <article className="student-card"><div className="student-card-heading"><div><h2>{student.initials} {student.surname}</h2><p>{student.fullNames}</p></div><Attendance student={student} /></div><dl><div><dt>Student number</dt><dd>{student.studentNumber}</dd></div><div><dt>Age group</dt><dd>{student.ageGroup}</dd></div><div><dt>Gender</dt><dd>{student.gender}</dd></div><div><dt>Ethnicity</dt><dd>{student.ethnicity}</dd></div></dl><p className="student-email">{student.studentEmail}</p><Actions student={student} onEdit={onEdit} onRemove={onRemove} /></article>
}

function Attendance({ student }: { student: StudentDirectoryItem }) {
  return <><span className={`status-badge attendance-${student.attendanceStatus === '—' ? 'pending' : student.attendanceStatus.toLowerCase()}`}>{student.attendanceStatus}</span>{student.isIncomingTransfer && <small>Effective {student.groupEffectiveDate}</small>}</>
}

interface StudentActions { student: StudentDirectoryItem; onEdit: (student: StudentDirectoryItem) => void; onRemove: (student: StudentDirectoryItem) => void }
function Actions({ student, onEdit, onRemove }: StudentActions) {
  return <div className="row-actions"><button className="text-button" disabled={student.isIncomingTransfer} type="button" onClick={() => onEdit(student)}>Edit</button><button className="danger-text-button" disabled={student.isIncomingTransfer} type="button" onClick={() => onRemove(student)}>Remove</button>{student.isIncomingTransfer && <small>Available tomorrow</small>}</div>
}
