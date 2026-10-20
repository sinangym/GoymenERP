import type { PurchaseOrderDetail, PurchaseOrderLine } from './contract'

/** Exact server-published capability id used only to fail-closed the receipt control mount. */
export const PURCHASE_GOODS_RECEIPT_CAPABILITY = 'purchasing.goods-receipts.record'

export interface GoodsReceiptRequest {
  readonly expectedPurchaseOrderVersion: string
  readonly expectedProductVersion: string
  readonly acceptanceId: string
  readonly acceptedQuantity: string
  readonly measuredQuantityKg: string
  readonly locationId: string
  readonly lotId: string
  readonly labelId: string
  readonly labelCode: string
  readonly heatNumber: string | null
  readonly coilNumber: string | null
  readonly movementId: string
  readonly occurredAtUtc: string
}

export interface GoodsReceipt {
  readonly companyId: string
  readonly acceptanceId: string
  readonly purchaseOrderId: string
  readonly purchaseOrderLineId: string
  readonly purchaseOrderVersion: string
  readonly productId: string
  readonly productVersion: string
  readonly unitId: string
  readonly acceptedQuantity: string
  readonly remainingQuantity: string
  readonly lotId: string
  readonly labelId: string
  readonly labelCode: string
  readonly labelVersion: string
  readonly movementId: string
  readonly locationId: string
  readonly measuredQuantityKg: string
  /** Original receipt state, not today's availability. Historical receipts must not be relabeled. */
  readonly initialBlock: 'Free' | 'Blocked'
  readonly occurredAtUtc: string
  /**
   * P05.1D (`PurchaseGoodsReceiptViewV1.stockItemQuantity`, backend #774 6f6968d6): the stock quantity Inventory froze
   * for this receipt. The key is REQUIRED on the wire and every decoded receipt carries it: `null` is a legacy receipt
   * recorded before the snapshot existed (never zero, never today's settings); an object is the validated snapshot.
   * It is optional only in this static type so earlier hand-written fixtures still compile; a receipt without the key
   * is never produced by the decoders below.
   */
  readonly stockItemQuantity?: GoodsReceiptStockQuantity | null
}

/** `PurchaseGoodsReceiptQuantityValueV2`: a stored quantity with the unit profile it was recorded under. */
export interface GoodsReceiptQuantityValue {
  /** Canonical decimal token, compared as text — never through Number. */
  readonly value: string
  readonly unitId: string
  readonly profileId: string
  /** Version of the unit profile at the time of the receipt (stored, not reread). */
  readonly profileVersion: string
  readonly dimension: typeof STOCK_QUANTITY_DIMENSION
  readonly scale: number
}

/** `PurchaseGoodsReceiptQuantityV2`. In this slice there is no conversion, so `alternateQuantity` is always null. */
export interface GoodsReceiptStockQuantity {
  readonly companyId: string
  readonly subject: { readonly kind: typeof STOCK_QUANTITY_SUBJECT_KIND; readonly id: string }
  readonly source: { readonly kind: typeof STOCK_QUANTITY_SOURCE_KIND; readonly id: string; readonly version: string }
  readonly baseQuantity: GoodsReceiptQuantityValue
  readonly alternateQuantity: null
}

/** `PurchaseGoodsReceiptApiV1` constants of #774. */
export const STOCK_QUANTITY_SUBJECT_KIND = 'Product'
export const STOCK_QUANTITY_SOURCE_KIND = 'purchasing.goods-acceptance'
export const STOCK_QUANTITY_DIMENSION = 'Mass'
export const STOCK_QUANTITY_MAX_SCALE = 3

