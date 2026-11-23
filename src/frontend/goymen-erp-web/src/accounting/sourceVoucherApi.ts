import type { ViewOutcome } from '../shell/uiState'
import { isDateOnly } from '../finance/dateOnly'
import { ACCOUNTING_BASE, devHeaders } from './api'
import { exactKeys } from './exactKeys'
import { decodeJournalReversalProposal, type JournalReversalProposal } from './journalReversalContract'

export interface SourceVoucher {
  readonly id: string; readonly companyId: string; readonly documentKind: 'sales-invoice' | 'purchase-invoice'
  readonly sourceDocumentId: string; readonly invoiceId: string; readonly finalizedVersion: number
  readonly sourceActorId: string; readonly partyId: string; readonly documentCurrencyId: string
  readonly exchangeRate: string | null; readonly documentDate: string; readonly dueDate: string | null
  readonly status: 'AwaitingApproval' | 'PeriodWaiting' | 'Posted' | 'Cancelled' | 'CancellationReversalPending' | 'CancelledByReversal'
  readonly version: number; readonly mappingVersion: number; readonly periodId: string | null
  readonly journalId: string | null; readonly approvedBy: string | null; readonly approvedAtUtc: string | null
  readonly cancellationReason: string | null; readonly officialInvoiceNo: string | null; readonly functionalPayable: string
  readonly cancellationId?: string | null; readonly reversalProposal?: JournalReversalProposal | null
  readonly lines: readonly { readonly lineNumber: number; readonly mappingKey: string; readonly side: 1 | 2
    readonly accountId: string; readonly functionalAmount: string; readonly sourceLineNumber: number | null
    readonly vatRateCode: string | null; readonly accountCode: string | null; readonly accountName: string | null }[]
}
export interface SourceVoucherAttempt { readonly id: string; readonly key: string; readonly expectedVersion: number }
export type SourceVoucherOutcome = { readonly type: 'applied'; readonly value: SourceVoucher }
  | { readonly type: 'refused'; readonly status: number; readonly code: string | null } | { readonly type: 'unknown' }
const rec = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null && !Array.isArray(v)
const guid = (v: unknown): v is string => typeof v === 'string' && /^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(v)
  && v !== '00000000-0000-0000-0000-000000000000'
const nullableGuid = (v: unknown) => v === null || guid(v)
const positive = (v: unknown): v is number => Number.isSafeInteger(v) && (v as number) > 0
const text = (v: unknown): v is string => typeof v === 'string' && v.trim() !== ''
const nullableText = (v: unknown) => v === null || text(v)
const amount = (v: unknown): v is string => typeof v === 'string' && /^(0|[1-9][0-9]*)(\.[0-9]+)?$/.test(v)
const base = `${ACCOUNTING_BASE}/source-vouchers`
// OpenAPI: AccountingSourceVoucherListV1, AccountingSourceVoucherViewV1 (cancellationId, reversalProposal not in `required`),
// AccountingSourceVoucherLineViewV1 (accountCode, accountName not in `required`).
const LIST_KEYS = ['companyId', 'items'] as const
const VOUCHER_KEYS = ['id', 'companyId', 'documentKind', 'sourceDocumentId', 'invoiceId', 'finalizedVersion', 'sourceActorId', 'partyId',
  'documentCurrencyId', 'exchangeRate', 'documentDate', 'dueDate', 'status', 'version', 'mappingVersion', 'periodId', 'journalId',
  'approvedBy', 'approvedAtUtc', 'cancellationReason', 'lines', 'officialInvoiceNo', 'functionalPayable'] as const
const VOUCHER_OPTIONAL_KEYS = ['cancellationId', 'reversalProposal'] as const
const LINE_KEYS = ['lineNumber', 'mappingKey', 'side', 'accountId', 'functionalAmount', 'sourceLineNumber', 'vatRateCode'] as const
const LINE_OPTIONAL_KEYS = ['accountCode', 'accountName'] as const

