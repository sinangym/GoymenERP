import { isPurchasingGuid, isPurchasingInstant, isPurchasingVersion } from './contract'
import { decodeFutureSupplyReleases, type FutureSupplyRelease } from './goodsReceiptContract'
import { decodePieceKilograms, type PieceKilograms } from './pieceKilogramsContract'

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
  /** AK-3b (KLN-D119): the label's whole piece count on a piece-unit line; absent on a kilogram line. */
  readonly pieceCount?: number
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
  /**
   * AK-3b: present only on a bulk receipt of a piece-unit line — the theoretical vs weighed kilograms over the whole batch as
   * recorded at submission. Such a batch is always Posted, never acknowledged or approved; `expectedQuantityKg` is then the
   * THEORETICAL kg of the received pieces, not the line's remainder.
   */
  readonly pieceKilograms?: PieceKilograms
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

/** A label of a piece batch carries its whole piece count above zero; a label of a kilogram batch carries none. */
function label(value: unknown, expectedLineNo: number, piece: boolean): boolean {
  const source = record(value)
  return source !== null && exact(source, piece ? [...LABEL_FIELDS, 'pieceCount'] : LABEL_FIELDS) && source.lineNo === expectedLineNo
    && (!piece || (typeof source.pieceCount === 'number' && Number.isSafeInteger(source.pieceCount) && source.pieceCount >= 1))
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
  const piece = source !== null && Object.hasOwn(source, 'pieceKilograms')
  if (source === null || !exact(source, piece ? [...BATCH_FIELDS, 'pieceKilograms'] : BATCH_FIELDS)
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
  const decided = source.decidedBy !== null
  if (decided !== (source.decidedAtUtc !== null)) return null
  if (piece) {
    // AK-3b: a piece batch is posted whatever the outcome and nobody decides it; its comparison is the batch's own figures and
    // its piece count the sum of its labels' (a consistency check of what was received, not a recomputation).
    const comparison = decodePieceKilograms(source.pieceKilograms)
    if (comparison === null || source.status !== 'Posted' || decided || source.decisionReason !== null
      || comparison.theoreticalQuantityKg !== source.expectedQuantityKg || comparison.measuredQuantityKg !== source.receivedQuantityKg
      || comparison.differenceKg !== source.differenceKg || comparison.varianceOutcome !== source.varianceOutcome
      || comparison.tolerancePercent !== source.tolerancePercent) return null
    let pieces = 0
    for (let index = 0; index < source.labels.length; index++) {
      if (!label(source.labels[index], index + 1, true)) return null
      pieces += (source.labels[index] as { pieceCount: number }).pieceCount
    }
    return pieces === comparison.pieceCount ? source as unknown as GoodsReceiptBatch : null
  }
  if (source.varianceOutcome === 'WithinTolerance' && !source.varianceAcknowledged) return null
  if (source.status === 'PendingApproval' && (decided || source.decisionReason !== null)) return null
  if (source.status === 'Rejected' && (!decided || source.decisionReason === null)) return null
  const needsApprover = source.varianceOutcome === 'OutsideTolerance' || source.varianceOutcome === 'ToleranceNotSet'
  if (source.status === 'Posted' && (needsApprover !== decided || source.decisionReason !== null)) return null
  if (decided && source.decidedBy === source.preparedBy) return null
  for (let index = 0; index < source.labels.length; index++) {
    if (!label(source.labels[index], index + 1, false)) return null
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

/**
 * AK-3b (KLN-D119): one label row of a bulk receipt request (`PurchaseGoodsReceiptBatchLabelInputV1`). `acceptedQuantity` is the
 * whole piece count — sent for EVERY label of a piece-unit line and ABSENT (not null, not "") for a kilogram line, whose earlier
 * requests must keep their hash. `measuredQuantityKg` is the weighed kg in both cases.
 */
export interface GoodsReceiptBatchLabelInput {
  readonly acceptanceId: string
  readonly lotId: string
  readonly labelId: string
  readonly movementId: string
  readonly supplierLabelNumber: string | null
  readonly labelCode: string | null
  readonly measuredQuantityKg: string
  readonly heatNumber: string | null
  readonly coilNumber: string | null
  readonly acceptedQuantity?: string
}

export interface SubmitGoodsReceiptBatchRequest {
  readonly expectedPurchaseOrderVersion: string
  readonly expectedProductVersion: string
  readonly batchId: string
  readonly locationId: string
  readonly occurredAtUtc: string
  readonly varianceAcknowledged: boolean
  readonly labels: readonly GoodsReceiptBatchLabelInput[]
}

/** One label row as typed; every member is the raw input text. */
export interface GoodsReceiptBatchLabelDraft {
  readonly supplierLabelNumber: string
  readonly labelCode: string
  readonly measuredQuantityKg: string
  /** Read only for a piece-unit line. */
  readonly pieceCount: string
  readonly heatNumber: string
  readonly coilNumber: string
}

export type GoodsReceiptBatchLabelField = 'measuredQuantityKg' | 'acceptedQuantity' | 'supplierLabelNumber' | 'labelCode' | 'heatNumber' | 'coilNumber'
export interface GoodsReceiptBatchLabelError { readonly index: number; readonly field: GoodsReceiptBatchLabelField }
export type GoodsReceiptBatchLabelsResult =
  | { readonly ok: true; readonly labels: readonly GoodsReceiptBatchLabelInput[]; readonly totalPieces: string | null }
  | { readonly ok: false; readonly errors: readonly GoodsReceiptBatchLabelError[]; readonly exceedsRemaining: boolean }

/** The backend's label cap per bulk receipt (`PurchaseGoodsReceiptBatchApiV1.MaximumLabels`). */
export const GOODS_RECEIPT_BATCH_MAXIMUM_LABELS = 200
const MAXIMUM_TOKEN_LENGTH = 64
const MAXIMUM_PIECES = 2147483647n
const KG_INPUT = /^[0-9]+(?:\.[0-9]{1,3})?$/

/** A whole piece count above zero as canonical text ("40"), or null: no fraction, no sign, no separator, within int range. */
export function pieceCountInput(value: string): string | null {
  const trimmed = value.trim()
  if (!/^[0-9]{1,10}$/.test(trimmed)) return null
  const pieces = BigInt(trimmed)
  return pieces >= 1n && pieces <= MAXIMUM_PIECES ? pieces.toString() : null
}

function kgInput(value: string): string | null {
  const normalized = value.trim().replace(',', '.')
  if (!KG_INPUT.test(normalized)) return null
  const [whole, fraction = ''] = normalized.split('.')
  const integer = whole.replace(/^0+(?=\d)/, '')
  const decimal = fraction.replace(/0+$/, '')
  const result = `${integer}${decimal === '' ? '' : `.${decimal}`}`
  return result !== '0' && integer.length <= 15 ? result : null
}

function token(value: string): string | null | undefined {
  const trimmed = value.trim()
  if (trimmed === '') return null
  // eslint-disable-next-line no-control-regex
  return trimmed.length <= MAXIMUM_TOKEN_LENGTH && !/[\u0000-\u001f\u007f-\u009f]/.test(trimmed) ? trimmed : undefined
}

/**
 * Builds the label rows of a bulk receipt request, or names every row that cannot be sent. `pieceLine` must be the SERVER's fact
 * about the order line (never a guess): only then every row needs a whole piece count and carries `acceptedQuantity`; otherwise the
 * member is left out entirely. With `remainingPieces` (the line's remaining quantity as the server's text) the total piece count
 * is checked against it before sending; the server stays the authority (409 `…_batch_exceeds_remaining`). No kg is summed and no
 * tolerance is evaluated here.
 */
export function buildGoodsReceiptBatchLabels(rows: readonly GoodsReceiptBatchLabelDraft[], pieceLine: boolean,
  identity: () => string, remainingPieces?: string): GoodsReceiptBatchLabelsResult {
  const errors: GoodsReceiptBatchLabelError[] = []
  const labels: GoodsReceiptBatchLabelInput[] = []
  let total = 0n
  rows.forEach((row, index) => {
    const measuredQuantityKg = kgInput(row.measuredQuantityKg)
    const acceptedQuantity = pieceLine ? pieceCountInput(row.pieceCount) : null
    const supplierLabelNumber = token(row.supplierLabelNumber), labelCode = token(row.labelCode)
    const heatNumber = token(row.heatNumber), coilNumber = token(row.coilNumber)
    if (measuredQuantityKg === null) errors.push({ index, field: 'measuredQuantityKg' })
    if (pieceLine && acceptedQuantity === null) errors.push({ index, field: 'acceptedQuantity' })
    if (supplierLabelNumber === undefined) errors.push({ index, field: 'supplierLabelNumber' })
    if (labelCode === undefined) errors.push({ index, field: 'labelCode' })
    if (heatNumber === undefined) errors.push({ index, field: 'heatNumber' })
    if (coilNumber === undefined) errors.push({ index, field: 'coilNumber' })
    if (measuredQuantityKg === null || (pieceLine && acceptedQuantity === null) || supplierLabelNumber === undefined
      || labelCode === undefined || heatNumber === undefined || coilNumber === undefined) return
    if (acceptedQuantity !== null) total += BigInt(acceptedQuantity)
    labels.push({ acceptanceId: identity(), lotId: identity(), labelId: identity(), movementId: identity(), supplierLabelNumber, labelCode,
      measuredQuantityKg, heatNumber, coilNumber, ...(acceptedQuantity === null ? {} : { acceptedQuantity }) })
  })
  const exceedsRemaining = pieceLine && remainingPieces !== undefined && /^(?:0|[1-9][0-9]{0,14})$/.test(remainingPieces)
    && total > BigInt(remainingPieces)
  if (rows.length === 0 || rows.length > GOODS_RECEIPT_BATCH_MAXIMUM_LABELS || errors.length > 0 || exceedsRemaining) {
    return { ok: false, errors, exceedsRemaining }
  }
  return { ok: true, labels, totalPieces: pieceLine ? total.toString() : null }
}

export interface GoodsReceiptBatchResponse {
  readonly batch: GoodsReceiptBatch
  readonly isReplay: boolean
  readonly originalCorrelationId: string
  readonly futureSupplyReleases: readonly FutureSupplyRelease[]
}

/**
 * The submit answer (`PurchaseGoodsReceiptBatchResponseV1`), fail closed: exactly its members; the batch measured by the batch
 * rule and the one that was sent — same batch, line and location, the same labels in order with their identities and weighed kg.
 * A request with piece counts is answered by a piece batch carrying the same counts; a request without them never is.
 */
export function decodeGoodsReceiptBatchResponse(value: unknown, companyId: string, orderId: string, lineId: string,
  sent: SubmitGoodsReceiptBatchRequest, replayHeader: boolean): GoodsReceiptBatchResponse | null {
  const source = record(value)
  if (source === null || !(exact(source, ['batch', 'isReplay', 'originalCorrelationId'])
    || exact(source, ['batch', 'isReplay', 'originalCorrelationId', 'futureSupplyReleases']))
    || source.isReplay !== replayHeader || !nonBlank(source.originalCorrelationId)) return null
  const batch = decodeGoodsReceiptBatch(source.batch, companyId, orderId)
  const futureSupplyReleases = decodeFutureSupplyReleases(source.futureSupplyReleases)
  if (batch === null || futureSupplyReleases === null || batch.batchId.toLowerCase() !== sent.batchId.toLowerCase()
    || batch.purchaseOrderLineId.toLowerCase() !== lineId.toLowerCase() || batch.locationId.toLowerCase() !== sent.locationId.toLowerCase()
    || batch.labels.length !== sent.labels.length) return null
  const pieceRequest = sent.labels.some((item) => item.acceptedQuantity !== undefined)
  if (pieceRequest !== (batch.pieceKilograms !== undefined)) return null
  for (let index = 0; index < sent.labels.length; index++) {
    const asked = sent.labels[index], answered = batch.labels[index]
    if (answered.acceptanceId.toLowerCase() !== asked.acceptanceId.toLowerCase() || answered.lotId.toLowerCase() !== asked.lotId.toLowerCase()
      || answered.labelId.toLowerCase() !== asked.labelId.toLowerCase() || answered.movementId.toLowerCase() !== asked.movementId.toLowerCase()
      || answered.measuredQuantityKg !== asked.measuredQuantityKg
      || (answered.pieceCount === undefined ? asked.acceptedQuantity !== undefined : String(answered.pieceCount) !== asked.acceptedQuantity)) return null
  }
  return { batch, isReplay: replayHeader, originalCorrelationId: source.originalCorrelationId, futureSupplyReleases }
}
