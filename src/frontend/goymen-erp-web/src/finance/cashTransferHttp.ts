/**
 * FNS-D24 / API-32/34 transport: the treasury transfer reads, the preview and the one commit.
 *
 * Reads answer a `ViewOutcome`; a 200 for another company, transfer, account or date is malformed. The preview writes
 * nothing and sends no key. The commit sends an `Idempotency-Key`; the HTTP status and the body's outcome must agree
 * (200 Succeeded, 409 VersionConflict or IdempotencyConflict, 422 ValidationFailed), and a replay must say so in both
 * the headers and the body. A reply that cannot be read is `unknown`: the caller keeps the SAME key for the retry (in
 * memory, per session, actor and company) so the server answers the first attempt instead of moving the money twice.
 * The bank and cash account lists are the same reads the expense payment screen uses.
 */

import type { ViewOutcome } from '../shell/uiState'
import { isBoundUuid, type CommandOutcomeName, type ValidationIssue } from './contract'
import {
  decodeCashTransferAccountContext,
  decodeCashTransferCommandResult,
  decodeCashTransferFor,
  decodeCashTransferPage,
  decodeCashTransferPreviewResult,
  type AccountContextRequest,
  type CashTransferAccountContext,
  type CashTransferPage,
  type CashTransferPreparation,
  type CashTransferView,
  type CommitCashTransferRequest,
} from './cashTransferContract'
import { isDateOnly } from './dateOnly'
import { API_BASE, devHeaders } from './http'

export { fetchBankAccountMaster, fetchCashAccounts } from './expensePaymentHttp'

export const CASH_TRANSFERS_PATH = '/finance/cash-transfers'
export const CASH_TRANSFER_PREVIEW_PATH = `${CASH_TRANSFERS_PATH}/preview`
export const CASH_TRANSFER_ACCOUNT_CONTEXT_PATH = `${CASH_TRANSFERS_PATH}/account-context`

