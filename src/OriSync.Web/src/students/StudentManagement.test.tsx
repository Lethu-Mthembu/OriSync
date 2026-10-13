import '@testing-library/jest-dom/vitest'
import { cleanup, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { StudentManagement } from './StudentManagement'

const emptyDirectory = { items: [], page: 1, pageSize: 10, totalItems: 0, totalPages: 0 }

describe('Student management', () => {
  afterEach(() => {
    cleanup()
    vi.unstubAllGlobals()
  })

  it('looks up a new number and registers the student as present', async () => {
    const requests: Array<{ path: string; init?: RequestInit }> = []
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const path = String(input)
      requests.push({ path, init })
      if (path.startsWith('/api/mentor/students?')) return response(emptyDirectory)
      if (path === '/api/mentor/students/lookup/223456789') return response({ match: 'New', studentNumber: '223456789', fullNames: null, surname: null, studentEmail: null, groupName: null, message: null })
      if (path === '/api/auth/csrf') return response({ requestToken: 'csrf-token' })
      if (path === '/api/mentor/students') return response({
        outcome: 'RegisteredPresent',
        student: {
          enrollmentId: 1,
          studentNumber: '223456789',
          fullNames: 'Élodie Anne-Marie',
          surname: "O'Connor",
          initials: 'EA',
          studentEmail: 'student@tut.test',
          personalEmail: null,
          ageGroup: 'C1',
          gender: 'Male',
          ethnicity: 'African',
          groupId: 4,
          groupName: 'RED',
          groupBadgeColor: '#C62828',
          attendanceStatus: 'Present',
          isIncomingTransfer: false,
          groupEffectiveDate: null,
          qrVersion: 1,
        },
      }, 201)
      throw new Error(`Unexpected request: ${path}`)
    }))
    const user = userEvent.setup()

    render(<StudentManagement />)
    await screen.findByText('No students match this search.')
    await user.click(screen.getByRole('button', { name: 'Add student' }))
    await user.type(screen.getByLabelText('Student number'), '223456789')
    await user.click(screen.getByRole('button', { name: 'Continue' }))

    await user.type(screen.getByLabelText('Full names'), 'Élodie Anne-Marie')
    await user.type(screen.getByLabelText('Surname'), "O'Connor")
    await user.type(screen.getByLabelText('Student email'), 'student@tut.test')
    await user.click(screen.getByRole('button', { name: 'Register as Present' }))

    expect(await screen.findByText("Élodie Anne-Marie O'Connor was registered as Present.")).toBeInTheDocument()
    const request = requests.find((item) => item.path === '/api/mentor/students')
    expect(request?.init?.body).toBe(JSON.stringify({
      studentNumber: '223456789',
      fullNames: 'Élodie Anne-Marie',
      surname: "O'Connor",
      studentEmail: 'student@tut.test',
      personalEmail: null,
      ageGroup: 'C1',
      gender: 'Male',
      ethnicity: 'African',
    }))
  })

  it('shows an incoming transfer but disables editing and removal', async () => {
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
      const path = String(input)
      if (path.startsWith('/api/mentor/students?')) return response({
        ...emptyDirectory,
        totalItems: 1,
        totalPages: 1,
        items: [{
          enrollmentId: 7,
          studentNumber: '223456789',
          fullNames: 'Jane Mary',
          surname: 'Doe',
          initials: 'JM',
          studentEmail: 'jane@tut.test',
          personalEmail: null,
          ageGroup: 'C2',
          gender: 'Female',
          ethnicity: 'African',
          groupId: 5,
          groupName: 'BLUE',
          groupBadgeColor: '#1565C0',
          attendanceStatus: '—',
          isIncomingTransfer: true,
          groupEffectiveDate: '2027-02-02',
          qrVersion: 1,
        }],
      })
      throw new Error(`Unexpected request: ${path}`)
    }))

    render(<StudentManagement />)
    await screen.findAllByText('Available tomorrow')
    expect(screen.getAllByRole('button', { name: 'Edit' }).every((button) => button.hasAttribute('disabled'))).toBe(true)
    expect(screen.getAllByRole('button', { name: 'Remove' }).every((button) => button.hasAttribute('disabled'))).toBe(true)
  })

  it('previews and downloads a current-group student QR code', async () => {
    const paths: string[] = []
    const student = {
      enrollmentId: 7,
      studentNumber: '223456789',
      fullNames: 'Jane Mary',
      surname: 'Doe',
      initials: 'JM',
      studentEmail: 'jane@tut.test',
      personalEmail: null,
      ageGroup: 'C2',
      gender: 'Female',
      ethnicity: 'African',
      groupId: 5,
      groupName: 'BLUE',
      groupBadgeColor: '#1565C0',
      attendanceStatus: 'Present',
      isIncomingTransfer: false,
      groupEffectiveDate: null,
      qrVersion: 1,
    }
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
      const path = String(input)
      paths.push(path)
      if (path.startsWith('/api/mentor/students?')) return response({
        ...emptyDirectory,
        totalItems: 1,
        totalPages: 1,
        items: [student],
      })
      if (path === '/api/mentor/students/7/qr' || path === '/api/mentor/students/7/qr/download') {
        return new Response(new Blob(['png'], { type: 'image/png' }), {
          headers: { 'Content-Type': 'image/png' },
        })
      }
      throw new Error(`Unexpected request: ${path}`)
    }))
    const createObjectUrl = vi.fn()
      .mockReturnValueOnce('blob:preview')
      .mockReturnValueOnce('blob:download')
    vi.stubGlobal('URL', {
      ...URL,
      createObjectURL: createObjectUrl,
      revokeObjectURL: vi.fn(),
    })
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined)
    const user = userEvent.setup()

    render(<StudentManagement />)
    await screen.findAllByText('Jane Mary')
    await user.click(screen.getAllByRole('button', { name: 'QR code' })[0])
    expect(await screen.findByRole('img', { name: 'QR code for Jane Mary Doe' })).toHaveAttribute('src', 'blob:preview')
    await user.click(screen.getByRole('button', { name: 'Download PNG' }))

    expect(paths).toContain('/api/mentor/students/7/qr')
    expect(paths).toContain('/api/mentor/students/7/qr/download')
    expect(click).toHaveBeenCalledOnce()
  })
})

function response(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}
