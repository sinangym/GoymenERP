/**
 * S-07 "Sevkiyat / İrsaliyeler" (UI-60) and S-08 "Faturaya Hazır" (UI-61) — the read-only wire contract of API-30
 * (`GET /api/v1/sales/dispatches`) and API-31 (`GET /api/v1/sales/invoice-candidates`), pinned to the backend
 * `SalesDispatchContractsV1.cs` and to their schemas in `contracts/openapi/goymen-erp-v1.json` (./dispatchContract.test.ts).
 *
 * The Sales strict rules apply (./contract.ts, ./poolContract.ts): every member PRESENT (nullable ones as explicit null), no
 * unknown member, quantity text stays text, a state and its facts must agree (an approved note carries its system number and
 * approval time). A decoder returns null for anything it cannot vouch for; the caller fails closed.
 *
 * NOTHING here is money: neither read carries a price, amount, tax or total (the order totals calculator, API-03, is not on
 * main; the screen never computes money). The grouping is the server's (DEC-SAL-006 as SAT-D101 / SAT-D102 read it; the rate of
 * the shipment day, SAT-D90); this module never regroups, sums or filters what the server answered.
 */

import { isDateOnly, isReferenceId } from './wirePrimitives'

/* ── Paths ────────────────────────────────────────────────────────────────── */

export const SALES_DISPATCHES_PATH = '/sales/dispatches'
export const SALES_INVOICE_CANDIDATES_PATH = '/sales/invoice-candidates'

/** `SalesDispatchApiV1.DefaultPageSize` (the Sales list convention, 1–100). */
export const DISPATCH_PAGE_SIZE = 50

/* ── States ───────────────────────────────────────────────────────────────── */

export const DISPATCH_STATE = {
  draft: 'Draft',
  approved: 'Approved',
  cancelled: 'Cancelled',
} as const
export type DispatchState = (typeof DISPATCH_STATE)[keyof typeof DISPATCH_STATE]
const DISPATCH_STATES: readonly string[] = Object.values(DISPATCH_STATE)
const isDispatchState = (v: unknown): v is DispatchState => typeof v === 'string' && DISPATCH_STATES.includes(v)

/** Only `NoSource` exists: no invoice owner records what was invoiced yet (API-32…36 are later S5 slices). */
export type InvoicedState = 'NoSource'
const isInvoicedState = (v: unknown): v is InvoicedState => v === 'NoSource'

/**
 * SAT-D101: the candidate key parts that can still lack a source — the rate value and the invoice type. The e-Fatura/e-Arşiv
 * status is the customer's (SAT-D102), and a missing due date is its own "vade eksik" flag, never one of these.
 */
export const GROUP_KEY_PART = ['ExchangeRate', 'InvoiceType'] as const
export type GroupKeyPart = (typeof GROUP_KEY_PART)[number]
const isGroupKeyPart = (v: unknown): v is GroupKeyPart => typeof v === 'string' && (GROUP_KEY_PART as readonly string[]).includes(v)

/** SAT-D90 / SAT-D101: whether the invoice rate applies (not in TL) and how far it is known. */
export const RATE_STATE = ['NotApplicable', 'Unresolved', 'Resolved'] as const
export type RateState = (typeof RATE_STATE)[number]
const isRateState = (v: unknown): v is RateState => typeof v === 'string' && (RATE_STATE as readonly string[]).includes(v)

/* ── Views ────────────────────────────────────────────────────────────────── */

export interface DispatchCustomer {
  readonly partyId: string
  readonly companyAccountId: string
  readonly code: string
  readonly legalName: string
  readonly tradeName: string | null
}

export interface DispatchLine {
  readonly lineNo: number
  readonly salesOrderId: string
  readonly salesOrderDocumentNo: string
  readonly salesOrderLineId: string
  readonly salesOrderLineNo: number
  readonly shipmentId: string
  readonly shippedAtUtc: string
  readonly productId: string
  readonly productCode: string
  readonly productName: string
  readonly unitId: string
  readonly unitCode: string
  readonly quantity: string
}

export interface DispatchSummary {
  readonly id: string
  readonly companyId: string
  readonly instructionId: string
  readonly state: DispatchState
  readonly systemNumber: string | null
  readonly eDispatchNumber: string | null
  readonly customer: DispatchCustomer
  readonly createdAtUtc: string
  readonly approvedAtUtc: string | null
  readonly cancelledAtUtc: string | null
  readonly invoicing: InvoicedState
  readonly lines: readonly DispatchLine[]
}

