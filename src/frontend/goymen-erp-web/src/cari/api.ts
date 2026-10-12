/**
 * Counterparty (Cari) transport. HANDOFF: replaced by the generated client (Issue #122);
 * the runtime decoding in ./contract.ts survives that move.
 *
 * Mirrors merged `GetCounterpartiesV1` (PR #141).
 */

import { decodeCounterpartyPage, type CounterpartyPage } from './contract'
import {
  API_BASE,
  contractAckOf,
  contractRequestHeader,
  beginContractRead,
  recordContractRead,
  recordContractReadFailure,
  type ContractContext,
} from './clientContract'
import type { ViewOutcome } from '../shell/uiState'
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
/** The address kinds the list can be narrowed by (#691): the exact query tokens, nothing else is accepted. */
export type AddressTypeFilter = 'billing' | 'delivery' | 'operational'
export const ADDRESS_TYPE_FILTERS: readonly AddressTypeFilter[] = ['billing', 'delivery', 'operational']
/** The server's bound for the city and district text (runes, after trimming). */
export const ADDRESS_FILTER_MAX_LENGTH = 100

export interface CounterpartyQuery {
  readonly q?: string
  readonly kind?: KindFilter
  readonly role?: readonly RoleFilter[]
  readonly roleMatch?: RoleMatch
  /**
   * The address filters (#691). The server matches all that are given against ONE address row of the counterparty,
   * before paging; the client sends them together and never narrows rows itself.
   */
  readonly addressCity?: string
  readonly addressDistrict?: string
  readonly addressType?: AddressTypeFilter
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

/** A city or district as it is sent: trimmed, NFC-normalized, or null when nothing is left. */
export function addressFilterText(value: string | undefined): string | null {
  const text = (value ?? '').trim().normalize('NFC')
  return text.length === 0 ? null : text
}

export function buildCounterpartyQuery(query: CounterpartyQuery): string {
  const params = new URLSearchParams()
  if (query.q !== undefined && query.q.trim().length > 0) params.set('q', query.q.trim())
  if (query.kind !== undefined) params.set('kind', query.kind)
  // `role` is repeatable — never comma-joined into a single value.
  for (const role of query.role ?? []) params.append('role', role)
  if (query.roleMatch !== undefined) params.set('roleMatch', query.roleMatch)
  // As the server reads them: trimmed and NFC, sent only when something is left. The letters are the user's own —
  // no case is changed here; the comparison is the server's.
  const addressCity = addressFilterText(query.addressCity)
  if (addressCity !== null) params.set('addressCity', addressCity)
  const addressDistrict = addressFilterText(query.addressDistrict)
  if (addressDistrict !== null) params.set('addressDistrict', addressDistrict)
  if (query.addressType !== undefined) params.set('addressType', query.addressType)
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
  /**
   * The context this read is issued in. Passing it lets the read establish — or
   * revoke — command readiness for exactly that context; omitting it reads normally
   * and grants nothing.
   */
  contractContext?: ContractContext | null,
): Promise<ViewOutcome<CounterpartyPage>> {
  const headers: Record<string, string> = DEV_IDENTITY_ENABLED
    ? { 'X-Development-User': DEV_USER, ...contractRequestHeader() }
    : { ...contractRequestHeader() }
  if (scope.mode === 'single') {
    if (scope.companyId === null) return { type: 'malformed' }
    headers['X-Company-Id'] = scope.companyId
  }

  /**
   * Mark this context current BEFORE the request goes out, so a response that lands
   * after the client has moved on can be recognised as late — including a successful
   * one.
   */
  beginContractRead(contractContext ?? null)

  let response: Response
  try {
    response = await fetch(`${API_BASE}/counterparties${buildCounterpartyQuery(query)}`, {
      headers,
      signal,
    })
  } catch {
    // An abort is OUR cancellation and says nothing about the server; anything else
    // is a server we can no longer reach, and readiness earned over a connection that
    // is gone must not stand.
    if (signal?.aborted) {
      recordContractReadFailure(contractContext ?? null, 'aborted')
      return { type: 'fence-stale' }
    }
    recordContractReadFailure(contractContext ?? null, 'unreachable')
    if (!navigator.onLine) return { type: 'offline' }
    // Online, but nothing came back — a different fact from a bad response body.
    return { type: 'network-error' }
  }

  let body: unknown = null
  try {
    body = await response.json()
  } catch {
    /**
     * The headers arrived, so the transport-level abort branch above never ran — but
     * the body can still be cut off mid-stream when the shell cancels this read.
     *
     * That is our own cancellation, exactly like an abort before the response. Letting
     * it fall through as "unreadable body" revoked a grant the server never withdrew.
     * A body that genuinely will not parse still does, which is the difference this
     * branch draws.
     */
    if (signal?.aborted) {
      recordContractReadFailure(contractContext ?? null, 'aborted')
      return { type: 'fence-stale' }
    }
    body = null
  }

  const ack = contractAckOf(response)

  // Exact 200 only — the whole 2xx range is not success (same rule as the shell client).
  if (response.status !== 200) {
    // A failed read never grants readiness, and it REVOKES any earlier grant for this
    // context: a server that has started refusing us is not one we may write to.
    recordContractRead(contractContext ?? null, {
      status: response.status,
      bodyValidated: false,
      ack,
    })
    return { type: 'http', status: response.status, body }
  }

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
  /**
   * Readiness is recorded from the DECODER's verdict, not the status code. A 200 whose
   * body we could not validate is a server we could not read; treating that as licence
   * to start writing to it would be exactly backwards.
   */
  recordContractRead(contractContext ?? null, {
    status: response.status,
    bodyValidated: page !== null,
    ack,
  })

  if (page === null) return { type: 'malformed' }
  return { type: 'ok-single', data: page }
}
