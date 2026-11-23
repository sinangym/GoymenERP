/**
 * F2 (SATIS_RISK_SEVK_KAPISI_VE_FASON_FATURA_PLANI_v1 §F2, SAT-D312, LOJ-D9/D12): the subcontract service ("fason hizmet")
 * invoice draft — `POST /sales/subcontract-service-invoices` and `GET /sales/subcontract-service-invoices/{id}`
 * (`SalesSubcontractServiceInvoiceDetailView`).
 *
 * Strict and fail-closed like ./invoiceFinalizationContract.ts: every object carries exactly the declared members (a nullable
 * member is present as `null`), integers are JSON numbers, every quantity, price, percentage and amount stays the server's TEXT,
 * and a value outside the shapes below makes the whole answer `null` — never a partial object. The net kilograms are the
 * delivery weighing's (Logistics owner); nothing here adds, rounds or compares a kilogram or an amount.
 */
import { decodeAmounts, decodeRate, decodeInstalment, decodeDecision, decodeMatch, decodeFinalization,
  type InvoiceAmounts, type InvoiceRate, type InvoiceInstalment, type InvoiceDecision, type InvoiceMatch, type InvoiceFinalization } from './invoiceFinalizationContract'
import { LIMITS } from './contract'
import { isDateOnly, isReferenceId, isVersionString } from './wirePrimitives'

type Obj = Record<string, unknown>

const record = (value: unknown): Obj | null =>
  typeof value === 'object' && value !== null && !Array.isArray(value) ? (value as Obj) : null

function exact(value: unknown, fields: readonly string[]): Obj | null {
  const object = record(value)
  if (object === null) return null
  const keys = Object.keys(object)
  if (keys.length !== fields.length) return null
  return fields.every((field) => Object.hasOwn(object, field)) ? object : null
}

const INSTANT_RE = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})$/
const MAX_INSTANT_LENGTH = 64
/** An unsigned decimal as the server writes it: digits, one optional fraction. */
const DECIMAL_RE = /^\d+(?:\.\d+)?$/
/** A positive decimal: at least one non-zero digit (a weighing is never billed at zero). */
const POSITIVE_RE = /[1-9]/
const REVISION_RE = /^Rev\.\d+$/
const MAX_TEXT_LENGTH = 512

const isInstant = (value: unknown): value is string =>
  typeof value === 'string' && value.length <= MAX_INSTANT_LENGTH && INSTANT_RE.test(value) && !Number.isNaN(Date.parse(value))
const isDecimal = (value: unknown): value is string =>
  typeof value === 'string' && value.length <= LIMITS.decimalText && DECIMAL_RE.test(value)
const isPositiveDecimal = (value: unknown): value is string => isDecimal(value) && POSITIVE_RE.test(value)
const isText = (value: unknown): value is string =>
  typeof value === 'string' && value.trim().length > 0 && value.length <= MAX_TEXT_LENGTH
const isCount = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value >= 0
const isPositiveCount = (value: unknown): value is number => isCount(value) && value >= 1

/* ── Paths ────────────────────────────────────────────────────────────────── */

export const SUBCONTRACT_SERVICE_INVOICES_PATH = '/sales/subcontract-service-invoices'
export const subcontractServiceInvoicePath = (invoiceId: string) =>
  `${SUBCONTRACT_SERVICE_INVOICES_PATH}/${encodeURIComponent(invoiceId)}`

/* ── Reason codes (409 `invoice_not_ready`) ───────────────────────────────── */

/** `SalesSubcontractServiceInvoiceReasonCodesV1` — beside the order and tax reasons of API-32. */
export const SUBCONTRACT_INVOICE_REASON = {
  notSubcontractOrder: 'not_subcontract_order',
  weighingMissing: 'weighing_missing',
  weighingOrderMismatch: 'weighing_order_mismatch',
  /** Retired by SAT-D376/D402 (the server splits a shared weighing now); kept so an older answer still reads. */
  sharePolicyPending: 'subcontract_share_policy_pending',
  theoreticalKgUnavailable: 'subcontract_theoretical_kg_unavailable',
  shareNotPositive: 'subcontract_share_not_positive',
  currencyPending: 'subcontract_currency_pending',
  lineNotKilogram: 'subcontract_line_not_kilogram',
  weighingAlreadyInvoiced: 'subcontract_weighing_already_invoiced',
} as const

/* ── Detail ───────────────────────────────────────────────────────────────── */

export const SUBCONTRACT_INVOICE_STATES = ['Draft', 'PendingApproval', 'Approved', 'Rejected', 'Cancelled', 'Finalized', 'OfficiallyCancelled'] as const
export type SubcontractInvoiceState = (typeof SUBCONTRACT_INVOICE_STATES)[number]
const isState = (value: unknown): value is SubcontractInvoiceState =>
  typeof value === 'string' && (SUBCONTRACT_INVOICE_STATES as readonly string[]).includes(value)