export interface DispatchPage {
  readonly items: readonly DispatchSummary[]
  readonly nextCursor: string | null
}

export interface InvoiceGroupRate {
  readonly state: RateState
  /** The Istanbul shipment day while the value is unresolved (the rate is the business day before it). */
  readonly shipmentDate: string | null
  readonly bulletinDate: string | null
  readonly rate: string | null
  readonly reasonCode: string | null
}

export interface InvoiceGroupKey {
  readonly companyId: string
  readonly partyId: string
  readonly companyAccountId: string
  readonly currencyId: string
  readonly exchangeRate: InvoiceGroupRate
  readonly dueDate: string | null
  readonly invoiceType: string | null
}

export interface InvoiceQuantityTotal {
  readonly unitId: string
  readonly unitCode: string
  readonly quantity: string
}

export interface InvoiceCandidateShare {
  readonly dispatchNoteId: string
  readonly dispatchSystemNumber: string
  readonly eDispatchNumber: string | null
  readonly approvedAtUtc: string
  readonly dispatchLineNo: number
  readonly shipmentId: string
  readonly shippedAtUtc: string
  readonly shipmentDate: string
  readonly salesOrderId: string
  readonly salesOrderDocumentNo: string
  readonly salesOrderLineId: string
  readonly salesOrderLineNo: number
  readonly productId: string
  readonly productCode: string
  readonly productName: string
  readonly unitId: string
  readonly unitCode: string
  readonly quantity: string
  readonly invoicing: InvoicedState
}

export interface InvoiceCandidateGroup {
  readonly key: InvoiceGroupKey
  readonly customer: DispatchCustomer
  /** SAT-D101: no due-date source — a "vade eksik" group, never invoiceable. */
  readonly dueDateMissing: boolean
  readonly unresolvedKeyParts: readonly GroupKeyPart[]
  readonly invoiceable: boolean
  readonly dispatchNoteCount: number
  readonly quantityTotals: readonly InvoiceQuantityTotal[]
  readonly shares: readonly InvoiceCandidateShare[]
}

export interface InvoiceCandidates {
  readonly groups: readonly InvoiceCandidateGroup[]
  readonly truncated: boolean
  readonly invoicedState: InvoicedState
}

/* ── Primitives ───────────────────────────────────────────────────────────── */

type Wire = Record<string, unknown>

function exact(value: unknown, keys: readonly string[]): Wire | null {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return null
  const obj = value as Wire
  if (Object.keys(obj).length !== keys.length) return null
  for (const key of keys) if (!Object.hasOwn(obj, key)) return null
  return obj
}

const DECIMAL_RE = /^(0|[1-9]\d*)(\.\d*[1-9])?$/
const INSTANT_RE = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})$/
const TEXT_MAX = 512
const CURSOR_MAX = 8192

const isText = (v: unknown): v is string => typeof v === 'string' && v.trim().length > 0 && v.length <= TEXT_MAX
const isNullableText = (v: unknown): v is string | null => v === null || isText(v)
const isInstant = (v: unknown): v is string => typeof v === 'string' && INSTANT_RE.test(v) && !Number.isNaN(Date.parse(v))
const isPositiveQuantity = (v: unknown): v is string => typeof v === 'string' && v.length <= 64 && DECIMAL_RE.test(v) && /[1-9]/.test(v)
const isPositiveInt = (v: unknown): v is number => typeof v === 'number' && Number.isInteger(v) && v >= 1
const isCursor = (v: unknown): v is string => typeof v === 'string' && v.length > 0 && v.length <= CURSOR_MAX

/* ── Decoders ─────────────────────────────────────────────────────────────── */

const CUSTOMER_KEYS = ['partyId', 'companyAccountId', 'code', 'legalName', 'tradeName'] as const

function decodeCustomer(value: unknown): DispatchCustomer | null {
  const o = exact(value, CUSTOMER_KEYS)
  if (o === null || !isReferenceId(o.partyId) || !isReferenceId(o.companyAccountId) || !isText(o.code) || !isText(o.legalName)
    || !isNullableText(o.tradeName)) return null
  return { partyId: o.partyId, companyAccountId: o.companyAccountId, code: o.code, legalName: o.legalName, tradeName: o.tradeName }
}

