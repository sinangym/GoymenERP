/**
 * FIN-S9 transport — opening balances (`/api/v1/finance/opening-balances`; FNS-D23).
 *
 * | Call | Route |
 * |---|---|
 * | `fetchOpeningPeriod` | `GET /finance/opening-balances/period` |
 * | `fetchOpeningBalances` | `GET /finance/opening-balances` (404 while the period is closed, 503 while unreadable) |
 * | `fetchOpeningBalanceCurrencyCandidates` | `GET /finance/opening-balances/currency-candidates` |
 * | `propose` | `POST /finance/opening-balances` with `Idempotency-Key` |
 * | `approve` | `POST /finance/opening-balances/{id}/approve` with `Idempotency-Key` |
 * | `reject` | `POST /finance/opening-balances/{id}/reject` with `Idempotency-Key` |
 *
 * Reads answer 200 with the resource; 403 / 404 / 503 are code-less platform problems and become `http` outcomes.
 * Commands answer the `CommandResult` body on 200 / 409 / 422; a replay carries `Idempotency-Replayed: true` and
 * `Idempotency-Original-Correlation-Id`, which must agree with the body. 400 / 401 / 403 / 404 / 415 are definite
 * refusals. 5xx, no reply or an undecodable body is an UNKNOWN outcome: the caller retries with the SAME key.
 *
 * The company is only the verified shell company in `X-Company-Id`; it is never put into a body.
 */

import type { ViewOutcome } from '../shell/uiState'
import { isBoundUuid, type Decoded } from './contract'
import {
  decodeOpeningBalanceCommandResult,
  decodeOpeningBalanceCurrencyCandidates,
  decodeOpeningBalanceListPage,
  decodeOpeningPeriod,
  type OpeningBalanceApproveBody,
  type OpeningBalanceCommandResult,
  type OpeningBalanceCurrencyCandidates,
  type OpeningBalanceListPage,
  type OpeningBalanceProposeBody,
  type OpeningBalanceRejectBody,
  type OpeningPeriodView,
} from './OpeningBalanceContract'
import { API_BASE, devHeaders } from './http'

export const OPENING_BALANCES_PATH = '/finance/opening-balances'

export const openingBalancesUrl = () => `${API_BASE}${OPENING_BALANCES_PATH}`
export const openingPeriodUrl = () => `${openingBalancesUrl()}/period`
export const openingBalanceCurrencyCandidatesUrl = () => `${openingBalancesUrl()}/currency-candidates`

async function read<T>(url: string, companyId: string, decode: (body: unknown) => Decoded<T>,
  signal?: AbortSignal): Promise<ViewOutcome<T>> {
  try {
    const response = await fetch(url, {
      method: 'GET',
      headers: { ...devHeaders(), 'X-Company-Id': companyId, Accept: 'application/json' },
      cache: 'no-store',
      signal,
    })
    if (response.status !== 200) return { type: 'http', status: response.status }
    let body: unknown
    try {
      body = await response.json()
    } catch {
      return signal?.aborted ? { type: 'fence-stale' } : { type: 'malformed' }
    }
    const decoded = decode(body)
    return decoded.ok ? { type: 'ok-single', data: decoded.value } : { type: 'malformed' }
  } catch {
    return signal?.aborted ? { type: 'fence-stale' } : navigator.onLine ? { type: 'network-error' } : { type: 'offline' }
  }
}

export function fetchOpeningPeriod(companyId: string, signal?: AbortSignal): Promise<ViewOutcome<OpeningPeriodView>> {
  if (!isBoundUuid(companyId)) return Promise.resolve({ type: 'malformed' })
  return read(openingPeriodUrl(), companyId, (body) => decodeOpeningPeriod(body, companyId), signal)
}

export function fetchOpeningBalances(companyId: string,
  signal?: AbortSignal): Promise<ViewOutcome<OpeningBalanceListPage>> {
  if (!isBoundUuid(companyId)) return Promise.resolve({ type: 'malformed' })
  return read(openingBalancesUrl(), companyId, (body) => decodeOpeningBalanceListPage(body, companyId), signal)
}

export function fetchOpeningBalanceCurrencyCandidates(companyId: string,
  signal?: AbortSignal): Promise<ViewOutcome<OpeningBalanceCurrencyCandidates>> {
  if (!isBoundUuid(companyId)) return Promise.resolve({ type: 'malformed' })
  return read(openingBalanceCurrencyCandidatesUrl(), companyId,
    (body) => decodeOpeningBalanceCurrencyCandidates(body, companyId), signal)
}

// ---------------------------------------------------------------------------------
// Commands
// ---------------------------------------------------------------------------------

