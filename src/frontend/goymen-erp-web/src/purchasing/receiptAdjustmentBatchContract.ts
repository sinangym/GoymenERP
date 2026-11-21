import { isPurchasingGuid as guid, isPurchasingVersion as version } from './contract'
import { decodeReceiptReversal, type ReceiptReversalRequest, type ReceiptReversalResponse } from './receiptAdjustmentContract'

export interface ReceiptReversalBatchLine extends Omit<ReceiptReversalRequest, 'expectedPurchaseOrderVersion' | 'reason' | 'occurredAtUtc'> {
  readonly acceptanceId: string
}
export interface ReceiptReversalBatchRequest {
  readonly batchId: string; readonly expectedPurchaseOrderVersion: string; readonly reason: string
  readonly occurredAtUtc: string; readonly lines: readonly ReceiptReversalBatchLine[]
}
export interface ReceiptReversalBatchSupply {
  readonly generalLineId: string; readonly netOpenedQuantity: string; readonly allocatableQuantity: string; readonly revived: boolean
}
export interface ReceiptReversalBatchResponse {
  readonly companyId: string; readonly orderId: string; readonly batchId: string; readonly orderVersion: string
  readonly lines: readonly ReceiptReversalResponse[]; readonly supplies: readonly ReceiptReversalBatchSupply[]
  readonly revived: boolean; readonly isReplay: boolean; readonly originalCorrelationId: string
}
type Obj = Record<string, unknown>
const obj = (v: unknown): Obj | null => typeof v === 'object' && v !== null && !Array.isArray(v) ? v as Obj : null
const exact = (v: Obj, keys: readonly string[]) => Object.keys(v).length === keys.length && keys.every((key) => Object.hasOwn(v, key))
const quantity = (v: unknown): v is string => typeof v === 'string' && /^(0|[1-9]\d{0,14})(\.[0-9]{0,2}[1-9])?$/.test(v)

/** Validate identities and frozen server facts. The browser never adds purchase/kg quantities or derives net revival. */
export function decodeReceiptReversalBatch(value: unknown, company: string, order: string, request: ReceiptReversalBatchRequest,
  replay: boolean): ReceiptReversalBatchResponse | null {
  const root = obj(value)
  if (root === null || !exact(root, ['companyId', 'orderId', 'batchId', 'orderVersion', 'lines', 'supplies', 'revived', 'isReplay', 'originalCorrelationId'])
    || root.companyId !== company || root.orderId !== order || root.batchId !== request.batchId || !version(root.orderVersion)
    || root.isReplay !== replay || typeof root.revived !== 'boolean' || typeof root.originalCorrelationId !== 'string'
    || root.originalCorrelationId.trim() !== root.originalCorrelationId || root.originalCorrelationId.length === 0
    || !Array.isArray(root.lines) || root.lines.length !== request.lines.length || !Array.isArray(root.supplies)) return null
  const lines: ReceiptReversalResponse[] = [], generals = new Set<string>(), accepted = new Set<string>()
  for (const raw of root.lines) {
    const purchase = obj(obj(raw)?.purchase)
    const line = request.lines.find((line) => line.acceptanceId === purchase?.acceptanceId)
    if (purchase === null || line === undefined || accepted.has(line.acceptanceId) || !guid(purchase.generalLineId)) return null
    const decoded = decodeReceiptReversal(raw, company, order, line.acceptanceId, { ...line,
      expectedPurchaseOrderVersion: request.expectedPurchaseOrderVersion, reason: request.reason, occurredAtUtc: request.occurredAtUtc }, replay)
    if (decoded === null || decoded.purchase.orderVersion !== root.orderVersion
      || decoded.purchase.originalCorrelationId !== root.originalCorrelationId) return null
    lines.push(decoded); accepted.add(line.acceptanceId); generals.add(purchase.generalLineId)
  }
  const supplies: ReceiptReversalBatchSupply[] = [], seen = new Set<string>()
  for (const raw of root.supplies) {
    const fact = obj(raw)
    if (fact === null || !exact(fact, ['generalLineId', 'netOpenedQuantity', 'allocatableQuantity', 'revived'])
      || !guid(fact.generalLineId) || !generals.has(fact.generalLineId) || seen.has(fact.generalLineId)
      || !quantity(fact.netOpenedQuantity) || !quantity(fact.allocatableQuantity) || typeof fact.revived !== 'boolean') return null
    seen.add(fact.generalLineId); supplies.push(fact as unknown as ReceiptReversalBatchSupply)
  }
  if (seen.size !== generals.size || root.revived !== supplies.some((fact) => fact.revived)) return null
  return { companyId: company, orderId: order, batchId: request.batchId, orderVersion: root.orderVersion, lines, supplies,
    revived: root.revived, isReplay: replay, originalCorrelationId: root.originalCorrelationId }
}