const LINE_KEYS = [
  'lineNo', 'salesOrderId', 'salesOrderDocumentNo', 'salesOrderLineId', 'salesOrderLineNo', 'shipmentId', 'shippedAtUtc',
  'productId', 'productCode', 'productName', 'unitId', 'unitCode', 'quantity',
] as const

function decodeLine(value: unknown): DispatchLine | null {
  const o = exact(value, LINE_KEYS)
  if (
    o === null || !isPositiveInt(o.lineNo) || !isReferenceId(o.salesOrderId) || !isText(o.salesOrderDocumentNo)
    || !isReferenceId(o.salesOrderLineId) || !isPositiveInt(o.salesOrderLineNo) || !isReferenceId(o.shipmentId)
    || !isInstant(o.shippedAtUtc) || !isReferenceId(o.productId) || !isText(o.productCode) || !isText(o.productName)
    || !isReferenceId(o.unitId) || !isText(o.unitCode) || !isPositiveQuantity(o.quantity)
  ) return null
  return {
    lineNo: o.lineNo, salesOrderId: o.salesOrderId, salesOrderDocumentNo: o.salesOrderDocumentNo, salesOrderLineId: o.salesOrderLineId,
    salesOrderLineNo: o.salesOrderLineNo, shipmentId: o.shipmentId, shippedAtUtc: o.shippedAtUtc, productId: o.productId,
    productCode: o.productCode, productName: o.productName, unitId: o.unitId, unitCode: o.unitCode, quantity: o.quantity,
  }
}

const SUMMARY_KEYS = [
  'id', 'companyId', 'instructionId', 'state', 'systemNumber', 'eDispatchNumber', 'customer', 'createdAtUtc', 'approvedAtUtc',
  'cancelledAtUtc', 'invoicing', 'lines',
] as const

function decodeSummary(value: unknown): DispatchSummary | null {
  const o = exact(value, SUMMARY_KEYS)
  if (
    o === null || !isReferenceId(o.id) || !isReferenceId(o.companyId) || !isReferenceId(o.instructionId) || !isDispatchState(o.state)
    || !isNullableText(o.systemNumber) || !isNullableText(o.eDispatchNumber) || !isInstant(o.createdAtUtc)
    || !(o.approvedAtUtc === null || isInstant(o.approvedAtUtc)) || !(o.cancelledAtUtc === null || isInstant(o.cancelledAtUtc))
    || !isInvoicedState(o.invoicing) || !Array.isArray(o.lines) || o.lines.length === 0
  ) return null
  // The state and its facts agree: a draft has no number and no approval; an approved note has both; a cancelled one its time.
  if (o.state === DISPATCH_STATE.draft && (o.systemNumber !== null || o.approvedAtUtc !== null || o.cancelledAtUtc !== null)) return null
  if (o.state === DISPATCH_STATE.approved && (o.systemNumber === null || o.approvedAtUtc === null || o.cancelledAtUtc !== null)) return null
  if (o.state === DISPATCH_STATE.cancelled && o.cancelledAtUtc === null) return null
  const customer = decodeCustomer(o.customer)
  if (customer === null) return null
  const lines: DispatchLine[] = []
  const numbers = new Set<number>()
  for (const entry of o.lines) {
    const line = decodeLine(entry)
    if (line === null || numbers.has(line.lineNo)) return null
    numbers.add(line.lineNo)
    lines.push(line)
  }
  return {
    id: o.id, companyId: o.companyId, instructionId: o.instructionId, state: o.state, systemNumber: o.systemNumber,
    eDispatchNumber: o.eDispatchNumber, customer, createdAtUtc: o.createdAtUtc, approvedAtUtc: o.approvedAtUtc,
    cancelledAtUtc: o.cancelledAtUtc, invoicing: o.invoicing, lines,
  }
}

/** `SalesDispatchListResponse`: each note once; `nextCursor` null at the end. */
export function decodeDispatchPage(value: unknown): DispatchPage | null {
  const o = exact(value, ['items', 'nextCursor'])
  if (o === null || !Array.isArray(o.items) || !(o.nextCursor === null || isCursor(o.nextCursor))) return null
  const items: DispatchSummary[] = []
  const ids = new Set<string>()
  for (const entry of o.items) {
    const item = decodeSummary(entry)
    if (item === null || ids.has(item.id)) return null
    ids.add(item.id)
    items.push(item)
  }
  return { items, nextCursor: o.nextCursor }
}