export interface SubcontractInvoiceLine {
  readonly lineId: string
  readonly lineNo: number
  readonly salesOrderLineId: string
  readonly priceBasisRevision: string
  readonly productId: string
  readonly unitId: string
  /** The billed kilograms: the line's whole-kg share of the weighing's net kg (SAT-D376/D377), as the server wrote it. */
  readonly quantity: string
  readonly netUnitPrice: string
  readonly vatPercent: string
  readonly withholdingShare: string | null
  readonly amounts: InvoiceAmounts
}

export const SUBCONTRACT_INVOICE_LINE_FIELDS = [
  'lineId', 'lineNo', 'salesOrderLineId', 'priceBasisRevision', 'productId', 'unitId', 'quantity', 'netUnitPrice',
  'vatPercent', 'withholdingShare', 'amounts',
] as const

function decodeLine(value: unknown): SubcontractInvoiceLine | null {
  const o = exact(value, SUBCONTRACT_INVOICE_LINE_FIELDS)
  if (
    o === null || !isReferenceId(o.lineId) || !isPositiveCount(o.lineNo) || !isReferenceId(o.salesOrderLineId)
    || typeof o.priceBasisRevision !== 'string' || !REVISION_RE.test(o.priceBasisRevision) || !isReferenceId(o.productId)
    || !isReferenceId(o.unitId) || !isPositiveDecimal(o.quantity) || !isDecimal(o.netUnitPrice) || !isDecimal(o.vatPercent)
    || !(o.withholdingShare === null || isDecimal(o.withholdingShare))
  ) {
    return null
  }
  const amounts = decodeAmounts(o.amounts)
  if (amounts === null) return null
  return {
    lineId: o.lineId,
    lineNo: o.lineNo,
    salesOrderLineId: o.salesOrderLineId,
    priceBasisRevision: o.priceBasisRevision,
    productId: o.productId,
    unitId: o.unitId,
    quantity: o.quantity,
    netUnitPrice: o.netUnitPrice,
    vatPercent: o.vatPercent,
    withholdingShare: o.withholdingShare,
    amounts,
  }
}

export interface SubcontractInvoiceDetail {
  readonly id: string
  readonly documentNo: string
  readonly state: SubcontractInvoiceState
  readonly version: string
  readonly businessDate: string
  readonly partyId: string
  readonly companyAccountId: string
  readonly currencyId: string
  readonly amountScale: number
  readonly salesOrderId: string
  readonly loadingInstructionId: string
  readonly weighingId: string
  readonly weighingRevision: number
  /** The delivery weighing's net kilograms (LOJ-D9: the fee kilograms; scrap is not billed), as the server wrote it. */
  readonly netKg: string
  readonly totals: InvoiceAmounts
  readonly lines: readonly SubcontractInvoiceLine[]
  readonly createdBy: string
  readonly createdAtUtc: string
  readonly isReplay: boolean
  readonly lifecycle: SubcontractLifecycle | null
}

export const SUBCONTRACT_INVOICE_DETAIL_FIELDS = [
  'id', 'documentNo', 'state', 'version', 'businessDate', 'partyId', 'companyAccountId', 'currencyId', 'amountScale',
  'salesOrderId', 'loadingInstructionId', 'weighingId', 'weighingRevision', 'netKg', 'totals', 'lines', 'createdBy',
  'createdAtUtc', 'isReplay', 'lifecycle',
] as const

/** The detail of `invoiceId` (or of the id the body names when `invoiceId` is null — a create answer); otherwise null. */
export function decodeSubcontractInvoiceDetail(value: unknown, invoiceId: string | null): SubcontractInvoiceDetail | null {
  const o = exact(value, SUBCONTRACT_INVOICE_DETAIL_FIELDS)
  if (
    o === null || !isReferenceId(o.id) || (invoiceId !== null && o.id.toLowerCase() !== invoiceId.toLowerCase())
    || !isText(o.documentNo) || !isState(o.state) || !isVersionString(o.version) || !isDateOnly(o.businessDate)
    || !isReferenceId(o.partyId) || !isReferenceId(o.companyAccountId) || !isReferenceId(o.currencyId) || !isCount(o.amountScale)
    || !isReferenceId(o.salesOrderId) || !isReferenceId(o.loadingInstructionId) || !isReferenceId(o.weighingId)
    || !isPositiveCount(o.weighingRevision) || !isPositiveDecimal(o.netKg) || !Array.isArray(o.lines) || o.lines.length === 0
    || !isText(o.createdBy) || !isInstant(o.createdAtUtc) || typeof o.isReplay !== 'boolean'
  ) {
    return null
  }
  const totals = decodeAmounts(o.totals)
  const lifecycle = o.lifecycle === null ? null : decodeSubcontractLifecycle(o.lifecycle)
  if (totals === null || (o.lifecycle !== null && lifecycle === null)
    || (o.state !== 'Draft' && o.state !== 'Cancelled' && (lifecycle === null || lifecycle.instalments.length === 0))
    || ((o.state === 'Finalized' || o.state === 'OfficiallyCancelled') !== (lifecycle?.finalization != null))) return null
  const lines: SubcontractInvoiceLine[] = []
  for (const entry of o.lines) {
    const line = decodeLine(entry)
    if (line === null) return null
    lines.push(line)
  }
  return {
    id: o.id,
    documentNo: o.documentNo,
    state: o.state,
    version: o.version,
    businessDate: o.businessDate,
    partyId: o.partyId,
    companyAccountId: o.companyAccountId,
    currencyId: o.currencyId,
    amountScale: o.amountScale,
    salesOrderId: o.salesOrderId,
    loadingInstructionId: o.loadingInstructionId,
    weighingId: o.weighingId,
    weighingRevision: o.weighingRevision,
    netKg: o.netKg,
    totals,
    lines,
    createdBy: o.createdBy,
    createdAtUtc: o.createdAtUtc,
    isReplay: o.isReplay,
    lifecycle,
  }
}

