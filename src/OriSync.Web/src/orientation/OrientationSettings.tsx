import { useEffect, useMemo, useState, type FormEvent } from 'react'
import { getError, sendJson } from '../auth/api'
import type { Orientation, OrientationDraft, OrientationGroup } from './types'

const knownColours: Record<string, string> = {
  BLACK: '#212121',
  BLUE: '#1565C0',
  BROWN: '#6D4C41',
  GREEN: '#2E7D32',
  GREY: '#616161',
  GRAY: '#616161',
  ORANGE: '#EF6C00',
  PINK: '#AD1457',
  PURPLE: '#6A1B9A',
  RED: '#C62828',
  WHITE: '#FFFFFF',
  YELLOW: '#F9A825',
}

function newOrientationDraft(): OrientationDraft {
  const year = new Date().getFullYear() + 1
  return {
    year,
    name: '',
    startDate: `${year}-02-01`,
    endDate: `${year}-02-12`,
    attendanceOpensAt: '07:45',
    attendanceClosesAt: '15:30',
  }
}

export function OrientationSettings() {
  const [orientations, setOrientations] = useState<Orientation[]>([])
  const [draft, setDraft] = useState(newOrientationDraft)
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [message, setMessage] = useState('')
  const [error, setError] = useState('')

  useEffect(() => {
    const controller = new AbortController()
    void loadOrientations(controller.signal)
    return () => controller.abort()
  }, [])

  async function loadOrientations(signal?: AbortSignal) {
    try {
      const response = await fetch('/api/admin/orientations', {
        credentials: 'same-origin',
        cache: 'no-store',
        signal,
      })
      if (!response.ok) throw new Error(await getError(response))
      setOrientations((await response.json()) as Orientation[])
    } catch (loadError) {
      if (!(loadError instanceof DOMException && loadError.name === 'AbortError')) {
        setError(loadError instanceof Error ? loadError.message : 'Orientation settings could not be loaded.')
      }
    } finally {
      if (!signal?.aborted) setLoading(false)
    }
  }

  async function createOrientation(event: FormEvent) {
    event.preventDefault()
    setSaving(true)
    setError('')
    setMessage('')
    const response = await sendJson('/api/admin/orientations', 'POST', {
      ...draft,
      name: draft.name.trim() || null,
    })
    if (response.ok) {
      const created = (await response.json()) as Orientation
      setOrientations((current) => [created, ...current])
      setDraft(newOrientationDraft())
      setMessage(`${created.name} was created.`)
    } else {
      setError(await getError(response))
    }
    setSaving(false)
  }

  function replaceOrientation(updated: Orientation) {
    setOrientations((current) => current.map((item) => (
      item.id === updated.id
        ? updated
        : updated.isActive ? { ...item, isActive: false } : item
    )))
  }

  async function mutate(path: string, method: 'POST' | 'PUT' | 'DELETE', body?: unknown) {
    setError('')
    setMessage('')
    const response = await sendJson(path, method, body)
    if (!response.ok) {
      setError(await getError(response))
      return null
    }
    if (response.status === 204) {
      await loadOrientations()
      return 'deleted'
    }
    const updated = (await response.json()) as Orientation
    replaceOrientation(updated)
    return updated
  }

  if (loading) return <section className="settings-panel" aria-live="polite">Loading orientation settings…</section>

  return (
    <section className="settings-workspace" aria-labelledby="orientation-settings-heading">
      <div className="settings-heading">
        <div>
          <p className="eyebrow">Admin settings</p>
          <h1 id="orientation-settings-heading">Orientations and groups</h1>
          <p>Configure yearly operating dates, attendance hours and colour groups.</p>
        </div>
      </div>

      {error && <p className="error-message" role="alert">{error}</p>}
      {message && <p className="success-message" role="status">{message}</p>}

      <form className="settings-card orientation-form" onSubmit={(event) => void createOrientation(event)}>
        <div className="card-heading">
          <div>
            <p className="form-kicker">New yearly setup</p>
            <h2>Create orientation</h2>
          </div>
          <span className="status-badge status-inactive">Inactive until activated</span>
        </div>
        <div className="form-grid">
          <label className="field">
            Year
            <input
              required
              type="number"
              min="2020"
              max="2200"
              value={draft.year}
              onChange={(event) => setDraft({ ...draft, year: Number(event.target.value) })}
            />
          </label>
          <label className="field field-wide">
            Name <span className="optional-label">Optional</span>
            <input
              maxLength={120}
              placeholder={`First Year Orientation ${draft.year}`}
              value={draft.name}
              onChange={(event) => setDraft({ ...draft, name: event.target.value })}
            />
          </label>
          <label className="field">
            Start date
            <input
              required
              type="date"
              value={draft.startDate}
              onChange={(event) => setDraft({ ...draft, startDate: event.target.value })}
            />
          </label>
          <label className="field">
            End date
            <input
              required
              type="date"
              value={draft.endDate}
              onChange={(event) => setDraft({ ...draft, endDate: event.target.value })}
            />
          </label>
          <label className="field">
            Attendance opens
            <input
              required
              type="time"
              value={draft.attendanceOpensAt}
              onChange={(event) => setDraft({ ...draft, attendanceOpensAt: event.target.value })}
            />
          </label>
          <label className="field">
            Attendance closes
            <input
              required
              type="time"
              value={draft.attendanceClosesAt}
              onChange={(event) => setDraft({ ...draft, attendanceClosesAt: event.target.value })}
            />
          </label>
        </div>
        <p className="form-note">Weekends are excluded automatically. Public holidays are not excluded.</p>
        <button className="primary-button" type="submit" disabled={saving}>
          {saving ? 'Creating…' : 'Create orientation'}
        </button>
      </form>

      <div className="orientation-list">
        {orientations.length === 0 ? (
          <div className="empty-state">No orientations have been created.</div>
        ) : orientations.map((orientation) => (
          <OrientationCard key={orientation.id} orientation={orientation} mutate={mutate} />
        ))}
      </div>
    </section>
  )
}