const KEY_KEYS = ['companyId', 'partyId', 'companyAccountId', 'currencyId', 'exchangeRate', 'dueDate', 'invoiceType'] as const
const RATE_KEYS = ['state', 'shipmentDate', 'bulletinDate', 'rate', 'reasonCode'] as const
const isNullableDate = (v: unknown): v is string | null => v === null || isDateOnly(v)

function decodeRate(value: unknown): InvoiceGroupRate | null {
  const o = exact(value, RATE_KEYS)
  if (o === null || !isRateState(o.state) || !isNullableDate(o.shipmentDate) || !isNullableDate(o.bulletinDate)
    || !(o.rate === null || isPositiveQuantity(o.rate)) || !isNullableText(o.reasonCode)) return null
  // The state and its facts agree: TL carries nothing; unresolved the shipment day and a reason; resolved the day and the value.
  const facts = [o.shipmentDate, o.bulletinDate, o.rate, o.reasonCode].map((fact) => fact !== null)
  const expected = { NotApplicable: [false, false, false, false], Unresolved: [true, false, false, true], Resolved: [false, true, true, false] }[o.state]
  if (facts.some((present, index) => present !== expected[index])) return null
  return { state: o.state, shipmentDate: o.shipmentDate, bulletinDate: o.bulletinDate, rate: o.rate, reasonCode: o.reasonCode }
}

function decodeKey(value: unknown): InvoiceGroupKey | null {
  const o = exact(value, KEY_KEYS)
  if (o === null || !isReferenceId(o.companyId) || !isReferenceId(o.partyId) || !isReferenceId(o.companyAccountId)
    || !isReferenceId(o.currencyId) || !isNullableDate(o.dueDate) || !isNullableText(o.invoiceType)) return null
  const exchangeRate = decodeRate(o.exchangeRate)
  if (exchangeRate === null) return null
  return {
    companyId: o.companyId, partyId: o.partyId, companyAccountId: o.companyAccountId, currencyId: o.currencyId, exchangeRate,
    dueDate: o.dueDate, invoiceType: o.invoiceType,
  }
}

const SHARE_KEYS = [
  'dispatchNoteId', 'dispatchSystemNumber', 'eDispatchNumber', 'approvedAtUtc', 'dispatchLineNo', 'shipmentId', 'shippedAtUtc',
  'shipmentDate', 'salesOrderId', 'salesOrderDocumentNo', 'salesOrderLineId', 'salesOrderLineNo', 'productId', 'productCode', 'productName', 'unitId',
  'unitCode', 'quantity', 'invoicing',
] as const

function decodeShare(value: unknown): InvoiceCandidateShare | null {
  const o = exact(value, SHARE_KEYS)
  if (
    o === null || !isReferenceId(o.dispatchNoteId) || !isText(o.dispatchSystemNumber) || !isNullableText(o.eDispatchNumber)
    || !isInstant(o.approvedAtUtc) || !isPositiveInt(o.dispatchLineNo) || !isReferenceId(o.shipmentId) || !isInstant(o.shippedAtUtc)
    || !isDateOnly(o.shipmentDate) || !isReferenceId(o.salesOrderId) || !isText(o.salesOrderDocumentNo)
    || !isReferenceId(o.salesOrderLineId) || !isPositiveInt(o.salesOrderLineNo) || !isReferenceId(o.productId) || !isText(o.productCode) || !isText(o.productName)
    || !isReferenceId(o.unitId) || !isText(o.unitCode) || !isPositiveQuantity(o.quantity) || !isInvoicedState(o.invoicing)
  ) return null
  return {
    dispatchNoteId: o.dispatchNoteId, dispatchSystemNumber: o.dispatchSystemNumber, eDispatchNumber: o.eDispatchNumber,
    approvedAtUtc: o.approvedAtUtc, dispatchLineNo: o.dispatchLineNo, shipmentId: o.shipmentId, shippedAtUtc: o.shippedAtUtc,
    shipmentDate: o.shipmentDate, salesOrderId: o.salesOrderId, salesOrderDocumentNo: o.salesOrderDocumentNo, salesOrderLineId: o.salesOrderLineId,
    salesOrderLineNo: o.salesOrderLineNo, productId: o.productId, productCode: o.productCode, productName: o.productName,
    unitId: o.unitId, unitCode: o.unitCode, quantity: o.quantity, invoicing: o.invoicing,
  }
}

