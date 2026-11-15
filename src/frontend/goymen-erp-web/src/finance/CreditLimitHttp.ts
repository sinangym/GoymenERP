/**
 * API-85a transport — the party credit limit master (`/api/v1/finance/credit-limits`).
 *
 * | Call | Route |
 * |---|---|
 * | `fetchCreditLimits` | `GET /finance/credit-limits` |
 * | `fetchCreditLimitAccount` | `GET /finance/credit-limits/accounts/{companyAccountId}` |
 * | `fetchCreditLimitHistory` | `GET /finance/credit-limits/accounts/{companyAccountId}/history` |
 * | `fetchCreditLimitCurrencyCandidates` | `GET /finance/credit-limits/currency-candidates` |
 * | `set` | `POST /finance/credit-limits` with `Idempotency-Key` (FNS-D27, approver, one step) |
 * | `remove` | `POST /finance/credit-limits/removals` with `Idempotency-Key` (FNS-D27) |
 * | `propose` | `POST /finance/credit-limits/proposals` with `Idempotency-Key` (FNS-D27a) |
 * | `approve` | `POST …/proposals/{proposalId}/approval` with `Idempotency-Key` |
 * | `reject` | `POST …/proposals/{proposalId}/rejection` with `Idempotency-Key` |
 *
 * Reads answer 200 with the resource; 403 / 404 / 503 are code-less platform problems and become `http` outcomes.
 * Commands answer the `CommandResult` body on 200 / 409 / 422; a replay (200 or a recorded refusal) carries
 * `Idempotency-Replayed: true` and `Idempotency-Original-Correlation-Id`, which must agree with the body. 403 is a
 * definite refusal. 503, no reply or an undecodable body is an UNKNOWN outcome: the caller retries with the SAME key.
 *
 * The company is only the verified shell company in `X-Company-Id`; it is never put into a body.
 */

import type { ViewOutcome } from '../shell/uiState'
import { isBoundUuid } from './contract'
import {
  decodeCreditLimitAccount,
  decodeCreditLimitCommandResult,
  decodeCreditLimitCurrencyCandidates,
  decodeCreditLimitHistoryPage,
  decodeCreditLimitListPage,
  type CreditLimitAccount,
  type CreditLimitApprovalBody,
  type CreditLimitCommandResult,
  type CreditLimitCurrencyCandidates,
  type CreditLimitHistoryPage,
  type CreditLimitListPage,
  type CreditLimitProposeBody,
  type CreditLimitRejectionBody,
  type CreditLimitRemoveBody,
  type CreditLimitSetBody,
} from './CreditLimitContract'
import { API_BASE, devHeaders } from './http'
import type { Decoded } from './contract'

export const CREDIT_LIMITS_PATH = '/finance/credit-limits'

export const creditLimitsUrl = () => `${API_BASE}${CREDIT_LIMITS_PATH}`
export const creditLimitAccountUrl = (companyAccountId: string) =>
  `${API_BASE}${CREDIT_LIMITS_PATH}/accounts/${encodeURIComponent(companyAccountId)}`
export const creditLimitHistoryUrl = (companyAccountId: string) => `${creditLimitAccountUrl(companyAccountId)}/history`
export const creditLimitCurrencyCandidatesUrl = () => `${API_BASE}${CREDIT_LIMITS_PATH}/currency-candidates`

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

export function fetchCreditLimits(companyId: string, signal?: AbortSignal): Promise<ViewOutcome<CreditLimitListPage>> {
  if (!isBoundUuid(companyId)) return Promise.resolve({ type: 'malformed' })
  return read(creditLimitsUrl(), companyId, (body) => decodeCreditLimitListPage(body, companyId), signal)
}

export function fetchCreditLimitAccount(companyId: string, companyAccountId: string,
  signal?: AbortSignal): Promise<ViewOutcome<CreditLimitAccount>> {
  if (!isBoundUuid(companyId) || !isBoundUuid(companyAccountId)) return Promise.resolve({ type: 'malformed' })
  return read(creditLimitAccountUrl(companyAccountId), companyId,
    (body) => decodeCreditLimitAccount(body, companyId, companyAccountId), signal)
}

