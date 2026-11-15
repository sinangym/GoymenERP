/**
 * ID-1F local sign-in / sign-out transport. Hand-written and thin like ./api.ts; the classification lives in
 * ./loginContract.ts so it survives the generated client (Issue #122).
 *
 * - The session is the server's `__Host-` cookie (HttpOnly, SameSite=Strict): this file never reads, stores or sends
 *   a token itself, and keeps nothing in web storage. `credentials: 'same-origin'` lets the browser attach and accept
 *   the cookie.
 * - No development identity header is ever sent from here: sign-in exists only in local-account mode, where that
 *   header means nothing.
 * - Request verification (KIM-D31): the header is attached by the ONE shared frontend client that KIM-S24 introduces
 *   for every cookie POST/PUT/DELETE. Both requests below go through `sessionPost`, the single place to route through
 *   that client when it lands; no second token mechanism is added here. Until then a server that requires the header
 *   answers 400, which the screen shows as a refused request, never as a wrong password.
 */

import { API_BASE } from './api'
import {
  classifyLogin,
  classifyLogout,
  type LocalLoginRequest,
  type LoginOutcome,
  type LogoutOutcome,
} from './loginContract'

export const LOGIN_PATH = '/identity/session/login'
export const LOGOUT_PATH = '/identity/session/logout'

interface SessionResponse {
  readonly status: number
  readonly body: unknown
}

async function sessionPost(path: string, body: unknown): Promise<SessionResponse | 'offline' | 'unreachable'> {
  const headers: Record<string, string> = { Accept: 'application/problem+json' }
  if (body !== undefined) headers['Content-Type'] = 'application/json'
  let response: Response
  try {
    response = await fetch(`${API_BASE}${path}`, {
      method: 'POST',
      credentials: 'same-origin',
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
    })
  } catch {
    return navigator.onLine ? 'unreachable' : 'offline'
  }
  if (response.status === 204) return { status: 204, body: null }
  let parsed: unknown = null
  try {
    parsed = await response.json()
  } catch {
    parsed = null
  }
  return { status: response.status, body: parsed }
}

/** `POST /identity/session/login` — 204 sets the session cookie; the password leaves this function only in the body. */
export async function submitLogin(request: LocalLoginRequest): Promise<LoginOutcome> {
  const result = await sessionPost(LOGIN_PATH, { userName: request.userName, password: request.password })
  if (result === 'offline') return { kind: 'offline' }
  if (result === 'unreachable') return { kind: 'network-error' }
  return classifyLogin(result.status, result.body)
}

/** `POST /identity/session/logout` — 204 ends every session of the user (KIM-D30). */
export async function submitLogout(): Promise<LogoutOutcome> {
  const result = await sessionPost(LOGOUT_PATH, undefined)
  if (result === 'offline') return { kind: 'offline' }
  if (result === 'unreachable') return { kind: 'network-error' }
  return classifyLogout(result.status)
}
