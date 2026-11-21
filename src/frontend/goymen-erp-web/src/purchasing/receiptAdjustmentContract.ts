import { isPurchasingGuid as guid, isPurchasingVersion as version } from './contract'
import { FUTURE_SUPPLY_RELEASE_CODES, type FutureSupplyRelease, type GoodsReceiptRequest } from './goodsReceiptContract'
import { decodeSupplierReturn, type SupplierReturn } from './supplierReturnContract'
import type { SupplierReturnCostAllocation } from './supplierReturnSourceContract'

export interface ReceiptReversalRequest {
  readonly expectedPurchaseOrderVersion: string
  readonly adjustmentId: string
  readonly reversalLineId: string
  readonly inventoryMovementId: string
  readonly undoQuantity: string
  readonly measuredUndoQuantityKg: string
  readonly sourceBlock: 'Free' | 'Blocked'
  readonly reason: string
  readonly occurredAtUtc: string
  readonly replacementReceipt?: GoodsReceiptRequest | null
}
export interface SupplierReturnRequest {
  readonly returnId: string
  readonly returnLineId: string
  readonly goodsAcceptanceId: string
  readonly inventoryMovementId: string
  readonly sourceBlock: 'Free' | 'Blocked'
  readonly quantityKg: string
  readonly manualUnitCostTryPerKg: string | null
  readonly replacementExpected: boolean
  readonly reason: string
  readonly occurredAtUtc: string
  readonly expectedPurchaseOrderVersion: string
  readonly undoQuantity: string
  readonly costAllocation?: SupplierReturnCostAllocation
}
export interface SupplierReturnDecision {
  readonly expectedVersion: string
  readonly reason: string | null
  readonly expectedPurchaseOrderVersion?: string
}
export interface ReceiptReversalConversion {
  readonly conversionId: string
  readonly futureAllocationId: string
  readonly undoneQuantity: string
  readonly undoneKg: string
  readonly reopenedQuantity: string
  readonly overflowReleasedQuantity: string
  readonly unheldQuantity: string
}
export interface ReceiptReversalResponse {
  readonly purchase: { readonly orderVersion: string; readonly netOpenedQuantity: string; readonly allocatableQuantity: string;
    readonly revived: boolean; readonly isReplay: boolean; readonly originalCorrelationId: string }
  readonly stock: { readonly quantityKg: string; readonly conversions: readonly ReceiptReversalConversion[]; readonly isReplay: boolean }
  readonly correctionStock: { readonly labelId: string; readonly labelVersion: string; readonly acceptedQuantity: string;
    readonly quantityKg: string; readonly reconsumedFutureAllocationIds: readonly string[]; readonly futureSupplyReleases: readonly FutureSupplyRelease[] } | null
}
export interface SupplierReturnResponse { readonly return: SupplierReturn; readonly isReplay: boolean; readonly originalCorrelationId: string }
type Obj = Record<string, unknown>
const obj = (v: unknown): Obj | null => typeof v === 'object' && v !== null && !Array.isArray(v) ? v as Obj : null
const exact = (v: Obj, keys: readonly string[]) => Object.keys(v).length === keys.length && keys.every((key) => Object.hasOwn(v, key))
const text = (v: unknown): v is string => typeof v === 'string' && v.length > 0 && v === v.trim()
const qty = (v: unknown): v is string => typeof v === 'string' && /^(0|[1-9]\d{0,14})(\.[0-9]{0,2}[1-9])?$/.test(v)
const ids = (v: unknown): v is readonly string[] => Array.isArray(v) && v.every(guid) && new Set(v).size === v.length

