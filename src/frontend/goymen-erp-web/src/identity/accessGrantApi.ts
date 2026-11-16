import { developmentIdentityHeaderEnabled } from '../shell/authMode'
import type { ViewOutcome } from '../shell/uiState'
import type { CommandOutcome } from '../settings/discountLimitApi'
import { API_BASE } from './api'
import { ACCESS_ASSIGNMENTS_PATH, ACCESS_REQUESTS_PATH, decodeAccessGrantOutcome, decodeAccessGrantRequests, type AccessGrantAction, type AccessGrantOutcome, type AccessGrantRequest } from './accessGrantContract'
const DEV_USER = (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user'
const headers = (company: string) => ({ 'X-Company-Id': company, ...(developmentIdentityHeaderEnabled() ? { 'X-Development-User': DEV_USER } : {}) })

export async function fetchAccessGrantRequests(company: string, signal?: AbortSignal): Promise<ViewOutcome<readonly AccessGrantRequest[]>> {
  let response: Response
  try { response = await fetch(`${API_BASE}${ACCESS_REQUESTS_PATH}?status=pending`, { headers: headers(company), credentials: 'same-origin', cache: 'no-store', signal }) }
  catch { return signal?.aborted ? { type: 'fence-stale' } : navigator.onLine ? { type: 'network-error' } : { type: 'offline' } }
  let raw: unknown = null
  try { raw = await response.json() } catch { /* classified below */ }
  if (response.status !== 200) return { type: 'http', status: response.status, body: raw }
  const data = decodeAccessGrantRequests(raw)
  return data === null ? { type: 'malformed' } : { type: 'ok-single', data }
}

export async function sendAccessGrant(company: string, key: string, action: AccessGrantAction, id: string | null, body: object): Promise<CommandOutcome<AccessGrantOutcome>> {
  const path = action === 'assign' ? ACCESS_ASSIGNMENTS_PATH : action === 'request' ? ACCESS_REQUESTS_PATH
    : action === 'revoke' ? `${ACCESS_ASSIGNMENTS_PATH}/${encodeURIComponent(id ?? '')}/revoke` : `${ACCESS_REQUESTS_PATH}/${encodeURIComponent(id ?? '')}/${action}`
  let response: Response
  try {
    // The existing cookie-session fetch wrapper supplies antiforgery verification; no second token mechanism.
    response = await fetch(`${API_BASE}${path}`, { method: 'POST', credentials: 'same-origin', cache: 'no-store',
      headers: { ...headers(company), 'Content-Type': 'application/json', 'Idempotency-Key': key }, body: JSON.stringify(body) })
  } catch { return { type: 'unknown', reason: navigator.onLine ? 'network-error' : 'offline' } }
  let raw: unknown = null
  try { raw = await response.json() } catch { /* a success without its typed receipt is unknown */ }
  const replayed = response.headers.get('Idempotency-Replayed')?.trim().toLowerCase() === 'true'
  if (response.status === (action === 'assign' || action === 'request' ? 201 : 200)) {
    const data = decodeAccessGrantOutcome(raw)
    return data === null ? { type: 'unknown', reason: 'unexpected' } : { type: 'applied', data, replayed }
  }
  if (response.status >= 500 || response.status < 400) return { type: 'unknown', reason: response.status >= 500 ? 'server' : 'unexpected' }
  const code = typeof raw === 'object' && raw !== null && 'code' in raw && typeof raw.code === 'string' ? raw.code : null
  return { type: 'refused', status: response.status, code, companyId: null, groupCode: null, replayed }
}
