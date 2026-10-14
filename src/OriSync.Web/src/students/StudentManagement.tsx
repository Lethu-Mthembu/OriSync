import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { getError, sendJson } from '../auth/api'
import type { AttendanceMutationResponse, AttendanceStatus } from '../attendance/types'
import { downloadResponse } from '../registers/download'
import type { RegisterFilter, RegisterSummary } from '../registers/types'
import { useConfirmation } from '../components/useConfirmation'
import BoxLoader from '@/components/ui/box-loader'
import { PageSkeleton } from '@/components/ui/page-skeleton'
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
const studentEmailDomain = '@tut4life.ac.za'

function studentEmailFor(studentNumber: string) {
  return studentNumber ? `${studentNumber}${studentEmailDomain}` : ''
}

const emptyStudent: StudentFormValues = {
  studentNumber: '',
  fullNames: '',
  surname: '',
  studentEmail: '',
  personalEmail: '',
  ageGroup: 'C1',
  gender: 'Male',
  ethnicity: 'African',
}

export function StudentManagement({ userName }: { userName: string }) {
  const [directory, setDirectory] = useState<StudentDirectoryResponse | null>(null)
  const [search, setSearch] = useState('')
  const [attendanceStatus, setAttendanceStatus] = useState<RegisterFilter>('All')
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(10)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [adding, setAdding] = useState(false)
  const [editing, setEditing] = useState<StudentDirectoryItem | null>(null)
  const [qrStudent, setQrStudent] = useState<StudentDirectoryItem | null>(null)
  const [qrImageUrl, setQrImageUrl] = useState('')
  const [qrLoading, setQrLoading] = useState(false)
  const [qrSending, setQrSending] = useState(false)
  const [registerSummary, setRegisterSummary] = useState<RegisterSummary | null>(null)
  const [registerDownloading, setRegisterDownloading] = useState(false)
  const { confirm, confirmationDialog } = useConfirmation()

  const load = useCallback(async (showLoading = true) => {
    if (showLoading) setLoading(true)
    setError('')
    const values = new URLSearchParams({ page: String(page), pageSize: String(pageSize) })
    if (search.trim()) values.set('search', search.trim())
    if (attendanceStatus !== 'All') values.set('attendanceStatus', attendanceStatus)
    try {
      const response = await fetch(`/api/mentor/students?${values}`, {
        credentials: 'same-origin',
        cache: 'no-store',
      })
      if (!response.ok) throw new Error(await getError(response))
      setDirectory((await response.json()) as StudentDirectoryResponse)
      const registerResponse = await fetch(`/api/mentor/registers/today/summary?attendanceStatus=${attendanceStatus}`, {
        credentials: 'same-origin',
        cache: 'no-store',
      })
      if (!registerResponse.ok) throw new Error(await getError(registerResponse))
      setRegisterSummary((await registerResponse.json()) as RegisterSummary)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Students could not be loaded.')
    } finally {
      if (showLoading) setLoading(false)
    }
  }, [attendanceStatus, page, pageSize, search])

  useEffect(() => {
    const task = window.setTimeout(() => void load(), 0)
    const refresh = window.setInterval(() => void load(false), 15000)
    return () => {
      window.clearTimeout(task)
      window.clearInterval(refresh)
    }
  }, [load])

  useEffect(() => () => {
    if (qrImageUrl) URL.revokeObjectURL(qrImageUrl)
  }, [qrImageUrl])

  async function remove(student: StudentDirectoryItem) {
    if (!await confirm({
      title: 'Remove student from group?',
      message: `Remove ${student.fullNames} ${student.surname} from ${student.groupName} Group? The student will become UNASSIGNED.`,
      confirmLabel: 'Remove student',
      tone: 'danger',
    })) return
    const response = await sendJson(`/api/mentor/students/${student.enrollmentId}/remove`, 'POST')
    if (!response.ok) {
      setError(await getError(response))
      return
    }
    setNotice(`${student.fullNames} ${student.surname} is now UNASSIGNED.`)
    await load(false)
  }

  function success(message: string) {
    setAdding(false)
    setEditing(null)
    setNotice(message)
    void load(false)
  }

  function studentSaved(result: StudentMutationResponse, mode: 'create' | 'edit', qrChanged: boolean) {
    const message = mode === 'create'
      ? `${result.student.fullNames} ${result.student.surname} was registered as Present.`
      : qrChanged
        ? `${result.student.fullNames} ${result.student.surname} was updated. The previous QR code is now expired.`
        : `${result.student.fullNames} ${result.student.surname} was updated. The existing QR code remains valid.`
    success(message)
    if (mode === 'create' || qrChanged) void showQr(result.student)
  }

  async function correctAttendance(student: StudentDirectoryItem, status: AttendanceStatus) {
    if (!await confirm({
      title: `Mark student ${status}?`,
      message: `Mark ${student.fullNames} ${student.surname} as ${status}? The last submitted status will replace the current one.`,
      confirmLabel: `Mark ${status}`,
    })) return
    setError('')
    const response = await sendJson(`/api/mentor/attendance/${student.enrollmentId}`, 'PUT', { status })
    if (!response.ok) {
      setError(await getError(response))
      return
    }

    const corrected = (await response.json()) as AttendanceMutationResponse
    setDirectory((current) => current === null ? null : {
      ...current,
      items: current.items.map((item) => item.enrollmentId === corrected.enrollmentId
        ? { ...item, attendanceStatus: corrected.status }
        : item),
    })
    setNotice(corrected.message)
    // Re-query immediately so an item that no longer matches the active
    // attendance filter disappears without waiting for the polling interval.
    await load(false)
  }

  async function showQr(student: StudentDirectoryItem) {
    if (qrImageUrl) URL.revokeObjectURL(qrImageUrl)
    setQrImageUrl('')
    setQrStudent(student)
    setQrLoading(true)
    setError('')
    try {
      const response = await fetch(`/api/mentor/students/${student.enrollmentId}/qr`, {
        credentials: 'same-origin',
        cache: 'no-store',
      })
      if (!response.ok) throw new Error(await getError(response))
      setQrImageUrl(URL.createObjectURL(await response.blob()))
    } catch (reason) {
      setQrStudent(null)
      setError(reason instanceof Error ? reason.message : 'The QR code could not be loaded.')
    } finally {
      setQrLoading(false)
    }
  }

  async function downloadQr() {
    if (!qrStudent) return
    setQrLoading(true)
    setError('')
    try {
      const response = await fetch(`/api/mentor/students/${qrStudent.enrollmentId}/qr/download`, {
        credentials: 'same-origin',
        cache: 'no-store',
      })
      if (!response.ok) throw new Error(await getError(response))
      const objectUrl = URL.createObjectURL(await response.blob())
      const link = document.createElement('a')
      link.href = objectUrl
      link.download = `${qrStudent.studentNumber}-qr.png`
      document.body.append(link)
      link.click()
      link.remove()
      window.setTimeout(() => URL.revokeObjectURL(objectUrl), 0)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'The QR code could not be downloaded.')
    } finally {
      setQrLoading(false)
    }
  }

  async function emailQr() {
    if (!qrStudent?.personalEmail) return
    if (!await confirm({
      title: 'Send QR code?',
      message: `Send ${qrStudent.fullNames} ${qrStudent.surname}'s QR code to ${qrStudent.personalEmail}? Verify this address before continuing.`,
      confirmLabel: 'Send QR code',
    })) return

    setQrSending(true)
    setError('')
    try {
      const response = await sendJson(`/api/mentor/students/${qrStudent.enrollmentId}/qr/email`, 'POST')
      if (!response.ok) throw new Error(await getError(response))
      const payload = (await response.json()) as { message: string }
      setNotice(payload.message)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'The QR email could not be queued.')
    } finally {
      setQrSending(false)
    }
  }

  function closeQr() {
    if (qrImageUrl) URL.revokeObjectURL(qrImageUrl)
    setQrImageUrl('')
    setQrStudent(null)
  }

  async function downloadRegister() {
    if (!registerSummary || registerSummary.rowCount === 0) return
    setRegisterDownloading(true)
    setError('')
    try {
      const response = await fetch(`/api/mentor/registers/today?attendanceStatus=${attendanceStatus}`, {
        credentials: 'same-origin',
        cache: 'no-store',
      })
      if (!response.ok) throw new Error(await getError(response))
      await downloadResponse(response, 'register.pdf')
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'The register could not be downloaded.')
    } finally {
      setRegisterDownloading(false)
    }
  }

  return (
    <main className="settings-workspace student-workspace">
      <header className="settings-heading student-heading">
        <div><p className="eyebrow">{userName}</p><h1>Students</h1><p>Manage only the students assigned to your group.</p></div>
        <div className="student-heading-actions"><button className="secondary-button" type="button" disabled={registerDownloading || !registerSummary || registerSummary.rowCount === 0} onClick={() => void downloadRegister()}>{registerDownloading ? 'Preparing…' : 'Download register'}</button><button className="primary-button" type="button" onClick={() => { setAdding(true); setEditing(null); setError(''); setNotice('') }}>Add student</button></div>
      </header>

      {error && <p className="error-message" role="alert">{error}</p>}
      {notice && <p className="success-message" role="status">{notice}</p>}
      {adding && <StudentOnboarding onCancel={() => setAdding(false)} onSuccess={success} onStudentSaved={studentSaved} onError={setError} />}
      {editing && <StudentForm mode="edit" initial={editing} onCancel={() => setEditing(null)} onSuccess={studentSaved} onError={setError} />}
      {qrStudent && <QrCodeDialog student={qrStudent} imageUrl={qrImageUrl} loading={qrLoading} sending={qrSending} onClose={closeQr} onDownload={() => void downloadQr()} onSend={() => void emailQr()} />}

      <section className="settings-card directory-card">
        <div className="directory-filters student-filters">
          <label className="field">Search<input type="search" value={search} placeholder="Student number or name" onChange={(event) => { setSearch(event.target.value); setPage(1) }} /></label>
          <label className="field">Attendance<select value={attendanceStatus} onChange={(event) => { setAttendanceStatus(event.target.value as RegisterFilter); setPage(1) }}><option value="All">All</option><option value="Present">Present</option><option value="Absent">Absent</option></select></label>
          <label className="field">Rows<select value={pageSize} onChange={(event) => { setPageSize(Number(event.target.value)); setPage(1) }}>{pageSizes.map((size) => <option key={size}>{size}</option>)}</select></label>
        </div>

        {loading ? <PageSkeleton label="Loading students" variant="table" rows={5} /> : directory && directory.items.length > 0 ? (
          <>
            <div className="table-scroll student-desktop-directory"><table className="directory-table student-table"><thead><tr><th>Student</th><th>Number</th><th>Demographics</th><th>Status</th><th>Actions</th></tr></thead><tbody>{directory.items.map((student) => <StudentRow key={student.enrollmentId} student={student} onEdit={setEditing} onRemove={(item) => void remove(item)} onQr={(item) => void showQr(item)} onAttendance={(item, status) => void correctAttendance(item, status)} />)}</tbody></table></div>
            <div className="student-mobile-directory">{directory.items.map((student) => <StudentCard key={student.enrollmentId} student={student} onEdit={setEditing} onRemove={(item) => void remove(item)} onQr={(item) => void showQr(item)} onAttendance={(item, status) => void correctAttendance(item, status)} />)}</div>
          </>
        ) : <p className="empty-state">No students match these filters.</p>}

        <div className="pagination"><button className="secondary-button" type="button" disabled={page <= 1} onClick={() => setPage((value) => value - 1)}>Previous</button><span>Page {directory?.page ?? page} of {Math.max(directory?.totalPages ?? 0, 1)}</span><button className="secondary-button" type="button" disabled={!directory || page >= directory.totalPages} onClick={() => setPage((value) => value + 1)}>Next</button></div>
      </section>
      {confirmationDialog}
    </main>
  )
}

