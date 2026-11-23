import { API_BASE, devHeaders } from './http'
import { isBoundUuid, isInstant, isOpaqueVersion } from './contract'
import { isDateOnly } from './dateOnly'
import { isNonNegativeDecimalText } from './decimalText'
import { fetchDocuments, uploadDocument, type UploadAttempt } from '../documents/api'
import type { ViewOutcome } from '../shell/uiState'
import { COMMAND_RESULT_FIELDS, hasExactKeys, replayHeadersAgree } from './wireShape'

export const OFFICIAL_INVOICE_OWNER = 'finance.official-sales-invoice' as const
export const OFFICIAL_AMOUNT_FIELDS = ['payableAmount', 'payableFunctionalAmount', 'taxBaseFunctionalAmount', 'vatFunctionalAmount', 'withholdingFunctionalAmount'] as const
export type OfficialAmountField = typeof OFFICIAL_AMOUNT_FIELDS[number]
export interface OfficialInvoiceInput extends Record<OfficialAmountField, string> {
  readonly officialNo: string; readonly ettn: string; readonly customerPartyId: string; readonly currencyId: string; readonly invoiceDate: string
}
/** FNS-D64: the server's non-blocking tax consistency warning. Every amount is the server's text; nothing is computed here. */
export interface OfficialInvoiceTaxWarning {
  readonly code: string; readonly sourceDocumentId: string | null; readonly expectedPayableFunctionalAmount: string
  readonly payableFunctionalAmount: string; readonly differenceFunctionalAmount: string
}
export interface OfficialInvoice extends Record<OfficialAmountField, string> {
  readonly invoiceId: string; readonly companyId: string; readonly officialNo: string; readonly ettn: string; readonly customerPartyId: string; readonly currencyId: string
  readonly invoiceDate?: string | null; readonly pdfDocumentId: string; readonly pdfDocumentVersionNo: number; readonly pdfSha256: string; readonly version: string
  readonly status: 'PendingApproval' | 'Approved' | 'Rejected' | 'Withdrawn'; readonly recordedBy: string; readonly recordedAtUtc: string
  readonly approvedBy: string | null; readonly approvedAtUtc: string | null; readonly rejectedBy: string | null; readonly rejectedAtUtc: string | null
  readonly rejectionReason: string | null; readonly withdrawnBy: string | null; readonly withdrawnAtUtc: string | null
  readonly sourceDifferences?: readonly { readonly importBatchId: string; readonly sourceDocumentId: string; readonly officialNo: string; readonly ettn: string; readonly fields: readonly string[]; readonly invoiceDate: string | null; readonly payableFunctionalAmount: string | null; readonly winner: string }[] | null
  readonly taxWarning?: OfficialInvoiceTaxWarning | null
}
export interface OfficialInvoicePage { readonly companyId: string; readonly items: readonly OfficialInvoice[]; readonly nextCursor: string | null }
export interface OfficialInvoiceCurrency { readonly currencyId: string; readonly code: string; readonly displayLabel: string }
export interface OfficialInvoiceCreateAttempt {
  readonly company: string; readonly actor: string; readonly input: OfficialInvoiceInput; readonly file: File
  readonly reserveKey: string; readonly uploadKey: string; readonly recordKey: string
  invoiceId: string | null; documentId: string | null; phase: 'reserve' | 'upload' | 'scan' | 'record'
}
export interface OfficialInvoiceDecisionAttempt { readonly key: string; readonly operation: 'approval' | 'rejection' | 'withdrawal'; readonly invoiceId: string; readonly expectedVersion: string; readonly reason?: string }
export type OfficialInvoiceWriteOutcome = { readonly type: 'applied'; readonly value: OfficialInvoice } | { readonly type: 'unknown' } | { readonly type: 'waiting-scan' }
  | { readonly type: 'refused'; readonly status: number; readonly codes: readonly string[]; readonly existingOfficialNo: string | null }