function OrientationCard({
  orientation,
  mutate,
}: {
  orientation: Orientation
  mutate: (
    path: string,
    method: 'POST' | 'PUT' | 'DELETE',
    body?: unknown,
  ) => Promise<Orientation | 'deleted' | null>
}) {
  const [editing, setEditing] = useState(false)
  const [draft, setDraft] = useState<OrientationDraft>(() => ({
    year: orientation.year,
    name: orientation.name,
    startDate: orientation.startDate,
    endDate: orientation.endDate,
    attendanceOpensAt: orientation.attendanceOpensAt.slice(0, 5),
    attendanceClosesAt: orientation.attendanceClosesAt.slice(0, 5),
  }))

  async function save(event: FormEvent) {
    event.preventDefault()
    const updated = await mutate(`/api/admin/orientations/${orientation.id}`, 'PUT', draft)
    if (updated && updated !== 'deleted') setEditing(false)
  }

  async function toggleOrientation() {
    const action = orientation.isActive ? 'deactivate' : 'activate'
    const question = orientation.isActive
      ? `Deactivate ${orientation.name}?`
      : `Activate ${orientation.name} and replace the current active orientation?`
    if (!window.confirm(question)) return
    await mutate(`/api/admin/orientations/${orientation.id}/${action}`, 'POST')
  }

  return (
    <article className="settings-card orientation-card">
      <div className="card-heading">
        <div>
          <p className="form-kicker">{orientation.year}</p>
          <h2>{orientation.name}</h2>
        </div>
        <span className={`status-badge ${orientation.isActive ? 'status-active' : 'status-inactive'}`}>
          {orientation.isActive ? 'Active' : 'Inactive'}
        </span>
      </div>

      <dl className="orientation-summary">
        <div><dt>Dates</dt><dd>{orientation.startDate} – {orientation.endDate}</dd></div>
        <div><dt>Hours</dt><dd>{orientation.attendanceOpensAt.slice(0, 5)} – {orientation.attendanceClosesAt.slice(0, 5)}</dd></div>
        <div><dt>Weekdays</dt><dd>{orientation.operatingDates.length}</dd></div>
        <div><dt>Timezone</dt><dd>{orientation.timeZoneId}</dd></div>
      </dl>

      <div className="button-row compact-row">
        <button className="secondary-button" type="button" onClick={() => setEditing((value) => !value)}>
          {editing ? 'Cancel editing' : 'Edit orientation'}
        </button>
        <button className="primary-button" type="button" onClick={() => void toggleOrientation()}>
          {orientation.isActive ? 'Deactivate' : 'Activate'}
        </button>
      </div>

      {editing && (
        <form className="inline-editor form-grid" onSubmit={(event) => void save(event)}>
          <label className="field">Year<input type="number" min="2020" max="2200" value={draft.year} onChange={(event) => setDraft({ ...draft, year: Number(event.target.value) })} /></label>
          <label className="field field-wide">Name<input maxLength={120} value={draft.name} onChange={(event) => setDraft({ ...draft, name: event.target.value })} /></label>
          <label className="field">Start date<input type="date" value={draft.startDate} onChange={(event) => setDraft({ ...draft, startDate: event.target.value })} /></label>
          <label className="field">End date<input type="date" value={draft.endDate} onChange={(event) => setDraft({ ...draft, endDate: event.target.value })} /></label>
          <label className="field">Attendance opens<input type="time" value={draft.attendanceOpensAt} onChange={(event) => setDraft({ ...draft, attendanceOpensAt: event.target.value })} /></label>
          <label className="field">Attendance closes<input type="time" value={draft.attendanceClosesAt} onChange={(event) => setDraft({ ...draft, attendanceClosesAt: event.target.value })} /></label>
          <button className="primary-button" type="submit">Save changes</button>
        </form>
      )}

      <GroupManager orientation={orientation} mutate={mutate} />
    </article>
  )
}

