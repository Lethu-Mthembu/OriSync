export const rearCameraConstraints: MediaStreamConstraints = {
  audio: false,
  video: { facingMode: { ideal: 'environment' } },
}

export interface CameraFailure {
  code: string
  detail: string
  message: string
}

export function requestRearCamera(deviceId?: string) {
  if (!navigator.mediaDevices?.getUserMedia) {
    return Promise.reject(new Error('Camera access is not supported by this browser.'))
  }

  return navigator.mediaDevices.getUserMedia(deviceId
    ? { audio: false, video: { deviceId: { exact: deviceId } } }
    : rearCameraConstraints)
}

export function stopMediaStream(stream: MediaStream) {
  stream.getTracks().forEach((track) => track.stop())
}

export function describeCameraError(error: unknown): CameraFailure {
  const name = typeof error === 'object' && error !== null && 'name' in error
    ? String(error.name)
    : ''
  const detail = typeof error === 'object' && error !== null && 'message' in error
    ? String(error.message)
    : ''

  switch (name) {
    case 'NotAllowedError':
    case 'PermissionDeniedError':
    case 'SecurityError':
      return failure(name, detail, 'Camera access was blocked. Allow camera access for OriSync in the browser and try again.')
    case 'NotReadableError':
    case 'TrackStartError':
    case 'AbortError':
      return failure(name, detail, 'The selected camera could not be started. Choose another rear camera below, or close other camera apps and try again.')
    case 'NotFoundError':
    case 'DevicesNotFoundError':
      return failure(name, detail, 'No camera was found on this device.')
    case 'OverconstrainedError':
    case 'ConstraintNotSatisfiedError':
      return failure(name, detail, 'This browser could not start the selected rear camera. Choose another camera below.')
    default:
      return failure(name || 'CameraError', detail, 'The camera could not be started. Choose another camera below or close other camera apps and try again.')
  }
}

export async function listVideoInputDevices() {
  if (!navigator.mediaDevices?.enumerateDevices) return []
  const devices = await navigator.mediaDevices.enumerateDevices()
  return devices.filter((device) => device.kind === 'videoinput')
}

export function preferredRearCameraDevice(devices: MediaDeviceInfo[]) {
  return devices.find((device) => {
    const label = device.label.toLocaleLowerCase()
    return /(back|rear|environment|world)/u.test(label) && !/(front|user|face)/u.test(label)
  }) ?? null
}

function failure(code: string, detail: string, message: string): CameraFailure {
  return { code: code || 'CameraError', detail, message }
}