export interface GoodsReceiptPage { readonly items: readonly GoodsReceipt[]; readonly searchWindowFull: boolean }
export interface ReceiptLocation { readonly locationId: string; readonly locationCode: string; readonly locationName: string; readonly siteName: string }
export interface GoodsReceiptOptions {
  readonly source: { readonly companyId: string; readonly purchaseOrderId: string; readonly purchaseOrderLineId: string;
    readonly purchaseOrderVersion: string; readonly productId: string; readonly productVersion: string; readonly unitId: string; readonly effectiveOn: string }
  readonly locations: readonly ReceiptLocation[]
  readonly searchWindowFull: boolean
}
export interface GoodsReceiptResponse { readonly receipt: GoodsReceipt; readonly isReplay: boolean; readonly originalCorrelationId: string }

const record = (value: unknown): Record<string, unknown> | null => typeof value === 'object' && value !== null && !Array.isArray(value)
  ? value as Record<string, unknown> : null
const text = (value: unknown): value is string => typeof value === 'string' && value.trim() === value && value.length > 0
const guid = (value: unknown): value is string => text(value) && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value)
  && value !== '00000000-0000-0000-0000-000000000000'
const version = (value: unknown): value is string => text(value) && /^[1-9][0-9]{0,18}$/.test(value) && BigInt(value) <= 9223372036854775807n
const quantity = (value: unknown): value is string => text(value) && /^(?:0|[1-9][0-9]{0,14})(?:\.[0-9]{0,2}[1-9])?$/.test(value)

export function receiptQuantityInput(value: string): string | null {
  const normalized = value.trim().replace(',', '.')
  if (!/^[0-9]+(?:\.[0-9]{1,3})?$/.test(normalized)) return null
  const [whole, fraction = ''] = normalized.split('.')
  const integer = whole.replace(/^0+(?=\d)/, '')
  const decimal = fraction.replace(/0+$/, '')
  const result = `${integer}${decimal === '' ? '' : `.${decimal}`}`
  return quantity(result) && result !== '0' ? result : null
}

const exactKeys = (value: Record<string, unknown>, keys: readonly string[]) => {
  const own = Object.keys(value)
  return own.length === keys.length && keys.every((key) => Object.prototype.hasOwnProperty.call(value, key))
}

const RECEIPT_KEYS = ['companyId', 'acceptanceId', 'purchaseOrderId', 'purchaseOrderLineId', 'purchaseOrderVersion', 'productId',
  'productVersion', 'unitId', 'acceptedQuantity', 'remainingQuantity', 'lotId', 'labelId', 'labelCode', 'labelVersion', 'movementId',
  'locationId', 'measuredQuantityKg', 'initialBlock', 'occurredAtUtc', 'stockItemQuantity'] as const

/** `scale` is int32 on the wire (OpenAPI admits a canonical integer string too); only 0..3 is a kilogram scale. */
function scaleOf(value: unknown): number | null {
  if (typeof value === 'number') return Number.isInteger(value) && value >= 0 && value <= STOCK_QUANTITY_MAX_SCALE ? value : null
  if (typeof value === 'string' && /^(?:0|[1-9][0-9]*)$/.test(value)) return scaleOf(Number(value))
  return null
}

/**
 * The stored snapshot, accepted only if it describes THIS receipt: same company, the receipt's product, this goods
 * acceptance at the receipt's post-receipt order version, and exactly the measured kilograms (text equality). The
 * snapshot is shown as stored; nothing is reread from Product or Settings and nothing is converted.
 */
