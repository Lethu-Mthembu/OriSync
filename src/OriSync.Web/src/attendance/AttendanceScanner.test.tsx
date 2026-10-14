import '@testing-library/jest-dom/vitest'
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { AttendanceScanner } from './AttendanceScanner'

const scanner = vi.hoisted(() => ({
  callback: undefined as undefined | ((result: { getText: () => string } | undefined, error: undefined, controls: { stop: () => void }) => void),
  decode: vi.fn(),
  options: undefined as { delayBetweenScanAttempts?: number } | undefined,
  stop: vi.fn(),
}))

vi.mock('@zxing/browser', () => ({
  BrowserQRCodeReader: vi.fn(function (_hints, options) {
    scanner.options = options
    return {
      decodeFromStream: scanner.decode.mockImplementation((_stream, _video, callback) => {
        scanner.callback = callback
        return Promise.resolve({ stop: scanner.stop })
      }),
    }
  }),
}))

describe('Attendance scanner', () => {
  afterEach(() => {
    cleanup()
    setDocumentVisibility('visible')
    vi.clearAllMocks()
    scanner.callback = undefined
    scanner.options = undefined
    vi.unstubAllGlobals()
  })

  it('uses the rear camera, throttles attempts, stops on decode and resumes after the popup', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(response({ requestToken: 'csrf' }))
      .mockResolvedValueOnce(response({
        outcome: 'MarkedPresent',
        enrollmentId: 7,
        studentNumber: '223456789',
        fullNames: 'Jane Mary',
        surname: 'Doe',
        status: 'Present',
        message: 'Jane Mary Doe was marked Present.',
      }))
    vi.stubGlobal('fetch', fetchMock)
    const firstStream = fakeStream()
    const resumedStream = fakeStream()
    const getUserMedia = vi.fn().mockResolvedValue(resumedStream.stream)
    vi.stubGlobal('navigator', { ...navigator, mediaDevices: { getUserMedia, enumerateDevices: vi.fn().mockResolvedValue([]) } })
    const user = userEvent.setup()

    render(<AttendanceScanner initialCameraStream={Promise.resolve(firstStream.stream)} onClose={vi.fn()} />)
    expect(screen.getByRole('status')).toHaveTextContent('Starting rear camera')
    await waitFor(() => expect(scanner.decode).toHaveBeenCalledTimes(1))
    expect(scanner.options?.delayBetweenScanAttempts).toBe(500)
    expect(scanner.decode).toHaveBeenCalledWith(firstStream.stream, expect.any(HTMLVideoElement), expect.any(Function))
    fireEvent.playing(screen.getByLabelText('Camera preview'))
    expect(screen.queryByText('Starting rear camera…')).not.toBeInTheDocument()

    await act(async () => {
      scanner.callback?.(
        { getText: () => 'https://orisync.test/scan#payload=signed' },
        undefined,
        { stop: scanner.stop },
      )
    })

    expect(scanner.stop).toHaveBeenCalled()
    expect(await screen.findByRole('heading', { name: 'Attendance recorded' })).toBeInTheDocument()
    expect(fetchMock).toHaveBeenLastCalledWith(
      '/api/mentor/attendance/scan',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ decodedUrl: 'https://orisync.test/scan#payload=signed' }),
      }),
    )

    await user.click(screen.getByRole('button', { name: 'Continue scanning' }))
    await waitFor(() => expect(scanner.decode).toHaveBeenCalledTimes(2))
    expect(getUserMedia).toHaveBeenCalledWith({
      audio: false,
      video: { facingMode: { ideal: 'environment' } },
    })
    expect(scanner.decode).toHaveBeenLastCalledWith(resumedStream.stream, expect.any(HTMLVideoElement), expect.any(Function))
  })

  it('stops the scanner when leaving the page', async () => {
    vi.stubGlobal('fetch', vi.fn())
    const camera = fakeStream()
    const view = render(<AttendanceScanner initialCameraStream={Promise.resolve(camera.stream)} onClose={vi.fn()} />)
    await waitFor(() => expect(scanner.decode).toHaveBeenCalledTimes(1))

    view.unmount()

    expect(scanner.stop).toHaveBeenCalled()
    expect(camera.stop).toHaveBeenCalled()
  })

  it('releases the camera in the background and acquires a fresh stream on return', async () => {
    vi.stubGlobal('fetch', vi.fn())
    const firstCamera = fakeStream()
    const resumedCamera = fakeStream()
    const getUserMedia = vi.fn().mockResolvedValue(resumedCamera.stream)
    vi.stubGlobal('navigator', { ...navigator, mediaDevices: { getUserMedia, enumerateDevices: vi.fn().mockResolvedValue([]) } })

    render(<AttendanceScanner initialCameraStream={Promise.resolve(firstCamera.stream)} onClose={vi.fn()} />)
    await waitFor(() => expect(scanner.decode).toHaveBeenCalledTimes(1))

    act(() => {
      setDocumentVisibility('hidden')
      document.dispatchEvent(new Event('visibilitychange'))
    })
    expect(firstCamera.stop).toHaveBeenCalled()

    act(() => {
      setDocumentVisibility('visible')
      document.dispatchEvent(new Event('visibilitychange'))
    })
    await waitFor(() => expect(scanner.decode).toHaveBeenCalledTimes(2))
    expect(getUserMedia).toHaveBeenCalledTimes(1)
    expect(scanner.decode).toHaveBeenLastCalledWith(
      resumedCamera.stream,
      expect.any(HTMLVideoElement),
      expect.any(Function),
    )
  })

  it('shows the real camera error and retries an explicitly selected rear camera', async () => {
    vi.stubGlobal('fetch', vi.fn())
    const busyError = new DOMException('Could not start video source', 'NotReadableError')
    const selectedStream = fakeStream()
    const cameras = [
      cameraDevice('front-id', 'Front Camera'),
      cameraDevice('rear-id', 'Camera2 0, facing back'),
    ]
    const getUserMedia = vi.fn().mockResolvedValue(selectedStream.stream)
    const enumerateDevices = vi.fn().mockResolvedValue(cameras)
    vi.stubGlobal('navigator', { ...navigator, mediaDevices: { getUserMedia, enumerateDevices } })
    const user = userEvent.setup()

    render(<AttendanceScanner initialCameraStream={Promise.reject(busyError)} onClose={vi.fn()} />)

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'The selected camera could not be started.',
    )
    expect(screen.getByRole('alert')).toHaveTextContent('NotReadableError: Could not start video source')
    expect(await screen.findByRole('option', { name: 'Camera2 0, facing back' })).toBeInTheDocument()
    expect(screen.getByRole('combobox')).toHaveValue('rear-id')

    await user.click(screen.getByRole('button', { name: 'Try selected camera' }))

    await waitFor(() => expect(getUserMedia).toHaveBeenCalledWith({
      audio: false,
      video: { deviceId: { exact: 'rear-id' } },
    }))
    await waitFor(() => expect(scanner.decode).toHaveBeenLastCalledWith(
      selectedStream.stream,
      expect.any(HTMLVideoElement),
      expect.any(Function),
    ))
  })
})

function fakeStream() {
  const stop = vi.fn()
  return {
    stop,
    stream: { getTracks: () => [{ stop }] } as unknown as MediaStream,
  }
}

function response(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

function setDocumentVisibility(state: DocumentVisibilityState) {
  Object.defineProperty(document, 'visibilityState', {
    configurable: true,
    value: state,
  })
}

function cameraDevice(deviceId: string, label: string) {
  return {
    deviceId,
    groupId: 'camera-group',
    kind: 'videoinput',
    label,
    toJSON: () => ({}),
  } as MediaDeviceInfo
}
