import type { ViewOutcome } from '../shell/uiState'
import { isDateOnly } from '../finance/dateOnly'
import { ACCOUNTING_BASE, devHeaders } from './api'
import { postedManufacturingCostShare, type PostedManufacturingCostShare } from './postedManufacturingCostProof'

export interface CogsAttributedEvidence { readonly source: PostedManufacturingCostShare; readonly currentJournalId: string
  readonly currentCogsAccountId: string; readonly counterAccountId: string; readonly currentJournalTotal: string }
export interface CogsAttributedCorrection { readonly attributionFingerprint: string; readonly sourceJournalId: string
  readonly reversingCorrectionJournalId: string; readonly replacementCorrectionJournalId: string }

export interface CogsAccount { readonly accountId: string; readonly code: string; readonly name: string }
export interface CogsAccountPlan { readonly packageId: string; readonly packageVersion: number; readonly catalogVersion: number | null
  readonly sourceVersion: string; readonly sourceSha256: string; readonly account: CogsAccount }
export interface CogsEvidence { readonly companyId: string; readonly invoiceId: string; readonly invoiceLineId: string
  readonly finalizedVersion: number; readonly journalId: string; readonly cogsJournalLineNumber: number; readonly counterJournalLineNumber: number
  readonly cogsAccountId: string; readonly counterAccountId: string; readonly currencyId: string; readonly amount: string; readonly sourceFingerprint: string }
export interface CogsProposal { readonly proposalId: string; readonly companyId: string; readonly invoiceId: string; readonly invoiceLineId: string
  readonly finalizedVersion: number; readonly version: number; readonly expectedLineVersion: number; readonly status: 'Prepared' | 'Approved'
  readonly classification: 1 | 2; readonly accountPlan: CogsAccountPlan; readonly periodId: string; readonly transactionDate: string
  readonly accountingDate: string; readonly valueDate: string | null; readonly preparedBy: string; readonly preparedAtUtc: string
  readonly approvedBy: string | null; readonly approvedAtUtc: string | null; readonly reason: string; readonly postedEvidence: CogsEvidence | null
  readonly reversingCorrectionJournalId: string | null; readonly replacementCorrectionJournalId: string | null
  readonly attributedEvidence?: readonly CogsAttributedEvidence[]; readonly attributedCorrections?: readonly CogsAttributedCorrection[] }
export interface CogsRevision { readonly version: number; readonly proposalId: string; readonly classification: 1 | 2
  readonly accountPlan: CogsAccountPlan; readonly preparedBy: string; readonly approvedBy: string; readonly approvedAtUtc: string
  readonly periodId: string; readonly transactionDate: string; readonly accountingDate: string; readonly reason: string
  readonly reversingCorrectionJournalId: string | null; readonly replacementCorrectionJournalId: string | null
  readonly attributedCorrections?: readonly CogsAttributedCorrection[] }
export interface CogsHistory { readonly companyId: string; readonly invoiceId: string; readonly invoiceLineId: string
  readonly source: { readonly companyId: string; readonly invoiceId: string; readonly invoiceLineId: string; readonly finalizedVersion: number
    readonly finalizationAdmissionId: string; readonly productId: string; readonly shipmentId: string; readonly dispatchNoteId: string
    readonly dispatchNoteLineNo: number; readonly invoiceLineNo: number; readonly invoiceDate: string; readonly officialInvoiceNumber: string; readonly ettn: string
    readonly fingerprint: string }
  readonly lineVersion: number; readonly current: CogsRevision | null; readonly revisions: readonly CogsRevision[]
  readonly proposals: readonly CogsProposal[]; readonly postingEvidenceState: 'NoPostedCogs' | 'PostedCogs' | 'Unreadable' | 'AttributedPostedCogs'
  readonly attributedEvidence?: readonly CogsAttributedEvidence[]
  readonly postedEvidence: CogsEvidence | null; readonly productReference: { readonly companyId: string; readonly productId: string
    readonly outcome: 'Available' | 'NotSet' | 'NotFound' | 'Unavailable'; readonly classification: 1 | 2 | null
    readonly productVersion: string | null; readonly classificationVersion: string | null } }