function decodeStockQuantity(value: unknown, receipt: Record<string, unknown>): GoodsReceiptStockQuantity | null {
  const item = record(value)
  const subject = record(item?.subject)
  const source = record(item?.source)
  const base = record(item?.baseQuantity)
  if (item === null || subject === null || source === null || base === null
    || !exactKeys(item, ['companyId', 'subject', 'source', 'baseQuantity', 'alternateQuantity'])
    || !exactKeys(subject, ['kind', 'id']) || !exactKeys(source, ['kind', 'id', 'version'])
    || !exactKeys(base, ['value', 'unitId', 'profileId', 'profileVersion', 'dimension', 'scale'])) return null
  const scale = scaleOf(base.scale)
  if (!guid(item.companyId) || item.companyId !== receipt.companyId
    || subject.kind !== STOCK_QUANTITY_SUBJECT_KIND || !guid(subject.id) || subject.id !== receipt.productId
    || source.kind !== STOCK_QUANTITY_SOURCE_KIND || !guid(source.id) || source.id !== receipt.acceptanceId
    || !version(source.version) || source.version !== receipt.purchaseOrderVersion
    || !quantity(base.value) || base.value !== receipt.measuredQuantityKg
    || !guid(base.unitId) || !guid(base.profileId) || !version(base.profileVersion)
    || base.dimension !== STOCK_QUANTITY_DIMENSION || scale === null
    // No conversion in this slice: an alternate quantity is not something this screen can honestly show.
    || item.alternateQuantity !== null) return null
  return {
    companyId: item.companyId,
    subject: { kind: STOCK_QUANTITY_SUBJECT_KIND, id: subject.id },
    source: { kind: STOCK_QUANTITY_SOURCE_KIND, id: source.id, version: source.version },
    baseQuantity: { value: base.value, unitId: base.unitId, profileId: base.profileId, profileVersion: base.profileVersion,
      dimension: STOCK_QUANTITY_DIMENSION, scale },
    alternateQuantity: null,
  }
}

export function decodeGoodsReceipt(value: unknown, detail: PurchaseOrderDetail): GoodsReceipt | null {
  const item = record(value)
  // The key set is closed and includes `stockItemQuantity`: a receipt without that key is refused, never read as legacy.
  if (item === null || !exactKeys(item, RECEIPT_KEYS) || item.companyId !== detail.order.companyId || item.purchaseOrderId !== detail.order.id
    || !guid(item.acceptanceId) || !guid(item.purchaseOrderLineId) || !version(item.purchaseOrderVersion)
    || !guid(item.productId) || !text(item.productVersion) || !guid(item.unitId)
    || !quantity(item.acceptedQuantity) || item.acceptedQuantity === '0' || !quantity(item.remainingQuantity)
    || !guid(item.lotId) || !guid(item.labelId) || !text(item.labelCode) || !version(item.labelVersion)
    || !guid(item.movementId) || !guid(item.locationId) || !quantity(item.measuredQuantityKg) || item.measuredQuantityKg === '0'
    || (item.initialBlock !== 'Free' && item.initialBlock !== 'Blocked')
    || !text(item.occurredAtUtc) || !Number.isFinite(Date.parse(item.occurredAtUtc))) return null
  const line = detail.lines.find((candidate) => candidate.lineId === item.purchaseOrderLineId)
  if (line === undefined || item.productId !== line.productId || item.unitId !== line.unitId) return null
  // Explicit null = legacy receipt; anything else must be the exact snapshot of this receipt.
  const stockItemQuantity = item.stockItemQuantity === null ? null : decodeStockQuantity(item.stockItemQuantity, item)
  if (item.stockItemQuantity !== null && stockItemQuantity === null) return null
  return { ...(item as unknown as GoodsReceipt), stockItemQuantity }
}

/**
 * What the screen may say about a decoded receipt's stock quantity: the stored snapshot, a legacy receipt that has none,
 * or — for a value that did not come through the decoder — unreadable. A missing value is never shown as legacy.
 */
export function stockQuantityOf(receipt: GoodsReceipt):
  | { readonly kind: 'snapshot'; readonly quantity: GoodsReceiptStockQuantity }
  | { readonly kind: 'legacy' }
  | { readonly kind: 'unreadable' } {
  if (receipt.stockItemQuantity === null) return { kind: 'legacy' }
  if (receipt.stockItemQuantity === undefined) return { kind: 'unreadable' }
  return { kind: 'snapshot', quantity: receipt.stockItemQuantity }
}

