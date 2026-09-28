/**
 * AT-025 — the operations work list's wire contract ("Satış ve satın alma belgeleri operasyon listelerinde karışmaz").
 *
 *   GET /api/v1/logistics/operations-work/sales?pageSize&cursor     → LogisticsSalesWorkPage    (section "Sales")
 *   GET /api/v1/logistics/operations-work/purchase?pageSize&cursor  → LogisticsPurchaseWorkPage (section "Purchase")
 *
 * Two routes, two grants, two wire types — never one mixed list with a filter. Each decoder takes EXACTLY its own
 * members and refuses a body carrying any other, and a page must name its own section: a purchase row answered to the
 * sales read (or the reverse) is `malformed`, never shown in the wrong list. No price, discount, currency or payment
 * member exists in either type (Sinan §4, #66 5738113623); a body carrying one is refused.
 */

import { LIMITS, isDateOnly, isPositiveQuantityText, isQuantityText, isReferenceId } from './contract'

export const OPERATIONS_WORK_PATH = '/logistics/operations-work'
export const SALES_WORK_PATH = `${OPERATIONS_WORK_PATH}/sales`
export const PURCHASE_WORK_PATH = `${OPERATIONS_WORK_PATH}/purchase`

export const OPERATIONS_SECTION = { sales: 'Sales', purchase: 'Purchase' } as const
export type OperationsSection = keyof typeof OPERATIONS_SECTION

export interface SalesWorkRow {
  readonly orderId: string
  readonly documentNo: string
  readonly lineId: string
  readonly lineNo: number
  readonly customerPartyId: string
  /** The Cari owner's display name, or a labelled `ref. …` reference when the owner does not answer; never empty. */
  readonly customerName: string
  readonly productId: string
  readonly productCode: string
  readonly productName: string
  readonly unitId: string
  readonly unitCode: string
  /** The Sales owner's exact ordered quantity text. */
  readonly ordered: string
  /** Logistics's own recorded totals for this order line (zero when none). */
  readonly instructed: string
  readonly loaded: string
  readonly shipped: string
  readonly delivered: string
  readonly requestedDeliveryDate: string | null
}

export interface PurchaseWorkRow {
  readonly orderId: string
  readonly orderEffectiveOn: string
  readonly lineId: string
  readonly lineNo: number
  readonly kind: string
  readonly supplierPartyId: string
  /** The Cari owner's display name, or a labelled `ref. …` reference; never empty. */
  readonly supplierName: string
  readonly productId: string
  readonly unitId: string
  /** The Purchasing owner's exact texts; remaining is above zero (that is why the line is here). */
  readonly committed: string
  readonly accepted: string
  readonly remaining: string
}

export interface WorkPage<T> {
  readonly items: readonly T[]
  /** null is the end of the collection. Opaque; the client never builds one. */
  readonly nextCursor: string | null
}

export const SALES_WORK_KEYS = [
  'orderId', 'documentNo', 'lineId', 'lineNo', 'customerPartyId', 'customerName', 'productId', 'productCode', 'productName', 'unitId',
  'unitCode', 'ordered', 'instructed', 'loaded', 'shipped', 'delivered', 'requestedDeliveryDate',
] as const

export const PURCHASE_WORK_KEYS = [
  'orderId', 'orderEffectiveOn', 'lineId', 'lineNo', 'kind', 'supplierPartyId', 'supplierName', 'productId', 'unitId', 'committed',
  'accepted', 'remaining',
] as const

type Obj = Record<string, unknown>

function exact(value: unknown, keys: readonly string[]): Obj | null {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return null
  const obj = value as Obj
  const own = Object.keys(obj)
  if (own.length !== keys.length) return null
  for (const key of keys) if (!Object.hasOwn(obj, key)) return null
  return obj
}

const isText = (v: unknown, max: number): v is string => typeof v === 'string' && v.trim().length > 0 && v.length <= max
const isLineNo = (v: unknown): v is number => typeof v === 'number' && Number.isInteger(v) && v >= 1 && v <= LIMITS.lines

