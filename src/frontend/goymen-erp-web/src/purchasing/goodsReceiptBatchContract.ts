import { isPurchasingGuid, isPurchasingInstant, isPurchasingVersion } from './contract'

/**
 * F-027 (ALC-S16; KLN-D41, KLN-D43, KLN-D45): GET …/orders/{id}/goods-receipt-batches — the order's bulk goods receipts, each
 * one receipt of one order line with many labels. The total kg, the difference, the variance outcome and the Ayarlar tolerance
 * snapshot are the server's; the screen shows them as received and never recomputes a tolerance or a total.
 */
export type GoodsReceiptBatchStatus = 'PendingApproval' | 'Posted' | 'Rejected'
export type GoodsReceiptVarianceOutcome = 'Exact' | 'WithinTolerance' | 'OutsideTolerance' | 'ToleranceNotSet'
export type GoodsReceiptLabelCodeSource = 'SupplierLabelNumber' | 'Entered' | 'Generated'

export interface GoodsReceiptBatchLabel {
  readonly lineNo: number
  readonly acceptanceId: string
  readonly lotId: string
  readonly labelId: string
  readonly movementId: string
  /** Our label code (KLN-D41): the supplier's number, an entered code or a generated one. */
  readonly labelCode: string
  readonly labelCodeSource: GoodsReceiptLabelCodeSource
  /** The supplier's original label number, kept apart and searchable. */
  readonly supplierLabelNumber: string | null
  readonly measuredQuantityKg: string
  readonly heatNumber: string | null
  readonly coilNumber: string | null
}

export interface GoodsReceiptBatch {
  readonly companyId: string
  readonly batchId: string
  readonly purchaseOrderId: string
  readonly purchaseOrderLineId: string
  readonly status: GoodsReceiptBatchStatus
  readonly version: string
  readonly expectedQuantityKg: string
  readonly receivedQuantityKg: string
  /** Signed: received − expected. */
  readonly differenceKg: string
  readonly varianceOutcome: GoodsReceiptVarianceOutcome
  /** The Ayarlar tolerance snapshot read at submission; null = not set (every difference needs an approver). */
  readonly tolerancePercent: string | null
  readonly varianceAcknowledged: boolean
  readonly locationId: string
  readonly occurredAtUtc: string
  readonly preparedBy: string
  readonly preparedAtUtc: string
  readonly decidedBy: string | null
  readonly decidedAtUtc: string | null
  readonly decisionReason: string | null
  readonly labels: readonly GoodsReceiptBatchLabel[]
}

export interface GoodsReceiptBatchPage {
  readonly items: readonly GoodsReceiptBatch[]
  readonly searchWindowFull: boolean
}

type JsonObject = Record<string, unknown>

const DECIMAL = /^(0|[1-9]\d*)(\.\d*[1-9])?$/
const SIGNED_DECIMAL = /^-?(0|[1-9]\d*)(\.\d*[1-9])?$/
const STATUSES: readonly string[] = ['PendingApproval', 'Posted', 'Rejected']
const OUTCOMES: readonly string[] = ['Exact', 'WithinTolerance', 'OutsideTolerance', 'ToleranceNotSet']
const SOURCES: readonly string[] = ['SupplierLabelNumber', 'Entered', 'Generated']
const BATCH_FIELDS = ['companyId', 'batchId', 'purchaseOrderId', 'purchaseOrderLineId', 'status', 'version', 'expectedQuantityKg',
  'receivedQuantityKg', 'differenceKg', 'varianceOutcome', 'tolerancePercent', 'varianceAcknowledged', 'locationId', 'occurredAtUtc',
  'preparedBy', 'preparedAtUtc', 'decidedBy', 'decidedAtUtc', 'decisionReason', 'labels'] as const
const LABEL_FIELDS = ['lineNo', 'acceptanceId', 'lotId', 'labelId', 'movementId', 'labelCode', 'labelCodeSource', 'supplierLabelNumber',
  'measuredQuantityKg', 'heatNumber', 'coilNumber'] as const

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

function label(value: unknown, expectedLineNo: number): boolean {
  const source = record(value)
  return source !== null && exact(source, LABEL_FIELDS) && source.lineNo === expectedLineNo
    && isPurchasingGuid(source.acceptanceId) && isPurchasingGuid(source.lotId) && isPurchasingGuid(source.labelId)
    && isPurchasingGuid(source.movementId) && nonBlank(source.labelCode) && typeof source.labelCodeSource === 'string'
    && SOURCES.includes(source.labelCodeSource) && nullableNonBlank(source.supplierLabelNumber)
    && (source.labelCodeSource !== 'SupplierLabelNumber' || source.supplierLabelNumber === source.labelCode)
    && positive(source.measuredQuantityKg) && nullableNonBlank(source.heatNumber) && nullableNonBlank(source.coilNumber)
}