export interface CogsOptions { readonly companyId: string; readonly accounts: readonly CogsAccount[]
  readonly openPeriods: readonly { readonly periodId: string; readonly startsOn: string; readonly endsOn: string }[] }
export interface CogsSourceSelection { readonly companyId: string; readonly invoiceId: string; readonly officialInvoiceNumber: string
  readonly ettn: string; readonly lines: readonly CogsHistory['source'][] }
export interface CogsPreparation { readonly proposalId: string; readonly expectedFinalizedVersion: number; readonly expectedLineVersion: number
  readonly classification: 1 | 2; readonly cogsAccountId: string; readonly periodId: string; readonly transactionDate: string
  readonly accountingDate: string; readonly reason: string; readonly valueDate: string | null }
export type CogsAttempt = { readonly key: string; readonly invoiceId: string; readonly lineId: string; readonly proposalId: string
  readonly operation: 'prepare'; readonly request: CogsPreparation } | { readonly key: string; readonly invoiceId: string; readonly lineId: string
    readonly proposalId: string; readonly operation: 'approve'; readonly request: { readonly expectedVersion: number } }
export type CogsCommandOutcome = { readonly type: 'applied'; readonly value: CogsProposal }
  | { readonly type: 'refused'; readonly status: number; readonly code: string | null } | { readonly type: 'unknown' }

const record = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null && !Array.isArray(v)
const guid = (v: unknown): v is string => typeof v === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(v) && !/^0{8}-0{4}-0{4}-0{4}-0{12}$/.test(v)
const same = (a: unknown, b: string) => guid(a) && a.toLowerCase() === b.toLowerCase()
const integer = (v: unknown, minimum = 1): v is number => typeof v === 'number' && Number.isSafeInteger(v) && v >= minimum
const text = (v: unknown): v is string => typeof v === 'string' && v.trim() !== ''
const hash = (v: unknown) => typeof v === 'string' && /^[0-9a-f]{64}$/i.test(v)
const date = (v: unknown) => typeof v === 'string' && isDateOnly(v)
const instant = (v: unknown) => typeof v === 'string' && /^\d{4}-\d{2}-\d{2}T/.test(v) && Number.isFinite(Date.parse(v))
const nullableGuid = (v: unknown) => v === null || guid(v)
const classification = (v: unknown) => v === 1 || v === 2
const account = (v: unknown) => record(v) && guid(v.accountId) && text(v.code) && text(v.name)
const plan = (v: unknown) => record(v) && guid(v.packageId) && integer(v.packageVersion) && (v.catalogVersion === null || integer(v.catalogVersion))
  && text(v.sourceVersion) && hash(v.sourceSha256) && account(v.account)
const pair = (v: Record<string, unknown>) => nullableGuid(v.reversingCorrectionJournalId) && nullableGuid(v.replacementCorrectionJournalId)
  && (v.reversingCorrectionJournalId === null) === (v.replacementCorrectionJournalId === null)