function StudentOnboarding({ onCancel, onSuccess, onStudentSaved, onError }: { onCancel: () => void; onSuccess: (message: string) => void; onStudentSaved: (result: StudentMutationResponse, mode: 'create' | 'edit', qrChanged: boolean) => void; onError: (message: string) => void }) {
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
      {lookup?.match === 'New' && <StudentForm mode="create" initial={{ ...emptyStudent, studentNumber: number }} onCancel={onCancel} onSuccess={onStudentSaved} onError={onError} />}
      {lookup?.match === 'Unassigned' && <div className="lookup-result"><p>{lookup.message}</p><button className="primary-button" disabled={loading} type="button" onClick={() => void onboard()}>Confirm onboarding</button></div>}
      {(lookup?.match === 'Assigned' || lookup?.match === 'Unavailable') && <p className="account-warning">{lookup.message}</p>}
    </section>
  )
}

function StudentForm({ mode, initial, onCancel, onSuccess, onError }: { mode: 'create' | 'edit'; initial: StudentFormValues & { enrollmentId?: number; qrVersion?: number }; onCancel: () => void; onSuccess: (result: StudentMutationResponse, mode: 'create' | 'edit', qrChanged: boolean) => void; onError: (message: string) => void }) {
  const [draft, setDraft] = useState<StudentFormValues>({
    ...initial,
    studentEmail: studentEmailFor(initial.studentNumber),
  })
  const [saving, setSaving] = useState(false)
  const { confirm, confirmationDialog } = useConfirmation()
  async function submit(event: FormEvent) {
    event.preventDefault()
    const qrChanged = mode === 'create' ||
      draft.studentNumber !== initial.studentNumber ||
      draft.fullNames.trim() !== initial.fullNames.trim() ||
      draft.surname.trim() !== initial.surname.trim() ||
      draft.studentEmail !== initial.studentEmail
    if (mode === 'edit' && !await confirm({
      title: 'Save student changes?',
      message: qrChanged
        ? 'These changes will expire the student’s previous QR code and create a replacement.'
        : 'These changes will update the student profile. The existing QR code will remain valid.',
      confirmLabel: 'Save changes',
    })) return
    setSaving(true)
    onError('')
    const path = mode === 'create' ? '/api/mentor/students' : `/api/mentor/students/${initial.enrollmentId}`
    const response = await sendJson(path, mode === 'create' ? 'POST' : 'PUT', { ...draft, personalEmail: draft.personalEmail })
    if (!response.ok) onError(await getError(response))
    else {
      const result = (await response.json()) as StudentMutationResponse
      const actualQrChanged = mode === 'create' || result.student.qrVersion !== initial.qrVersion
      onSuccess(result, mode, actualQrChanged)
    }
    setSaving(false)
  }

  return (
    <form className="student-form" onSubmit={(event) => void submit(event)}>
      <div className="student-form-heading"><div><p className="form-kicker">{mode === 'create' ? 'New student' : 'Edit profile'}</p><h2>{mode === 'create' ? 'Registration details' : `${initial.fullNames} ${initial.surname}`}</h2></div><p className="form-note">Names accept letters, spaces, apostrophes and hyphens only.</p></div>
      <div className="form-grid student-form-grid">
        <label className="field">Student number<input required inputMode="numeric" pattern="2[0-9]{8}" maxLength={9} value={draft.studentNumber} onChange={(event) => { const studentNumber = event.target.value.replace(/\D/g, '').slice(0, 9); setDraft({ ...draft, studentNumber, studentEmail: studentEmailFor(studentNumber) }) }} /></label>
        <label className="field field-wide">Full names<input required maxLength={100} value={draft.fullNames} onChange={(event) => setDraft({ ...draft, fullNames: event.target.value })} /></label>
        <label className="field">Surname<input required maxLength={100} value={draft.surname} onChange={(event) => setDraft({ ...draft, surname: event.target.value })} /></label>
        <label className="field field-wide">Student email<input required readOnly type="email" maxLength={254} value={draft.studentEmail} /><span className="form-note">Generated from the student number.</span></label>
        <label className="field field-wide">Personal email<input required type="email" maxLength={254} value={draft.personalEmail ?? ''} onChange={(event) => setDraft({ ...draft, personalEmail: event.target.value })} /><span className="form-note">The student QR code can be sent only to this address.</span></label>
        <label className="field">Age group<select value={draft.ageGroup} onChange={(event) => setDraft({ ...draft, ageGroup: event.target.value as StudentAgeGroup })}>{([['C1', '16–18'], ['C2', '19–21'], ['C3', '22–24'], ['C4', '25–27'], ['C5', '28 and above']] as Array<[StudentAgeGroup, string]>).map(([value, label]) => <option key={value} value={value}>{value} ({label})</option>)}</select></label>
        <label className="field">Gender<select value={draft.gender} onChange={(event) => setDraft({ ...draft, gender: event.target.value as Gender })}>{(['Male', 'Female', 'Other'] as Gender[]).map((value) => <option key={value}>{value}</option>)}</select></label>
        <label className="field">Ethnicity<select value={draft.ethnicity} onChange={(event) => setDraft({ ...draft, ethnicity: event.target.value as Ethnicity })}>{(['White', 'African', 'Coloured', 'Indian', 'Other'] as Ethnicity[]).map((value) => <option key={value}>{value}</option>)}</select></label>
      </div>
      <div className="button-row compact-row"><button className="primary-button" disabled={saving} type="submit">{saving ? 'Saving…' : mode === 'create' ? 'Register as Present' : 'Save changes'}</button><button className="secondary-button" type="button" onClick={onCancel}>Cancel</button></div>
      {confirmationDialog}
    </form>
  )
}

