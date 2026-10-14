export function registerServiceWorker() {
  if (!('serviceWorker' in navigator) || import.meta.env.DEV) return

  window.addEventListener('load', () => {
    void navigator.serviceWorker.register('/sw.js').catch(() => {
      // The web app remains fully usable when registration is blocked. All
      // attendance operations still require the live ASP.NET Core backend.
    })
  })
}
