/**
 * API-85b transport — the customer collateral master (`/api/v1/finance/collaterals`; FNS-D57, FNS-D31, FNS-D58).
 *
 * | Call | Route |
 * |---|---|
 * | `fetchCollateralAccount` | `GET /finance/collaterals/accounts/{companyAccountId}` |
 * | `fetchCollateralHistory` | `GET /finance/collaterals/accounts/{companyAccountId}/history` |
 * | `fetchCollateralTypeCandidates` | `GET /finance/collaterals/type-candidates` |
 * | `fetchCollateralCurrencyCandidates` | `GET /finance/collaterals/currency-candidates` |
 * | `record` | `POST /finance/collaterals` with `Idempotency-Key` (one step, effective at once) |
 * | `change` | `POST /finance/collaterals/{collateralId}/changes` with `Idempotency-Key` |
 * | `release` | `POST /finance/collaterals/{collateralId}/release` with `Idempotency-Key` |
 *
 * Reads answer 200 with the resource; 403 / 404 / 503 are code-less platform problems and become `http` outcomes (a
 * history with no record is 404). Commands answer the `CommandResult` body on 200 / 409 / 422; a replay carries
 * `Idempotency-Replayed: true` and `Idempotency-Original-Correlation-Id`, which must agree with the body. 403 is a
 * definite refusal. 503, no reply or an undecodable body is an UNKNOWN outcome: the caller retries with the SAME key.
 *
 * The company is only the verified shell company in `X-Company-Id`; it is never put into a body.
 */

import type { ViewOutcome } from '../shell/uiState'
import { isBoundUuid, type Decoded } from './contract'
import {
  decodeCollateralAccount,
  decodeCollateralCommandResult,
  decodeCollateralCurrencyCandidates,
  decodeCollateralHistoryPage,
  decodeCollateralTypeCandidates,
  type CollateralAccount,
  type CollateralChangeBody,
  type CollateralCommandResult,
  type CollateralCurrencyCandidates,
  type CollateralHistoryPage,
  type CollateralRecordBody,
  type CollateralReleaseBody,
  type CollateralTypeCandidates,
} from './CollateralContract'
import { API_BASE, devHeaders } from './http'

export const COLLATERALS_PATH = '/finance/collaterals'

export const collateralsUrl = () => `${API_BASE}${COLLATERALS_PATH}`
export const collateralAccountUrl = (companyAccountId: string) =>
  `${API_BASE}${COLLATERALS_PATH}/accounts/${encodeURIComponent(companyAccountId)}`
export const collateralHistoryUrl = (companyAccountId: string) => `${collateralAccountUrl(companyAccountId)}/history`
export const collateralTypeCandidatesUrl = () => `${API_BASE}${COLLATERALS_PATH}/type-candidates`
export const collateralCurrencyCandidatesUrl = () => `${API_BASE}${COLLATERALS_PATH}/currency-candidates`

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

export function fetchCollateralAccount(companyId: string, companyAccountId: string,
  signal?: AbortSignal): Promise<ViewOutcome<CollateralAccount>> {
  if (!isBoundUuid(companyId) || !isBoundUuid(companyAccountId)) return Promise.resolve({ type: 'malformed' })
  return read(collateralAccountUrl(companyAccountId), companyId,
    (body) => decodeCollateralAccount(body, companyId, companyAccountId), signal)
}

export function fetchCollateralHistory(companyId: string, companyAccountId: string,
  signal?: AbortSignal): Promise<ViewOutcome<CollateralHistoryPage>> {
  if (!isBoundUuid(companyId) || !isBoundUuid(companyAccountId)) return Promise.resolve({ type: 'malformed' })
  return read(collateralHistoryUrl(companyAccountId), companyId,
    (body) => decodeCollateralHistoryPage(body, companyId, companyAccountId), signal)
}