export function decodeGoodsReceiptPage(value: unknown, detail: PurchaseOrderDetail): GoodsReceiptPage | null {
  const body = record(value)
  if (body === null || !Array.isArray(body.items) || body.items.length > 100 || typeof body.searchWindowFull !== 'boolean') return null
  const items: GoodsReceipt[] = []
  for (const raw of body.items) {
    const item = decodeGoodsReceipt(raw, detail)
    if (item === null || items.some((current) => current.acceptanceId === item.acceptanceId)) return null
    items.push(item)
  }
  return { items, searchWindowFull: body.searchWindowFull }
}

/**
 * GET one receipt (`PurchaseGoodsReceiptDetailV1`): exactly `{ receipt }` — not the command answer (`isReplay`,
 * `originalCorrelationId`) and not a page. The receipt must be the one that was asked for, in the order that is open.
 */
export function decodeGoodsReceiptDetail(value: unknown, detail: PurchaseOrderDetail, acceptanceId: string): GoodsReceipt | null {
  const body = record(value)
  if (body === null || Object.keys(body).length !== 1 || !('receipt' in body)) return null
  const receipt = decodeGoodsReceipt(body.receipt, detail)
  return receipt === null || receipt.acceptanceId !== acceptanceId ? null : receipt
}

export function decodeGoodsReceiptOptions(value: unknown, detail: PurchaseOrderDetail, line: PurchaseOrderLine): GoodsReceiptOptions | null {
  const body = record(value)
  const source = record(body?.source)
  if (body === null || source === null || source.companyId !== detail.order.companyId || source.purchaseOrderId !== detail.order.id
    || source.purchaseOrderLineId !== line.lineId || source.purchaseOrderVersion !== detail.order.version
    || source.productId !== line.productId || source.productVersion !== line.productVersion || source.unitId !== line.unitId
    || source.effectiveOn !== detail.order.effectiveOn || !Array.isArray(body.locations) || body.locations.length > 100
    || typeof body.searchWindowFull !== 'boolean') return null
  const locations: ReceiptLocation[] = []
  for (const raw of body.locations) {
    const location = record(raw)
    if (location === null || !guid(location.locationId) || !text(location.locationCode) || !text(location.locationName)
      || !text(location.siteName) || locations.some((item) => item.locationId === location.locationId)) return null
    locations.push(location as unknown as ReceiptLocation)
  }
  return { source: source as unknown as GoodsReceiptOptions['source'], locations, searchWindowFull: body.searchWindowFull }
}

export function decodeGoodsReceiptResponse(value: unknown, detail: PurchaseOrderDetail, lineId: string,
  sent: GoodsReceiptRequest, replayHeader: boolean): GoodsReceiptResponse | null {
  const body = record(value)
  const receipt = body === null ? null : decodeGoodsReceipt(body.receipt, detail)
  if (body === null || receipt === null || body.isReplay !== replayHeader || !text(body.originalCorrelationId)
    || (!replayHeader && receipt.initialBlock !== 'Free')
    || receipt.purchaseOrderLineId !== lineId || receipt.acceptanceId !== sent.acceptanceId
    || receipt.productVersion !== sent.expectedProductVersion || receipt.acceptedQuantity !== sent.acceptedQuantity
    || receipt.measuredQuantityKg !== sent.measuredQuantityKg || receipt.lotId !== sent.lotId || receipt.labelId !== sent.labelId
    || receipt.labelCode !== sent.labelCode || receipt.locationId !== sent.locationId || receipt.movementId !== sent.movementId
    || !version(sent.expectedPurchaseOrderVersion)
    || BigInt(receipt.purchaseOrderVersion) !== BigInt(sent.expectedPurchaseOrderVersion) + 1n
    || Date.parse(receipt.occurredAtUtc) !== Date.parse(sent.occurredAtUtc)) return null
  return { receipt, isReplay: replayHeader, originalCorrelationId: body.originalCorrelationId }
}