function StudentRow({ student, onEdit, onRemove, onQr, onAttendance }: StudentActions) {
  return <tr><td><strong>{student.initials} {student.surname}</strong><small>{student.fullNames}</small><small>{student.studentEmail}</small></td><td>{student.studentNumber}</td><td>{student.ageGroup} · {student.gender}<small>{student.ethnicity}</small></td><td><Attendance student={student} /></td><td><Actions student={student} onEdit={onEdit} onRemove={onRemove} onQr={onQr} onAttendance={onAttendance} /></td></tr>
}

function StudentCard({ student, onEdit, onRemove, onQr, onAttendance }: StudentActions) {
  return <article className="student-card"><div className="student-card-heading"><div><h2>{student.initials} {student.surname}</h2><p>{student.fullNames}</p></div><Attendance student={student} /></div><dl><div><dt>Student number</dt><dd>{student.studentNumber}</dd></div><div><dt>Age group</dt><dd>{student.ageGroup}</dd></div><div><dt>Gender</dt><dd>{student.gender}</dd></div><div><dt>Ethnicity</dt><dd>{student.ethnicity}</dd></div></dl><p className="student-email">{student.studentEmail}</p><Actions student={student} onEdit={onEdit} onRemove={onRemove} onQr={onQr} onAttendance={onAttendance} /></article>
}

