/**
 * FNS-D25 / API-25 transport: the expense-payment reads and its three commands (draft, approval, record).
 *
 * Reads answer a `ViewOutcome`; a 200 for another company, payment, source or date is malformed. Commands send an
 * `Idempotency-Key`; the HTTP status and the body's outcome must agree (200 Succeeded, 409 VersionConflict or
 * IdempotencyConflict, 422 ValidationFailed), and a replay must say so in both the headers and the body. A reply
 * that cannot be read is `unknown`: the caller keeps the SAME key for the retry (in memory, per session, actor,
 * company and target) so the server answers the first attempt instead of creating a second payment.
 */

import type { ViewOutcome } from '../shell/uiState'
import { decodeBankAccountMasterPage, type BankAccountMasterPage } from './bankAccountContract'
import { isBoundUuid, type CommandOutcomeName, type ValidationIssue } from './contract'
import { isDateOnly } from './dateOnly'
import {
  decodeCashAccountList,
  decodeExpenseDraftContext,
  decodeExpenseItemCatalog,
  decodeExpensePaymentCommandResult,
  decodeExpensePaymentFor,
  decodeExpensePaymentPage,
  type ApproveExpensePaymentRequest,
  type CashAccountList,
  type DraftContextRequest,
  type ExpenseItemCatalog,
  type ExpensePaymentDraftContext,
  type ExpensePaymentDraftSaveRequest,
  type ExpensePaymentPage,
  type ExpensePaymentStatus,
  type ExpensePaymentView,
  type RecordExpensePaymentRequest,
} from './expensePaymentContract'
import { API_BASE, devHeaders } from './http'

export const EXPENSE_PAYMENTS_PATH = '/finance/expense-payments'
export const EXPENSE_ITEMS_PATH = `${EXPENSE_PAYMENTS_PATH}/expense-items`
export const EXPENSE_DRAFT_CONTEXT_PATH = `${EXPENSE_PAYMENTS_PATH}/draft-context`
export const CASH_ACCOUNTS_PATH = '/finance/cash-accounts'
export const BANK_ACCOUNT_MASTER_READ_PATH = '/finance/bank-accounts/master'

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
export function fetchExpensePayments(companyId: string, cursor: string | null,
  signal?: AbortSignal): Promise<ViewOutcome<ExpensePaymentPage>> {
  if (!isBoundUuid(companyId) || (cursor !== null && cursor.trim().length === 0)) return MALFORMED
  const path = cursor === null
    ? EXPENSE_PAYMENTS_PATH
    : `${EXPENSE_PAYMENTS_PATH}?${new URLSearchParams({ cursor }).toString()}`
  return read(path, companyId, (body) => decodeExpensePaymentPage(body, companyId), signal)
}

export function fetchExpensePayment(companyId: string, paymentId: string,
  signal?: AbortSignal): Promise<ViewOutcome<ExpensePaymentView>> {
  if (!isBoundUuid(companyId) || !isBoundUuid(paymentId)) return MALFORMED
  return read(`${EXPENSE_PAYMENTS_PATH}/${encodeURIComponent(paymentId)}`, companyId,
    (body) => decodeExpensePaymentFor(body, companyId, paymentId), signal)
}

export function fetchExpenseItems(companyId: string, signal?: AbortSignal): Promise<ViewOutcome<ExpenseItemCatalog>> {
  if (!isBoundUuid(companyId)) return MALFORMED
  return read(EXPENSE_ITEMS_PATH, companyId, (body) => decodeExpenseItemCatalog(body, companyId), signal)
}

export function fetchExpenseDraftContext(companyId: string, request: DraftContextRequest,
  signal?: AbortSignal): Promise<ViewOutcome<ExpensePaymentDraftContext>> {
  if (!isBoundUuid(companyId) || !isBoundUuid(request.treasuryAccountId) || !isDateOnly(request.occurredOn)) {
    return MALFORMED
  }
  const query = new URLSearchParams({
    sourceKind: request.sourceKind,
    treasuryAccountId: request.treasuryAccountId,
    occurredOn: request.occurredOn,
  })
  return read(`${EXPENSE_DRAFT_CONTEXT_PATH}?${query.toString()}`, companyId,
    (body) => decodeExpenseDraftContext(body, companyId, request), signal)
}

export function fetchCashAccounts(companyId: string, signal?: AbortSignal): Promise<ViewOutcome<CashAccountList>> {
  if (!isBoundUuid(companyId)) return MALFORMED
  return read(CASH_ACCOUNTS_PATH, companyId, (body) => decodeCashAccountList(body, companyId), signal)
}

export function fetchBankAccountMaster(companyId: string,
  signal?: AbortSignal): Promise<ViewOutcome<BankAccountMasterPage>> {
  if (!isBoundUuid(companyId)) return MALFORMED
  return read(BANK_ACCOUNT_MASTER_READ_PATH, companyId, (body) => {
    const decoded = decodeBankAccountMasterPage(body, companyId)
    return decoded.ok ? decoded.value : null
  }, signal)
}

// ---------------------------------------------------------------------------------------------------------------
// Commands
// ---------------------------------------------------------------------------------------------------------------

