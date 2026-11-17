import type { ViewOutcome } from '../shell/uiState'
import { isDateOnly } from '../finance/dateOnly'
import { ACCOUNTING_BASE, devHeaders } from './api'
import { decodeJournalReversalProposal, type JournalReversalProposal } from './journalReversalContract'
import { postedManufacturingCostShare, type PostedManufacturingCostShare } from './postedManufacturingCostProof'

export type InventoryCostKind = 1 | 2
export interface InventoryCostAccount { readonly accountId: string; readonly code: string; readonly name: string }
export interface InventoryCostOptions {
  readonly companyId: string; readonly accounts: readonly InventoryCostAccount[]
  readonly openPeriods: readonly { readonly periodId: string; readonly startsOn: string; readonly endsOn: string }[]
}
export interface InventoryCostProposal {
  readonly proposalId: string; readonly companyId: string; readonly eventId: string; readonly kind: InventoryCostKind
  readonly invoiceId: string; readonly invoiceVersion: number; readonly admissionId: string; readonly invoiceDate: string
  readonly officialInvoiceNumber: string | null; readonly version: number; readonly status: string; readonly sourceOccurredAtUtc: string; readonly exactTlAmount: string
  readonly component: { readonly componentKey: string; readonly productId: string; readonly invoiceLineId: string | null
    readonly invoiceLineNumber: number | null; readonly locationId: string | null
    readonly signedTlAmount: { readonly numerator: string; readonly denominator: string }; readonly sourceProofSha256: string
    readonly alreadyPostedComponents?: readonly PostedManufacturingCostShare[] }
  readonly preparation: { readonly periodId: string; readonly accountingDate: string; readonly transactionDate: string
    readonly currencyId: string; readonly signedAmount: string; readonly moneyProfileId: string; readonly moneyProfileVersion: number; readonly moneyScale: number
    readonly preparedBy: string; readonly preparedAtUtc: string; readonly reason: string
    readonly classification: { readonly classification: 1 | 2; readonly accountId: string; readonly origin: 'Product' | 'InvoiceLine'
      readonly productVersion: string | null; readonly classificationVersion: string | null; readonly mappingVersion: number | null; readonly invoiceLineRevision: number }
    readonly counterAccount: InventoryCostAccount; readonly accountPlanPackageId: string; readonly accountPlanPackageVersion: number
    readonly accountPlanCatalogVersion: number | null; readonly accountPlanSourceVersion: string; readonly accountPlanSha256: string } | null
  readonly approvedBy: string | null; readonly approvedAtUtc: string | null; readonly journalId: string | null
  readonly currentPostedJournalId: string | null
  readonly cancellationId: string | null; readonly reversalProposal: JournalReversalProposal | null
}
export type InventoryCostAttempt = { readonly id: string; readonly kind: InventoryCostKind; readonly key: string; readonly actor: string } & (
  { readonly operation: 'prepare'; readonly request: { readonly expectedVersion: number; readonly periodId: string; readonly accountingDate: string
    readonly transactionDate: string; readonly counterAccountId: string; readonly reason: string } }
  | { readonly operation: 'approve'; readonly request: { readonly expectedVersion: number } }
  | { readonly operation: 'reverse'; readonly request: { readonly expectedVersion: number; readonly proposalId: string
    readonly periodId: string; readonly accountingDate: string; readonly transactionDate: string; readonly reason: string } })
export type InventoryCostOutcome = { readonly type: 'applied'; readonly value: InventoryCostProposal }
  | { readonly type: 'refused'; readonly status: number; readonly code: string | null } | { readonly type: 'unknown' }