const record = (v: unknown): v is Record<string, unknown> => v !== null && typeof v === 'object' && !Array.isArray(v)
const text = (v: unknown): v is string => typeof v === 'string' && v.trim() !== ''
const date = (v: unknown) => typeof v === 'string' && isDateOnly(v)
const nullableText = (v: unknown) => v === null || text(v)
const nullableInstant = (v: unknown) => v === null || isInstant(v)
// Exact OpenAPI property sets — K3 P2-3.
const INVOICE_REQUIRED = ['approvedAtUtc', 'approvedBy', 'companyId', 'currencyId', 'customerPartyId', 'ettn', 'invoiceId', 'officialNo',
  ...OFFICIAL_AMOUNT_FIELDS, 'pdfDocumentId', 'pdfDocumentVersionNo', 'pdfSha256', 'recordedAtUtc', 'recordedBy', 'rejectedAtUtc', 'rejectedBy',
  'rejectionReason', 'status', 'version', 'withdrawnAtUtc', 'withdrawnBy']
const INVOICE_OPTIONAL = ['invoiceDate', 'sourceDifferences', 'taxWarning']
// FinanceOfficialInvoiceTaxWarningV1: all five keys are required; sourceDocumentId is null on a manual record.
const TAX_WARNING_FIELDS = ['code', 'differenceFunctionalAmount', 'expectedPayableFunctionalAmount', 'payableFunctionalAmount', 'sourceDocumentId']
// The expected payable may be negative text (withholding above base + VAT); the other two amounts never are.
const SIGNED_DECIMAL_RE = /^-?[0-9]+(?:\.[0-9]+)?$/
export function decodeOfficialInvoiceTaxWarning(value: unknown): OfficialInvoiceTaxWarning | null {
  return record(value) && hasExactKeys(value, TAX_WARNING_FIELDS) && text(value.code)
    && (value.sourceDocumentId === null || isBoundUuid(value.sourceDocumentId))
    && typeof value.expectedPayableFunctionalAmount === 'string' && SIGNED_DECIMAL_RE.test(value.expectedPayableFunctionalAmount)
    && isNonNegativeDecimalText(value.payableFunctionalAmount) && isNonNegativeDecimalText(value.differenceFunctionalAmount)
    ? value as unknown as OfficialInvoiceTaxWarning : null
}
const DIFFERENCE_FIELDS = ['ettn', 'fields', 'importBatchId', 'invoiceDate', 'officialNo', 'payableFunctionalAmount', 'sourceDocumentId', 'winner']
const PAGE_FIELDS = ['companyId', 'items', 'nextCursor']
const CURRENCIES_FIELDS = ['companyId', 'items']
const CURRENCY_FIELDS = ['code', 'currencyId', 'displayLabel']
const RESERVATION_FIELDS = ['companyId', 'invoiceId', 'reservedAtUtc', 'reservedBy']
const INVOICE_RESULT_FIELDS = [...COMMAND_RESULT_FIELDS, 'existingOfficialNo']
const ISSUE_FIELDS = ['code', 'field']
const differenceFields = ['officialNo', 'ettn', 'customerPartyId', 'currencyId', ...OFFICIAL_AMOUNT_FIELDS, 'invoiceDate']
export function decodeOfficialInvoice(value: unknown, company: string): OfficialInvoice | null {
  if (!record(value) || !hasExactKeys(value, INVOICE_REQUIRED, INVOICE_OPTIONAL) || !isBoundUuid(value.invoiceId) || value.companyId !== company || !text(value.officialNo) || !isBoundUuid(value.ettn)
    || !isBoundUuid(value.customerPartyId) || !isBoundUuid(value.currencyId) || OFFICIAL_AMOUNT_FIELDS.some(k => !isNonNegativeDecimalText(value[k]))
    || value.invoiceDate != null && !date(value.invoiceDate) || !isBoundUuid(value.pdfDocumentId) || !Number.isSafeInteger(value.pdfDocumentVersionNo) || (value.pdfDocumentVersionNo as number) < 1
    || typeof value.pdfSha256 !== 'string' || !/^[0-9a-f]{64}$/i.test(value.pdfSha256) || !isOpaqueVersion(value.version)
    || !['PendingApproval', 'Approved', 'Rejected', 'Withdrawn'].includes(value.status as string) || !text(value.recordedBy) || !isInstant(value.recordedAtUtc)
    || !nullableText(value.approvedBy) || !nullableInstant(value.approvedAtUtc) || !nullableText(value.rejectedBy) || !nullableInstant(value.rejectedAtUtc)
    || !nullableText(value.withdrawnBy) || !nullableInstant(value.withdrawnAtUtc) || !nullableText(value.rejectionReason)) return null
  if (value.status === 'PendingApproval' && [value.approvedBy, value.approvedAtUtc, value.rejectedBy, value.rejectedAtUtc, value.rejectionReason, value.withdrawnBy, value.withdrawnAtUtc].some(x => x !== null)
    || value.status === 'Approved' && (!text(value.approvedBy) || !isInstant(value.approvedAtUtc) || value.approvedBy === value.recordedBy)
    || value.status === 'Rejected' && (!text(value.rejectedBy) || !isInstant(value.rejectedAtUtc) || !text(value.rejectionReason) || value.rejectedBy === value.recordedBy)
    || value.status === 'Withdrawn' && (value.withdrawnBy !== value.recordedBy || !isInstant(value.withdrawnAtUtc))) return null
  if (value.status === 'Approved' && [value.rejectedBy, value.rejectedAtUtc, value.rejectionReason, value.withdrawnBy, value.withdrawnAtUtc].some(x => x !== null)
    || value.status === 'Rejected' && [value.approvedBy, value.approvedAtUtc, value.withdrawnBy, value.withdrawnAtUtc].some(x => x !== null)
    || value.status === 'Withdrawn' && [value.approvedBy, value.approvedAtUtc, value.rejectedBy, value.rejectedAtUtc, value.rejectionReason].some(x => x !== null)) return null
  if (value.sourceDifferences != null && (!Array.isArray(value.sourceDifferences) || value.sourceDifferences.some(x => !record(x) || !hasExactKeys(x, DIFFERENCE_FIELDS) || !isBoundUuid(x.importBatchId)
    || !isBoundUuid(x.sourceDocumentId) || !text(x.officialNo) || !isBoundUuid(x.ettn) || !Array.isArray(x.fields) || x.fields.length === 0
    || x.fields.some(k => typeof k !== 'string' || !differenceFields.includes(k)) || x.invoiceDate !== null && !date(x.invoiceDate)
    || x.payableFunctionalAmount !== null && !isNonNegativeDecimalText(x.payableFunctionalAmount) || !['manual', 'approved-import'].includes(x.winner as string)))) return null
  if (value.taxWarning != null && decodeOfficialInvoiceTaxWarning(value.taxWarning) === null) return null
  return value as unknown as OfficialInvoice
}
export function decodeOfficialInvoicePage(body: unknown, company: string): OfficialInvoicePage | null {
  if (!record(body) || !hasExactKeys(body, PAGE_FIELDS) || body.companyId !== company || !Array.isArray(body.items) || !nullableText(body.nextCursor)) return null
  const rows = body.items.map(x => decodeOfficialInvoice(x, company))
  return rows.some(x => x === null) || new Set(rows.map(x => x?.invoiceId)).size !== rows.length ? null : body as unknown as OfficialInvoicePage
}
const headers = (company: string) => ({ ...devHeaders(), 'X-Company-Id': company })
async function read<T>(company: string, path: string, decode: (body: unknown) => T | null, signal?: AbortSignal): Promise<ViewOutcome<T>> {
  try { const response = await fetch(API_BASE + path, { headers: headers(company), cache: 'no-store', signal }); const body: unknown = await response.json().catch(() => null)
    if (response.status !== 200) return { type: 'http', status: response.status, body }; const data = decode(body)
    return data === null ? { type: 'malformed' } : { type: 'ok-single', data }
  } catch { return signal?.aborted ? { type: 'fence-stale' } : navigator.onLine ? { type: 'network-error' } : { type: 'offline' } }
}
export const fetchOfficialInvoices = (company: string, pendingOnly: boolean, cursor: string | null, signal?: AbortSignal) => read(company,
  `/finance/official-sales-invoices?${new URLSearchParams({ pendingOnly: String(pendingOnly), ...(cursor === null ? {} : { cursor }) })}`, x => decodeOfficialInvoicePage(x, company), signal)