export interface SubcontractLifecycle {
  readonly decision: InvoiceDecision | null
  readonly instalments: readonly InvoiceInstalment[]
  readonly exchangeRate: InvoiceRate | null
  readonly functionalTotals: InvoiceAmounts | null
  readonly match: InvoiceMatch | null
  readonly finalization: InvoiceFinalization | null
}
export function decodeSubcontractLifecycle(value: unknown): SubcontractLifecycle | null {
  const o = exact(value, ['decision', 'instalments', 'exchangeRate', 'functionalTotals', 'match', 'finalization'])
  if (o === null || !Array.isArray(o.instalments)) return null
  const decision = o.decision === null ? null : decodeDecision(o.decision)
  const rate = o.exchangeRate === null ? null : decodeRate(o.exchangeRate)
  const totals = o.functionalTotals === null ? null : decodeAmounts(o.functionalTotals)
  const match = o.match === null ? null : decodeMatch(o.match)
  const finalization = o.finalization === null ? null : decodeFinalization(o.finalization)
  const instalments = o.instalments.map(decodeInstalment)
  if ((o.decision !== null && decision === null) || (o.exchangeRate !== null && rate === null)
    || (o.functionalTotals !== null && totals === null) || (o.match !== null && match === null)
    || (o.finalization !== null && finalization === null) || instalments.some(item => item === null)
    || ((rate === null) !== (totals === null)) || (rate !== null && (match === null || rate.rateDay !== match.invoiceDate))
    || (match !== null && (match.invoiceDate === null || match.sourceProof === null))) return null
  return { decision, instalments: instalments as InvoiceInstalment[], exchangeRate: rate, functionalTotals: totals, match, finalization }
}

export interface SubcontractInvoiceListItem {
  readonly id: string; readonly documentNo: string; readonly state: SubcontractInvoiceDetail['state']
  readonly version: string; readonly businessDate: string; readonly createdAtUtc: string
}
export interface SubcontractInvoiceList {
  readonly salesOrderId: string; readonly items: readonly SubcontractInvoiceListItem[]; readonly nextCursor: string | null
}
export const subcontractInvoicesOfOrderPath = (orderId: string) => `${SUBCONTRACT_SERVICE_INVOICES_PATH}/by-order/${encodeURIComponent(orderId)}`
export function decodeSubcontractInvoiceList(value: unknown, salesOrderId: string): SubcontractInvoiceList | null {
  const o = exact(value, ['salesOrderId', 'items', 'nextCursor'])
  if (o === null || !isReferenceId(o.salesOrderId) || o.salesOrderId.toLowerCase() !== salesOrderId.toLowerCase()
    || !Array.isArray(o.items) || o.items.length > 100
    || (o.nextCursor !== null && (typeof o.nextCursor !== 'string' || o.nextCursor.length === 0 || o.nextCursor.length > 8192))) return null
  const items: SubcontractInvoiceListItem[] = []
  const seen = new Set<string>()
  for (const value of o.items) {
    const item = exact(value, ['id', 'documentNo', 'state', 'version', 'businessDate', 'createdAtUtc'])
    if (item === null || !isReferenceId(item.id) || seen.has(item.id.toLowerCase()) || !isText(item.documentNo)
      || !isState(item.state) || !isVersionString(item.version) || !isDateOnly(item.businessDate) || !isInstant(item.createdAtUtc)) return null
    seen.add(item.id.toLowerCase())
    items.push({ id: item.id, documentNo: item.documentNo, state: item.state, version: item.version,
      businessDate: item.businessDate, createdAtUtc: item.createdAtUtc })
  }
  return { salesOrderId: o.salesOrderId, items, nextCursor: o.nextCursor }
}
