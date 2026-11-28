import { API_BASE, purchasingHeaders } from './api'
import { decodeRecordUnorderedAcceptance, decodeUnorderedAcceptanceSource, decodeUnorderedAcceptanceWrite, unorderedGuid,
  type RecordUnorderedAcceptance, type UnorderedAcceptanceSource, type UnorderedAcceptanceWrite } from './unorderedAcceptanceContract'
import type { ViewOutcome } from '../shell/uiState'

const PATH = `${API_BASE}/purchasing/unordered-acceptances`
/** Caller supplies the current authorized shell scope; null after logout, route permission loss or an unresolved company. */
export interface UnorderedAcceptanceScope {
  readonly sessionBinding: string; readonly actorId: string; readonly companyId: string
  /** Shell committedContextRevision: a departed read cannot revive after switching away and back. */
  readonly contextRevision: number
}
export interface UnorderedAcceptanceAttempt {
  readonly scope: UnorderedAcceptanceScope; readonly key: string; readonly request: RecordUnorderedAcceptance; readonly body: string
}
export type UnorderedAcceptanceOutcome = { readonly type: 'applied'; readonly response: UnorderedAcceptanceWrite }
  | { readonly type: 'failed'; readonly status: number; readonly code: string | null }
  | { readonly type: 'unknown'; readonly reason: 'authority' | 'service-unavailable' | 'network-error' | 'malformed' | 'fence-stale'; readonly status?: number }
  | { readonly type: 'fence-stale' }

const pending = new Map<string, UnorderedAcceptanceAttempt>() // Memory only; no storage, URL, logging or implicit new key.
const issued = new WeakSet<UnorderedAcceptanceAttempt>()
const inFlight = new WeakSet<UnorderedAcceptanceAttempt>()
const validScope = (v: UnorderedAcceptanceScope | null): v is UnorderedAcceptanceScope => v !== null && typeof v.sessionBinding === 'string'
  && v.sessionBinding.trim() !== '' && v.sessionBinding.trim() === v.sessionBinding && typeof v.actorId === 'string' && v.actorId.trim() !== ''
  && v.actorId.trim() === v.actorId && unorderedGuid(v.companyId) && Number.isSafeInteger(v.contextRevision) && v.contextRevision >= 0
