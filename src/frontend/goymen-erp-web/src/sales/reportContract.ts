/**
 * UI-80 "Satış raporları" — the read-only wire contract of API-41 (`GET /api/v1/sales/reports/open-orders`) and API-42
 * (`GET /api/v1/sales/reports/lost-opportunities`, `GET /api/v1/sales/reports/price-variance`), pinned to the backend
 * `SalesReportContractsV1.cs` and to their schemas in `contracts/openapi/goymen-erp-v1.json` (./reportContract.test.ts).
 *
 * The Sales strict rules apply (./dispatchContract.ts): every member PRESENT (nullable ones as explicit null), no unknown member,
 * quantity and amount text stay text, a state and its facts must agree. A decoder returns null for anything it cannot vouch for;
 * the caller fails closed.
 *
 * The screen computes NOTHING: the open quantity (KPI No 124) is the server's approved − shipped per customer, product and unit
 * (quantities of different units are never added); a lost quote's net amount is the server's totals calculator answer in the
 * quote's own currency (never summed across quotes or currencies); the price variance (No 25) has no source until the invoice
 * owner exists and says so (`NoSource`) — never an empty list.
 */

import { isDateOnly, isReferenceId } from './wirePrimitives'

/* ── Paths ────────────────────────────────────────────────────────────────── */

export const SALES_REPORT_OPEN_ORDERS_PATH = '/sales/reports/open-orders'
export const SALES_REPORT_LOST_OPPORTUNITIES_PATH = '/sales/reports/lost-opportunities'
export const SALES_REPORT_PRICE_VARIANCE_PATH = '/sales/reports/price-variance'

/** `SalesReportApiV1.DefaultPageSize` (the Sales list convention, 1–100). */
export const REPORT_PAGE_SIZE = 50

/* ── Views ────────────────────────────────────────────────────────────────── */

export interface ReportCustomer {
  readonly partyId: string
  readonly companyAccountId: string
  readonly code: string
  readonly legalName: string
  readonly tradeName: string | null
}

export interface OpenOrderLine {
  readonly orderId: string
  readonly documentNo: string
  readonly businessDate: string
  readonly requestedDeliveryDate: string | null
  readonly basisRevision: string
  readonly lineId: string
  readonly lineNo: number
  readonly approvedQuantity: string
  readonly shippedQuantity: string
  readonly remainingQuantity: string
  /** SAT-D130: shipped short WITHIN the SAT-D5 tolerance — "Tamamlandı", nothing open although shipped < approved. */
  readonly completedWithinTolerance: boolean
}

/**
 * Why a line is not in its group's sums: `PendingReapproval` (the last approved quantities cannot be taken exactly while a
 * revision awaits approval) or `TolerancePolicyUnavailable` (SAT-D130: shipped short and no readable SAT-D5 tolerance — neither
 * open nor closed).
 */
export type UnmeasuredReason = 'PendingReapproval' | 'TolerancePolicyUnavailable'
const UNMEASURED_REASONS: readonly string[] = ['PendingReapproval', 'TolerancePolicyUnavailable']

export interface OpenOrderUnmeasuredLine {
  readonly orderId: string
  readonly documentNo: string
  readonly lineId: string
  readonly lineNo: number
  readonly reason: UnmeasuredReason
}

export interface OpenOrderGroup {
  /** Client-side row identity: the group key (customer account, party, product, unit). */
  readonly id: string
  readonly customer: ReportCustomer
  readonly productId: string
  readonly productCode: string
  readonly productName: string
  readonly unitId: string
  readonly unitCode: string
  readonly orderCount: number
  readonly approvedQuantity: string
  readonly shippedQuantity: string
  readonly remainingQuantity: string
  readonly complete: boolean
  readonly lines: readonly OpenOrderLine[]
  readonly unmeasured: readonly OpenOrderUnmeasuredLine[]
}

export interface OpenOrderReport {
  readonly measure: 'ApprovedMinusShipped'
  readonly items: readonly OpenOrderGroup[]
  readonly nextCursor: string | null
  /**
   * Client-side only (never on the wire): the server refused the FIRST page as spanning too many orders (422
   * `report_scope_too_large`); the screen asks to narrow the filter instead of showing a cut group.
   */
  readonly tooWide: boolean
}

/** API-41 422 code: one page of groups spans more orders than one read may measure. */
export const REPORT_SCOPE_TOO_LARGE = 'report_scope_too_large'


export interface LostReason {
  readonly reasonId: string
  readonly code: string
  readonly label: string
  readonly profileId: string
  readonly profileVersion: number
}