const base = `${ACCOUNTING_BASE}/inventory-cost-proposals`
const rec = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null && !Array.isArray(v)
const guid = (v: unknown): v is string => typeof v === 'string' && /^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(v) && v !== '00000000-0000-0000-0000-000000000000'
const same = (a: string, b: string) => a.toLowerCase() === b.toLowerCase()
const text = (v: unknown): v is string => typeof v === 'string' && v.trim() === v && v.length > 0
const version = (v: unknown): v is number => Number.isSafeInteger(v) && (v as number) > 0
const hash = (v: unknown) => typeof v === 'string' && /^[a-f0-9]{64}$/i.test(v)
const date = (v: unknown): v is string => typeof v === 'string' && isDateOnly(v)
const utc = (v: unknown) => typeof v === 'string' && /(?:Z|\+00:00)$/.test(v) && Number.isFinite(Date.parse(v))
const canonicalDecimal = (v: unknown): v is string => typeof v === 'string'
  && /^-?(0|[1-9][0-9]*)(\.[0-9]{0,27}[1-9])?$/.test(v) && v !== '-0'
const account = (v: unknown): v is InventoryCostAccount => rec(v) && guid(v.accountId) && text(v.code) && text(v.name)

export function decodeInventoryCost(body: unknown, company: string, kind: InventoryCostKind): InventoryCostProposal | null {
  if (!rec(body) || !['proposalId', 'companyId', 'eventId', 'invoiceId', 'admissionId'].every(k => guid(body[k]))
    || !same(body.companyId as string, company) || body.kind !== kind || !version(body.invoiceVersion) || !version(body.version)
    || !date(body.invoiceDate) || !utc(body.sourceOccurredAtUtc) || !text(body.exactTlAmount) || body.officialInvoiceNumber !== null && !text(body.officialInvoiceNumber)
    || !['AwaitingPreparation', 'Prepared', 'Posted', 'NoCost', 'Cancelled', 'CancellationReversalPending', 'CancelledByReversal', 'AlreadyAccounted', 'AttributedCancellationPending'].includes(body.status as string)) return null
  const c = body.component
  if (!rec(c) || !text(c.componentKey) || !guid(c.productId) || !hash(c.sourceProofSha256) || !rec(c.signedTlAmount)
    || typeof c.signedTlAmount.numerator !== 'string' || !/^-?(0|[1-9][0-9]*)$/.test(c.signedTlAmount.numerator)
    || typeof c.signedTlAmount.denominator !== 'string' || !/^[1-9][0-9]*$/.test(c.signedTlAmount.denominator)) return null
  if (kind === 1 ? !guid(c.invoiceLineId) || !version(c.invoiceLineNumber) || c.locationId !== null
    || c.componentKey !== `invoice-line:${(c.invoiceLineId as string).toLowerCase()}` || c.signedTlAmount.numerator.startsWith('-')
    : c.invoiceLineId !== null || c.invoiceLineNumber !== null || !guid(c.locationId)
      || c.componentKey !== `product-location:${c.productId.toLowerCase()}:${(c.locationId as string).toLowerCase()}`) return null
  // Validate the server's lossless display shape; never calculate or round an amount in the browser.
  if (!canonicalDecimal(body.exactTlAmount)
    && body.exactTlAmount !== `${c.signedTlAmount.numerator} / ${c.signedTlAmount.denominator}`) return null
  const p = body.preparation
  const attributed = ['AlreadyAccounted', 'AttributedCancellationPending'].includes(body.status as string)
  if (attributed ? kind !== 1 || !Array.isArray(c.alreadyPostedComponents) || c.alreadyPostedComponents.length === 0
      || c.alreadyPostedComponents.some(value => !postedManufacturingCostShare(value, company, c.productId as string))
      || new Set(c.alreadyPostedComponents.map(value => (value as PostedManufacturingCostShare).fingerprint)).size !== c.alreadyPostedComponents.length
    : c.alreadyPostedComponents !== undefined && c.alreadyPostedComponents !== null) return null
  if (p !== null) {
    if (!rec(p) || !guid(p.periodId) || !guid(p.currencyId) || !guid(p.preparedBy) || !utc(p.preparedAtUtc)
      || !date(p.accountingDate) || !date(p.transactionDate) || !canonicalDecimal(p.signedAmount)
      || body.exactTlAmount !== p.signedAmount || !text(p.reason)
      || !guid(p.moneyProfileId) || !version(p.moneyProfileVersion) || !Number.isSafeInteger(p.moneyScale) || (p.moneyScale as number) < 0 || (p.moneyScale as number) > 28
      || !guid(p.accountPlanPackageId) || !version(p.accountPlanPackageVersion) || p.accountPlanCatalogVersion !== null && !version(p.accountPlanCatalogVersion)
      || !text(p.accountPlanSourceVersion) || !hash(p.accountPlanSha256) || !account(p.counterAccount) || !rec(p.classification)) return null
    const cl = p.classification
    if (![1, 2].includes(cl.classification as number) || !guid(cl.accountId) || same(cl.accountId, p.counterAccount.accountId)
      || cl.origin === 'Product' && (!text(cl.productVersion) || !text(cl.classificationVersion) || !version(cl.mappingVersion) || cl.invoiceLineRevision !== 0)
      || cl.origin === 'InvoiceLine' && (!version(cl.invoiceLineRevision) || cl.productVersion !== null || cl.classificationVersion !== null || cl.mappingVersion !== null)
      || cl.origin !== 'Product' && cl.origin !== 'InvoiceLine') return null
  }
  if (['Prepared', 'Posted', 'CancellationReversalPending', 'CancelledByReversal'].includes(body.status as string) && p === null) return null
  if ((['AwaitingPreparation', 'NoCost'].includes(body.status as string) || attributed) && p !== null) return null
  if (body.status === 'NoCost' ? c.signedTlAmount.numerator !== '0' || c.signedTlAmount.denominator !== '1' || body.exactTlAmount !== '0'
    : body.status !== 'Cancelled' && !attributed && c.signedTlAmount.numerator === '0') return null
  if ((['AwaitingPreparation', 'NoCost'].includes(body.status as string) || attributed) && body.version !== 1
    || body.status === 'Prepared' && (body.version as number) < 2) return null
  if (['Posted', 'CancellationReversalPending', 'CancelledByReversal'].includes(body.status as string)) {
    if (!rec(p) || !guid(body.approvedBy) || !guid(body.journalId) || !utc(body.approvedAtUtc) || same(body.approvedBy, p.preparedBy as string)) return null
  } else if (body.approvedBy !== null || body.approvedAtUtc !== null || body.journalId !== null) return null
  if (body.cancellationId !== null && !guid(body.cancellationId)) return null
  if (['Cancelled', 'CancellationReversalPending', 'CancelledByReversal', 'AttributedCancellationPending'].includes(body.status as string) && !guid(body.cancellationId)) return null
  const currentJournal = body.currentPostedJournalId === undefined ? null : body.currentPostedJournalId
  if (currentJournal !== null && (!guid(currentJournal) || !guid(body.journalId))) return null
  const reversal = body.reversalProposal === null ? null : decodeJournalReversalProposal(body.reversalProposal, company)
  if (body.reversalProposal !== null && reversal === null) return null
  if (reversal !== null && (typeof currentJournal !== 'string' || !same(reversal.sourceJournalId, currentJournal)
    || !['CancellationReversalPending', 'CancelledByReversal'].includes(body.status as string))) return null
  return { ...body, currentPostedJournalId: currentJournal, reversalProposal: reversal } as unknown as InventoryCostProposal
}

