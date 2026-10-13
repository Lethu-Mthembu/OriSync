import { BrowserQRCodeReader, type IScannerControls } from '@zxing/browser'
import { useCallback, useEffect, useRef, useState } from 'react'
import { sendJson } from '../auth/api'
import {
  describeCameraError,
  listVideoInputDevices,
  preferredRearCameraDevice,
  requestRearCamera,
  stopMediaStream,
  type CameraFailure,
} from './camera'
import type { AttendanceMutationResponse, AttendanceProblem } from './types'
import './AttendanceScanner.css'

interface ScannerResult {
  tone: 'success' | 'warning' | 'error'
  title: string
  message: string
  autoClose: boolean
}

export function AttendanceScanner({ initialCameraStream, onClose }: { initialCameraStream: Promise<MediaStream>; onClose: () => void }) {
  const videoRef = useRef<HTMLVideoElement>(null)
  const controlsRef = useRef<IScannerControls | null>(null)
  const streamRef = useRef<MediaStream | null>(null)
  const initialCameraStreamRef = useRef<Promise<MediaStream> | null>(initialCameraStream)
  const [processing, setProcessing] = useState(false)
  const [cameraError, setCameraError] = useState<CameraFailure | null>(null)
  const [cameraDevices, setCameraDevices] = useState<MediaDeviceInfo[]>([])
  const [selectedCameraId, setSelectedCameraId] = useState('')
  const [result, setResult] = useState<ScannerResult | null>(null)
  const [pageVisible, setPageVisible] = useState(() => document.visibilityState !== 'hidden')

  const stopCamera = useCallback(() => {
    controlsRef.current?.stop()
    controlsRef.current = null
    if (streamRef.current) stopMediaStream(streamRef.current)
    streamRef.current = null
    if (videoRef.current) videoRef.current.srcObject = null
  }, [])

  const stopPendingInitialCamera = useCallback(() => {
    const pendingCamera = initialCameraStreamRef.current
    initialCameraStreamRef.current = null
    if (pendingCamera) {
      // getUserMedia promises cannot be cancelled. If Android resolves the
      // request after the page is hidden, stop that late stream immediately.
      void pendingCamera.then(stopMediaStream, () => undefined)
    }
  }, [])

  const submitDecodedUrl = useCallback(async (decodedUrl: string) => {
    try {
      const response = await sendJson('/api/mentor/attendance/scan', 'POST', { decodedUrl })
      if (response.ok) {
        const attendance = (await response.json()) as AttendanceMutationResponse
        setResult(attendance.outcome === 'AlreadyPresent'
          ? {
              tone: 'warning',
              title: 'Already present',
              message: attendance.message,
              autoClose: false,
            }
          : {
              tone: 'success',
              title: 'Attendance recorded',
              message: attendance.message,
              autoClose: true,
            })
      } else {
        const problem = await readProblem(response)
        setResult({
          tone: problem.code === 'WrongGroup' || problem.code === 'ExpiredQr' ? 'warning' : 'error',
          title: problem.title ?? 'Attendance rejected',
          message: problem.detail ?? 'The QR code could not be accepted.',
          autoClose: false,
        })
      }
    } catch {
      setResult({
        tone: 'error',
        title: 'Connection failed',
        message: 'Attendance could not reach the server. Check the connection and try again.',
        autoClose: false,
      })
    } finally {
      setProcessing(false)
    }
  }, [])

  useEffect(() => {
    function suspendCamera() {
      setPageVisible(false)
      stopPendingInitialCamera()
      stopCamera()
    }

    function resumeCamera() {
      setCameraError(null)
      setPageVisible(true)
    }

    function handleVisibilityChange() {
      if (document.visibilityState === 'hidden') suspendCamera()
      else resumeCamera()
    }

    document.addEventListener('visibilitychange', handleVisibilityChange)
    window.addEventListener('pagehide', suspendCamera)
    window.addEventListener('pageshow', resumeCamera)
    return () => {
      document.removeEventListener('visibilitychange', handleVisibilityChange)
      window.removeEventListener('pagehide', suspendCamera)
      window.removeEventListener('pageshow', resumeCamera)
      stopPendingInitialCamera()
      stopCamera()
    }
  }, [stopCamera, stopPendingInitialCamera])

  useEffect(() => {
    const video = videoRef.current
    if (!pageVisible || processing || result || cameraError !== null || !video) return

    let disposed = false
    const reader = new BrowserQRCodeReader(undefined, {
      delayBetweenScanAttempts: 500,
      delayBetweenScanSuccess: 500,
    })

    const cameraStream = initialCameraStreamRef.current ?? requestRearCamera(selectedCameraId || undefined)
    initialCameraStreamRef.current = null
    void cameraStream
      .then((stream) => {
        if (disposed) {
          stopMediaStream(stream)
          return undefined
        }

        streamRef.current = stream
        return reader.decodeFromStream(
          stream,
          video,
          (decoded, _error, controls) => {
            if (!decoded || disposed) return

            // A decoded frame must not be processed twice while the request is in flight.
            disposed = true
            controls.stop()
            stopCamera()
            setProcessing(true)
            void submitDecodedUrl(decoded.getText())
          },
        )
      })
      .then((controls) => {
        if (!controls) return
        if (disposed) controls.stop()
        else controlsRef.current = controls
      })
      .catch(async (error: unknown) => {
        if (!disposed) {
          stopCamera()
          const failure = describeCameraError(error)
          let devices: MediaDeviceInfo[] = []
          try {
            devices = await listVideoInputDevices()
          } catch {
            // Enumeration is a recovery aid. Preserve the original startup
            // failure when a browser does not expose its device list.
          }
          if (disposed) return
          setCameraDevices(devices)
          setSelectedCameraId((current) => current || preferredRearCameraDevice(devices)?.deviceId || devices[0]?.deviceId || '')
          setCameraError(failure)
        }
      })

    return () => {
      disposed = true
      stopCamera()
    }
  }, [cameraError, pageVisible, processing, result, selectedCameraId, stopCamera, submitDecodedUrl])

  useEffect(() => {
    if (!result?.autoClose) return
    const timeout = window.setTimeout(() => setResult(null), 5000)
    return () => window.clearTimeout(timeout)
  }, [result])

  function closeResult() {
    setResult(null)
  }

  return (
    <main className="attendance-scanner">
      <header className="scanner-heading">
        <div><p className="eyebrow">Mentor scanner</p><h1>Scan attendance</h1></div>
        <button className="scanner-close" type="button" onClick={() => { stopCamera(); onClose() }}>Back to students</button>
      </header>
      <section className="scanner-view" aria-label="QR scanner">
        <video ref={videoRef} muted playsInline aria-label="Camera preview" />
        <div className="scanner-frame" aria-hidden="true" />
        <p className="scanner-instruction">Place the student QR code inside the frame.</p>
        {processing && <div className="scanner-processing" role="status">Verifying attendance…</div>}
        {cameraError !== null && (
          <div className="scanner-camera-error" role="alert">
            <p>{cameraError.message}</p>
            {cameraDevices.length > 0 && (
              <label className="scanner-camera-picker">
                Camera
                <select value={selectedCameraId} onChange={(event) => setSelectedCameraId(event.target.value)}>
                  {cameraDevices.map((device, index) => (
                    <option key={device.deviceId} value={device.deviceId}>{device.label || `Camera ${index + 1}`}</option>
                  ))}
                </select>
              </label>
            )}
            <small className="scanner-camera-code">{cameraError.code}{cameraError.detail ? `: ${cameraError.detail}` : ''}</small>
            <button className="primary-button" type="button" onClick={() => setCameraError(null)}>Try selected camera</button>
          </div>
        )}
      </section>
      {result && (
        <div className="scanner-result-backdrop" role="presentation">
          <section className={`scanner-result scanner-result-${result.tone}`} role="dialog" aria-modal="true" aria-labelledby="scanner-result-title">
            <h2 id="scanner-result-title">{result.title}</h2>
            <p>{result.message}</p>
            {result.autoClose && <p className="scanner-countdown">Scanning resumes in five seconds.</p>}
            <button className="primary-button" type="button" onClick={closeResult}>Continue scanning</button>
          </section>
        </div>
      )}
    </main>
  )
}

async function readProblem(response: Response): Promise<AttendanceProblem> {
  try {
    return (await response.json()) as AttendanceProblem
  } catch {
    return { detail: 'The QR code could not be accepted.' }
  }
}