export type AmountState = 'Computed' | 'Incomplete' | 'AmountOutOfRange'

export interface LostAmount {
  readonly state: AmountState
  readonly currencyId: string
  readonly amountScale: number
  readonly netAmount: string | null
  readonly unpricedLineCount: number
  readonly priceUnitNotOrderUnitLineCount: number
}

export interface LostOpportunity {
  /** The quote id (the row identity). */
  readonly id: string
  readonly documentNo: string
  readonly revision: string
  readonly businessDate: string
  readonly validUntil: string | null
  readonly customer: ReportCustomer
  readonly currencyId: string
  readonly lostAtUtc: string
  readonly lostOn: string
  readonly lostBy: string
  readonly reason: LostReason | null
  readonly explanation: string | null
  readonly lineCount: number
  readonly amount: LostAmount | null
}

export type AmountDisclosure = 'Visible' | 'Withheld'

export interface LostOpportunityReport {
  readonly amountDisclosure: AmountDisclosure
  readonly items: readonly LostOpportunity[]
  readonly nextCursor: string | null
}

export type VarianceDirection = 'Below' | 'Above'

export interface PriceVariance {
  readonly invoiceId: string
  readonly invoiceNo: string
  readonly invoiceDate: string
  readonly customer: ReportCustomer
  readonly orderId: string
  readonly orderDocumentNo: string
  readonly orderRevision: string
  readonly orderLineId: string
  readonly orderLineNo: number
  readonly productId: string
  readonly productCode: string
  readonly productName: string
  readonly unitId: string
  readonly unitCode: string
  readonly invoicedQuantity: string
  readonly currencyId: string
  readonly orderNetUnitPrice: string
  readonly invoiceNetUnitPrice: string
  readonly unitPriceDifference: string
  readonly differenceAmount: string
  readonly direction: VarianceDirection
}

/** No 25: `NoSource` (no invoice owner yet — NOT "no variance") or the invoice owner's page. */
export type PriceVarianceReport =
  | { readonly state: 'NoSource' }
  | { readonly state: 'Available'; readonly items: readonly PriceVariance[]; readonly nextCursor: string | null }

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
const SIGNED_DECIMAL_RE = /^-?(0|[1-9]\d*)(\.\d+)?$/
const INSTANT_RE = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})$/
const REVISION_RE = /^Rev\.(0|[1-9]\d*)$/
const TEXT_MAX = 512
const LONG_TEXT_MAX = 4000
const CURSOR_MAX = 8192
const SCALE_MAX = 28

const isText = (v: unknown): v is string => typeof v === 'string' && v.trim().length > 0 && v.length <= TEXT_MAX
const isNullableText = (v: unknown): v is string | null => v === null || isText(v)
const isLongText = (v: unknown): v is string => typeof v === 'string' && v.trim().length > 0 && v.length <= LONG_TEXT_MAX
const isInstant = (v: unknown): v is string => typeof v === 'string' && INSTANT_RE.test(v) && !Number.isNaN(Date.parse(v))
const isQuantity = (v: unknown): v is string => typeof v === 'string' && v.length <= 64 && DECIMAL_RE.test(v)
const isPositiveQuantity = (v: unknown): v is string => isQuantity(v) && /[1-9]/.test(v)
const isSignedDecimal = (v: unknown): v is string => typeof v === 'string' && v.length <= 64 && SIGNED_DECIMAL_RE.test(v)
const isPositiveInt = (v: unknown): v is number => typeof v === 'number' && Number.isInteger(v) && v >= 1
const isCount = (v: unknown): v is number => typeof v === 'number' && Number.isInteger(v) && v >= 0
const isCursor = (v: unknown): v is string => typeof v === 'string' && v.length > 0 && v.length <= CURSOR_MAX
const isRevision = (v: unknown): v is string => typeof v === 'string' && REVISION_RE.test(v)
const isNullableDate = (v: unknown): v is string | null => v === null || isDateOnly(v)

/** An amount padded to its scale ("1250.00" at 2, "1250" at 0), the server's invariant text. */
function isScaledAmount(v: unknown, scale: number): v is string {
  if (typeof v !== 'string' || v.length > 64) return false
  const pattern = scale === 0 ? /^-?(0|[1-9]\d*)$/ : new RegExp(`^-?(0|[1-9]\\d*)\\.\\d{${scale}}$`)
  return pattern.test(v)
}

/* ── Decoders ─────────────────────────────────────────────────────────────── */

const CUSTOMER_KEYS = ['partyId', 'companyAccountId', 'code', 'legalName', 'tradeName'] as const

