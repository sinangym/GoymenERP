import type { PurchaseOrderDetail, PurchaseOrderLine } from './contract'

/** UI mount identity only, NOT a permission or grant. Central must publish it against the accepted receipt action. */
export const PURCHASE_GOODS_RECEIPT_CAPABILITY = 'purchasing.orders.goods-receipt'

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
  readonly initialBlock: 'Blocked'
  readonly occurredAtUtc: string
}

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

export function decodeGoodsReceipt(value: unknown, detail: PurchaseOrderDetail): GoodsReceipt | null {
  const item = record(value)
  if (item === null || Object.keys(item).length !== 19 || item.companyId !== detail.order.companyId || item.purchaseOrderId !== detail.order.id
    || !guid(item.acceptanceId) || !guid(item.purchaseOrderLineId) || !version(item.purchaseOrderVersion)
    || !guid(item.productId) || !text(item.productVersion) || !guid(item.unitId)
    || !quantity(item.acceptedQuantity) || item.acceptedQuantity === '0' || !quantity(item.remainingQuantity)
    || !guid(item.lotId) || !guid(item.labelId) || !text(item.labelCode) || !version(item.labelVersion)
    || !guid(item.movementId) || !guid(item.locationId) || !quantity(item.measuredQuantityKg) || item.measuredQuantityKg === '0'
    || item.initialBlock !== 'Blocked' || !text(item.occurredAtUtc) || !Number.isFinite(Date.parse(item.occurredAtUtc))) return null
  const line = detail.lines.find((candidate) => candidate.lineId === item.purchaseOrderLineId)
  if (line === undefined || item.productId !== line.productId || item.unitId !== line.unitId) return null
  return item as unknown as GoodsReceipt
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
    || receipt.purchaseOrderLineId !== lineId || receipt.acceptanceId !== sent.acceptanceId
    || receipt.productVersion !== sent.expectedProductVersion || receipt.acceptedQuantity !== sent.acceptedQuantity
    || receipt.measuredQuantityKg !== sent.measuredQuantityKg || receipt.lotId !== sent.lotId || receipt.labelId !== sent.labelId
    || receipt.labelCode !== sent.labelCode || receipt.locationId !== sent.locationId || receipt.movementId !== sent.movementId
    || !version(sent.expectedPurchaseOrderVersion)
    || BigInt(receipt.purchaseOrderVersion) !== BigInt(sent.expectedPurchaseOrderVersion) + 1n
    || Date.parse(receipt.occurredAtUtc) !== Date.parse(sent.occurredAtUtc)) return null
  return { receipt, isReplay: replayHeader, originalCorrelationId: body.originalCorrelationId }
}
