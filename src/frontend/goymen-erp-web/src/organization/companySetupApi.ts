import { API_BASE } from '../identity/api'
import { developmentIdentityHeaderEnabled } from '../shell/authMode'
import type { RemoteViewState } from '../shell/uiState'
import { decodeAdminCandidates, decodeFirstAdminSelected, decodeSetupCapabilities, decodeSetupCompanies, decodeSetupCompany, record, type OpenCompanyRequest, type SelectFirstAdminRequest } from './companySetupContract'

export type SetupCommandOutcome = { readonly type: 'applied'; readonly replayed: boolean }
  | { readonly type: 'refused'; readonly status: number; readonly code: string | null }
  | { readonly type: 'unknown'; readonly state: RemoteViewState<never> }
const headers = (): Record<string, string> => developmentIdentityHeaderEnabled() ? { 'X-Development-User': (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user' } : {}
export function setupHttpState(status: number): RemoteViewState<never> {
  return { kind: status === 401 ? 'unauthenticated' : status === 403 ? 'forbidden' : status === 404 ? 'not-found'
    : status === 409 ? 'conflict' : status >= 500 ? 'service-unavailable' : 'malformed-response' }
}
async function read<T>(path: string, decode: (value: unknown) => T | null, signal?: AbortSignal): Promise<RemoteViewState<T>> {
  if (!navigator.onLine) return { kind: 'offline' }
  try {
    const response = await fetch(`${API_BASE}${path}`, { headers: headers(), credentials: 'same-origin', cache: 'no-store', signal })
    if (signal?.aborted) return { kind: 'stale-rejected' }
    if (response.status !== 200) return setupHttpState(response.status)
    let raw: unknown
    try { raw = await response.json() } catch { return { kind: 'malformed-response' } }
    const data = decode(raw)
    return data === null ? { kind: 'malformed-response' } : { kind: 'ready', data }
  } catch { return { kind: signal?.aborted ? 'stale-rejected' : navigator.onLine ? 'network-error' : 'offline' } }
}
export const readSetupCapabilities = (signal?: AbortSignal) => read('/organization/company-setup/capabilities', decodeSetupCapabilities, signal)
export const readPendingSetups = (signal?: AbortSignal) => read('/organization/company-setup/companies', decodeSetupCompanies, signal)
export const readAdminCandidates = (search: string, signal?: AbortSignal) => read(`/identity/company-setup/candidates?search=${encodeURIComponent(search)}`, decodeAdminCandidates, signal)
export async function sendCompanySetup(kind: 'open' | 'assign', companyId: string, request: OpenCompanyRequest | SelectFirstAdminRequest, key: string): Promise<SetupCommandOutcome> {
  if (!navigator.onLine) return { type: 'unknown', state: { kind: 'offline' } }
  let response: Response
  try {
    response = await fetch(`${API_BASE}${kind === 'open' ? '/organization/companies' : '/identity/company-setup/first-admin'}`, {
      method: 'POST', credentials: 'same-origin', cache: 'no-store',
      headers: { ...headers(), 'Content-Type': 'application/json', 'Idempotency-Key': key, ...(kind === 'assign' ? { 'X-Company-Id': companyId } : {}) }, body: JSON.stringify(request),
    })
  } catch { return { type: 'unknown', state: { kind: navigator.onLine ? 'network-error' : 'offline' } } }
  let raw: unknown = null
  try { raw = await response.json() } catch { /* A successful malformed response is an unknown write. */ }
  const replay = response.headers.get('Idempotency-Replayed')
  const original = response.headers.get('Idempotency-Original-Correlation-Id')
  if ((replay !== null && replay !== 'true') || (replay === 'true') !== (original !== null && original.trim().length > 0))
    return { type: 'unknown', state: { kind: 'malformed-response' } }
  if (response.status === 201) {
    const data = kind === 'open' ? decodeSetupCompany(raw) : decodeFirstAdminSelected(raw)
    if (data === null || kind === 'open' && 'companyId' in data && data.companyId !== companyId) return { type: 'unknown', state: { kind: 'malformed-response' } }
    return { type: 'applied', replayed: replay === 'true' }
  }
  if (response.status >= 500 || response.status < 400) return { type: 'unknown', state: setupHttpState(response.status) }
  const code = record(raw) && typeof raw.code === 'string' ? raw.code : null
  const conflicts = kind === 'open' ? ['organization.company.already_exists', 'organization.company.code_in_use', 'organization.company.tax_id_in_use', 'organization.company.idempotency_conflict']
    : ['organization.company.setup_pending', 'identity.company.admin_already_assigned', 'organization.company.setup_pending_required', 'identity.access.version_conflict', 'identity.access.idempotency_conflict', 'identity.assignment.user_not_assignable']
  if (response.status === 409 && (code === null || !conflicts.includes(code))) return { type: 'unknown', state: { kind: 'malformed-response' } }
  if (![400, 401, 403, 404, 409, 415, 422].includes(response.status)) return { type: 'unknown', state: { kind: 'malformed-response' } }
  return { type: 'refused', status: response.status, code }
}