function decodeCustomer(value: unknown): ReportCustomer | null {
  const o = exact(value, CUSTOMER_KEYS)
  if (o === null || !isReferenceId(o.partyId) || !isReferenceId(o.companyAccountId) || !isText(o.code) || !isText(o.legalName)
    || !isNullableText(o.tradeName)) return null
  return { partyId: o.partyId, companyAccountId: o.companyAccountId, code: o.code, legalName: o.legalName, tradeName: o.tradeName }
}

const OPEN_LINE_KEYS = [
  'orderId', 'documentNo', 'businessDate', 'requestedDeliveryDate', 'basisRevision', 'lineId', 'lineNo', 'approvedQuantity',
  'shippedQuantity', 'remainingQuantity', 'completedWithinTolerance',
] as const

function decodeOpenLine(value: unknown): OpenOrderLine | null {
  const o = exact(value, OPEN_LINE_KEYS)
  if (
    o === null || !isReferenceId(o.orderId) || !isText(o.documentNo) || !isDateOnly(o.businessDate) || !isNullableDate(o.requestedDeliveryDate)
    || !isRevision(o.basisRevision) || !isReferenceId(o.lineId) || !isPositiveInt(o.lineNo) || !isPositiveQuantity(o.approvedQuantity)
    || !isQuantity(o.shippedQuantity) || !isQuantity(o.remainingQuantity) || typeof o.completedWithinTolerance !== 'boolean'
  ) return null
  // SAT-D130: a line completed within the tolerance has nothing open.
  if (o.completedWithinTolerance && o.remainingQuantity !== '0') return null
  return {
    orderId: o.orderId, documentNo: o.documentNo, businessDate: o.businessDate, requestedDeliveryDate: o.requestedDeliveryDate,
    basisRevision: o.basisRevision, lineId: o.lineId, lineNo: o.lineNo, approvedQuantity: o.approvedQuantity,
    shippedQuantity: o.shippedQuantity, remainingQuantity: o.remainingQuantity, completedWithinTolerance: o.completedWithinTolerance,
  }
}

const UNMEASURED_KEYS = ['orderId', 'documentNo', 'lineId', 'lineNo', 'reason'] as const

function decodeUnmeasured(value: unknown): OpenOrderUnmeasuredLine | null {
  const o = exact(value, UNMEASURED_KEYS)
  if (o === null || !isReferenceId(o.orderId) || !isText(o.documentNo) || !isReferenceId(o.lineId) || !isPositiveInt(o.lineNo)
    || typeof o.reason !== 'string' || !UNMEASURED_REASONS.includes(o.reason)) return null
  return { orderId: o.orderId, documentNo: o.documentNo, lineId: o.lineId, lineNo: o.lineNo, reason: o.reason as UnmeasuredReason }
}

const GROUP_KEYS = [
  'customer', 'productId', 'productCode', 'productName', 'unitId', 'unitCode', 'orderCount', 'approvedQuantity', 'shippedQuantity',
  'remainingQuantity', 'complete', 'lines', 'unmeasured',
] as const

function decodeGroup(value: unknown): OpenOrderGroup | null {
  const o = exact(value, GROUP_KEYS)
  if (
    o === null || !isReferenceId(o.productId) || !isText(o.productCode) || !isText(o.productName) || !isReferenceId(o.unitId)
    || !isText(o.unitCode) || !isCount(o.orderCount) || !isQuantity(o.approvedQuantity) || !isQuantity(o.shippedQuantity)
    || !isQuantity(o.remainingQuantity) || typeof o.complete !== 'boolean' || !Array.isArray(o.lines) || !Array.isArray(o.unmeasured)
    || o.lines.length + o.unmeasured.length === 0
  ) return null
  const customer = decodeCustomer(o.customer)
  if (customer === null) return null
  const lines: OpenOrderLine[] = []
  for (const entry of o.lines) {
    const line = decodeOpenLine(entry)
    if (line === null || lines.some((seen) => seen.lineId === line.lineId)) return null
    lines.push(line)
  }
  const unmeasured: OpenOrderUnmeasuredLine[] = []
  for (const entry of o.unmeasured) {
    const line = decodeUnmeasured(entry)
    if (line === null || unmeasured.some((seen) => seen.lineId === line.lineId) || lines.some((seen) => seen.lineId === line.lineId)) return null
    unmeasured.push(line)
  }
  // The facts agree: complete exactly when nothing is unmeasured; the order count is the measured lines' distinct orders.
  if (o.complete !== (unmeasured.length === 0) || o.orderCount !== new Set(lines.map((line) => line.orderId)).size) return null
  return {
    id: [customer.companyAccountId, customer.partyId, o.productId, o.unitId].join('|'),
    customer, productId: o.productId, productCode: o.productCode, productName: o.productName, unitId: o.unitId, unitCode: o.unitCode,
    orderCount: o.orderCount, approvedQuantity: o.approvedQuantity, shippedQuantity: o.shippedQuantity,
    remainingQuantity: o.remainingQuantity, complete: o.complete, lines, unmeasured,
  }
}