export function decodeSalesWorkRow(value: unknown): SalesWorkRow | null {
  const o = exact(value, SALES_WORK_KEYS)
  if (
    o === null ||
    !isReferenceId(o.orderId) ||
    !isText(o.documentNo, 200) ||
    !isReferenceId(o.lineId) ||
    !isLineNo(o.lineNo) ||
    !isReferenceId(o.customerPartyId) ||
    !isText(o.customerName, 400) ||
    !isReferenceId(o.productId) ||
    !isText(o.productCode, 200) ||
    !isText(o.productName, 500) ||
    !isReferenceId(o.unitId) ||
    !isText(o.unitCode, 64) ||
    !isPositiveQuantityText(o.ordered) ||
    !isQuantityText(o.instructed) ||
    !isQuantityText(o.loaded) ||
    !isQuantityText(o.shipped) ||
    !isQuantityText(o.delivered) ||
    !(o.requestedDeliveryDate === null || isDateOnly(o.requestedDeliveryDate))
  ) {
    return null
  }
  return {
    orderId: o.orderId,
    documentNo: o.documentNo,
    lineId: o.lineId,
    lineNo: o.lineNo,
    customerPartyId: o.customerPartyId,
    customerName: o.customerName,
    productId: o.productId,
    productCode: o.productCode,
    productName: o.productName,
    unitId: o.unitId,
    unitCode: o.unitCode,
    ordered: o.ordered,
    instructed: o.instructed,
    loaded: o.loaded,
    shipped: o.shipped,
    delivered: o.delivered,
    requestedDeliveryDate: o.requestedDeliveryDate,
  }
}

export function decodePurchaseWorkRow(value: unknown): PurchaseWorkRow | null {
  const o = exact(value, PURCHASE_WORK_KEYS)
  if (
    o === null ||
    !isReferenceId(o.orderId) ||
    !isDateOnly(o.orderEffectiveOn) ||
    !isReferenceId(o.lineId) ||
    !isLineNo(o.lineNo) ||
    !isText(o.kind, 32) ||
    !isReferenceId(o.supplierPartyId) ||
    !isText(o.supplierName, 400) ||
    !isReferenceId(o.productId) ||
    !isReferenceId(o.unitId) ||
    !isQuantityText(o.committed) ||
    !isQuantityText(o.accepted) ||
    !isPositiveQuantityText(o.remaining)
  ) {
    return null
  }
  return {
    orderId: o.orderId,
    orderEffectiveOn: o.orderEffectiveOn,
    lineId: o.lineId,
    lineNo: o.lineNo,
    kind: o.kind,
    supplierPartyId: o.supplierPartyId,
    supplierName: o.supplierName,
    productId: o.productId,
    unitId: o.unitId,
    committed: o.committed,
    accepted: o.accepted,
    remaining: o.remaining,
  }
}

function decodePage<T extends { readonly lineId: string }>(
  value: unknown,
  section: (typeof OPERATIONS_SECTION)[OperationsSection],
  decodeRow: (row: unknown) => T | null,
): WorkPage<T> | null {
  const o = exact(value, ['section', 'items', 'nextCursor'])
  if (
    o === null ||
    o.section !== section ||
    !Array.isArray(o.items) ||
    !(o.nextCursor === null || isText(o.nextCursor, LIMITS.cursor))
  ) {
    return null
  }
  const items: T[] = []
  const ids = new Set<string>()
  for (const entry of o.items) {
    const item = decodeRow(entry)
    if (item === null || ids.has(item.lineId)) return null
    ids.add(item.lineId)
    items.push(item)
  }
  return { items, nextCursor: o.nextCursor as string | null }
}

/** A sales page: section "Sales" and nothing but sales rows. */
export const decodeSalesWorkPage = (value: unknown): WorkPage<SalesWorkRow> | null =>
  decodePage(value, OPERATIONS_SECTION.sales, decodeSalesWorkRow)

/** A purchase page: section "Purchase" and nothing but purchase rows. */
export const decodePurchaseWorkPage = (value: unknown): WorkPage<PurchaseWorkRow> | null =>
  decodePage(value, OPERATIONS_SECTION.purchase, decodePurchaseWorkRow)