export function decodeSourceVoucher(body: unknown, company: string): SourceVoucher | null {
  if (!rec(body) || !exactKeys(body, VOUCHER_KEYS, VOUCHER_OPTIONAL_KEYS) || !guid(body.companyId) || body.companyId.toLowerCase() !== company.toLowerCase()
    || !['id', 'sourceDocumentId', 'invoiceId', 'sourceActorId', 'partyId', 'documentCurrencyId'].every(key => guid(body[key]))
    || !['finalizedVersion', 'version', 'mappingVersion'].every(key => positive(body[key]))
    || !['sales-invoice', 'purchase-invoice'].includes(body.documentKind as string)
    || !['AwaitingApproval', 'PeriodWaiting', 'Posted', 'Cancelled', 'CancellationReversalPending', 'CancelledByReversal'].includes(body.status as string)
    || typeof body.documentDate !== 'string' || !isDateOnly(body.documentDate)
    || body.dueDate !== null && (typeof body.dueDate !== 'string' || !isDateOnly(body.dueDate))
    || body.exchangeRate !== null && (!amount(body.exchangeRate) || /^0(\.0+)?$/.test(body.exchangeRate))
    || !['periodId', 'journalId', 'approvedBy'].every(key => nullableGuid(body[key]))
    || !nullableText(body.officialInvoiceNo) || !nullableText(body.cancellationReason) || !amount(body.functionalPayable)
    || !Array.isArray(body.lines) || body.lines.length === 0 || body.lines.some(line => !rec(line) || !exactKeys(line, LINE_KEYS, LINE_OPTIONAL_KEYS)
      || !positive(line.lineNumber) || !text(line.mappingKey) || ![1, 2].includes(line.side as number) || !guid(line.accountId)
      || !amount(line.functionalAmount) || /^0(\.0+)?$/.test(line.functionalAmount)
      || line.sourceLineNumber !== null && !positive(line.sourceLineNumber)
      || !nullableText(line.vatRateCode) || !nullableText(line.accountCode) || !nullableText(line.accountName))) return null
  if (new Set(body.lines.map(line => line.lineNumber)).size !== body.lines.length) return null
  const posted = ['Posted', 'CancellationReversalPending', 'CancelledByReversal'].includes(body.status as string)
  if (posted ? !guid(body.journalId) || body.journalId.toLowerCase() !== (body.id as string).toLowerCase()
      || !guid(body.approvedBy) || body.approvedBy.toLowerCase() === (body.sourceActorId as string).toLowerCase()
      || !guid(body.periodId) || typeof body.approvedAtUtc !== 'string' || !Number.isFinite(Date.parse(body.approvedAtUtc))
    : body.journalId !== null || body.approvedBy !== null || body.approvedAtUtc !== null) return null
  if (['Cancelled', 'CancellationReversalPending', 'CancelledByReversal'].includes(body.status as string) !== (body.cancellationReason !== null)) return null
  if (body.cancellationId !== undefined && !nullableGuid(body.cancellationId)) return null
  if (body.cancellationId != null && body.cancellationReason === null) return null
  if (body.reversalProposal != null) {
    const proposal = decodeJournalReversalProposal(body.reversalProposal, company)
    if (proposal === null || !guid(body.cancellationId) || !guid(body.journalId)
      || proposal.sourceJournalId.toLowerCase() !== body.journalId.toLowerCase()
      || !['CancellationReversalPending', 'CancelledByReversal'].includes(body.status as string)
      || (proposal.status === 'Approved') !== (body.status === 'CancelledByReversal')) return null
  } else if (body.status === 'CancelledByReversal') return null
  return body as unknown as SourceVoucher
}