/** `SalesOpenOrderReportResponse`: each group once; `nextCursor` null at the end. */
export function decodeOpenOrderReport(value: unknown): OpenOrderReport | null {
  const o = exact(value, ['measure', 'groups', 'nextCursor'])
  if (o === null || o.measure !== 'ApprovedMinusShipped' || !Array.isArray(o.groups) || !(o.nextCursor === null || isCursor(o.nextCursor))) return null
  const items: OpenOrderGroup[] = []
  for (const entry of o.groups) {
    const group = decodeGroup(entry)
    if (group === null || items.some((seen) => seen.id === group.id)) return null
    items.push(group)
  }
  return { measure: o.measure, items, nextCursor: o.nextCursor, tooWide: false }
}

const REASON_KEYS = ['reasonId', 'code', 'label', 'profileId', 'profileVersion'] as const
const AMOUNT_KEYS = ['state', 'currencyId', 'amountScale', 'netAmount', 'unpricedLineCount', 'priceUnitNotOrderUnitLineCount'] as const
const AMOUNT_STATES: readonly string[] = ['Computed', 'Incomplete', 'AmountOutOfRange']

function decodeReason(value: unknown): LostReason | null {
  const o = exact(value, REASON_KEYS)
  if (o === null || !isReferenceId(o.reasonId) || !isText(o.code) || !isText(o.label) || !isReferenceId(o.profileId)
    || !isPositiveInt(o.profileVersion)) return null
  return { reasonId: o.reasonId, code: o.code, label: o.label, profileId: o.profileId, profileVersion: o.profileVersion }
}

function decodeAmount(value: unknown): LostAmount | null {
  const o = exact(value, AMOUNT_KEYS)
  if (o === null || typeof o.state !== 'string' || !AMOUNT_STATES.includes(o.state) || !isReferenceId(o.currencyId)
    || !isCount(o.amountScale) || o.amountScale > SCALE_MAX || !isCount(o.unpricedLineCount) || !isCount(o.priceUnitNotOrderUnitLineCount)) return null
  // The state and its facts agree: a computed net is present at its scale; anything else carries no net.
  if (o.state === 'Computed' ? !isScaledAmount(o.netAmount, o.amountScale) : o.netAmount !== null) return null
  return {
    state: o.state as AmountState, currencyId: o.currencyId, amountScale: o.amountScale, netAmount: o.netAmount as string | null,
    unpricedLineCount: o.unpricedLineCount, priceUnitNotOrderUnitLineCount: o.priceUnitNotOrderUnitLineCount,
  }
}

const LOST_KEYS = [
  'quoteId', 'documentNo', 'revision', 'businessDate', 'validUntil', 'customer', 'currencyId', 'lostAtUtc', 'lostOn', 'lostBy', 'reason',
  'explanation', 'lineCount', 'amount',
] as const

function decodeLost(value: unknown, disclosure: AmountDisclosure): LostOpportunity | null {
  const o = exact(value, LOST_KEYS)
  if (
    o === null || !isReferenceId(o.quoteId) || !isText(o.documentNo) || !isRevision(o.revision) || !isDateOnly(o.businessDate)
    || !isNullableDate(o.validUntil) || !isReferenceId(o.currencyId) || !isInstant(o.lostAtUtc) || !isDateOnly(o.lostOn)
    || !isText(o.lostBy) || !(o.explanation === null || isLongText(o.explanation)) || !isPositiveInt(o.lineCount)
  ) return null
  const customer = decodeCustomer(o.customer)
  const reason = o.reason === null ? null : decodeReason(o.reason)
  if (customer === null || (o.reason !== null && reason === null)) return null
  // A loss before SAT-D25 carries its free text as the explanation; a loss without either is not a record to show.
  if (reason === null && o.explanation === null) return null
  // Withheld ⇒ no amount at all; visible ⇒ the amount, in the quote's own currency.
  let amount: LostAmount | null = null
  if (disclosure === 'Withheld') {
    if (o.amount !== null) return null
  } else {
    amount = decodeAmount(o.amount)
    if (amount === null || amount.currencyId !== o.currencyId) return null
  }
  return {
    id: o.quoteId, documentNo: o.documentNo, revision: o.revision, businessDate: o.businessDate, validUntil: o.validUntil, customer,
    currencyId: o.currencyId, lostAtUtc: o.lostAtUtc, lostOn: o.lostOn, lostBy: o.lostBy, reason, explanation: o.explanation,
    lineCount: o.lineCount, amount,
  }
}

