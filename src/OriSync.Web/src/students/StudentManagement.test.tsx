import '@testing-library/jest-dom/vitest'
import { cleanup, render, screen, within } from '@testing-library/react'
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
      if (path.startsWith('/api/mentor/registers/today/summary')) return response({ rowCount: 0, groupCount: 0, attendanceStatus: 'All' })
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
          studentEmail: '223456789@tut4life.ac.za',
          personalEmail: 'elodie@example.com',
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
      if (path === '/api/mentor/students/1/qr') return new Response(new Blob(['png'], { type: 'image/png' }))
      throw new Error(`Unexpected request: ${path}`)
    }))
    vi.stubGlobal('URL', {
      ...URL,
      createObjectURL: vi.fn().mockReturnValue('blob:created-qr'),
      revokeObjectURL: vi.fn(),
    })
    const user = userEvent.setup()

    render(<StudentManagement userName="Jane Mentor" />)
    expect(screen.getByText('Jane Mentor')).toBeInTheDocument()
    expect(screen.queryByText('Mentor workspace')).not.toBeInTheDocument()
    await screen.findByText('No students match these filters.')
    await user.click(screen.getByRole('button', { name: 'Add student' }))
    await user.type(screen.getByLabelText('Student number'), '223456789')
    await user.click(screen.getByRole('button', { name: 'Continue' }))

    const studentEmail = screen.getByLabelText(/Student email/)
    expect(studentEmail).toHaveValue('223456789@tut4life.ac.za')
    expect(studentEmail).toHaveAttribute('readonly')
    await user.type(screen.getByLabelText('Full names'), 'Élodie Anne-Marie')
    await user.type(screen.getByLabelText('Surname'), "O'Connor")
    await user.type(screen.getByLabelText(/^Personal email/), 'elodie@example.com')
    await user.click(screen.getByRole('button', { name: 'Register as Present' }))

    expect(await screen.findByText("Élodie Anne-Marie O'Connor was registered as Present.")).toBeInTheDocument()
    const request = requests.find((item) => item.path === '/api/mentor/students')
    expect(request?.init?.body).toBe(JSON.stringify({
      studentNumber: '223456789',
      fullNames: 'Élodie Anne-Marie',
      surname: "O'Connor",
      studentEmail: '223456789@tut4life.ac.za',
      personalEmail: 'elodie@example.com',
      ageGroup: 'C1',
      gender: 'Male',
      ethnicity: 'African',
    }))
  })

  it('confirms an encoded-detail edit and opens the replacement QR dialog', async () => {
    const original = {
      enrollmentId: 7,
      studentNumber: '223456789',
      fullNames: 'Jane Mary',
      surname: 'Doe',
      initials: 'JM',
      studentEmail: '223456789@tut4life.ac.za',
      personalEmail: 'jane@example.com',
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
    vi.stubGlobal('URL', {
      ...URL,
      createObjectURL: vi.fn().mockReturnValue('blob:replacement-qr'),
      revokeObjectURL: vi.fn(),
    })
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
      const path = String(input)
      if (path.startsWith('/api/mentor/students?')) return response({ ...emptyDirectory, items: [original], totalItems: 1, totalPages: 1 })
      if (path.startsWith('/api/mentor/registers/today/summary')) return response({ rowCount: 1, groupCount: 1, attendanceStatus: 'All' })
      if (path === '/api/auth/csrf') return response({ requestToken: 'csrf-token' })
      if (path === '/api/mentor/students/7') return response({
        outcome: 'Updated',
        student: { ...original, fullNames: 'Jane Marie', qrVersion: 2 },
      })
      if (path === '/api/mentor/students/7/qr') return new Response(new Blob(['png'], { type: 'image/png' }))
      throw new Error(`Unexpected request: ${path}`)
    }))
    const user = userEvent.setup()

    render(<StudentManagement userName="Jane Mentor" />)
    await screen.findAllByText('Jane Mary')
    await user.click(screen.getAllByRole('button', { name: 'Edit' })[0])
    await user.clear(screen.getByLabelText('Full names'))
    await user.type(screen.getByLabelText('Full names'), 'Jane Marie')
    await user.click(screen.getByRole('button', { name: 'Save changes' }))

    const confirmation = await screen.findByRole('alertdialog', { name: 'Save student changes?' })
    expect(within(confirmation).getByText(/expire the student’s previous QR code/)).toBeInTheDocument()
    await user.click(within(confirmation).getByRole('button', { name: 'Save changes' }))

    expect(await screen.findByRole('dialog', { name: 'Jane Marie Doe' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Send to student' })).toBeEnabled()
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
      if (path.startsWith('/api/mentor/registers/today/summary')) return response({ rowCount: 0, groupCount: 0, attendanceStatus: 'All' })
      throw new Error(`Unexpected request: ${path}`)
    }))

    render(<StudentManagement userName="Jane Mentor" />)
    await screen.findAllByText('Available tomorrow')
    expect(screen.getAllByRole('button', { name: 'Edit' }).every((button) => button.hasAttribute('disabled'))).toBe(true)
    expect(screen.getAllByRole('button', { name: 'Remove' }).every((button) => button.hasAttribute('disabled'))).toBe(true)
  })

  it('confirms and immediately applies a manual attendance correction', async () => {
    const student = {
      enrollmentId: 7,
      studentNumber: '223456789',
      fullNames: 'Jane Mary',
      surname: 'Doe',
      initials: 'JM',
      studentEmail: '223456789@tut4life.ac.za',
      personalEmail: null,
      ageGroup: 'C2',
      gender: 'Female',
      ethnicity: 'African',
      groupId: 5,
      groupName: 'BLUE',
      groupBadgeColor: '#1565C0',
      attendanceStatus: 'Absent',
      isIncomingTransfer: false,
      groupEffectiveDate: null,
      qrVersion: 1,
    }
    const requests: Array<{ path: string; init?: RequestInit }> = []
    let currentStatus: 'Present' | 'Absent' = 'Absent'
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const path = String(input)
      requests.push({ path, init })
      if (path.startsWith('/api/mentor/students?')) return response({
        ...emptyDirectory,
        totalItems: 1,
        totalPages: 1,
        items: [{ ...student, attendanceStatus: currentStatus }],
      })
      if (path.startsWith('/api/mentor/registers/today/summary')) return response({ rowCount: 1, groupCount: 1, attendanceStatus: 'All' })
      if (path === '/api/auth/csrf') return response({ requestToken: 'csrf-token' })
      if (path === '/api/mentor/attendance/7') {
        currentStatus = 'Present'
        return response({
          outcome: 'Corrected',
          enrollmentId: 7,
          studentNumber: student.studentNumber,
          fullNames: student.fullNames,
          surname: student.surname,
          status: 'Present',
          message: 'Jane Mary Doe was marked Present.',
        })
      }
      throw new Error(`Unexpected request: ${path}`)
    }))
    const user = userEvent.setup()

    render(<StudentManagement userName="Jane Mentor" />)
    await screen.findAllByText('Jane Mary')
    await user.click(screen.getAllByRole('button', { name: 'Mark Present' })[0])
    const confirmation = await screen.findByRole('alertdialog', { name: 'Mark student Present?' })
    await user.click(within(confirmation).getByRole('button', { name: 'Mark Present' }))

    expect(await screen.findByText('Jane Mary Doe was marked Present.')).toBeInTheDocument()
    expect(requests.find((item) => item.path === '/api/mentor/attendance/7')?.init?.body)
      .toBe(JSON.stringify({ status: 'Present' }))
    expect(screen.getAllByRole('button', { name: 'Mark Present' }).every((button) => button.hasAttribute('disabled'))).toBe(true)
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
      personalEmail: 'jane@example.com',
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
      if (path.startsWith('/api/mentor/registers/today/summary')) return response({ rowCount: 1, groupCount: 1, attendanceStatus: 'All' })
      if (path === '/api/mentor/students/7/qr' || path === '/api/mentor/students/7/qr/download') {
        return new Response(new Blob(['png'], { type: 'image/png' }), {
          headers: { 'Content-Type': 'image/png' },
        })
      }
      if (path === '/api/auth/csrf') return response({ requestToken: 'csrf-token' })
      if (path === '/api/mentor/students/7/qr/email') return response({ recipientEmail: 'jane@example.com', message: 'The QR code was queued for delivery to jane@example.com.' }, 202)
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

    render(<StudentManagement userName="Jane Mentor" />)
    await screen.findAllByText('Jane Mary')
    await user.click(screen.getAllByRole('button', { name: 'QR code' })[0])
    expect(await screen.findByRole('img', { name: 'QR code for Jane Mary Doe' })).toHaveAttribute('src', 'blob:preview')
    await user.click(screen.getByRole('button', { name: 'Download PNG' }))
    await user.click(screen.getByRole('button', { name: 'Send to student' }))
    await user.click(await screen.findByRole('button', { name: 'Send QR code' }))

    expect(paths).toContain('/api/mentor/students/7/qr')
    expect(paths).toContain('/api/mentor/students/7/qr/download')
    expect(paths).toContain('/api/mentor/students/7/qr/email')
    expect(click).toHaveBeenCalledOnce()
  })
})

function response(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}