function Attendance({ student }: { student: StudentDirectoryItem }) {
  return <><span className={`status-badge attendance-${student.attendanceStatus === '—' ? 'pending' : student.attendanceStatus.toLowerCase()}`}>{student.attendanceStatus}</span>{student.isIncomingTransfer && <small>Effective {student.groupEffectiveDate}</small>}</>
}

interface StudentActions { student: StudentDirectoryItem; onEdit: (student: StudentDirectoryItem) => void; onRemove: (student: StudentDirectoryItem) => void; onQr: (student: StudentDirectoryItem) => void; onAttendance: (student: StudentDirectoryItem, status: AttendanceStatus) => void }
function Actions({ student, onEdit, onRemove, onQr, onAttendance }: StudentActions) {
  return <div className="row-actions"><button className="attendance-action" disabled={student.isIncomingTransfer || student.attendanceStatus === 'Present'} type="button" onClick={() => onAttendance(student, 'Present')}>Mark Present</button><button className="attendance-action" disabled={student.isIncomingTransfer || student.attendanceStatus === 'Absent'} type="button" onClick={() => onAttendance(student, 'Absent')}>Mark Absent</button><button className="text-button" disabled={student.isIncomingTransfer} type="button" onClick={() => onQr(student)}>QR code</button><button className="text-button" disabled={student.isIncomingTransfer} type="button" onClick={() => onEdit(student)}>Edit</button><button className="danger-text-button" disabled={student.isIncomingTransfer} type="button" onClick={() => onRemove(student)}>Remove</button>{student.isIncomingTransfer && <small>Available tomorrow</small>}</div>
}

