import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { getError, sendJson } from '../auth/api'
import type { Orientation, OrientationGroup } from '../orientation/types'
import type { MentorDirectoryItem, MentorDirectoryResponse } from './types'

const statuses = ['All', 'Active', 'Disabled', 'Pending delivery', 'Invited', 'Expired', 'Delivery failed', 'Cancelled']

export function MentorManagement() {
  const [groups, setGroups] = useState<OrientationGroup[]>([])
  const [directory, setDirectory] = useState<MentorDirectoryResponse | null>(null)
  const [search, setSearch] = useState('')
  const [groupId, setGroupId] = useState('')
  const [status, setStatus] = useState('All')
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(20)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [message, setMessage] = useState('')

  const loadDirectory = useCallback(async (signal?: AbortSignal) => {
    const query = new URLSearchParams({ page: String(page), pageSize: String(pageSize), status })
    if (search.trim()) query.set('search', search.trim())
    if (groupId) query.set('groupId', groupId)
    try {
      const response = await fetch(`/api/admin/mentors?${query}`, { credentials: 'same-origin', cache: 'no-store', signal })
      if (!response.ok) throw new Error(await getError(response))
      setDirectory((await response.json()) as MentorDirectoryResponse)
      setError('')
    } catch (loadError) {
      if (!(loadError instanceof DOMException && loadError.name === 'AbortError')) {
        setError(loadError instanceof Error ? loadError.message : 'Mentors could not be loaded.')
      }
    } finally {
      if (!signal?.aborted) setLoading(false)
    }
  }, [groupId, page, pageSize, search, status])

  useEffect(() => {
    const controller = new AbortController()
    async function loadGroups() {
      const response = await fetch('/api/admin/orientations', { credentials: 'same-origin', cache: 'no-store', signal: controller.signal })
      if (!response.ok) throw new Error(await getError(response))
      const orientations = (await response.json()) as Orientation[]
      setGroups(orientations.find((item) => item.isActive)?.groups.filter((item) => item.isActive) ?? [])
    }
    async function loadAll() {
      try {
        await Promise.all([loadGroups(), loadDirectory(controller.signal)])
      } catch (loadError) {
        if (!(loadError instanceof DOMException && loadError.name === 'AbortError')) {
          setError(loadError instanceof Error ? loadError.message : 'Mentor management could not be loaded.')
          setLoading(false)
        }
      }
    }
    void loadAll()
    return () => controller.abort()
  }, [loadDirectory])

  async function mutate(path: string, method: 'POST' | 'PUT' | 'DELETE', body?: unknown, success?: string) {
    setError('')
    setMessage('')
    const response = await sendJson(path, method, body)
    if (!response.ok) {
      setError(await getError(response))
      return false
    }
    if (success) setMessage(success)
    await loadDirectory()
    return true
  }

  return (
    <section className="settings-workspace" aria-labelledby="mentor-management-heading">
      <div className="settings-heading">
        <div>
          <p className="eyebrow">Admin settings</p>
          <h1 id="mentor-management-heading">Mentors</h1>
          <p>Invite mentors, assign their current group and control account access.</p>
        </div>
      </div>
      {error && <p className="error-message" role="alert">{error}</p>}
      {message && <p className="success-message" role="status">{message}</p>}
      <InviteMentor groups={groups} mutate={mutate} />

      <section className="settings-card directory-card">
        <div className="card-heading"><div><p className="form-kicker">Central directory</p><h2>All mentors</h2></div><span>{directory?.totalItems ?? 0} records</span></div>
        <div className="directory-filters">
          <label className="field">Search<input type="search" value={search} placeholder="Name, email, number or group" onChange={(event) => { setSearch(event.target.value); setPage(1) }} /></label>
          <label className="field">Group<select value={groupId} onChange={(event) => { setGroupId(event.target.value); setPage(1) }}><option value="">All groups</option>{groups.map((group) => <option key={group.id} value={group.id}>{group.name}</option>)}</select></label>
          <label className="field">Status<select value={status} onChange={(event) => { setStatus(event.target.value); setPage(1) }}>{statuses.map((item) => <option key={item}>{item}</option>)}</select></label>
          <label className="field">Rows<select value={pageSize} onChange={(event) => { setPageSize(Number(event.target.value)); setPage(1) }}><option>20</option><option>50</option><option>100</option></select></label>
        </div>

        {loading ? <p aria-live="polite">Loading mentors…</p> : directory?.items.length ? (
          <div className="table-scroll"><table className="directory-table"><thead><tr><th>Mentor</th><th>Mentor number</th><th>Group</th><th>Status</th><th>Actions</th></tr></thead><tbody>{directory.items.map((item) => <MentorRow key={`${item.recordType}-${item.id}`} item={item} groups={groups} mutate={mutate} />)}</tbody></table></div>
        ) : <p className="empty-state">No mentor records match these filters.</p>}

        <div className="pagination"><button className="secondary-button" type="button" disabled={page <= 1} onClick={() => setPage((value) => value - 1)}>Previous</button><span>Page {directory?.page ?? page} of {Math.max(directory?.totalPages ?? 0, 1)}</span><button className="secondary-button" type="button" disabled={!directory || page >= directory.totalPages} onClick={() => setPage((value) => value + 1)}>Next</button></div>
      </section>
    </section>
  )
}