function attributedEvidence(v: unknown, company: string): boolean {
  return v === undefined || Array.isArray(v) && v.length > 0 && v.every(item => record(item)
    && postedManufacturingCostShare(item.source, company) && guid(item.currentJournalId) && guid(item.currentCogsAccountId)
    && guid(item.counterAccountId) && !same(item.currentCogsAccountId, item.counterAccountId)
    && same(item.counterAccountId, item.source.counterAccountId) && typeof item.currentJournalTotal === 'string'
    && /^(0|[1-9][0-9]*)(\.[0-9]+)?$/.test(item.currentJournalTotal) && /[1-9]/.test(item.currentJournalTotal))
    && new Set(v.map(item => (item as CogsAttributedEvidence).source.fingerprint)).size === v.length
}
function attributedCorrections(v: unknown): boolean {
  return v === undefined || Array.isArray(v) && v.every(item => record(item) && hash(item.attributionFingerprint)
    && guid(item.sourceJournalId) && guid(item.reversingCorrectionJournalId) && guid(item.replacementCorrectionJournalId)
    && !same(item.reversingCorrectionJournalId, item.replacementCorrectionJournalId)
    && !same(item.sourceJournalId, item.reversingCorrectionJournalId) && !same(item.sourceJournalId, item.replacementCorrectionJournalId))
    && new Set(v.map(item => (item as CogsAttributedCorrection).attributionFingerprint)).size === v.length
}
function evidence(v: unknown, company: string, invoice: string, line: string): boolean {
  return v === null || record(v) && same(v.companyId, company) && same(v.invoiceId, invoice) && same(v.invoiceLineId, line)
    && integer(v.finalizedVersion) && guid(v.journalId) && v.cogsJournalLineNumber === 1 && v.counterJournalLineNumber === 2
    && guid(v.cogsAccountId) && guid(v.counterAccountId) && v.cogsAccountId !== v.counterAccountId && guid(v.currencyId)
    && typeof v.amount === 'string' && /^(0|[1-9][0-9]*)(\.[0-9]+)?$/.test(v.amount) && /[1-9]/.test(v.amount) && hash(v.sourceFingerprint)
}
export function decodeCogsProposal(v: unknown, company: string, invoice: string, line: string): CogsProposal | null {
  if (!record(v) || !guid(v.proposalId) || !same(v.companyId, company) || !same(v.invoiceId, invoice) || !same(v.invoiceLineId, line)
    || !integer(v.finalizedVersion) || !integer(v.expectedLineVersion, 0) || !classification(v.classification) || !plan(v.accountPlan)
    || !guid(v.periodId) || !date(v.transactionDate) || !date(v.accountingDate) || v.valueDate !== null && !date(v.valueDate)
    || !guid(v.preparedBy) || !instant(v.preparedAtUtc) || !text(v.reason) || !pair(v) || !evidence(v.postedEvidence, company, invoice, line)
    || !attributedEvidence(v.attributedEvidence, company) || !attributedCorrections(v.attributedCorrections)
    || v.attributedEvidence !== undefined && (v.postedEvidence !== null || v.reversingCorrectionJournalId !== null)) return null
  if (v.status === 'Prepared' ? v.version !== 1 || v.approvedBy !== null || v.approvedAtUtc !== null || v.reversingCorrectionJournalId !== null
    : v.status !== 'Approved' || v.version !== 2 || !guid(v.approvedBy) || !instant(v.approvedAtUtc)
      || v.preparedBy.toLowerCase() === v.approvedBy.toLowerCase()) return null
  if (v.status === 'Prepared' && v.attributedCorrections !== undefined || Array.isArray(v.attributedCorrections)
    && (v.attributedEvidence === undefined || v.attributedCorrections.some(c => !(v.attributedEvidence as CogsAttributedEvidence[])
      .some(e => e.source.fingerprint === c.attributionFingerprint && same(c.sourceJournalId, e.currentJournalId))))) return null
  return v as unknown as CogsProposal
}
function revision(v: unknown): v is CogsRevision {
  return record(v) && integer(v.version) && guid(v.proposalId) && classification(v.classification) && plan(v.accountPlan)
    && guid(v.preparedBy) && guid(v.approvedBy) && v.preparedBy.toLowerCase() !== v.approvedBy.toLowerCase()
    && instant(v.approvedAtUtc) && guid(v.periodId) && date(v.transactionDate) && date(v.accountingDate) && text(v.reason) && pair(v)
    && attributedCorrections(v.attributedCorrections)
}
function sourceReference(v: unknown, company: string, invoice: string, line: string): boolean {
  return record(v) && same(v.companyId, company) && same(v.invoiceId, invoice) && same(v.invoiceLineId, line)
    && integer(v.finalizedVersion) && ['finalizationAdmissionId', 'productId', 'shipmentId', 'dispatchNoteId', 'ettn'].every(k => guid(v[k]))
    && integer(v.dispatchNoteLineNo) && integer(v.invoiceLineNo) && date(v.invoiceDate) && text(v.officialInvoiceNumber)
    && hash(v.fingerprint) && Object.keys(v).every(key => ['companyId', 'invoiceId', 'invoiceLineId', 'finalizedVersion', 'finalizationAdmissionId',
      'productId', 'shipmentId', 'dispatchNoteId', 'dispatchNoteLineNo', 'invoiceLineNo', 'invoiceDate', 'officialInvoiceNumber', 'ettn', 'fingerprint'].includes(key))
}
export function decodeCogsSourceSelection(v: unknown, company: string): CogsSourceSelection | null {
  if (!record(v) || !same(v.companyId, company) || !guid(v.invoiceId) || !guid(v.ettn) || !text(v.officialInvoiceNumber)
    || !Array.isArray(v.lines) || v.lines.length === 0 || v.lines.some(line => !record(line) || !guid(line.invoiceLineId)
      || !sourceReference(line, company, v.invoiceId as string, line.invoiceLineId) || line.ettn !== v.ettn)
    || new Set(v.lines.map(line => (line as CogsHistory['source']).invoiceLineId.toLowerCase())).size !== v.lines.length) return null
  return v as unknown as CogsSourceSelection
}
function attributedSourceMatches(value: unknown, source: unknown): boolean {
  if (value === undefined) return true
  if (!Array.isArray(value) || !record(source) || !guid(source.productId) || !guid(source.shipmentId)) return false
  const product = source.productId; const shipment = source.shipmentId
  return value.every((entry: CogsAttributedEvidence) => same(entry.source.shipment.productId, product) && same(entry.source.shipment.shipmentId, shipment))
}
export function decodeCogsHistory(v: unknown, company: string, invoice: string, line: string): CogsHistory | null {
  if (!record(v) || !same(v.companyId, company) || !same(v.invoiceId, invoice) || !same(v.invoiceLineId, line) || !integer(v.lineVersion, 0)
    || !record(v.source) || !sourceReference(v.source, company, invoice, line) || !Array.isArray(v.proposals) || !Array.isArray(v.revisions)
    || !v.revisions.every(revision) || v.revisions.length !== v.lineVersion || v.revisions.some((r, i) => r.version !== i + 1)
    || v.proposals.some(p => decodeCogsProposal(p, company, invoice, line) === null)
    || new Set(v.proposals.map(p => (p as CogsProposal).proposalId.toLowerCase())).size !== v.proposals.length
    || (v.lineVersion === 0 ? v.current !== null : !revision(v.current) || JSON.stringify(v.current) !== JSON.stringify(v.revisions.at(-1)))
    || !['NoPostedCogs', 'PostedCogs', 'Unreadable', 'AttributedPostedCogs'].includes(v.postingEvidenceState as string)
    || !evidence(v.postedEvidence, company, invoice, line) || (v.postingEvidenceState === 'PostedCogs') !== (v.postedEvidence !== null)
    || !attributedEvidence(v.attributedEvidence, company) || (v.postingEvidenceState === 'AttributedPostedCogs') !== (v.attributedEvidence !== undefined)
    || !attributedSourceMatches(v.attributedEvidence, v.source)
    || !record(v.productReference) || !same(v.productReference.companyId, company) || !same(v.productReference.productId, v.source.productId as string)
    || !['Available', 'NotSet', 'NotFound', 'Unavailable'].includes(v.productReference.outcome as string)
    || (v.productReference.outcome === 'Available' ? !classification(v.productReference.classification) || !text(v.productReference.productVersion)
      || !text(v.productReference.classificationVersion) : v.productReference.classification !== null)) return null
  return v as unknown as CogsHistory
}
export function decodeCogsOptions(v: unknown, company: string): CogsOptions | null {
  if (!record(v) || !same(v.companyId, company) || !Array.isArray(v.accounts) || !v.accounts.every(account)
    || !Array.isArray(v.openPeriods) || v.openPeriods.some(p => !record(p) || !guid(p.periodId) || !date(p.startsOn) || !date(p.endsOn)
      || (p.endsOn as string) < (p.startsOn as string))) return null
  return v as unknown as CogsOptions
}
const base = `${ACCOUNTING_BASE}/invoice-line-cogs`
const linePath = (invoice: string, line: string) => `${base}/${encodeURIComponent(invoice)}/lines/${encodeURIComponent(line)}`
async function read<T>(url: string, company: string, decode: (v: unknown) => T | null, signal?: AbortSignal): Promise<ViewOutcome<T>> {
  try {
    const response = await fetch(url, { headers: { ...devHeaders(), 'X-Company-Id': company }, signal })
    const body: unknown = await response.json().catch(() => null)
    if (response.status !== 200) return { type: 'http', status: response.status, body }
    const value = decode(body)
    return value === null ? { type: 'malformed' } : { type: 'ok-single', data: value }
  } catch { return signal?.aborted ? { type: 'fence-stale' } : navigator.onLine ? { type: 'network-error' } : { type: 'offline' } }
}
export const fetchCogsHistory = (company: string, invoice: string, line: string, signal?: AbortSignal) =>
  read(linePath(invoice, line), company, body => decodeCogsHistory(body, company, invoice, line), signal)
