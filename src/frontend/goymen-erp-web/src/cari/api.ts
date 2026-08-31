/**
 * Counterparty (Cari) transport. HANDOFF: replaced by the generated client (Issue #122);
 * the runtime decoding in ./contract.ts survives that move.
 *
 * Mirrors merged `GetCounterpartiesV1` (PR #141).
 */

import { decodeCounterpartyPage, type CounterpartyPage } from './contract'
import type { ViewOutcome } from '../shell/uiState'

const API_BASE = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? '/api/v1'
const DEV_USER = (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user'
const DEV_IDENTITY_ENABLED =
  import.meta.env.DEV || import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === 'true'

export type PartyStateFilter = 'active' | 'inactive' | 'all'
/** Visible company-account state. A DIFFERENT fact from `partyState` (Codex #147 P1-4). */
export type AccountStateFilter = 'active' | 'inactive' | 'all'
export type SortField = 'code' | 'legalName' | 'updatedAt'
export type SortDirection = 'asc' | 'desc'
export type RoleMatch = 'any' | 'all'
export type KindFilter = 'LegalEntity' | 'NaturalPerson'
export type RoleFilter = 'Customer' | 'Supplier' | 'Tolling' | 'Service'

export interface CounterpartyQuery {
  readonly q?: string
  readonly kind?: KindFilter
  readonly role?: readonly RoleFilter[]
  readonly roleMatch?: RoleMatch
  readonly partyState?: PartyStateFilter
  readonly accountState?: AccountStateFilter
  readonly sort?: SortField
  readonly direction?: SortDirection
  readonly pageSize?: number
  readonly cursor?: string
  /** Contract default is false. The decoder binds the response to what we asked for. */
  readonly includeCount?: boolean
}

/**
 * `X-Company-Id` is sent only in single-company mode. The accepted capability declares
 * `SingleOrAuthorizedMultiCompanyRead` (Codex PR #149), so group mode deliberately omits
 * the header and lets the server resolve the authorized company set — the client never
 * unions companies itself (ERP-000K §9.2).
 */
export interface CounterpartyScope {
  readonly mode: 'single' | 'group'
  readonly companyId: string | null
}

export function buildCounterpartyQuery(query: CounterpartyQuery): string {
  const params = new URLSearchParams()
  if (query.q !== undefined && query.q.trim().length > 0) params.set('q', query.q.trim())
  if (query.kind !== undefined) params.set('kind', query.kind)
  // `role` is repeatable — never comma-joined into a single value.
  for (const role of query.role ?? []) params.append('role', role)
  if (query.roleMatch !== undefined) params.set('roleMatch', query.roleMatch)
  if (query.partyState !== undefined) params.set('partyState', query.partyState)
  if (query.accountState !== undefined) params.set('accountState', query.accountState)
  if (query.includeCount !== undefined) params.set('includeCount', String(query.includeCount))
  if (query.sort !== undefined) params.set('sort', query.sort)
  if (query.direction !== undefined) params.set('direction', query.direction)
  if (query.pageSize !== undefined) params.set('pageSize', String(query.pageSize))
  if (query.cursor !== undefined) params.set('cursor', query.cursor)
  const serialized = params.toString()
  return serialized.length > 0 ? `?${serialized}` : ''
}

export async function fetchCounterparties(
  scope: CounterpartyScope,
  query: CounterpartyQuery,
  signal?: AbortSignal,
): Promise<ViewOutcome<CounterpartyPage>> {
  const headers: Record<string, string> = DEV_IDENTITY_ENABLED
    ? { 'X-Development-User': DEV_USER }
    : {}
  if (scope.mode === 'single') {
    if (scope.companyId === null) return { type: 'malformed' }
    headers['X-Company-Id'] = scope.companyId
  }

  let response: Response
  try {
    response = await fetch(`${API_BASE}/counterparties${buildCounterpartyQuery(query)}`, {
      headers,
      signal,
    })
  } catch (error) {
    if (signal?.aborted) return { type: 'fence-stale' }
    if (!navigator.onLine) return { type: 'offline' }
    throw error
  }

  let body: unknown = null
  try {
    body = await response.json()
  } catch {
    body = null
  }

  // Exact 200 only — the whole 2xx range is not success (same rule as the shell client).
  if (response.status !== 200) return { type: 'http', status: response.status, body }

  // The decoder binds the page to the caller's company context: in single mode a
  // foreign or missing account rejects the whole page (JIT spec §3.5). It is also
  // bound to what we ASKED for: a count we did not request is not a count we can
  // interpret (Codex #147 P1-4).
  const page = decodeCounterpartyPage(
    body,
    scope.mode === 'single' && scope.companyId !== null
      ? { mode: 'single', activeCompanyId: scope.companyId }
      : { mode: 'group' },
    query.includeCount === true,
  )
  if (page === null) return { type: 'malformed' }
  return { type: 'ok-single', data: page }
}
