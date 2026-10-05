import type { GoodsReceipt } from './goodsReceiptContract'

export type GoodsReceiptComparisonStatus = 'SameKilogramUnit' | 'Unavailable'

export interface GoodsReceiptQuantityComparison {
  readonly companyId: string
  readonly acceptanceId: string
  readonly purchaseOrderId: string
  readonly purchaseOrderLineId: string
  readonly purchaseOrderVersion: string
  readonly productId: string
  readonly productVersion: string
  readonly unitId: string
  readonly acceptedQuantity: string
  readonly measuredQuantityKg: string
  readonly comparisonStatus: GoodsReceiptComparisonStatus
  readonly acceptedQuantityKg: string | null
  readonly measuredMinusAcceptedKg: string | null
}

const record = (value: unknown): Record<string, unknown> | null => typeof value === 'object' && value !== null && !Array.isArray(value)
  ? value as Record<string, unknown> : null
const text = (value: unknown): value is string => typeof value === 'string' && value.trim() === value && value.length > 0
const guid = (value: unknown): value is string => text(value) && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value)
  && value !== '00000000-0000-0000-0000-000000000000'
const version = (value: unknown): value is string => text(value) && /^[1-9][0-9]{0,18}$/.test(value) && BigInt(value) <= 9223372036854775807n
const quantity = (value: unknown): value is string => text(value) && /^(?:0|[1-9][0-9]{0,14})(?:\.[0-9]{0,2}[1-9])?$/.test(value)
const signedQuantity = (value: unknown): value is string => text(value)
  && /^-?(?:0|[1-9][0-9]{0,14})(?:\.[0-9]{0,2}[1-9])?$/.test(value) && value !== '-0'

function scaled(value: string): bigint {
  const negative = value.startsWith('-')
  const unsigned = negative ? value.slice(1) : value
  const [whole, fraction = ''] = unsigned.split('.')
  const result = BigInt(`${whole}${fraction.padEnd(3, '0')}`)
  return negative ? -result : result
}

/**
 * Strict companion decoder. ApprovedConversion is deliberately rejected until a real, versioned
 * conversion-proof source is published; the frontend never invents a conversion or comparison.
 */
export function decodeGoodsReceiptQuantityComparison(
  value: unknown,
  receipt: GoodsReceipt,
): GoodsReceiptQuantityComparison | null {
  const item = record(value)
  if (item === null || Object.keys(item).length !== 13
    || !guid(item.companyId) || item.companyId !== receipt.companyId
    || !guid(item.acceptanceId) || item.acceptanceId !== receipt.acceptanceId
    || !guid(item.purchaseOrderId) || item.purchaseOrderId !== receipt.purchaseOrderId
    || !guid(item.purchaseOrderLineId) || item.purchaseOrderLineId !== receipt.purchaseOrderLineId
    || !version(item.purchaseOrderVersion) || item.purchaseOrderVersion !== receipt.purchaseOrderVersion
    || !guid(item.productId) || item.productId !== receipt.productId
    || !text(item.productVersion) || item.productVersion !== receipt.productVersion
    || !guid(item.unitId) || item.unitId !== receipt.unitId
    || !quantity(item.acceptedQuantity) || item.acceptedQuantity !== receipt.acceptedQuantity
    || !quantity(item.measuredQuantityKg) || item.measuredQuantityKg !== receipt.measuredQuantityKg) return null

  if (item.comparisonStatus === 'Unavailable') {
    if (item.acceptedQuantityKg !== null || item.measuredMinusAcceptedKg !== null) return null
    return item as unknown as GoodsReceiptQuantityComparison
  }

  if (item.comparisonStatus !== 'SameKilogramUnit'
    || !quantity(item.acceptedQuantityKg) || item.acceptedQuantityKg !== item.acceptedQuantity
    || !signedQuantity(item.measuredMinusAcceptedKg)
    || scaled(item.measuredQuantityKg) - scaled(item.acceptedQuantityKg) !== scaled(item.measuredMinusAcceptedKg)) return null

  return item as unknown as GoodsReceiptQuantityComparison
}