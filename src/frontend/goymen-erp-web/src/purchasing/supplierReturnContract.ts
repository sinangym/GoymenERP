import { isPurchasingGuid, isPurchasingInstant, isPurchasingVersion } from './contract'

/**
 * B-19 (ALC-S5; SAT-D306, SAT-D392, SAT-D393, SAT-D396): GET …/orders/{id}/supplier-returns — the order's returns to supplier.
 * Each return is one received label leaving stock at the original purchase line's unit cost. The unit cost is the invoice's
 * (InvoiceUnitCost) or a manual cost that waits for an exception approval (ManualCostWithException); the screen shows the
 * server's values as received and never computes a cost.
 */
export type SupplierReturnStatus = 'PendingCostException' | 'Recorded' | 'Rejected'
export type SupplierReturnCostBasis = 'InvoiceUnitCost' | 'ManualCostWithException'
export type SupplierReturnSourceBlock = 'Free' | 'Blocked'

export interface SupplierReturn {
  readonly companyId: string
  readonly returnId: string
  readonly returnLineId: string
  readonly purchaseOrderId: string
  readonly purchaseOrderLineId: string
  readonly goodsAcceptanceId: string
  readonly inventoryMovementId: string
  readonly status: SupplierReturnStatus
  readonly version: string
  readonly sourceBlock: SupplierReturnSourceBlock
  readonly quantityKg: string
  readonly costBasis: SupplierReturnCostBasis
  readonly unitCostTryPerKg: string
  readonly replacementExpected: boolean
  readonly reason: string
  readonly occurredAtUtc: string
  readonly requestedBy: string
  readonly requestedAtUtc: string
  readonly decidedBy: string | null
  readonly decidedAtUtc: string | null
  readonly decisionReason: string | null
}

export interface SupplierReturnPage {
  readonly items: readonly SupplierReturn[]
  readonly searchWindowFull: boolean
}

type JsonObject = Record<string, unknown>

const DECIMAL = /^(0|[1-9]\d*)(\.\d*[1-9])?$/
const STATUSES: readonly string[] = ['PendingCostException', 'Recorded', 'Rejected']
const BASES: readonly string[] = ['InvoiceUnitCost', 'ManualCostWithException']
const BLOCKS: readonly string[] = ['Free', 'Blocked']
const RETURN_FIELDS = ['companyId', 'returnId', 'returnLineId', 'purchaseOrderId', 'purchaseOrderLineId', 'goodsAcceptanceId',
  'inventoryMovementId', 'status', 'version', 'sourceBlock', 'quantityKg', 'costBasis', 'unitCostTryPerKg', 'replacementExpected',
  'reason', 'occurredAtUtc', 'requestedBy', 'requestedAtUtc', 'decidedBy', 'decidedAtUtc', 'decisionReason'] as const

function record(value: unknown): JsonObject | null {
  return typeof value === 'object' && value !== null && !Array.isArray(value) ? value as JsonObject : null
}

function exact(value: JsonObject, names: readonly string[]): boolean {
  const actual = Object.keys(value).sort()
  const expected = [...names].sort()
  return actual.length === expected.length && actual.every((name, index) => name === expected[index])
}

function nonBlank(value: unknown): value is string {
  return typeof value === 'string' && value.length > 0 && value.trim() === value
}

function nullableNonBlank(value: unknown): value is string | null {
  return value === null || nonBlank(value)
}

function positive(value: unknown): value is string {
  return typeof value === 'string' && DECIMAL.test(value) && value !== '0'
}

function oneOf(value: unknown, names: readonly string[]): value is string {
  return typeof value === 'string' && names.includes(value)
}

/**
 * Strict decoder for one return, fail closed: exactly its fields; this company and the order asked for; known status, cost basis
 * and source block names; positive canonical kg and unit cost texts. An invoice-cost return is recorded at once and never decided;
 * a manual-cost return is pending with no decision, recorded by a decider, or rejected by a decider with a reason. A replacement
 * return is not offered by this contract version (ALS-D91), so `true` is malformed. Anything else is `null`, never a partial return.
 */
export function decodeSupplierReturn(value: unknown, companyId: string, orderId: string): SupplierReturn | null {
  const source = record(value)
  if (source === null || !exact(source, RETURN_FIELDS)
    || !isPurchasingGuid(source.companyId) || source.companyId.toLowerCase() !== companyId.toLowerCase()
    || !isPurchasingGuid(source.returnId) || !isPurchasingGuid(source.returnLineId) || !isPurchasingGuid(source.purchaseOrderId)
    || source.purchaseOrderId.toLowerCase() !== orderId.toLowerCase() || !isPurchasingGuid(source.purchaseOrderLineId)
    || !isPurchasingGuid(source.goodsAcceptanceId) || !isPurchasingGuid(source.inventoryMovementId)
    || !oneOf(source.status, STATUSES) || !isPurchasingVersion(source.version) || !oneOf(source.sourceBlock, BLOCKS)
    || !positive(source.quantityKg) || !oneOf(source.costBasis, BASES) || !positive(source.unitCostTryPerKg)
    || source.replacementExpected !== false || !nonBlank(source.reason) || !isPurchasingInstant(source.occurredAtUtc)
    || !nonBlank(source.requestedBy) || !isPurchasingInstant(source.requestedAtUtc)
    || !nullableNonBlank(source.decidedBy) || !(source.decidedAtUtc === null || isPurchasingInstant(source.decidedAtUtc))
    || !nullableNonBlank(source.decisionReason)) return null
  const decided = source.decidedBy !== null
  if (decided !== (source.decidedAtUtc !== null)) return null
  if (source.costBasis === 'InvoiceUnitCost') {
    if (source.status !== 'Recorded' || decided || source.decisionReason !== null) return null
  } else if (source.status === 'PendingCostException') {
    if (decided || source.decisionReason !== null) return null
  } else if (source.status === 'Recorded') {
    if (!decided || source.decisionReason !== null) return null
  } else if (!decided || source.decisionReason === null) return null
  return source as unknown as SupplierReturn
}

/** The page decoder: exactly `items` and `searchWindowFull`; every item measured by the return rule; no return twice. */
export function decodeSupplierReturnPage(value: unknown, companyId: string, orderId: string): SupplierReturnPage | null {
  const source = record(value)
  if (source === null || !exact(source, ['items', 'searchWindowFull']) || !Array.isArray(source.items)
    || typeof source.searchWindowFull !== 'boolean') return null
  const seen = new Set<string>()
  for (const item of source.items) {
    const decoded = decodeSupplierReturn(item, companyId, orderId)
    if (decoded === null || seen.has(decoded.returnId.toLowerCase())) return null
    seen.add(decoded.returnId.toLowerCase())
  }
  return source as unknown as SupplierReturnPage
}