export function fetchCollateralTypeCandidates(companyId: string,
  signal?: AbortSignal): Promise<ViewOutcome<CollateralTypeCandidates>> {
  if (!isBoundUuid(companyId)) return Promise.resolve({ type: 'malformed' })
  return read(collateralTypeCandidatesUrl(), companyId, (body) => decodeCollateralTypeCandidates(body, companyId), signal)
}

export function fetchCollateralCurrencyCandidates(companyId: string,
  signal?: AbortSignal): Promise<ViewOutcome<CollateralCurrencyCandidates>> {
  if (!isBoundUuid(companyId)) return Promise.resolve({ type: 'malformed' })
  return read(collateralCurrencyCandidatesUrl(), companyId,
    (body) => decodeCollateralCurrencyCandidates(body, companyId), signal)
}

// ---------------------------------------------------------------------------------
// Commands
// ---------------------------------------------------------------------------------

export type CollateralCommand =
  | { readonly operation: 'record'; readonly body: CollateralRecordBody }
  | { readonly operation: 'change'; readonly collateralId: string; readonly body: CollateralChangeBody }
  | { readonly operation: 'release'; readonly collateralId: string; readonly body: CollateralReleaseBody }

export type CollateralOperation = CollateralCommand['operation']

export type CollateralUnknownReason = 'offline' | 'network-error' | 'service-unavailable' | 'undecodable' | 'unexpected-status'

export type CollateralCommandReply =
  /** A decoded `CommandResult` (200 / 409 / 422) whose replay headers agree with its body. */
  | { readonly kind: 'answered'; readonly result: CollateralCommandResult }
  /** A definite refusal stated by status alone: 400 (malformed body), 401, 403, 404, 415. */
  | { readonly kind: 'refused'; readonly status: number }
  /** The outcome is not known: retry with the SAME key. */
  | { readonly kind: 'unknown'; readonly reason: CollateralUnknownReason }

export function collateralCommandUrl(command: CollateralCommand): string {
  const base = collateralsUrl()
  switch (command.operation) {
    case 'record':
      return base
    case 'change':
      return `${base}/${encodeURIComponent(command.collateralId)}/changes`
    case 'release':
      return `${base}/${encodeURIComponent(command.collateralId)}/release`
  }
}

const RESULT_STATUSES: ReadonlySet<number> = new Set([200, 409, 422])
const DEFINITE_STATUSES: ReadonlySet<number> = new Set([400, 401, 403, 404, 415])

/** `isReplay` ⇔ `Idempotency-Replayed: true`, and a replay names the original correlation in both places. */
function replayHeadersAgree(response: Response, result: CollateralCommandResult): boolean {
  const replayed = response.headers.get('Idempotency-Replayed')
  const original = response.headers.get('Idempotency-Original-Correlation-Id')
  if (result.isReplay) return replayed === 'true' && original === result.originalCorrelationId
  return replayed === null && original === null
}

/** A success answers the collateral that was named (change / release) or the account it was recorded on. */
function answersTheCommand(command: CollateralCommand, result: CollateralCommandResult): boolean {
  if (result.outcome !== 'Succeeded') return true
  const resource = result.resource
  switch (command.operation) {
    case 'record':
      return resource.kind === 'Record' && resource.companyAccountId === command.body.companyAccountId &&
        resource.partyId === command.body.partyId
    case 'change':
      return resource.kind === 'Change' && resource.collateralId === command.collateralId
    case 'release':
      return resource.kind === 'Release' && resource.collateralId === command.collateralId
  }
}

export async function sendCollateralCommand(companyId: string, command: CollateralCommand, idempotencyKey: string,
  signal?: AbortSignal): Promise<CollateralCommandReply> {
  if (!isBoundUuid(companyId)) return { kind: 'unknown', reason: 'undecodable' }
  let response: Response
  try {
    response = await fetch(collateralCommandUrl(command), {
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
    const decoded = decodeCollateralCommandResult(body, companyId)
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