export interface SourceVoucherReversalAttempt {
  readonly id: string; readonly key: string
  readonly request: { readonly expectedVersion: number; readonly proposalId: string; readonly periodId: string
    readonly transactionDate: string; readonly accountingDate: string; readonly valueDate: string | null; readonly reason: string }
}
export async function proposeSourceVoucherReversal(company: string, attempt: SourceVoucherReversalAttempt): Promise<SourceVoucherOutcome> {
  try {
    const response = await fetch(`${base}/${encodeURIComponent(attempt.id)}/cancellation-reversal-proposals`, {
      method: 'POST', headers: { ...devHeaders(), 'X-Company-Id': company, 'Content-Type': 'application/json', 'Idempotency-Key': attempt.key },
      body: JSON.stringify(attempt.request) })
    const body: unknown = await response.json().catch(() => null)
    if (response.status === 200) {
      const value = decodeSourceVoucher(body, company); const proposal = value?.reversalProposal
      return value !== null && value.id.toLowerCase() === attempt.id.toLowerCase() && value.status === 'CancellationReversalPending'
        && value.version === attempt.request.expectedVersion + 1 && proposal?.status === 'Pending'
        && proposal.proposalId.toLowerCase() === attempt.request.proposalId.toLowerCase()
        && proposal.periodId.toLowerCase() === attempt.request.periodId.toLowerCase()
        && proposal.transactionDate === attempt.request.transactionDate && proposal.accountingDate === attempt.request.accountingDate
        && proposal.valueDate === attempt.request.valueDate && proposal.reason === attempt.request.reason.trim()
        ? { type: 'applied', value } : { type: 'unknown' }
    }
    const code = rec(body) && typeof body.code === 'string' ? body.code : null
    const refusal = code !== null && (codes.has(code) || ['source_voucher_reversal_not_proposable',
      'source_voucher_reversal_period_not_open', 'source_voucher_source_conflict',
      'accounting_journal_reversal_proposal_content_conflict', 'accounting_journal_reversal_source_journal_not_found',
      'accounting_journal_reversal_period_not_found'].includes(code))
    return [400, 401, 403, 404, 415].includes(response.status) || [409, 422].includes(response.status) && refusal
      ? { type: 'refused', status: response.status, code } : { type: 'unknown' }
  } catch { return { type: 'unknown' } }
}

export async function fetchSourceVouchers(company: string, signal?: AbortSignal): Promise<ViewOutcome<readonly SourceVoucher[]>> {
  try {
    const response = await fetch(base, { headers: { ...devHeaders(), 'X-Company-Id': company }, signal })
    const body: unknown = await response.json().catch(() => null)
    if (response.status !== 200) return { type: 'http', status: response.status, body }
    if (!rec(body) || !exactKeys(body, LIST_KEYS) || typeof body.companyId !== 'string' || body.companyId.toLowerCase() !== company.toLowerCase() || !Array.isArray(body.items)) return { type: 'malformed' }
    const items = body.items.map(item => decodeSourceVoucher(item, company))
    if (items.some(item => item === null) || new Set(items.map(item => item?.id.toLowerCase())).size !== items.length) return { type: 'malformed' }
    return { type: 'ok-single', data: items as SourceVoucher[] }
  } catch { return signal?.aborted ? { type: 'fence-stale' } : navigator.onLine ? { type: 'network-error' } : { type: 'offline' } }
}

const codes = new Set(['idempotency_conflict', 'accounting_version_conflict', 'accounting_concurrent_change',
  'source_voucher_not_approvable', 'source_voucher_posting_conflict', 'source_voucher_period_waiting',
  'source_voucher_account_plan_missing', 'source_voucher_account_not_in_plan', 'source_voucher_amount_unaccepted', 'source_voucher_posting_refused'])
export async function approveSourceVoucher(company: string, attempt: SourceVoucherAttempt): Promise<SourceVoucherOutcome> {
  try {
    const response = await fetch(`${base}/${encodeURIComponent(attempt.id)}/approval`, { method: 'POST',
      headers: { ...devHeaders(), 'X-Company-Id': company, 'Content-Type': 'application/json', 'Idempotency-Key': attempt.key },
      body: JSON.stringify({ expectedVersion: attempt.expectedVersion }) })
    const body: unknown = await response.json().catch(() => null)
    if (response.status === 200) {
      const value = decodeSourceVoucher(body, company)
      return value !== null && value.id.toLowerCase() === attempt.id.toLowerCase() && value.status === 'Posted'
        && value.version === attempt.expectedVersion + 1 ? { type: 'applied', value } : { type: 'unknown' }
    }
    const code = rec(body) && typeof body.code === 'string' ? body.code : null
    return [400, 401, 403, 404, 415].includes(response.status) || [409, 422].includes(response.status) && code !== null && codes.has(code)
      ? { type: 'refused', status: response.status, code } : { type: 'unknown' }
  } catch { return { type: 'unknown' } }
}