export type OpeningBalanceCommand =
  | { readonly operation: 'propose'; readonly body: OpeningBalanceProposeBody }
  | { readonly operation: 'approve'; readonly openingBalanceId: string; readonly body: OpeningBalanceApproveBody }
  | { readonly operation: 'reject'; readonly openingBalanceId: string; readonly body: OpeningBalanceRejectBody }

export type OpeningBalanceOperation = OpeningBalanceCommand['operation']

export type OpeningBalanceUnknownReason =
  'offline' | 'network-error' | 'service-unavailable' | 'undecodable' | 'unexpected-status'

export type OpeningBalanceCommandReply =
  | { readonly kind: 'answered'; readonly result: OpeningBalanceCommandResult }
  | { readonly kind: 'refused'; readonly status: number }
  | { readonly kind: 'unknown'; readonly reason: OpeningBalanceUnknownReason }

export function openingBalanceCommandUrl(command: OpeningBalanceCommand): string {
  const base = openingBalancesUrl()
  switch (command.operation) {
    case 'propose':
      return base
    case 'approve':
      return `${base}/${encodeURIComponent(command.openingBalanceId)}/approve`
    case 'reject':
      return `${base}/${encodeURIComponent(command.openingBalanceId)}/reject`
  }
}

const RESULT_STATUSES: ReadonlySet<number> = new Set([200, 409, 422])
const DEFINITE_STATUSES: ReadonlySet<number> = new Set([400, 401, 403, 404, 415])

function replayHeadersAgree(response: Response, result: OpeningBalanceCommandResult): boolean {
  const replayed = response.headers.get('Idempotency-Replayed')
  const original = response.headers.get('Idempotency-Original-Correlation-Id')
  if (result.isReplay) return replayed === 'true' && original === result.originalCorrelationId
  return replayed === null && original === null
}

/** A success answers what was sent: a new proposal for that target, or the named opening in its decided status. */
function answersTheCommand(command: OpeningBalanceCommand, result: OpeningBalanceCommandResult): boolean {
  if (result.outcome !== 'Succeeded') return true
  const resource = result.resource
  switch (command.operation) {
    case 'propose':
      return resource.status === 'Proposed' && resource.targetKind === command.body.targetKind &&
        resource.targetId === command.body.targetId && resource.currencyId === command.body.currencyId &&
        resource.partyId === command.body.partyId && resource.direction === command.body.direction
    case 'approve':
      return resource.status === 'Approved' && resource.id === command.openingBalanceId
    case 'reject':
      return resource.status === 'Rejected' && resource.id === command.openingBalanceId
  }
}

export async function sendOpeningBalanceCommand(companyId: string, command: OpeningBalanceCommand,
  idempotencyKey: string, signal?: AbortSignal): Promise<OpeningBalanceCommandReply> {
  if (!isBoundUuid(companyId)) return { kind: 'unknown', reason: 'undecodable' }
  let response: Response
  try {
    response = await fetch(openingBalanceCommandUrl(command), {
      method: 'POST',
      headers: {
        ...devHeaders(),
        'X-Company-Id': companyId,
        'Idempotency-Key': idempotencyKey,
        'Content-Type': 'application/json',
        Accept: 'application/json',
      },
      body: JSON.stringify(command.body),
      cache: 'no-store',
      signal,
    })
  } catch {
    return { kind: 'unknown', reason: navigator.onLine ? 'network-error' : 'offline' }
  }

  if (RESULT_STATUSES.has(response.status)) {
    let body: unknown
    try {
      body = await response.json()
    } catch {
      return { kind: 'unknown', reason: 'undecodable' }
    }
    const decoded = decodeOpeningBalanceCommandResult(body, companyId)
    if (!decoded.ok || !replayHeadersAgree(response, decoded.value) || !answersTheCommand(command, decoded.value)) {
      return { kind: 'unknown', reason: 'undecodable' }
    }
    const expected = response.status === 200 ? 'Succeeded' : response.status === 422 ? 'ValidationFailed' : null
    const outcome = decoded.value.outcome
    // 200 is only a success, 422 only a validation refusal, 409 only a conflict.
    if (expected !== null ? outcome !== expected : outcome !== 'VersionConflict' && outcome !== 'IdempotencyConflict') {
      return { kind: 'unknown', reason: 'undecodable' }
    }
    return { kind: 'answered', result: decoded.value }
  }
  if (response.status >= 500) return { kind: 'unknown', reason: 'service-unavailable' }
  if (DEFINITE_STATUSES.has(response.status)) return { kind: 'refused', status: response.status }
  return { kind: 'unknown', reason: 'unexpected-status' }
}