function GroupManager({
  orientation,
  mutate,
}: {
  orientation: Orientation
  mutate: (
    path: string,
    method: 'POST' | 'PUT' | 'DELETE',
    body?: unknown,
  ) => Promise<Orientation | 'deleted' | null>
}) {
  const [name, setName] = useState('')
  const [customColour, setCustomColour] = useState('#64748B')
  const normalizedName = name.trim().toUpperCase()
  const knownColour = knownColours[normalizedName]
  const previewColour = knownColour ?? customColour

  async function addGroup(event: FormEvent) {
    event.preventDefault()
    const updated = await mutate(
      `/api/admin/orientations/${orientation.id}/groups`,
      'POST',
      { name, badgeColor: knownColour ? null : customColour },
    )
    if (updated) setName('')
  }

  return (
    <section className="group-section" aria-label={`${orientation.name} groups`}>
      <div className="subsection-heading">
        <h3>Groups</h3>
        <span>{orientation.groups.length}</span>
      </div>
      <form className="group-form" onSubmit={(event) => void addGroup(event)}>
        <label className="field">
          Group name
          <span className="group-name-input">
            <span className="colour-dot" style={{ backgroundColor: previewColour }} aria-hidden="true" />
            <input required maxLength={40} value={name} onChange={(event) => setName(event.target.value)} placeholder="RED" />
          </span>
        </label>
        {!knownColour && normalizedName && (
          <label className="field colour-field">
            Badge colour
            <input type="color" value={customColour} onChange={(event) => setCustomColour(event.target.value.toUpperCase())} />
          </label>
        )}
        <button className="secondary-button" type="submit">Add group</button>
      </form>

      {orientation.groups.length === 0 ? (
        <p className="form-note">At least one active group is required before activation.</p>
      ) : (
        <div className="group-list">
          {orientation.groups.map((group) => (
            <GroupRow key={group.id} orientationId={orientation.id} group={group} mutate={mutate} />
          ))}
        </div>
      )}
    </section>
  )
}

function GroupRow({
  orientationId,
  group,
  mutate,
}: {
  orientationId: number
  group: OrientationGroup
  mutate: (
    path: string,
    method: 'POST' | 'PUT' | 'DELETE',
    body?: unknown,
  ) => Promise<Orientation | 'deleted' | null>
}) {
  const [name, setName] = useState(group.name)
  const [badgeColor, setBadgeColor] = useState(group.badgeColor)
  const path = `/api/admin/orientations/${orientationId}/groups/${group.id}`

  const textColour = useMemo(() => {
    const value = badgeColor.replace('#', '')
    const brightness = (Number.parseInt(value.slice(0, 2), 16) * 299 +
      Number.parseInt(value.slice(2, 4), 16) * 587 +
      Number.parseInt(value.slice(4, 6), 16) * 114) / 1000
    return brightness > 160 ? '#18364D' : '#FFFFFF'
  }, [badgeColor])

  async function save() {
    await mutate(path, 'PUT', { name, badgeColor })
  }

  async function toggle() {
    const action = group.isActive ? 'deactivate' : 'activate'
    if (!window.confirm(`${group.isActive ? 'Deactivate' : 'Activate'} ${group.name} Group?`)) return
    await mutate(`${path}/${action}`, 'POST')
  }

  async function remove() {
    if (!window.confirm(`Delete ${group.name} Group? This cannot be undone.`)) return
    await mutate(path, 'DELETE')
  }

  return (
    <div className={`group-row ${group.isActive ? '' : 'group-row-inactive'}`}>
      <span className="group-badge" style={{ backgroundColor: badgeColor, color: textColour }}>{group.name}</span>
      <label className="sr-only" htmlFor={`group-name-${group.id}`}>Group name</label>
      <input id={`group-name-${group.id}`} value={name} disabled={!group.canDelete} onChange={(event) => setName(event.target.value)} />
      <label className="colour-picker" title="Badge colour">
        <span className="sr-only">Badge colour</span>
        <input type="color" value={badgeColor} onChange={(event) => setBadgeColor(event.target.value.toUpperCase())} />
      </label>
      <button className="text-button" type="button" onClick={() => void save()}>Save</button>
      <button className="text-button" type="button" onClick={() => void toggle()}>{group.isActive ? 'Deactivate' : 'Activate'}</button>
      {group.canDelete && <button className="danger-text-button" type="button" onClick={() => void remove()}>Delete</button>}
    </div>
  )
}