async function read<T>(path: string, company: string, decode: (body: unknown) => T | null, signal?: AbortSignal): Promise<ViewOutcome<T>> {
  try {
    const response = await fetch(path, { headers: { ...devHeaders(), 'X-Company-Id': company }, signal })
    const body: unknown = await response.json().catch(() => null)
    if (response.status !== 200) return { type: 'http', status: response.status, body }
    const data = decode(body); return data === null ? { type: 'malformed' } : { type: 'ok-single', data }
  } catch { return signal?.aborted ? { type: 'fence-stale' } : navigator.onLine ? { type: 'network-error' } : { type: 'offline' } }
}
export const fetchInventoryCosts = (company: string, kind: InventoryCostKind, signal?: AbortSignal) => read<readonly InventoryCostProposal[]>(`${base}/${kind}`, company, body => {
  if (!rec(body) || !guid(body.companyId) || !same(body.companyId, company) || !Array.isArray(body.items)) return null
  const values = body.items.map(v => decodeInventoryCost(v, company, kind))
  return values.some(v => v === null) || new Set(values.map(v => v?.proposalId.toLowerCase())).size !== values.length ? null : values as InventoryCostProposal[]
}, signal)
export const fetchInventoryCostOptions = (company: string, kind: InventoryCostKind, signal?: AbortSignal) => read<InventoryCostOptions>(`${base}/${kind}/options`, company, body => {
  if (!rec(body) || !guid(body.companyId) || !same(body.companyId, company) || !Array.isArray(body.accounts) || !body.accounts.every(account)
    || new Set(body.accounts.map(v => v.accountId.toLowerCase())).size !== body.accounts.length || !Array.isArray(body.openPeriods)
    || body.openPeriods.some(v => !rec(v) || !guid(v.periodId) || !date(v.startsOn) || !date(v.endsOn) || v.startsOn > v.endsOn)) return null
  return body as unknown as InventoryCostOptions
}, signal)
const definitive = new Set(['idempotency_conflict', 'accounting_version_conflict', 'accounting_concurrent_change',
  'inventory_cost_preparation_stale', 'inventory_cost_source_conflict', 'inventory_cost_posting_conflict', 'inventory_cost_not_preparable',
  'inventory_cost_request_invalid', 'inventory_cost_open_period_required', 'inventory_cost_account_plan_missing', 'inventory_cost_account_invalid',
  'inventory_cost_classification_missing', 'inventory_cost_mapping_missing', 'inventory_cost_amount_not_representable',
  'inventory_cost_amount_unaccepted', 'inventory_cost_posting_refused', 'inventory_cost_source_cancelled', 'inventory_cost_reversal_not_proposable'])
