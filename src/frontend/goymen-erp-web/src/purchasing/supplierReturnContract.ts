import { isPurchasingGuid, isPurchasingInstant, isPurchasingVersion } from './contract'
import { decodeSupplierReturnCostAllocation, decodeSupplierReturnSourceLabels, type SupplierReturnCostAllocation, type SupplierReturnSourceLabel } from './supplierReturnSourceContract'
import { decodeSupplierReturnLineageParts, type SupplierReturnLineageMode, type SupplierReturnLineagePart } from './supplierReturnLineageContract'

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
  readonly unitCostTryPerKg: string | null
  readonly priceDisclosure: 'Visible' | 'Withheld'
  readonly replacementExpected: boolean
  readonly reason: string
  readonly occurredAtUtc: string
  readonly requestedBy: string
  readonly requestedAtUtc: string
  readonly decidedBy: string | null
  readonly decidedAtUtc: string | null
  readonly decisionReason: string | null
  readonly expectedPurchaseOrderVersion?: string
  readonly undoQuantity?: string
  readonly costAllocation?: SupplierReturnCostAllocation
  readonly costAllocationSources?: readonly SupplierReturnSourceLabel[]
  /** AK-8/AK-8b: the label that physically left when it is not the receipt's own label, and how; both present or both absent. */
  readonly sourceLabelId?: string
  readonly lineageMode?: SupplierReturnLineageMode
  /** AK-8b: the returned pieces of a piece-tracking source label and the per-receipt parts; only with a source label. */
  readonly pieceCount?: number
  readonly lineageParts?: readonly SupplierReturnLineagePart[]
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
const LINEAGE_MODES: readonly string[] = ['Lineage', 'Quantity']
const RETURN_FIELDS = ['companyId', 'returnId', 'returnLineId', 'purchaseOrderId', 'purchaseOrderLineId', 'goodsAcceptanceId',
  'inventoryMovementId', 'status', 'version', 'sourceBlock', 'quantityKg', 'costBasis', 'unitCostTryPerKg', 'priceDisclosure', 'replacementExpected',
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
 * return requires the explicit purchase-unit undo quantity and original expected order version (ALS-D91/D92). AK-8/AK-8b: a
 * source label and its mode come together; pieces and per-receipt parts exist only with them and are measured by the parts rule.
 */
export function decodeSupplierReturn(value: unknown, companyId: string, orderId: string): SupplierReturn | null {
  const source = record(value)
  const metadataPresent = source !== null && Object.hasOwn(source, 'expectedPurchaseOrderVersion')
  const allocationPresent = source !== null && Object.hasOwn(source, 'costAllocation')
  const labelsPresent = source !== null && Object.hasOwn(source, 'costAllocationSources')
  const allocation = allocationPresent ? decodeSupplierReturnCostAllocation(source.costAllocation) : null
  const lineagePresent = source !== null && Object.hasOwn(source, 'sourceLabelId')
  const piecesPresent = source !== null && Object.hasOwn(source, 'pieceCount')
  const partsPresent = source !== null && Object.hasOwn(source, 'lineageParts')
  const fields = [...RETURN_FIELDS, ...(metadataPresent ? ['expectedPurchaseOrderVersion', 'undoQuantity'] : []),
    ...(allocationPresent ? ['costAllocation'] : []), ...(labelsPresent ? ['costAllocationSources'] : []),
    ...(lineagePresent ? ['sourceLabelId', 'lineageMode'] : []), ...(piecesPresent ? ['pieceCount'] : []),
    ...(partsPresent ? ['lineageParts'] : [])]
  if (source === null || !exact(source, fields)
    || !isPurchasingGuid(source.companyId) || source.companyId.toLowerCase() !== companyId.toLowerCase()
    || !isPurchasingGuid(source.returnId) || !isPurchasingGuid(source.returnLineId) || !isPurchasingGuid(source.purchaseOrderId)
    || source.purchaseOrderId.toLowerCase() !== orderId.toLowerCase() || !isPurchasingGuid(source.purchaseOrderLineId)
    || !isPurchasingGuid(source.goodsAcceptanceId) || !isPurchasingGuid(source.inventoryMovementId)
    || !oneOf(source.status, STATUSES) || !isPurchasingVersion(source.version) || !oneOf(source.sourceBlock, BLOCKS)
    || !positive(source.quantityKg) || !oneOf(source.costBasis, BASES)
    || !(source.priceDisclosure === 'Withheld' && source.unitCostTryPerKg === null
      || source.priceDisclosure === 'Visible' && positive(source.unitCostTryPerKg))
    || typeof source.replacementExpected !== 'boolean' || source.replacementExpected && !metadataPresent
    || metadataPresent && (!isPurchasingVersion(source.expectedPurchaseOrderVersion) || !positive(source.undoQuantity)
      || !/^(0|[1-9]\d{0,14})(\.[0-9]{0,2}[1-9])?$/.test(source.undoQuantity))
    || !nonBlank(source.reason) || !isPurchasingInstant(source.occurredAtUtc)
    || !nonBlank(source.requestedBy) || !isPurchasingInstant(source.requestedAtUtc)
    || !nullableNonBlank(source.decidedBy) || !(source.decidedAtUtc === null || isPurchasingInstant(source.decidedAtUtc))
    || !nullableNonBlank(source.decisionReason)
    || allocationPresent && (!metadataPresent || allocation === null || !labelsPresent)
    || labelsPresent && (allocation === null || decodeSupplierReturnSourceLabels(source.costAllocationSources, allocation) === null)
    // AK-8/AK-8b: a foreign source label never adjusts the receipt and never carries the receipt-label allocation.
    || lineagePresent && (!isPurchasingGuid(source.sourceLabelId) || !oneOf(source.lineageMode, LINEAGE_MODES) || metadataPresent || allocationPresent)
    || (piecesPresent || partsPresent) && !lineagePresent
    || piecesPresent && !(typeof source.pieceCount === 'number' && Number.isInteger(source.pieceCount) && source.pieceCount >= 1)
    || partsPresent && decodeSupplierReturnLineageParts(source.lineageParts, {
      goodsAcceptanceId: source.goodsAcceptanceId, returnLineId: source.returnLineId, inventoryMovementId: source.inventoryMovementId,
      quantityKg: source.quantityKg, purchaseOrderId: source.purchaseOrderId, purchaseOrderLineId: source.purchaseOrderLineId,
      pieceCount: piecesPresent ? source.pieceCount as number : undefined, priceVisible: source.priceDisclosure === 'Visible',
      lineageMode: source.lineageMode as SupplierReturnLineageMode }) === null) return null
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