const GROUP_KEYS = [
  'key', 'customer', 'dueDateMissing', 'unresolvedKeyParts', 'invoiceable', 'dispatchNoteCount', 'quantityTotals', 'shares',
] as const

function decodeGroup(value: unknown, companyId: string, seenShipments: Set<string>): InvoiceCandidateGroup | null {
  const o = exact(value, GROUP_KEYS)
  if (o === null || typeof o.dueDateMissing !== 'boolean' || typeof o.invoiceable !== 'boolean' || !isPositiveInt(o.dispatchNoteCount) || !Array.isArray(o.unresolvedKeyParts)
    || !Array.isArray(o.quantityTotals) || o.quantityTotals.length === 0 || !Array.isArray(o.shares) || o.shares.length === 0) return null
  const key = decodeKey(o.key)
  const customer = decodeCustomer(o.customer)
  // A group of another company, or whose customer is not its key's, is a tenant-binding breach, not a group to show.
  if (key === null || customer === null || key.companyId !== companyId || customer.partyId !== key.partyId
    || customer.companyAccountId !== key.companyAccountId) return null
  const parts: GroupKeyPart[] = []
  for (const part of o.unresolvedKeyParts) {
    if (!isGroupKeyPart(part) || parts.includes(part)) return null
    parts.push(part)
  }
  // The flags agree with the key: "vade eksik" exactly when the key has no due date, the rate part unresolved exactly when the
  // rate is, and `invoiceable` only with a due date and no unresolved part.
  if (o.dueDateMissing !== (key.dueDate === null)) return null
  if (parts.includes('ExchangeRate') !== (key.exchangeRate.state === 'Unresolved')) return null
  if (parts.includes('InvoiceType') !== (key.invoiceType === null)) return null
  if (o.invoiceable !== (!o.dueDateMissing && parts.length === 0)) return null
  const totals: InvoiceQuantityTotal[] = []
  for (const entry of o.quantityTotals) {
    const total = exact(entry, ['unitId', 'unitCode', 'quantity'])
    if (total === null || !isReferenceId(total.unitId) || !isText(total.unitCode) || !isPositiveQuantity(total.quantity)
      || totals.some((seen) => seen.unitId === total.unitId)) return null
    totals.push({ unitId: total.unitId, unitCode: total.unitCode, quantity: total.quantity })
  }
  const shares: InvoiceCandidateShare[] = []
  for (const entry of o.shares) {
    const share = decodeShare(entry)
    // AT-055: one shipment is one share, across every group of the answer. While the rate is unresolved, every share of the
    // group is of the key's shipment day (SAT-D101: shipment days never merge then).
    if (share === null || seenShipments.has(share.shipmentId) || !totals.some((total) => total.unitId === share.unitId)
      || (key.exchangeRate.state === 'Unresolved' && share.shipmentDate !== key.exchangeRate.shipmentDate)) return null
    seenShipments.add(share.shipmentId)
    shares.push(share)
  }
  return {
    key, customer, dueDateMissing: o.dueDateMissing, unresolvedKeyParts: parts, invoiceable: o.invoiceable,
    dispatchNoteCount: o.dispatchNoteCount, quantityTotals: totals, shares,
  }
}

/** `SalesInvoiceCandidatesResponse`, read under `companyId`: every group of that company, each shipment once. */
export function decodeInvoiceCandidates(value: unknown, companyId: string): InvoiceCandidates | null {
  const o = exact(value, ['groups', 'truncated', 'invoicedState'])
  if (o === null || !Array.isArray(o.groups) || typeof o.truncated !== 'boolean' || !isInvoicedState(o.invoicedState)) return null
  const seen = new Set<string>()
  const groups: InvoiceCandidateGroup[] = []
  for (const entry of o.groups) {
    const group = decodeGroup(entry, companyId, seen)
    if (group === null) return null
    groups.push(group)
  }
  return { groups, truncated: o.truncated, invoicedState: o.invoicedState }
}