export type ExpensePaymentCommand =
  | { readonly action: 'draft'; readonly body: ExpensePaymentDraftSaveRequest }
  | { readonly action: 'approve'; readonly body: ApproveExpensePaymentRequest }
  | { readonly action: 'record'; readonly body: RecordExpensePaymentRequest }

export type UnknownReason = 'offline' | 'network-error' | 'service-unavailable' | 'undecodable' | 'unexpected-status'

export type ExpensePaymentCommandOutcome =
  | { readonly kind: 'applied'; readonly resource: ExpensePaymentView; readonly replayed: boolean }
  | {
      readonly kind: 'failed'
      readonly status: 400 | 401 | 403 | 404 | 409 | 415 | 422
      readonly outcome: CommandOutcomeName | null
      readonly issues: readonly ValidationIssue[]
    }
  | { readonly kind: 'unknown'; readonly reason: UnknownReason }

const EXPECTED_STATUS: Record<ExpensePaymentCommand['action'], ExpensePaymentStatus> = {
  draft: 'Draft',
  approve: 'Approved',
  record: 'Recorded',
}

function commandPath(command: ExpensePaymentCommand): string | null {
  if (command.action === 'draft') return EXPENSE_PAYMENTS_PATH
  if (!isBoundUuid(command.body.paymentId)) return null
  const id = encodeURIComponent(command.body.paymentId)
  return command.action === 'approve' ? `${EXPENSE_PAYMENTS_PATH}/${id}/approval` : `${EXPENSE_PAYMENTS_PATH}/${id}/record`
}

const CODE_LESS: ReadonlySet<number> = new Set([400, 401, 403, 404, 415])

export async function sendExpensePaymentCommand(
  companyId: string,
  key: string,
  command: ExpensePaymentCommand,
  signal?: AbortSignal,
): Promise<ExpensePaymentCommandOutcome> {
  const path = commandPath(command)
  if (!isBoundUuid(companyId) || key.trim().length === 0 || key.trim() !== key || path === null) {
    return { kind: 'unknown', reason: 'undecodable' }
  }

  let response: Response
  try {
    response = await fetch(`${API_BASE}${path}`, {
      method: 'POST',
      headers: {
        ...devHeaders(),
        'X-Company-Id': companyId,
        'Idempotency-Key': key,
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

  if (CODE_LESS.has(response.status)) {
    return {
      kind: 'failed',
      status: response.status as 400 | 401 | 403 | 404 | 415,
      outcome: null,
      issues: [],
    }
  }
  if (response.status >= 500) return { kind: 'unknown', reason: 'service-unavailable' }
  if (response.status !== 200 && response.status !== 409 && response.status !== 422) {
    return { kind: 'unknown', reason: 'unexpected-status' }
  }

  let body: unknown
  try {
    body = await response.json()
  } catch {
    return { kind: 'unknown', reason: 'undecodable' }
  }
  const decoded = decodeExpensePaymentCommandResult(body, companyId)
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
  // The answer must carry the payment acted on; a new draft may carry any id.
  const id = command.body.paymentId
  if ((id !== null && result.resource.id !== id) || result.resource.status !== EXPECTED_STATUS[command.action]) {
    return { kind: 'unknown', reason: 'undecodable' }
  }
  return { kind: 'applied', resource: result.resource, replayed: result.isReplay }
}

// ---------------------------------------------------------------------------------------------------------------
// Pending attempt keys
// ---------------------------------------------------------------------------------------------------------------

export interface PendingExpenseAttempt {
  readonly key: string
  readonly command: ExpensePaymentCommand
}

/**
 * In memory only: an attempt whose outcome is unknown keeps its key and exact body until it is answered, the session
 * or actor changes, or the document is left. Nothing about a payment is written to browser storage.
 */
const pendingAttempts = new Map<string, PendingExpenseAttempt>()

export function pendingExpenseAttemptKey(
  action: ExpensePaymentCommand['action'],
  sessionEpoch: number,
  actorId: string,
  companyId: string,
  paymentId: string | null,
): string {
  const base = `goymen.finance.expense-payment.${action}.v1:${sessionEpoch}:${actorId}:${companyId}`
  return paymentId === null ? base : `${base}:${paymentId}`
}

export function recallPendingExpenseAttempt(storeKey: string): PendingExpenseAttempt | null {
  return pendingAttempts.get(storeKey) ?? null
}

export function rememberPendingExpenseAttempt(storeKey: string, attempt: PendingExpenseAttempt): void {
  pendingAttempts.set(storeKey, attempt)
}

export function forgetPendingExpenseAttempt(storeKey: string): void {
  pendingAttempts.delete(storeKey)
}

/** Test seam: forget every pending attempt. */
export function clearPendingExpenseAttempts(): void {
  pendingAttempts.clear()
}

export function generateExpenseAttemptKey(source: Pick<Crypto, 'getRandomValues'> = globalThis.crypto): string {
  const bytes = new Uint8Array(16)
  source.getRandomValues(bytes)
  let key = ''
  for (const byte of bytes) key += byte.toString(16).padStart(2, '0')
  return key
}