function QrCodeDialog({ student, imageUrl, loading, sending, onClose, onDownload, onSend }: { student: StudentDirectoryItem; imageUrl: string; loading: boolean; sending: boolean; onClose: () => void; onDownload: () => void; onSend: () => void }) {
  return <div className="qr-dialog-backdrop" role="presentation"><section className="qr-dialog" role="dialog" aria-modal="true" aria-labelledby="qr-dialog-title"><div className="qr-dialog-heading"><div><p className="form-kicker">Student QR code</p><h2 id="qr-dialog-title">{student.fullNames} {student.surname}</h2></div><button className="secondary-button" type="button" onClick={onClose}>Close</button></div>{imageUrl ? <img src={imageUrl} alt={`QR code for ${student.fullNames} ${student.surname}`} /> : <div className="qr-inline-loader" role="status" aria-live="polite"><BoxLoader /><span className="sr-only">Loading QR code</span></div>}<p className="form-note">This QR remains valid until encoded student details are changed.</p>{student.personalEmail ? <p className="qr-recipient">Email recipient: <strong>{student.personalEmail}</strong></p> : <p className="account-warning">Add a personal email before sending this QR code.</p>}<div className="button-row compact-row"><button className="primary-button" disabled={loading || !imageUrl} type="button" onClick={onDownload}>{loading ? 'Preparing…' : 'Download PNG'}</button><button className="secondary-button" disabled={sending || !imageUrl || !student.personalEmail} type="button" onClick={onSend}>{sending ? 'Queueing…' : 'Send to student'}</button></div></section></div>
}