export const fetchCogsOptions = (company: string, signal?: AbortSignal) => read(`${base}/options`, company, body => decodeCogsOptions(body, company), signal)
export const findCogsSource = (company: string, officialInvoiceNumber: string, signal?: AbortSignal) =>
  read(`${base}/source?${new URLSearchParams({ officialInvoiceNumber }).toString()}`, company, body => decodeCogsSourceSelection(body, company), signal)
const refusalCodes = new Set(['idempotency_conflict', 'accounting_version_conflict', 'accounting_concurrent_change',
  'accounting_invoice_line_cogs_correction_source_changed', 'accounting_invoice_line_cogs_correction_version_conflict',
  'accounting_invoice_line_cogs_correction_not_prepared', 'accounting_invoice_line_cogs_correction_request_invalid',
  'accounting_invoice_line_cogs_correction_open_period_required', 'accounting_invoice_line_cogs_correction_account_not_in_active_plan',
  'accounting_invoice_line_cogs_correction_posting_evidence_invalid'])
export async function sendCogsCorrection(company: string, attempt: CogsAttempt): Promise<CogsCommandOutcome> {
  try {
    const path = attempt.operation === 'prepare' ? '/preparation' : `/proposals/${encodeURIComponent(attempt.proposalId)}/approval`
    const response = await fetch(linePath(attempt.invoiceId, attempt.lineId) + path, { method: 'POST', headers: { ...devHeaders(),
      'X-Company-Id': company, 'Content-Type': 'application/json', 'Idempotency-Key': attempt.key }, body: JSON.stringify(attempt.request) })
    const body: unknown = await response.json().catch(() => null)
    if (response.status === 200) {
      const value = decodeCogsProposal(body, company, attempt.invoiceId, attempt.lineId)
      return value !== null && value.proposalId.toLowerCase() === attempt.proposalId.toLowerCase()
        && value.status === (attempt.operation === 'prepare' ? 'Prepared' : 'Approved') ? { type: 'applied', value } : { type: 'unknown' }
    }
    const code = record(body) && typeof body.code === 'string' ? body.code : null
    return [400, 401, 403, 404, 415].includes(response.status) || [409, 422].includes(response.status) && code !== null && refusalCodes.has(code)
      ? { type: 'refused', status: response.status, code } : { type: 'unknown' }
  } catch { return { type: 'unknown' } }
}