/**
 * Strict decoder for one batch, fail closed: exactly its fields; this company and the order asked for; known status, variance
 * outcome and code source names; canonical kg texts; labels numbered 1..n in order. A decided batch carries its decider and time;
 * a rejected one its reason; a pending one neither. A posted batch never shows a variance that needed an approver without one.
 * Anything else is `null` (malformed), never a partial batch.
 */
export function decodeGoodsReceiptBatch(value: unknown, companyId: string, orderId: string): GoodsReceiptBatch | null {
  const source = record(value)
  if (source === null || !exact(source, BATCH_FIELDS)
    || !isPurchasingGuid(source.companyId) || source.companyId.toLowerCase() !== companyId.toLowerCase()
    || !isPurchasingGuid(source.batchId) || !isPurchasingGuid(source.purchaseOrderId)
    || source.purchaseOrderId.toLowerCase() !== orderId.toLowerCase() || !isPurchasingGuid(source.purchaseOrderLineId)
    || typeof source.status !== 'string' || !STATUSES.includes(source.status) || !isPurchasingVersion(source.version)
    || !positive(source.expectedQuantityKg) || typeof source.receivedQuantityKg !== 'string' || !DECIMAL.test(source.receivedQuantityKg)
    || typeof source.differenceKg !== 'string' || !SIGNED_DECIMAL.test(source.differenceKg) || source.differenceKg === '-0'
    || typeof source.varianceOutcome !== 'string' || !OUTCOMES.includes(source.varianceOutcome)
    || !(source.tolerancePercent === null || (typeof source.tolerancePercent === 'string' && DECIMAL.test(source.tolerancePercent)))
    || typeof source.varianceAcknowledged !== 'boolean' || !isPurchasingGuid(source.locationId)
    || !isPurchasingInstant(source.occurredAtUtc) || !nonBlank(source.preparedBy) || !isPurchasingInstant(source.preparedAtUtc)
    || !nullableNonBlank(source.decidedBy) || !(source.decidedAtUtc === null || isPurchasingInstant(source.decidedAtUtc))
    || !nullableNonBlank(source.decisionReason) || !Array.isArray(source.labels) || source.labels.length === 0) return null
  if ((source.varianceOutcome === 'Exact') !== (source.differenceKg === '0')) return null
  if ((source.varianceOutcome === 'ToleranceNotSet') !== (source.tolerancePercent === null && source.differenceKg !== '0')) return null
  if (source.varianceOutcome === 'WithinTolerance' && !source.varianceAcknowledged) return null
  const decided = source.decidedBy !== null
  if (decided !== (source.decidedAtUtc !== null)) return null
  if (source.status === 'PendingApproval' && (decided || source.decisionReason !== null)) return null
  if (source.status === 'Rejected' && (!decided || source.decisionReason === null)) return null
  const needsApprover = source.varianceOutcome === 'OutsideTolerance' || source.varianceOutcome === 'ToleranceNotSet'
  if (source.status === 'Posted' && (needsApprover !== decided || source.decisionReason !== null)) return null
  if (decided && source.decidedBy === source.preparedBy) return null
  for (let index = 0; index < source.labels.length; index++) {
    if (!label(source.labels[index], index + 1)) return null
  }
  return source as unknown as GoodsReceiptBatch
}

/** The page decoder: exactly `items` and `searchWindowFull`; every item measured by the batch rule; no batch twice. */
export function decodeGoodsReceiptBatchPage(value: unknown, companyId: string, orderId: string): GoodsReceiptBatchPage | null {
  const source = record(value)
  if (source === null || !exact(source, ['items', 'searchWindowFull']) || !Array.isArray(source.items)
    || typeof source.searchWindowFull !== 'boolean') return null
  const seen = new Set<string>()
  for (const item of source.items) {
    const batch = decodeGoodsReceiptBatch(item, companyId, orderId)
    if (batch === null || seen.has(batch.batchId.toLowerCase())) return null
    seen.add(batch.batchId.toLowerCase())
  }
  return source as unknown as GoodsReceiptBatchPage
}