/** `SalesLostOpportunityReportResponse`: each quote once, the disclosure applied to every amount. */
export function decodeLostOpportunityReport(value: unknown): LostOpportunityReport | null {
  const o = exact(value, ['amountDisclosure', 'items', 'nextCursor'])
  if (o === null || (o.amountDisclosure !== 'Visible' && o.amountDisclosure !== 'Withheld') || !Array.isArray(o.items)
    || !(o.nextCursor === null || isCursor(o.nextCursor))) return null
  const disclosure: AmountDisclosure = o.amountDisclosure
  const items: LostOpportunity[] = []
  for (const entry of o.items) {
    const item = decodeLost(entry, disclosure)
    if (item === null || items.some((seen) => seen.id === item.id)) return null
    items.push(item)
  }
  return { amountDisclosure: disclosure, items, nextCursor: o.nextCursor }
}

const VARIANCE_KEYS = [
  'invoiceId', 'invoiceNo', 'invoiceDate', 'customer', 'orderId', 'orderDocumentNo', 'orderRevision', 'orderLineId', 'orderLineNo',
  'productId', 'productCode', 'productName', 'unitId', 'unitCode', 'invoicedQuantity', 'currencyId', 'orderNetUnitPrice',
  'invoiceNetUnitPrice', 'unitPriceDifference', 'differenceAmount', 'direction',
] as const

function decodeVariance(value: unknown): PriceVariance | null {
  const o = exact(value, VARIANCE_KEYS)
  if (
    o === null || !isReferenceId(o.invoiceId) || !isText(o.invoiceNo) || !isDateOnly(o.invoiceDate) || !isReferenceId(o.orderId)
    || !isText(o.orderDocumentNo) || !isRevision(o.orderRevision) || !isReferenceId(o.orderLineId) || !isPositiveInt(o.orderLineNo)
    || !isReferenceId(o.productId) || !isText(o.productCode) || !isText(o.productName) || !isReferenceId(o.unitId) || !isText(o.unitCode)
    || !isPositiveQuantity(o.invoicedQuantity) || !isReferenceId(o.currencyId) || !isSignedDecimal(o.orderNetUnitPrice)
    || !isSignedDecimal(o.invoiceNetUnitPrice) || !isSignedDecimal(o.unitPriceDifference) || !isSignedDecimal(o.differenceAmount)
    || (o.direction !== 'Below' && o.direction !== 'Above')
  ) return null
  const customer = decodeCustomer(o.customer)
  if (customer === null) return null
  return {
    invoiceId: o.invoiceId, invoiceNo: o.invoiceNo, invoiceDate: o.invoiceDate, customer, orderId: o.orderId, orderDocumentNo: o.orderDocumentNo,
    orderRevision: o.orderRevision, orderLineId: o.orderLineId, orderLineNo: o.orderLineNo, productId: o.productId, productCode: o.productCode,
    productName: o.productName, unitId: o.unitId, unitCode: o.unitCode, invoicedQuantity: o.invoicedQuantity, currencyId: o.currencyId,
    orderNetUnitPrice: o.orderNetUnitPrice, invoiceNetUnitPrice: o.invoiceNetUnitPrice, unitPriceDifference: o.unitPriceDifference,
    differenceAmount: o.differenceAmount, direction: o.direction,
  }
}

/** `SalesPriceVarianceReportResponse`: `NoSource` carries null items and cursor — an empty list there is malformed. */
export function decodePriceVarianceReport(value: unknown): PriceVarianceReport | null {
  const o = exact(value, ['state', 'items', 'nextCursor'])
  if (o === null) return null
  if (o.state === 'NoSource') return o.items === null && o.nextCursor === null ? { state: 'NoSource' } : null
  if (o.state !== 'Available' || !Array.isArray(o.items) || !(o.nextCursor === null || isCursor(o.nextCursor))) return null
  const items: PriceVariance[] = []
  for (const entry of o.items) {
    const item = decodeVariance(entry)
    if (item === null) return null
    items.push(item)
  }
  return { state: 'Available', items, nextCursor: o.nextCursor }
}