/** Shape and identity checks only. Quantities and share totals are never calculated on this screen. */
export function decodeReceiptReversal(value: unknown, company: string, order: string, acceptance: string,
  request: ReceiptReversalRequest, replayHeader: boolean): ReceiptReversalResponse | null {
  const root = obj(value), purchase = obj(root?.purchase), stock = obj(root?.stock)
  if (root === null || !exact(root, ['purchase', 'stock', 'correctionStock']) || purchase === null || stock === null
    || !exact(purchase, ['companyId', 'adjustmentId', 'orderId', 'acceptanceId', 'lineId', 'generalLineId', 'orderVersion',
      'netOpenedQuantity', 'allocatableQuantity', 'revived', 'isReplay', 'originalCorrelationId', 'newAcceptanceId', 'newAcceptedQuantity'])
    || !exact(stock, ['companyId', 'adjustmentId', 'acceptanceId', 'inventoryMovementId', 'originalReceiptMovementId', 'labelId',
      'labelVersion', 'quantityKg', 'conversions', 'isReplay', 'originalCorrelationId'])
    || purchase.companyId !== company || stock.companyId !== company || purchase.orderId !== order
    || purchase.acceptanceId !== acceptance || stock.acceptanceId !== acceptance
    || purchase.adjustmentId !== request.adjustmentId || stock.adjustmentId !== request.adjustmentId
    || !guid(purchase.lineId) || !guid(purchase.generalLineId) || !version(purchase.orderVersion)
    || !qty(purchase.netOpenedQuantity) || !qty(purchase.allocatableQuantity) || typeof purchase.revived !== 'boolean'
    || purchase.isReplay !== replayHeader || stock.isReplay !== replayHeader
    || !text(purchase.originalCorrelationId) || stock.originalCorrelationId !== purchase.originalCorrelationId
    || stock.inventoryMovementId !== request.inventoryMovementId || !guid(stock.originalReceiptMovementId) || !guid(stock.labelId)
    || !version(stock.labelVersion) || stock.quantityKg !== request.measuredUndoQuantityKg || !Array.isArray(stock.conversions)) return null
  const conversions: ReceiptReversalConversion[] = []
  for (const raw of stock.conversions) {
    const item = obj(raw)
    if (item === null || !exact(item, ['conversionId', 'futureAllocationId', 'undoneQuantity', 'undoneKg', 'reopenedQuantity', 'overflowReleasedQuantity', 'unheldQuantity'])
      || !guid(item.conversionId) || !guid(item.futureAllocationId) || !qty(item.undoneQuantity) || item.undoneQuantity === '0'
      || !qty(item.undoneKg) || item.undoneKg === '0' || !qty(item.reopenedQuantity) || !qty(item.overflowReleasedQuantity)
      || !qty(item.unheldQuantity) || conversions.some((prior) => prior.conversionId === item.conversionId)) return null
    conversions.push(item as unknown as ReceiptReversalConversion)
  }
  const next = request.replacementReceipt
  if (purchase.newAcceptanceId !== (next?.acceptanceId ?? null) || purchase.newAcceptedQuantity !== (next?.acceptedQuantity ?? '0')) return null
  const correction = obj(root.correctionStock)
  if (next == null ? root.correctionStock !== null : correction === null) return null
  if (next != null && correction !== null && (!exact(correction, ['acceptanceId', 'labelId', 'labelVersion', 'movementId',
    'acceptedQuantity', 'quantityKg', 'reconsumedFutureAllocationIds', 'futureSupplyReleases'])
    || correction.acceptanceId !== next.acceptanceId || correction.labelId !== next.labelId || !version(correction.labelVersion)
    || correction.movementId !== next.movementId || correction.acceptedQuantity !== next.acceptedQuantity
    || correction.quantityKg !== next.measuredQuantityKg || !ids(correction.reconsumedFutureAllocationIds)
    || !Array.isArray(correction.futureSupplyReleases))) return null
  if (correction !== null) {
    const released = new Set<string>()
    for (const raw of correction.futureSupplyReleases as unknown[]) {
      const item = obj(raw)
      if (item === null || !exact(item, ['futureAllocationId', 'releasedQuantity', 'failureCode'])
        || !guid(item.futureAllocationId) || !qty(item.releasedQuantity) || item.releasedQuantity === '0'
        || typeof item.failureCode !== 'string' || !(FUTURE_SUPPLY_RELEASE_CODES as readonly string[]).includes(item.failureCode)
        || released.has(item.futureAllocationId)) return null
      released.add(item.futureAllocationId)
    }
  }
  return { purchase: purchase as unknown as ReceiptReversalResponse['purchase'],
    stock: { quantityKg: stock.quantityKg as string, conversions, isReplay: replayHeader },
    correctionStock: correction as unknown as ReceiptReversalResponse['correctionStock'] }
}

export function decodeReturnResponse(value: unknown, company: string, order: string, returnId: string,
  replayHeader: boolean): SupplierReturnResponse | null {
  const root = obj(value)
  if (root === null || !exact(root, ['return', 'isReplay', 'originalCorrelationId']) || root.isReplay !== replayHeader
    || !text(root.originalCorrelationId)) return null
  const item = decodeSupplierReturn(root.return, company, order)
  return item === null || item.returnId !== returnId ? null
    : { return: item, isReplay: replayHeader, originalCorrelationId: root.originalCorrelationId }
}
