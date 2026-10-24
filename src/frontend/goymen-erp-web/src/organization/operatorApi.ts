/**
 * O02 location operator transport: the two reads of PR #795 and the write already on main.
 *
 *   GET /organization/locations/{id}/operator             (organization.locations.read)
 *   GET /organization/locations/{id}/operator-candidates  (organization.locations.write; q, cursor; pageSize unsent)
 *   PUT /organization/locations/{id}/operator             (organization.locations.write) -> 204
 *
 * The write's wire rules are the active-state command's (./commandApi.ts): the caller owns the `Idempotency-Key`, one per
 * submitted snapshot; success is EXACTLY 204 with no body, so the card is re-read and nothing is predicted; 409 is a
 * version conflict (with the `conflict` object) or an idempotency conflict (without); an answer we cannot read is
 * UNKNOWN. Two coded answers are this command's own:
 *
 *  - 422 `organization_location_operator_ineligible` — the location is not external or the party may not operate it;
 *  - 503 `organization_location_operator_dependency_unavailable` — the Cari eligibility check could not run. The server
 *    returns it BEFORE recording anything (`OrganizationMasterDataService.SetLocationOperatorAsync`), so the same key may
 *    be sent again; it is kept apart from an uncoded 5xx, which stays UNKNOWN.
 */

import { decodeVersionConflict, versionToken } from './contract'
import { API_BASE, devHeaders, read, transportOutcome } from './api'
import type { CommandFailure, CommandUnknown } from './commandApi'
import {
  decodeLocationOperator,
  decodeOperatorCandidatePage,
  type LocationOperatorContext,
  type OperatorCandidatePage,
} from './operatorContract'
import type { ViewOutcome } from '../shell/uiState'

export const OPERATOR_INELIGIBLE = 'organization_location_operator_ineligible'
export const OPERATOR_DEPENDENCY_UNAVAILABLE = 'organization_location_operator_dependency_unavailable'
export const INVALID_CURSOR = 'invalid_cursor'

function locationPath(locationId: string): string {
  return `/organization/locations/${encodeURIComponent(locationId)}`
}

export async function fetchLocationOperator(
  companyId: string,
  locationId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<LocationOperatorContext>> {
  const result = await read(`${locationPath(locationId)}/operator`, companyId, signal)
  if (typeof result === 'string') return transportOutcome(result)
  if (!result.isOk200) return { type: 'http', status: result.status, body: result.body }
  const context = decodeLocationOperator(result.body, locationId)
  return context === null ? { type: 'malformed' } : { type: 'ok-single', data: context }
}

export interface CandidateQuery {
  /** Already trimmed; empty means "no search text". */
  readonly q: string
  readonly cursor: string | null
}

export function candidatesPath(locationId: string, query: CandidateQuery): string {
  const params = new URLSearchParams()
  if (query.q.length > 0) params.set('q', query.q)
  if (query.cursor !== null) params.set('cursor', query.cursor)
  const search = params.toString()
  return `${locationPath(locationId)}/operator-candidates${search.length > 0 ? `?${search}` : ''}`
}

export async function fetchOperatorCandidates(
  companyId: string,
  locationId: string,
  query: CandidateQuery,
  signal?: AbortSignal,
): Promise<ViewOutcome<OperatorCandidatePage>> {
  const result = await read(candidatesPath(locationId, query), companyId, signal)
  if (typeof result === 'string') return transportOutcome(result)
  if (!result.isOk200) return { type: 'http', status: result.status, body: result.body }
  const page = decodeOperatorCandidatePage(result.body)
  return page === null ? { type: 'malformed' } : { type: 'ok-single', data: page }
}

export interface SetOperatorPayload {
  readonly locationId: string
  readonly operatorPartyId: string
  /** The version READ from the server. Never a predicted one. */
  readonly expectedVersion: number
  readonly reason: string
}

export type OperatorFailure = CommandFailure | { readonly kind: 'ineligible' }

export type OperatorOutcome =
  | { readonly type: 'applied'; readonly replayed: boolean }
  | { readonly type: 'failed'; readonly failure: OperatorFailure; readonly replayed: boolean }
  /** Coded 503: nothing was recorded; the same key may be sent again. */
  | { readonly type: 'dependency-unavailable' }
  | { readonly type: 'unknown'; readonly unknown: CommandUnknown }

function isObjectBody(body: unknown): body is Record<string, unknown> {
  return typeof body === 'object' && body !== null && !Array.isArray(body)
}

function classify(status: number, body: unknown, bodyParsed: boolean, replayed: boolean): OperatorFailure | null {
  if (status === 400) return { kind: 'validation' }
  if (status === 401) return { kind: 'unauthenticated' }
  if (status === 403) return { kind: 'forbidden' }
  if (status === 404) return { kind: 'not-found' }
  if (status === 415) return { kind: 'unsupported-media' }
  if (status === 422) return isObjectBody(body) && body.code === OPERATOR_INELIGIBLE ? { kind: 'ineligible' } : null
  if (status === 409) {
    const conflict = decodeVersionConflict(body)
    if (conflict !== null) return { kind: 'version-conflict', conflict, conflictIsCurrent: !replayed }
    return bodyParsed && isObjectBody(body) ? { kind: 'idempotency-conflict' } : null
  }
  return null
}

async function readBody(response: Response): Promise<{ readonly body: unknown; readonly parsed: boolean }> {
  try {
    return { body: await response.json(), parsed: true }
  } catch {
    return { body: null, parsed: false }
  }
}

/** Sends the assignment with the caller's key. Never retries on its own and never rotates the key. */
export async function setLocationOperator(
  companyId: string,
  idempotencyKey: string,
  payload: SetOperatorPayload,
  signal?: AbortSignal,
): Promise<OperatorOutcome> {
  let response: Response
  try {
    response = await fetch(`${API_BASE}${locationPath(payload.locationId)}/operator`, {
      method: 'PUT',
      headers: {
        ...devHeaders(),
        'Content-Type': 'application/json',
        'X-Company-Id': companyId,
        'Idempotency-Key': idempotencyKey,
      },
      body: JSON.stringify({
        operatorPartyId: payload.operatorPartyId,
        expectedVersion: versionToken(payload.expectedVersion),
        reason: payload.reason.trim(),
      }),
      signal,
    })
  } catch {
    if (signal?.aborted) return { type: 'unknown', unknown: { kind: 'aborted' } }
    if (!navigator.onLine) return { type: 'unknown', unknown: { kind: 'offline' } }
    return { type: 'unknown', unknown: { kind: 'network-error' } }
  }

  const replayed = (response.headers.get('Idempotency-Replayed') ?? '').trim().toLowerCase() === 'true'
  if (response.status === 204) return { type: 'applied', replayed }
  if (response.status >= 200 && response.status < 300) {
    return { type: 'unknown', unknown: { kind: 'unexpected-success', status: response.status } }
  }

  const { body, parsed } = await readBody(response)
  if (response.status === 503 && isObjectBody(body) && body.code === OPERATOR_DEPENDENCY_UNAVAILABLE) {
    return { type: 'dependency-unavailable' }
  }
  if (response.status >= 500) {
    return { type: 'unknown', unknown: { kind: 'service-unavailable', status: response.status } }
  }
  const failure = classify(response.status, body, parsed, replayed)
  return failure === null
    ? { type: 'unknown', unknown: { kind: 'undecodable', status: response.status } }
    : { type: 'failed', failure, replayed }
}