export async function sendInventoryCost(company: string, attempt: InventoryCostAttempt): Promise<InventoryCostOutcome> {
  const path = attempt.operation === 'reverse' ? `${base}/${attempt.id}/cancellation-reversal-proposals`
    : `${base}/${attempt.kind}/${attempt.id}/${attempt.operation === 'prepare' ? 'preparation' : 'approval'}`
  try {
    const response = await fetch(path, { method: 'POST', headers: { ...devHeaders(), 'X-Company-Id': company,
      'Content-Type': 'application/json', 'Idempotency-Key': attempt.key }, body: JSON.stringify(attempt.request) })
    const body: unknown = await response.json().catch(() => null)
    if (response.status === 200) {
      const value = decodeInventoryCost(body, company, attempt.kind)
      if (value === null || !same(value.proposalId, attempt.id)) return { type: 'unknown' }
      const valid = attempt.operation === 'prepare' ? value.status === 'Prepared' && value.version === attempt.request.expectedVersion + 1
        && value.preparation !== null && same(value.preparation.preparedBy, attempt.actor) && same(value.preparation.periodId, attempt.request.periodId)
        && value.preparation.accountingDate === attempt.request.accountingDate && value.preparation.transactionDate === attempt.request.transactionDate
        && same(value.preparation.counterAccount.accountId, attempt.request.counterAccountId) && value.preparation.reason === attempt.request.reason
        : attempt.operation === 'approve' ? value.status === 'Posted' && value.version === attempt.request.expectedVersion + 1 && value.approvedBy !== null && same(value.approvedBy, attempt.actor)
          : value.status === 'CancellationReversalPending' && value.version === attempt.request.expectedVersion && value.reversalProposal !== null
            && same(value.reversalProposal.proposalId, attempt.request.proposalId) && same(value.reversalProposal.proposedBy, attempt.actor)
            && same(value.reversalProposal.periodId, attempt.request.periodId) && value.reversalProposal.accountingDate === attempt.request.accountingDate
            && value.reversalProposal.transactionDate === attempt.request.transactionDate && value.reversalProposal.reason === attempt.request.reason
      return valid ? { type: 'applied', value } : { type: 'unknown' }
    }
    const code = rec(body) && typeof body.code === 'string' ? body.code : null
    return [400, 401, 403, 404, 415].includes(response.status) || [409, 422].includes(response.status) && code !== null && definitive.has(code)
      ? { type: 'refused', status: response.status, code } : { type: 'unknown' }
  } catch { return { type: 'unknown' } }
}
