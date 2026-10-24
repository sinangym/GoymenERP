/**
 * Audit event list read transport. Thin and hand-written like `../identity/api.ts`; when the generated client lands
 * (Issue #122) the fetch call moves and the decoder in ./contract.ts survives.
 *
 * GETs only: the audit trail is append-only and this screen never writes. No `pageSize` is sent — the
 * server's default page is used, so the screen picks no paging policy of its own. Only the filters the server has
 * (`correlationId`, `entityType`, `entityId`) and its opaque `cursor` are sent, each already trimmed and within the
 * server's length bound (./capabilities.ts `readAuditQuery`).
 */

import { decodeAuditEventDetails, decodeAuditEventPage, type AuditEventDetails, type AuditEventPage } from './contract'
import type { AuditQuery } from './capabilities'
import type { ViewOutcome } from '../shell/uiState'

export const API_BASE = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? '/api/v1'
const DEV_USER = (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user'
const DEV_IDENTITY_ENABLED =
  import.meta.env.DEV || import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === 'true'

function devHeaders(): Record<string, string> {
  return DEV_IDENTITY_ENABLED ? { 'X-Development-User': DEV_USER } : {}
}

export function auditEventsUrl(query: AuditQuery): string {
  const params = new URLSearchParams()
  if (query.correlationId !== null) params.set('correlationId', query.correlationId)
  if (query.entityType !== null) params.set('entityType', query.entityType)
  if (query.entityId !== null) params.set('entityId', query.entityId)
  if (query.cursor !== null) params.set('cursor', query.cursor)
  const search = params.toString()
  return `${API_BASE}/audit/events${search.length > 0 ? `?${search}` : ''}`
}

/**
 * `GET /audit/events` for the context company. A 400 (an expired or foreign cursor, an over-long filter) passes
 * through as an `http` outcome with its status, so the page can offer the first page instead of a generic error.
 */
export async function fetchAuditEvents(
  companyId: string,
  query: AuditQuery,
  signal?: AbortSignal,
): Promise<ViewOutcome<AuditEventPage>> {
  let response: Response
  try {
    response = await fetch(auditEventsUrl(query), {
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

/**
 * PROPOSED `GET /audit/events/{id}/details` — the hidden fields of one event, read only when the authorized user presses
 * "Ayrıntılar". The server must refuse it without `identity.audit.details.read`; a 403/404 passes through as a status.
 */
export async function fetchAuditEventDetails(
  companyId: string,
  eventId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<AuditEventDetails>> {
  let response: Response
  try {
    response = await fetch(`${API_BASE}/audit/events/${encodeURIComponent(eventId)}/details`, {
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
  const details = decodeAuditEventDetails(body, eventId)
  return details === null ? { type: 'malformed' } : { type: 'ok-single', data: details }
}