function InviteMentor({ groups, mutate }: { groups: OrientationGroup[]; mutate: Mutation }) {
  const [email, setEmail] = useState('')
  const [groupId, setGroupId] = useState('')
  const [saving, setSaving] = useState(false)
  async function submit(event: FormEvent) {
    event.preventDefault()
    setSaving(true)
    const created = await mutate('/api/admin/mentor-invitations', 'POST', { email, groupId: Number(groupId) }, `Invitation queued for ${email.trim()}.`)
    if (created) setEmail('')
    setSaving(false)
  }
  return (
    <form className="settings-card invite-form" onSubmit={(event) => void submit(event)}>
      <div><p className="form-kicker">New mentor</p><h2>Send activation invitation</h2><p className="form-note">The email becomes the mentor's permanent login email. The link expires 24 hours after delivery.</p></div>
      <label className="field">Email<input required type="email" maxLength={320} value={email} onChange={(event) => setEmail(event.target.value)} /></label>
      <label className="field">Group<select required value={groupId} onChange={(event) => setGroupId(event.target.value)}><option value="">Select active group</option>{groups.map((group) => <option key={group.id} value={group.id}>{group.name}</option>)}</select></label>
      <button className="primary-button" type="submit" disabled={saving || groups.length === 0}>{saving ? 'Queuing…' : 'Send invitation'}</button>
      {groups.length === 0 && <p className="account-warning">Create and activate an orientation with an active group before inviting mentors.</p>}
    </form>
  )
}

type Mutation = (path: string, method: 'POST' | 'PUT' | 'DELETE', body?: unknown, success?: string) => Promise<boolean>