export const fetchOfficialInvoiceCurrencies = (company: string, signal?: AbortSignal) => read<readonly OfficialInvoiceCurrency[]>(company, '/finance/official-sales-invoices/currency-candidates', body => {
  if (!record(body) || !hasExactKeys(body, CURRENCIES_FIELDS) || body.companyId !== company || !Array.isArray(body.items)
    || body.items.some(x => !record(x) || !hasExactKeys(x, CURRENCY_FIELDS) || !isBoundUuid(x.currencyId) || !text(x.code) || !text(x.displayLabel))
    || new Set(body.items.map(x => x.currencyId)).size !== body.items.length) return null
  return body.items as OfficialInvoiceCurrency[]
}, signal)
interface PostResult { readonly status: number; readonly body: unknown; readonly headers: Headers }
async function post(company: string, path: string, key: string, input: unknown): Promise<PostResult | null> {
  try { const response = await fetch(`${API_BASE}/finance/official-sales-invoices${path}`, { method: 'POST', headers: { ...headers(company), 'Content-Type': 'application/json', 'Idempotency-Key': key }, body: JSON.stringify(input) })
    return { status: response.status, body: await response.json().catch(() => null), headers: response.headers }
  } catch { return null }
}
function refusal(result: PostResult | null, fields: readonly string[] = INVOICE_RESULT_FIELDS): OfficialInvoiceWriteOutcome {
  if (result === null || result.status >= 500) return { type: 'unknown' }
  const { status, body } = result
  if ([400, 401, 403, 404, 415].includes(status)) return { type: 'refused', status, codes: [], existingOfficialNo: null }
  if ([409, 422].includes(status) && record(body) && hasExactKeys(body, fields) && body.resource === null && Array.isArray(body.issues)
    && body.issues.every(x => record(x) && hasExactKeys(x, ISSUE_FIELDS) && text(x.code) && text(x.field))
    && (status === 422 ? body.outcome === 'ValidationFailed' : ['VersionConflict', 'IdempotencyConflict'].includes(body.outcome as string)))
    return { type: 'refused', status, codes: body.issues.map(x => x.code as string), existingOfficialNo: text(body.existingOfficialNo) ? body.existingOfficialNo : null }
  return { type: 'unknown' }
}
function resultInvoice(result: PostResult | null, company: string): OfficialInvoice | null {
  if (result === null) return null
  const body = result.body
  return result.status === 200 && record(body) && hasExactKeys(body, INVOICE_RESULT_FIELDS) && body.existingOfficialNo === null
    && body.outcome === 'Succeeded' && typeof body.isReplay === 'boolean' && text(body.originalCorrelationId)
    && replayHeadersAgree(result.headers, body.isReplay, body.originalCorrelationId) && Array.isArray(body.issues) && body.issues.length === 0 ? decodeOfficialInvoice(body.resource, company) : null
}
// The owner strips only integer leading zeros while preserving source scale. This is wire identity checking, no money calculation.
const ownerAmountText = (text: string) => { const parts = text.split('.'); return (parts[0].replace(/^0+(?=\d)/, '') || '0') + (parts.length === 2 ? `.${parts[1]}` : '') }
/** Reservation, PDF upload and final record keep separate, stable keys; retries never generate another invoice identity. */
export async function continueOfficialInvoice(attempt: OfficialInvoiceCreateAttempt): Promise<OfficialInvoiceWriteOutcome> {
  if (attempt.phase === 'reserve') {
    const result = await post(attempt.company, '/reservations', attempt.reserveKey, {}); const body = result?.body
    if (result?.status !== 200 || !record(body) || !hasExactKeys(body, COMMAND_RESULT_FIELDS) || body.outcome !== 'Succeeded'
      || typeof body.isReplay !== 'boolean' || !text(body.originalCorrelationId)
      || !replayHeadersAgree(result.headers, body.isReplay, body.originalCorrelationId)
      || !Array.isArray(body.issues) || body.issues.length !== 0 || !record(body.resource) || !hasExactKeys(body.resource, RESERVATION_FIELDS)
      || body.resource.companyId !== attempt.company
      || !isBoundUuid(body.resource.invoiceId) || body.resource.reservedBy !== attempt.actor || !isInstant(body.resource.reservedAtUtc)) return refusal(result, COMMAND_RESULT_FIELDS)
    attempt.invoiceId = body.resource.invoiceId; attempt.phase = 'upload'
  }
  if (attempt.invoiceId === null) return { type: 'unknown' }
  if (attempt.phase === 'upload') {
    const upload: UploadAttempt = { companyId: attempt.company, ownerKind: OFFICIAL_INVOICE_OWNER, ownerId: attempt.invoiceId, file: attempt.file, idempotencyKey: attempt.uploadKey }
    const result = await uploadDocument(upload)
    if (result.type !== 'stored') return result.type === 'refused' ? { type: 'refused', status: result.status, codes: result.code === null ? [] : [result.code], existingOfficialNo: null } : { type: 'unknown' }
    if (result.upload.document.contentType !== 'application/pdf') return { type: 'unknown' }
    attempt.documentId = result.upload.document.id; attempt.phase = 'scan'
  }
  if (attempt.documentId === null) return { type: 'unknown' }
  if (attempt.phase === 'scan') {
    const documents = await fetchDocuments(OFFICIAL_INVOICE_OWNER, attempt.invoiceId, attempt.company)
    if (documents.type === 'http' && [401, 403, 404].includes(documents.status)) return { type: 'refused', status: documents.status, codes: [], existingOfficialNo: null }
    if (documents.type !== 'ok-single') return { type: 'unknown' }
    const matches = documents.data.items.filter(x => x.id === attempt.documentId)
    if (matches.length !== 1 || matches[0].contentType !== 'application/pdf') return { type: 'unknown' }
    if (!matches[0].downloadable || !['Clean', 'SystemGenerated'].includes(matches[0].scanState)) return { type: 'waiting-scan' }
    attempt.phase = 'record'
  }
  const result = await post(attempt.company, '', attempt.recordKey, { ...attempt.input, invoiceId: attempt.invoiceId, pdfDocumentId: attempt.documentId })
  const value = resultInvoice(result, attempt.company)
  return value !== null && value.invoiceId === attempt.invoiceId && value.pdfDocumentId === attempt.documentId && value.status === 'PendingApproval'
    && value.recordedBy === attempt.actor && value.officialNo === attempt.input.officialNo && value.ettn.toLowerCase() === attempt.input.ettn.toLowerCase()
    && value.customerPartyId === attempt.input.customerPartyId && value.currencyId === attempt.input.currencyId && value.invoiceDate === attempt.input.invoiceDate
    && OFFICIAL_AMOUNT_FIELDS.every(k => value[k] === ownerAmountText(attempt.input[k]))
    ? { type: 'applied', value } : refusal(result)
}
export async function decideOfficialInvoice(company: string, actor: string, attempt: OfficialInvoiceDecisionAttempt): Promise<OfficialInvoiceWriteOutcome> {
  const result = await post(company, `/${encodeURIComponent(attempt.invoiceId)}/${attempt.operation}`, attempt.key,
    { invoiceId: attempt.invoiceId, expectedVersion: attempt.expectedVersion, ...(attempt.operation === 'rejection' ? { reason: attempt.reason } : {}) })
  const value = resultInvoice(result, company)
  return value !== null && value.invoiceId === attempt.invoiceId && value.status === ({ approval: 'Approved', rejection: 'Rejected', withdrawal: 'Withdrawn' } as const)[attempt.operation]
    && value[({ approval: 'approvedBy', rejection: 'rejectedBy', withdrawal: 'withdrawnBy' } as const)[attempt.operation]] === actor
    && (attempt.operation !== 'rejection' || value.rejectionReason === attempt.reason)
    ? { type: 'applied', value } : refusal(result)
}