async function read<T>(path: string, companyId: string, decode: (body: unknown) => T | null,
  signal?: AbortSignal): Promise<ViewOutcome<T>> {
  try {
    const response = await fetch(`${API_BASE}${path}`, {
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
    const data = decode(body)
    return data === null ? { type: 'malformed' } : { type: 'ok-single', data }
  } catch {
    return signal?.aborted ? { type: 'fence-stale' } : navigator.onLine ? { type: 'network-error' } : { type: 'offline' }
  }
}

const MALFORMED = Promise.resolve({ type: 'malformed' } as const)

/** One page, newest first; `cursor` is the previous page's `nextCursor`, sent back unchanged. */
export function fetchCashTransfers(companyId: string, cursor: string | null,
  signal?: AbortSignal): Promise<ViewOutcome<CashTransferPage>> {
  if (!isBoundUuid(companyId) || (cursor !== null && cursor.trim().length === 0)) return MALFORMED
  const path = cursor === null
    ? CASH_TRANSFERS_PATH
    : `${CASH_TRANSFERS_PATH}?${new URLSearchParams({ cursor }).toString()}`
  return read(path, companyId, (body) => decodeCashTransferPage(body, companyId), signal)
}

export function fetchCashTransfer(companyId: string, transferId: string,
  signal?: AbortSignal): Promise<ViewOutcome<CashTransferView>> {
  if (!isBoundUuid(companyId) || !isBoundUuid(transferId)) return MALFORMED
  return read(`${CASH_TRANSFERS_PATH}/${encodeURIComponent(transferId)}`, companyId,
    (body) => decodeCashTransferFor(body, companyId, transferId), signal)
}

/** A leg's currency and money profile for an eligible account on a date. */
export function fetchCashTransferAccountContext(companyId: string, request: AccountContextRequest,
  signal?: AbortSignal): Promise<ViewOutcome<CashTransferAccountContext>> {
  if (!isBoundUuid(companyId) || !isBoundUuid(request.accountId) || !isDateOnly(request.occurredOn)) return MALFORMED
  const query = new URLSearchParams({
    accountKind: request.accountKind,
    accountId: request.accountId,
    occurredOn: request.occurredOn,
  })
  return read(`${CASH_TRANSFER_ACCOUNT_CONTEXT_PATH}?${query.toString()}`, companyId,
    (body) => decodeCashTransferAccountContext(body, companyId, request), signal)
}

export type UnknownReason = 'offline' | 'network-error' | 'service-unavailable' | 'undecodable' | 'unexpected-status'

// ---------------------------------------------------------------------------------------------------------------
// Preview
// ---------------------------------------------------------------------------------------------------------------

export type CashTransferPreviewOutcome =
  | { readonly kind: 'accepted'; readonly preparation: CashTransferPreparation }
  | { readonly kind: 'refused'; readonly status: 400 | 401 | 403 | 415 | 422; readonly issues: readonly ValidationIssue[] }
  | { readonly kind: 'unknown'; readonly reason: UnknownReason }

/** The echoed or stored preparation must be the one sent, field by field and text for text (nothing re-written). */
export function samePreparation(a: CashTransferPreparation, b: CashTransferPreparation): boolean {
  return a.kind === b.kind
    && a.sourceKind === b.sourceKind
    && a.sourceAccountId === b.sourceAccountId
    && a.sourceMoney.amount === b.sourceMoney.amount
    && a.sourceMoney.currencyId === b.sourceMoney.currencyId
    && a.sourceProfile.profileId === b.sourceProfile.profileId
    && a.sourceProfile.version === b.sourceProfile.version
    && a.targetKind === b.targetKind
    && a.targetAccountId === b.targetAccountId
    && a.targetMoney.amount === b.targetMoney.amount
    && a.targetMoney.currencyId === b.targetMoney.currencyId
    && a.targetProfile.profileId === b.targetProfile.profileId
    && a.targetProfile.version === b.targetProfile.version
    && a.rate === b.rate
    && a.occurredOn === b.occurredOn
    && a.valueOn === b.valueOn
    && a.reference === b.reference
    && a.description === b.description
}

const PREVIEW_CODE_LESS: ReadonlySet<number> = new Set([400, 401, 403, 415])

/** The server's checks without a write. A refusal changes nothing, so no key is kept. */
export async function previewCashTransfer(companyId: string, preparation: CashTransferPreparation,
  signal?: AbortSignal): Promise<CashTransferPreviewOutcome> {
  if (!isBoundUuid(companyId)) return { kind: 'unknown', reason: 'undecodable' }
  let response: Response
  try {
    response = await fetch(`${API_BASE}${CASH_TRANSFER_PREVIEW_PATH}`, {
      method: 'POST',
      headers: { ...devHeaders(), 'X-Company-Id': companyId, 'Content-Type': 'application/json', Accept: 'application/json' },
      body: JSON.stringify(preparation),
      cache: 'no-store',
      signal,
    })
  } catch {
    return { kind: 'unknown', reason: navigator.onLine ? 'network-error' : 'offline' }
  }
  if (PREVIEW_CODE_LESS.has(response.status)) {
    return { kind: 'refused', status: response.status as 400 | 401 | 403 | 415, issues: [] }
  }
  if (response.status >= 500) return { kind: 'unknown', reason: 'service-unavailable' }
  if (response.status !== 200 && response.status !== 422) return { kind: 'unknown', reason: 'unexpected-status' }
  let body: unknown
  try {
    body = await response.json()
  } catch {
    return { kind: 'unknown', reason: 'undecodable' }
  }
  const decoded = decodeCashTransferPreviewResult(body)
  if (!decoded.ok) return { kind: 'unknown', reason: 'undecodable' }
  const result = decoded.value
  if (response.status === 200) {
    return result.outcome === 'Succeeded' && samePreparation(result.preparation, preparation)
      ? { kind: 'accepted', preparation: result.preparation }
      : { kind: 'unknown', reason: 'undecodable' }
  }
  return result.outcome === 'ValidationFailed'
    ? { kind: 'refused', status: 422, issues: result.issues }
    : { kind: 'unknown', reason: 'undecodable' }
}

// ---------------------------------------------------------------------------------------------------------------
// Commit
// ---------------------------------------------------------------------------------------------------------------

export type CashTransferCommandOutcome =
  | { readonly kind: 'applied'; readonly resource: CashTransferView; readonly replayed: boolean }
  | {
      readonly kind: 'failed'
      readonly status: 400 | 401 | 403 | 404 | 409 | 415 | 422
      readonly outcome: CommandOutcomeName | null
      readonly issues: readonly ValidationIssue[]
    }
  | { readonly kind: 'unknown'; readonly reason: UnknownReason }

const CODE_LESS: ReadonlySet<number> = new Set([400, 401, 403, 404, 415])

export async function commitCashTransfer(
  companyId: string,
  key: string,
  body: CommitCashTransferRequest,
  signal?: AbortSignal,
): Promise<CashTransferCommandOutcome> {
  if (!isBoundUuid(companyId) || key.trim().length === 0 || key.trim() !== key) {
    return { kind: 'unknown', reason: 'undecodable' }
  }

  let response: Response
  try {
    response = await fetch(`${API_BASE}${CASH_TRANSFERS_PATH}`, {
      method: 'POST',
      headers: {
        ...devHeaders(),
        'X-Company-Id': companyId,
        'Idempotency-Key': key,
        'Content-Type': 'application/json',
        Accept: 'application/json',
      },
      body: JSON.stringify(body),
      cache: 'no-store',
      signal,
    })
  } catch {
    return { kind: 'unknown', reason: navigator.onLine ? 'network-error' : 'offline' }
  }

  if (CODE_LESS.has(response.status)) {
    return { kind: 'failed', status: response.status as 400 | 401 | 403 | 404 | 415, outcome: null, issues: [] }
  }
  if (response.status >= 500) return { kind: 'unknown', reason: 'service-unavailable' }
  if (response.status !== 200 && response.status !== 409 && response.status !== 422) {
    return { kind: 'unknown', reason: 'unexpected-status' }
  }

  let raw: unknown
  try {
    raw = await response.json()
  } catch {
    return { kind: 'unknown', reason: 'undecodable' }
  }
  const decoded = decodeCashTransferCommandResult(raw, companyId)
  if (!decoded.ok) return { kind: 'unknown', reason: 'undecodable' }
  const result = decoded.value

  const coherent = response.status === 200
    ? result.outcome === 'Succeeded'
    : response.status === 409
      ? result.outcome === 'VersionConflict' || result.outcome === 'IdempotencyConflict'
      : result.outcome === 'ValidationFailed'
  if (!coherent) return { kind: 'unknown', reason: 'undecodable' }

  const replayHeader = response.headers.get('Idempotency-Replayed')
  const originalHeader = response.headers.get('Idempotency-Original-Correlation-Id')
  if (result.isReplay
    ? replayHeader !== 'true' || originalHeader !== result.originalCorrelationId
    : replayHeader !== null) {
    return { kind: 'unknown', reason: 'undecodable' }
  }

  if (result.outcome !== 'Succeeded') {
    return { kind: 'failed', status: response.status as 409 | 422, outcome: result.outcome, issues: result.issues }
  }
  // The record must be the transfer that was sent: the same legs, amounts, rate and dates.
  if (!samePreparation(result.resource.preparation, body.preparation)) return { kind: 'unknown', reason: 'undecodable' }
  return { kind: 'applied', resource: result.resource, replayed: result.isReplay }
}

// ---------------------------------------------------------------------------------------------------------------
// Pending attempt keys
// ---------------------------------------------------------------------------------------------------------------

export interface PendingCashTransferAttempt {
  readonly key: string
  readonly body: CommitCashTransferRequest
}

/**
 * In memory only: an attempt whose outcome is unknown keeps its key and exact body until it is answered, the session
 * or actor changes, or the document is left. Nothing about a transfer is written to browser storage.
 */
const pendingAttempts = new Map<string, PendingCashTransferAttempt>()

export function pendingCashTransferAttemptKey(sessionEpoch: number, actorId: string, companyId: string): string {
  return `goymen.finance.cash-transfer.commit.v1:${sessionEpoch}:${actorId}:${companyId}`
}

export function recallPendingCashTransferAttempt(storeKey: string): PendingCashTransferAttempt | null {
  return pendingAttempts.get(storeKey) ?? null
}

export function rememberPendingCashTransferAttempt(storeKey: string, attempt: PendingCashTransferAttempt): void {
  pendingAttempts.set(storeKey, attempt)
}

export function forgetPendingCashTransferAttempt(storeKey: string): void {
  pendingAttempts.delete(storeKey)
}

/** Test seam: forget every pending attempt. */
export function clearPendingCashTransferAttempts(): void {
  pendingAttempts.clear()
}

export function generateCashTransferAttemptKey(source: Pick<Crypto, 'getRandomValues'> = globalThis.crypto): string {
  const bytes = new Uint8Array(16)
  source.getRandomValues(bytes)
  let key = ''
  for (const byte of bytes) key += byte.toString(16).padStart(2, '0')
  return key
}
