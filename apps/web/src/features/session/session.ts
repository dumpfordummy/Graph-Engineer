import { reactive } from 'vue'

// Session and antiforgery state are memory-only. The HttpOnly cookie is owned by the browser.
export const localSession = reactive({ authenticated: false, checked: false, csrfToken: null as string | null, error: '' })
let checkGeneration = 0

export function expireSession() {
  ++checkGeneration
  localSession.authenticated = false
  localSession.csrfToken = null
  localSession.checked = true
}

export async function checkSession() {
  const generation = ++checkGeneration
  try {
    const response = await fetch('/api/session', { credentials: 'same-origin', cache: 'no-store' })
    if (!response.ok) throw new Error('Unable to check the local session. Check that the backend is running.')
    const value = await response.json() as { authenticated: boolean; csrfToken: string | null }
    if (generation !== checkGeneration) return
    localSession.authenticated = value.authenticated === true
    localSession.csrfToken = value.authenticated ? value.csrfToken : null
    localSession.error = ''
  } catch {
    if (generation !== checkGeneration) return
    expireSession()
    localSession.error = 'Unable to check the local session. Check that the backend is running.'
  } finally { localSession.checked = true }
}

export async function pairSession(token: string) {
  const response = await fetch('/api/session/pair', {
    method: 'POST', credentials: 'same-origin', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ token }),
  })
  if (!response.ok) throw new Error(response.status === 429
    ? 'Too many pairing attempts. Wait one minute before trying again.'
    : 'Pairing failed. Check the current launch token and use the configured local browser address.')
  await checkSession()
  if (!localSession.authenticated) throw new Error('The session could not be established. Check the local API and try pairing again.')
}
