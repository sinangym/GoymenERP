import { developmentIdentityHeaderEnabled } from '../shell/authMode'
import type { ViewOutcome } from '../shell/uiState'
import type { CommandOutcome } from '../settings/discountLimitApi'
import { API_BASE } from './api'
import { accessGrantVersion, type AccessGrantOutcome } from './accessGrantContract'
import { isWellFormedId } from './capabilities'

export interface ManagedUser { readonly userId: string; readonly displayName: string; readonly userName: string; readonly isActive: boolean; readonly version: string; readonly mustChangePassword: boolean; readonly isLocked: boolean }
const record = (x: unknown): x is Record<string, unknown> => typeof x === 'object' && x !== null && !Array.isArray(x)
export function decodeManagedUsers(raw: unknown): readonly ManagedUser[] | null {
  if (!Array.isArray(raw)) return null
  const result: ManagedUser[] = []
  for (const x of raw) {
    if (!record(x) || Object.keys(x).sort().join(',') !== 'displayName,isActive,isLocked,mustChangePassword,userId,userName,version'
      || !isWellFormedId(x.userId as string) || typeof x.displayName !== 'string' || !x.displayName.trim() || typeof x.userName !== 'string' || !x.userName.trim()
      || typeof x.isActive !== 'boolean' || typeof x.isLocked !== 'boolean' || typeof x.mustChangePassword !== 'boolean' || !accessGrantVersion(x.version)
      || result.some((u) => u.userId.toLowerCase() === (x.userId as string).toLowerCase())) return null
    result.push(x as unknown as ManagedUser)
  }
  return result
}
const headers = (company: string) => ({ 'X-Company-Id': company, ...(developmentIdentityHeaderEnabled() ? { 'X-Development-User': (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user' } : {}) })
export async function readManagedUsers(company: string, signal?: AbortSignal): Promise<ViewOutcome<readonly ManagedUser[]>> {
  try {
    const response = await fetch(`${API_BASE}/identity/access/managed-users`, { headers: headers(company), credentials: 'same-origin', cache: 'no-store', signal })
    let raw: unknown = null; try { raw = await response.json() } catch { /* strict below */ }
    if (response.status !== 200) return { type: 'http', status: response.status, body: raw }
    const data = decodeManagedUsers(raw)
    return data === null ? { type: 'malformed' } : { type: 'ok-single', data }
  } catch { return signal?.aborted ? { type: 'fence-stale' } : { type: navigator.onLine ? 'network-error' : 'offline' } }
}
export async function sendUserManagement(company: string, id: string | null, action: string, body: object, key: string): Promise<CommandOutcome<AccessGrantOutcome>> {
  try {
    const path = id === null ? '/identity/access/users' : `/identity/access/users/${encodeURIComponent(id)}/${action}`
    const response = await fetch(`${API_BASE}${path}`, { method: 'POST', headers: { ...headers(company), 'Content-Type': 'application/json', 'Idempotency-Key': key }, credentials: 'same-origin', cache: 'no-store', body: JSON.stringify(body) })
    let raw: unknown = null; try { raw = await response.json() } catch { /* success without receipt is unknown */ }
    if (response.status === (id === null ? 201 : 200)) {
      if (!record(raw) || Object.keys(raw).sort().join(',') !== 'id,roleAssignmentId,status,version' || !isWellFormedId(raw.id as string) || !accessGrantVersion(raw.version) || !['Active', 'Inactive'].includes(raw.status as string) || raw.roleAssignmentId !== null
        || (id !== null && raw.id !== id)) return { type: 'unknown', reason: 'unexpected' }
      return { type: 'applied', data: raw as unknown as AccessGrantOutcome, replayed: response.headers.get('Idempotency-Replayed') === 'true' }
    }
    if (response.status >= 500 || response.status < 400) return { type: 'unknown', reason: 'server' }
    return { type: 'refused', status: response.status, code: record(raw) && typeof raw.code === 'string' ? raw.code : null, companyId: null, groupCode: null, replayed: false }
  } catch { return { type: 'unknown', reason: navigator.onLine ? 'network-error' : 'offline' } }
}
