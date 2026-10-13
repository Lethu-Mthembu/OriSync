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