const scopeKey = (v: UnorderedAcceptanceScope) => JSON.stringify([v.sessionBinding, v.actorId, v.companyId.toLowerCase()])
const current = (saved: UnorderedAcceptanceScope, read: () => UnorderedAcceptanceScope | null, signal?: AbortSignal) => {
  const live = read(); return !signal?.aborted && validScope(live) && scopeKey(saved) === scopeKey(live) && saved.contextRevision === live.contextRevision
}
function freeze<T>(value: T): T {
  if (typeof value === 'object' && value !== null) { Object.values(value).forEach(freeze); Object.freeze(value) }
  return value
}
export function recallUnorderedAcceptanceAttempt(scope: UnorderedAcceptanceScope | null): UnorderedAcceptanceAttempt | null {
  return validScope(scope) ? pending.get(scopeKey(scope)) ?? null : null
}
/** Freeze before any asynchronous work. A different key/body cannot replace an unresolved original command. */
export function prepareUnorderedAcceptanceAttempt(scope: UnorderedAcceptanceScope | null, request: RecordUnorderedAcceptance,
  key: string): UnorderedAcceptanceAttempt | null {
  if (!validScope(scope) || typeof key !== 'string' || key.trim() !== key || key === '' || key.length > 128 || key.includes(',') || /[\r\n]/.test(key)) return null
  const captured = structuredClone(request)
  if (decodeRecordUnorderedAcceptance(captured) === null) return null
  const body = JSON.stringify(captured), old = pending.get(scopeKey(scope))
  if (old !== undefined) return old.key === key && old.body === body ? old : null
  const attempt = freeze({ scope: { ...scope }, key, request: captured, body })
  issued.add(attempt)
  return attempt
}
async function bodyOf(response: Response): Promise<unknown> { try { return await response.json() } catch { return null } }
export async function recordUnorderedAcceptance(attempt: UnorderedAcceptanceAttempt, getCurrentScope: () => UnorderedAcceptanceScope | null,
  signal?: AbortSignal): Promise<UnorderedAcceptanceOutcome> {
  const live = getCurrentScope()
  if (!issued.has(attempt) || !validScope(attempt.scope) || !validScope(live) || signal?.aborted || scopeKey(attempt.scope) !== scopeKey(live)) return { type: 'fence-stale' }
  const execution = { ...live } // A fresh authorized retry keeps its original body/key, with a new per-dispatch read fence.
  const index = scopeKey(attempt.scope), old = pending.get(index)
  if (old !== undefined && old !== attempt) return { type: 'fence-stale' }
  if (inFlight.has(attempt)) return { type: 'unknown', reason: 'fence-stale' }
  pending.set(index, attempt)
  inFlight.add(attempt)
  try {
    const response = await fetch(PATH, { method: 'POST', headers: { ...purchasingHeaders(attempt.scope.companyId),
      'Content-Type': 'application/json', 'Idempotency-Key': attempt.key }, body: attempt.body, cache: 'no-store', signal })
    if (!current(execution, getCurrentScope, signal)) return { type: 'unknown', reason: 'fence-stale' }
    const body = await bodyOf(response)
    if (!current(execution, getCurrentScope, signal)) return { type: 'unknown', reason: 'fence-stale' }
    if (response.status === 401 || response.status === 403) return { type: 'unknown', reason: 'authority', status: response.status }
    if (response.status >= 500) return { type: 'unknown', reason: 'service-unavailable', status: response.status }
    if (response.status !== 201) {
      if (![400, 404, 409, 415, 422].includes(response.status)) return { type: 'unknown', reason: 'malformed' }
      // Once a previous dispatch is unresolved, a refusal cannot prove that the original command had no effect.
      if (old !== undefined) return { type: 'unknown', reason: 'malformed', status: response.status }
      pending.delete(index)
      return { type: 'failed', status: response.status, code: typeof body === 'object' && body !== null && 'code' in body && typeof body.code === 'string' ? body.code : null }
    }
    const decoded = decodeUnorderedAcceptanceWrite(body, attempt.scope.companyId, attempt.scope.actorId, attempt.request,
      response.headers.get('Idempotency-Replayed') === 'true', response.headers.get('Idempotency-Original-Correlation-Id'))
    if (decoded === null) return { type: 'unknown', reason: 'malformed' }
    pending.delete(index)
    return { type: 'applied', response: decoded }
  } catch { return { type: 'unknown', reason: current(execution, getCurrentScope, signal) ? 'network-error' : 'fence-stale' } }
  finally { inFlight.delete(attempt) }
}

export async function fetchUnorderedAcceptance(scope: UnorderedAcceptanceScope, acceptanceId: string, lineId: string,
  getCurrentScope: () => UnorderedAcceptanceScope | null, signal?: AbortSignal): Promise<ViewOutcome<UnorderedAcceptanceSource>> {
  if (!validScope(scope) || !unorderedGuid(acceptanceId) || !unorderedGuid(lineId)) return { type: 'fence-stale' }
  const captured = { ...scope }
  if (!current(captured, getCurrentScope, signal)) return { type: 'fence-stale' }
  try {
    const response = await fetch(`${PATH}/${encodeURIComponent(acceptanceId)}/lines/${encodeURIComponent(lineId)}`,
      { headers: purchasingHeaders(captured.companyId), cache: 'no-store', signal })
    if (!current(captured, getCurrentScope, signal)) return { type: 'fence-stale' }
    const body = await bodyOf(response)
    if (!current(captured, getCurrentScope, signal)) return { type: 'fence-stale' }
    if (response.status !== 200) return response.status >= 200 && response.status < 300 ? { type: 'malformed' } : { type: 'http', status: response.status, body }
    const data = decodeUnorderedAcceptanceSource(body, captured.companyId, acceptanceId, lineId)
    return data === null ? { type: 'malformed' } : { type: 'ok-single', data }
  } catch { return signal?.aborted || !current(captured, getCurrentScope, signal) ? { type: 'fence-stale' } : { type: 'network-error' } }
}
