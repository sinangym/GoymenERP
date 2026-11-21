import { API_BASE } from './api'
import { classifyPasswordChange, type PasswordChangeOutcome, type PasswordChangeRequest } from './passwordContract'

export async function changePassword(request: PasswordChangeRequest, key: string): Promise<PasswordChangeOutcome> {
  if (!navigator.onLine) return { kind: 'unknown', reason: 'offline' }
  let response: Response
  try {
    // The shared antiforgery fetch wrapper supplies the cookie-session verification header.
    response = await fetch(`${API_BASE}/identity/session/password`, {
      method: 'POST', credentials: 'same-origin', cache: 'no-store',
      headers: { Accept: 'application/json', 'Content-Type': 'application/json', 'Idempotency-Key': key },
      body: JSON.stringify(request),
    })
  } catch {
    return { kind: 'unknown', reason: navigator.onLine ? 'network-error' : 'offline' }
  }
  let body: unknown = null
  if (response.status !== 204) {
    try { body = await response.json() } catch { /* malformed, classified below */ }
  }
  return classifyPasswordChange(response.status, body)
}
