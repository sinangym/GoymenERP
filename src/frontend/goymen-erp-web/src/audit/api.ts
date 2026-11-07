/**
 * Audit log read transport (UI-K02). Thin and hand-written like `../identity/api.ts`; when the generated client lands
 * (Issue #122) the fetch call moves and the decoder in ./contract.ts survives.
 *
 * One GET only: `GET /api/v1/audit/events?pageSize&cursor&correlationId&entityType&entityId`. There is no write,
 * export or delete function in this file and there must not be one. Filters are exact-match, trimmed, and an empty
 * filter is not sent; the cursor is opaque and sent back exactly as the server issued it.
 */

import { decodeAuditEventPage, type AuditEventPage } from './contract'
import type { AuditFilters } from './capabilities'
import type { ViewOutcome } from '../shell/uiState'

export const API_BASE = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? '/api/v1'
const DEV_USER = (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user'
const DEV_IDENTITY_ENABLED =
  import.meta.env.DEV || import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === 'true'

/** The page size this screen asks for; the server clamps any value into 1…100 and echoes what it used. */
export const AUDIT_PAGE_SIZE = 25

function devHeaders(): Record<string, string> {
  return DEV_IDENTITY_ENABLED ? { 'X-Development-User': DEV_USER } : {}
}

export function auditEventsPath(filters: AuditFilters, cursor: string | null): string {
  const query = new URLSearchParams()
  query.set('pageSize', String(AUDIT_PAGE_SIZE))
  if (cursor !== null) query.set('cursor', cursor)
  if (filters.correlationId !== '') query.set('correlationId', filters.correlationId)
  if (filters.entityType !== '') query.set('entityType', filters.entityType)
  if (filters.entityId !== '') query.set('entityId', filters.entityId)
  return `/audit/events?${query.toString()}`
}

/**
 * One page for the active company. `fence-stale` when the request was aborted (a newer read or context took over),
 * `offline` / `network-error` when no response arrived, `http` for any non-200 with its body, `malformed` for a 200
 * that is not exactly one page of this company.
 */
export async function fetchAuditEventPage(
  companyId: string,
  filters: AuditFilters,
  cursor: string | null,
  signal?: AbortSignal,
): Promise<ViewOutcome<AuditEventPage>> {
  let response: Response
  try {
    response = await fetch(`${API_BASE}${auditEventsPath(filters, cursor)}`, {
      headers: { ...devHeaders(), 'X-Company-Id': companyId },
      signal,
    })
  } catch {
    if (signal?.aborted) return { type: 'fence-stale' }
    return navigator.onLine ? { type: 'network-error' } : { type: 'offline' }
  }
  let body: unknown = null
  try {
    body = await response.json()
  } catch {
    if (signal?.aborted) return { type: 'fence-stale' }
    body = null
  }
  if (response.status !== 200) return { type: 'http', status: response.status, body }
  const page = decodeAuditEventPage(body, companyId)
  if (page === null) return { type: 'malformed' }
  return { type: 'ok-single', data: page }
}
