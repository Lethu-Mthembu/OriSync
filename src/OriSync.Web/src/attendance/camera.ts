export const rearCameraConstraints: MediaStreamConstraints = {
  audio: false,
  video: { facingMode: { ideal: 'environment' } },
}

export function requestRearCamera() {
  if (!navigator.mediaDevices?.getUserMedia) {
    return Promise.reject(new Error('Camera access is not supported by this browser.'))
  }

  return navigator.mediaDevices.getUserMedia(rearCameraConstraints)
}

export function stopMediaStream(stream: MediaStream) {
  stream.getTracks().forEach((track) => track.stop())
}

export function describeCameraError(error: unknown) {
  const name = typeof error === 'object' && error !== null && 'name' in error
    ? String(error.name)
    : ''

  switch (name) {
    case 'NotAllowedError':
    case 'PermissionDeniedError':
    case 'SecurityError':
      return 'Camera access was blocked. Allow camera access for OriSync in the browser and try again.'
    case 'NotReadableError':
    case 'TrackStartError':
    case 'AbortError':
      return 'The rear camera is busy or Android has not released it yet. Close other camera or QR scanner apps, wait a few seconds, then try again.'
    case 'NotFoundError':
    case 'DevicesNotFoundError':
      return 'No camera was found on this device.'
    case 'OverconstrainedError':
    case 'ConstraintNotSatisfiedError':
      return 'This browser could not start a compatible rear camera.'
    default:
      return 'The camera could not be started. Close other camera apps, wait a few seconds, then try again.'
  }
}