function MentorRow({ item, groups, mutate }: { item: MentorDirectoryItem; groups: OrientationGroup[]; mutate: Mutation }) {
  const [editing, setEditing] = useState(false)
  const [draft, setDraft] = useState({ firstName: item.firstName ?? '', surname: item.surname ?? '', mentorNumber: item.mentorNumber ?? '', email: item.email, groupId: item.groupId })
  const groupOptions = groups.some((group) => group.id === item.groupId) ? groups : [{ id: item.groupId, name: item.groupName, badgeColor: item.groupBadgeColor, isActive: false, canDelete: false }, ...groups]

  async function save(event: FormEvent) {
    event.preventDefault()
    if (await mutate(`/api/admin/mentors/${item.id}`, 'PUT', draft, `${draft.firstName} ${draft.surname} was updated.`)) setEditing(false)
  }
  async function invitationGroupChanged(nextGroupId: number) {
    await mutate(`/api/admin/mentor-invitations/${item.id}/group`, 'PUT', { groupId: nextGroupId }, `Invitation reassigned to ${groups.find((group) => group.id === nextGroupId)?.name ?? 'the selected group'}.`)
  }
  async function confirmed(path: string, method: 'POST' | 'DELETE', question: string, success: string) {
    if (window.confirm(question)) await mutate(path, method, undefined, success)
  }

  if (editing && item.recordType === 'Mentor') {
    return <tr><td colSpan={5}><form className="mentor-edit-grid" onSubmit={(event) => void save(event)}><label className="field">First name<input required maxLength={100} value={draft.firstName} onChange={(event) => setDraft({ ...draft, firstName: event.target.value })} /></label><label className="field">Surname<input required maxLength={100} value={draft.surname} onChange={(event) => setDraft({ ...draft, surname: event.target.value })} /></label><label className="field">Mentor number<input required pattern="2[0-9]{8}" maxLength={9} value={draft.mentorNumber} onChange={(event) => setDraft({ ...draft, mentorNumber: event.target.value.replace(/\D/g, '').slice(0, 9) })} /></label><label className="field">Email<input required type="email" maxLength={320} value={draft.email} onChange={(event) => setDraft({ ...draft, email: event.target.value })} /></label><label className="field">Group<select required value={draft.groupId} onChange={(event) => setDraft({ ...draft, groupId: Number(event.target.value) })}>{groups.map((group) => <option key={group.id} value={group.id}>{group.name}</option>)}</select></label><div className="button-row compact-row"><button className="primary-button" type="submit">Save</button><button className="secondary-button" type="button" onClick={() => setEditing(false)}>Cancel</button></div></form></td></tr>
  }

  return (
    <tr>
      <td><strong>{item.recordType === 'Mentor' ? `${item.firstName} ${item.surname}` : item.email}</strong><small>{item.recordType === 'Mentor' ? item.email : 'Invitation'}</small></td>
      <td>{item.mentorNumber ?? '—'}</td>
      <td><span className="group-badge" style={{ backgroundColor: item.groupBadgeColor }}>{item.groupName}</span>{item.recordType === 'Invitation' && !['Cancelled'].includes(item.status) && <select aria-label={`Group for ${item.email}`} value={item.groupId} onChange={(event) => void invitationGroupChanged(Number(event.target.value))}>{groupOptions.map((group) => <option key={group.id} value={group.id} disabled={!group.isActive}>{group.name}</option>)}</select>}</td>
      <td><span className={`status-badge status-${item.status.toLowerCase().replaceAll(' ', '-')}`}>{item.status}</span>{item.deletionEligibleAt && <small>Delete after {new Date(item.deletionEligibleAt).toLocaleDateString()}</small>}</td>
      <td><div className="row-actions">{item.recordType === 'Invitation' ? <>{item.status !== 'Cancelled' && <><button className="text-button" type="button" onClick={() => void mutate(`/api/admin/mentor-invitations/${item.id}/resend`, 'POST', undefined, 'A replacement invitation was queued.')}>Resend</button><button className="danger-text-button" type="button" onClick={() => void confirmed(`/api/admin/mentor-invitations/${item.id}/cancel`, 'POST', `Cancel the invitation for ${item.email}?`, 'Invitation cancelled.')}>Cancel</button></>}</> : <><button className="text-button" type="button" onClick={() => setEditing(true)}>Edit</button>{item.status === 'Active' ? <button className="danger-text-button" type="button" onClick={() => void confirmed(`/api/admin/mentors/${item.id}/disable`, 'POST', `Disable ${item.firstName} ${item.surname} and end their active session?`, 'Mentor disabled.')}>Disable</button> : <><button className="text-button" type="button" onClick={() => void confirmed(`/api/admin/mentors/${item.id}/reactivate`, 'POST', `Reactivate ${item.firstName} ${item.surname}?`, 'Mentor reactivated.')}>Reactivate</button>{item.canDelete && <button className="danger-text-button" type="button" onClick={() => void confirmed(`/api/admin/mentors/${item.id}`, 'DELETE', `Permanently delete ${item.firstName} ${item.surname}? This cannot be undone.`, 'Mentor permanently deleted.')}>Delete</button>}</>}</>}</div></td>
    </tr>
  )
}