export function fetchCreditLimitHistory(companyId: string, companyAccountId: string,
  signal?: AbortSignal): Promise<ViewOutcome<CreditLimitHistoryPage>> {
  if (!isBoundUuid(companyId) || !isBoundUuid(companyAccountId)) return Promise.resolve({ type: 'malformed' })
  return read(creditLimitHistoryUrl(companyAccountId), companyId,
    (body) => decodeCreditLimitHistoryPage(body, companyId, companyAccountId), signal)
}

export function fetchCreditLimitCurrencyCandidates(companyId: string,
  signal?: AbortSignal): Promise<ViewOutcome<CreditLimitCurrencyCandidates>> {
  if (!isBoundUuid(companyId)) return Promise.resolve({ type: 'malformed' })
  return read(creditLimitCurrencyCandidatesUrl(), companyId, (body) => decodeCreditLimitCurrencyCandidates(body, companyId), signal)
}

// ---------------------------------------------------------------------------------
// Commands
// ---------------------------------------------------------------------------------

export type CreditLimitCommand =
  | { readonly operation: 'set'; readonly body: CreditLimitSetBody }
  | { readonly operation: 'remove'; readonly body: CreditLimitRemoveBody }
  | { readonly operation: 'propose'; readonly body: CreditLimitProposeBody }
  | { readonly operation: 'approve'; readonly proposalId: string; readonly body: CreditLimitApprovalBody }
  | { readonly operation: 'reject'; readonly proposalId: string; readonly body: CreditLimitRejectionBody }

export type CreditLimitOperation = CreditLimitCommand['operation']

export type CreditLimitUnknownReason = 'offline' | 'network-error' | 'service-unavailable' | 'undecodable' | 'unexpected-status'

export type CreditLimitCommandReply =
  /** A decoded `CommandResult` (200 / 409 / 422) whose replay headers agree with its body. */
  | { readonly kind: 'answered'; readonly result: CreditLimitCommandResult }
  /** A definite refusal stated by status alone: 400 (malformed body), 401, 403, 404, 415. */
  | { readonly kind: 'refused'; readonly status: number }
  /** The outcome is not known: retry with the SAME key. */
  | { readonly kind: 'unknown'; readonly reason: CreditLimitUnknownReason }

export function creditLimitCommandUrl(command: CreditLimitCommand): string {
  const base = `${API_BASE}${CREDIT_LIMITS_PATH}`
  switch (command.operation) {
    case 'set':
      return base
    case 'remove':
      return `${base}/removals`
    case 'propose':
      return `${base}/proposals`
    case 'approve':
      return `${base}/proposals/${encodeURIComponent(command.proposalId)}/approval`
    case 'reject':
      return `${base}/proposals/${encodeURIComponent(command.proposalId)}/rejection`
  }
}

const RESULT_STATUSES: ReadonlySet<number> = new Set([200, 409, 422])
const DEFINITE_STATUSES: ReadonlySet<number> = new Set([400, 401, 403, 404, 415])

/** `isReplay` ⇔ `Idempotency-Replayed: true`, and a replay names the original correlation in both places. */
function replayHeadersAgree(response: Response, result: CreditLimitCommandResult): boolean {
  const replayed = response.headers.get('Idempotency-Replayed')
  const original = response.headers.get('Idempotency-Original-Correlation-Id')
  if (result.isReplay) return replayed === 'true' && original === result.originalCorrelationId
  return replayed === null && original === null
}

export async function sendCreditLimitCommand(companyId: string, command: CreditLimitCommand, idempotencyKey: string,
  signal?: AbortSignal): Promise<CreditLimitCommandReply> {
  if (!isBoundUuid(companyId)) return { kind: 'unknown', reason: 'undecodable' }
  let response: Response
  try {
    response = await fetch(creditLimitCommandUrl(command), {
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
    const decoded = decodeCreditLimitCommandResult(body, companyId)
    if (!decoded.ok || !replayHeadersAgree(response, decoded.value)) return { kind: 'unknown', reason: 'undecodable' }
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
