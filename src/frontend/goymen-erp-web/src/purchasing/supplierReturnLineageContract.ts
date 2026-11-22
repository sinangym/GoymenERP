import { isPurchasingGuid } from './contract'

/**
 * AK-8b (SAT-D433, SAT-D440–SAT-D445, K3-B2): a supplier return that leaves a source label other than the receipt's own label.
 * A lineage return takes from every receipt in the label's lineage (one part per receipt in ONE return document); a quantity
 * return takes from the one chosen receipt. Inside a receipt the invoiced / invoice-pending part is the preparer's explicit
 * choice. The screen shows the server's distribution, limits and cost source as received and never computes them.
 */
export type SupplierReturnLineageMode = 'Lineage' | 'Quantity'
export type SupplierReturnCostSource = 'InvoiceUnitCost' | 'ManualCostException' | 'MovingAverage'

export interface SupplierReturnInvoicedSelection {
  readonly sourceEventId: string
  readonly quantityKg: string
}

/** One receipt's part of a stored return (GET …/supplier-returns). The unit cost is null when the price is withheld. */
export interface SupplierReturnLineagePart {
  readonly goodsAcceptanceId: string
  readonly purchaseOrderId: string
  readonly purchaseOrderLineId: string
  readonly returnLineId: string
  readonly inventoryMovementId: string
  readonly quantityKg: string
  readonly costBasis: 'InvoiceUnitCost' | 'ManualCostWithException'
  readonly unitCostTryPerKg: string | null
  readonly pieceCount?: number
  readonly pendingKg?: string
  readonly invoiced?: readonly SupplierReturnInvoicedSelection[]
  /** SAT-D454/SAT-D455: the price the supplier is billed at when recorded apart from the stock cost (absent when withheld). */
  readonly supplierUnitPriceTry?: string
  /** SAT-D455: the document's pieces of a quantity return from a piece order line; the price above is then per piece. */
  readonly supplierPieceQuantity?: number
  /** SAT-D453/SAT-D456: the kuruş-rounded amount the document bills; present exactly when the supplier price is. */
  readonly supplierAmountTry?: string
  /** SAT-D460: the kuruş-rounded stock exit amount and its difference from the billed amount, recorded for approval. */
  readonly stockExitAmountTry?: string
  readonly differenceTry?: string
}

export interface SupplierReturnInvoicedPart {
  readonly sourceEventId: string
  readonly invoiceId: string
  readonly invoiceLineId: string
  readonly remainingKg: string
}

export interface SupplierReturnLineagePreviewPart {
  readonly goodsAcceptanceId: string
  readonly purchaseOrderId: string
  readonly purchaseOrderLineId: string
  readonly quantityKg: string
  readonly pieceCount: number | null
  readonly costSource: SupplierReturnCostSource
  readonly receivedKg: string
  readonly openKg: string
  readonly pendingRemainingKg: string
  readonly invoiced: readonly SupplierReturnInvoicedPart[]
  readonly selectionRequired: boolean
}

export interface SupplierReturnLineagePreview {
  readonly companyId: string
  readonly purchaseOrderId: string
  readonly goodsAcceptanceId: string
  readonly sourceLabelId: string
  readonly lineageMode: SupplierReturnLineageMode
  readonly quantityKg: string
  readonly pieceCount: number | null
  readonly tracksPieces: boolean
  readonly freeKg: string
  readonly blockedKg: string
  readonly freePieces: number | null
  readonly blockedPieces: number | null
  readonly parts: readonly SupplierReturnLineagePreviewPart[]
  /** The code the command would answer with as the plan stands; absent when the plan can be posted. */
  readonly refusal?: string
}

export interface SupplierReturnLineagePartRequest {
  readonly goodsAcceptanceId: string
  readonly returnLineId: string
  readonly inventoryMovementId: string
  readonly pendingKg?: string
  readonly invoiced?: readonly SupplierReturnInvoicedSelection[]
  readonly manualUnitCostTryPerKg?: string
}

export interface SupplierReturnLineageRequest {
  readonly returnId: string
  readonly returnLineId: string
  readonly goodsAcceptanceId: string
  readonly inventoryMovementId: string
  readonly sourceBlock: 'Free' | 'Blocked'
  readonly quantityKg: string
  readonly manualUnitCostTryPerKg: string | null
  readonly replacementExpected: false
  readonly reason: string
  readonly occurredAtUtc: string
  readonly sourceLabelId: string
  readonly pieceCount?: number
  readonly lineageParts?: readonly SupplierReturnLineagePartRequest[]
  /** SAT-D455: the pieces returned on the document when the chosen order line is a piece line. */
  readonly supplierPieceQuantity?: number
}

type JsonObject = Record<string, unknown>

const KG = /^(0|[1-9]\d{0,14})(\.\d{0,2}[1-9])?$/
const COST = /^(0|[1-9]\d{0,13})(\.\d{0,3}[1-9])?$/
const AMOUNT = /^(0|[1-9]\d{0,15})(\.\d?[1-9])?$/
const CODE = /^[a-z0-9_.]{1,128}$/
const MODES: readonly string[] = ['Lineage', 'Quantity']
const SOURCES: readonly string[] = ['InvoiceUnitCost', 'ManualCostException', 'MovingAverage']
const BASES: readonly string[] = ['InvoiceUnitCost', 'ManualCostWithException']
const PART_FIELDS = ['goodsAcceptanceId', 'purchaseOrderId', 'purchaseOrderLineId', 'returnLineId', 'inventoryMovementId', 'quantityKg',
  'costBasis', 'unitCostTryPerKg'] as const
const PREVIEW_PART_FIELDS = ['goodsAcceptanceId', 'purchaseOrderId', 'purchaseOrderLineId', 'quantityKg', 'pieceCount', 'costSource',
  'receivedKg', 'openKg', 'pendingRemainingKg', 'invoiced', 'selectionRequired'] as const
const PREVIEW_FIELDS = ['companyId', 'purchaseOrderId', 'goodsAcceptanceId', 'sourceLabelId', 'lineageMode', 'quantityKg', 'pieceCount',
  'tracksPieces', 'freeKg', 'blockedKg', 'freePieces', 'blockedPieces', 'parts'] as const
const MAXIMUM_PARTS = 64

function record(value: unknown): JsonObject | null {
  return typeof value === 'object' && value !== null && !Array.isArray(value) ? value as JsonObject : null
}

function exact(value: JsonObject, names: readonly string[]): boolean {
  const actual = Object.keys(value).sort()
  const expected = [...names].sort()
  return actual.length === expected.length && actual.every((name, index) => name === expected[index])
}

function same(left: unknown, right: string): boolean {
  return typeof left === 'string' && left.toLowerCase() === right.toLowerCase()
}

/** A canonical non-negative kg text with at most three decimals. */
export function isLineageKg(value: unknown): value is string {
  return typeof value === 'string' && KG.test(value)
}

function positiveKg(value: unknown): value is string {
  return isLineageKg(value) && value !== '0'
}

function count(value: unknown): value is number {
  return typeof value === 'number' && Number.isInteger(value) && value >= 0 && value <= 2_147_483_647
}

/**
 * Exact thousandths of a canonical kg text. Only used to compare what the server sent with itself (a document's parts against
 * its total); never to derive a quantity the server did not send.
 */
export function kgThousandths(value: string): bigint {
  const [whole, fraction = ''] = value.split('.')
  return BigInt(whole) * 1000n + BigInt(fraction.padEnd(3, '0'))
}

function distinct(values: readonly string[]): boolean {
  return new Set(values.map((value) => value.toLowerCase())).size === values.length
}

function decodeSelections(value: unknown): readonly SupplierReturnInvoicedSelection[] | null {
  if (!Array.isArray(value) || value.length > 100) return null
  for (const item of value) {
    const source = record(item)
    if (source === null || !exact(source, ['sourceEventId', 'quantityKg']) || !isPurchasingGuid(source.sourceEventId)
      || !positiveKg(source.quantityKg)) return null
  }
  const items = value as readonly SupplierReturnInvoicedSelection[]
  return distinct(items.map((item) => item.sourceEventId)) ? items : null
}

/**
 * The stored parts of one return, fail closed: exactly their fields; the price visible on every part or on none (as the
 * document); pieces on every part or on none; an explicit choice (pending kg + invoiced sources) adds up to the part; the parts
 * add up to the document's kg and pieces; the document's own receipt is one of them and carries the document's line and movement.
 */
export function decodeSupplierReturnLineageParts(value: unknown, header: {
  readonly goodsAcceptanceId: string; readonly returnLineId: string; readonly inventoryMovementId: string; readonly quantityKg: string
  readonly purchaseOrderId: string; readonly purchaseOrderLineId: string; readonly pieceCount: number | undefined
  readonly priceVisible: boolean; readonly lineageMode: SupplierReturnLineageMode
}): readonly SupplierReturnLineagePart[] | null {
  if (!Array.isArray(value) || value.length < 1 || value.length > MAXIMUM_PARTS
    || header.lineageMode === 'Quantity' && value.length !== 1) return null
  let kg = 0n
  let pieces = 0
  for (const item of value) {
    const source = record(item)
    if (source === null) return null
    const piecePresent = Object.hasOwn(source, 'pieceCount')
    const choicePresent = Object.hasOwn(source, 'pendingKg')
    const pricePresent = Object.hasOwn(source, 'supplierUnitPriceTry')
    const supplierPiecesPresent = Object.hasOwn(source, 'supplierPieceQuantity')
    const differencePresent = Object.hasOwn(source, 'stockExitAmountTry')
    const fields = [...PART_FIELDS, ...(piecePresent ? ['pieceCount'] : []), ...(choicePresent ? ['pendingKg', 'invoiced'] : []),
      ...(pricePresent ? ['supplierUnitPriceTry', 'supplierAmountTry'] : []), ...(supplierPiecesPresent ? ['supplierPieceQuantity'] : []),
      ...(differencePresent ? ['stockExitAmountTry', 'differenceTry'] : [])]
    if (!exact(source, fields) || !isPurchasingGuid(source.goodsAcceptanceId) || !isPurchasingGuid(source.purchaseOrderId)
      || !isPurchasingGuid(source.purchaseOrderLineId) || !isPurchasingGuid(source.returnLineId)
      || !isPurchasingGuid(source.inventoryMovementId) || !positiveKg(source.quantityKg)
      || typeof source.costBasis !== 'string' || !BASES.includes(source.costBasis)
      || !(header.priceVisible ? typeof source.unitCostTryPerKg === 'string' && COST.test(source.unitCostTryPerKg)
        && source.unitCostTryPerKg !== '0' : source.unitCostTryPerKg === null)
      || piecePresent !== (header.pieceCount !== undefined) || piecePresent && !count(source.pieceCount)
      // SAT-D454/SAT-D455: a supplier price only on a quantity return and only while the price is visible; pieces are whole.
      || pricePresent && (header.lineageMode !== 'Quantity' || !header.priceVisible || typeof source.supplierUnitPriceTry !== 'string'
        || !COST.test(source.supplierUnitPriceTry) || source.supplierUnitPriceTry === '0'
        || typeof source.supplierAmountTry !== 'string' || !AMOUNT.test(source.supplierAmountTry)
        || source.supplierAmountTry === '0')
      // SAT-D460: a recorded difference comes with both amounts; it is a non-zero signed kuruş text and the part waits on approval.
      || differencePresent && (!pricePresent || source.costBasis !== 'ManualCostWithException'
        || typeof source.stockExitAmountTry !== 'string' || !AMOUNT.test(source.stockExitAmountTry) || source.stockExitAmountTry === '0'
        || typeof source.differenceTry !== 'string' || !/^-?(0|[1-9]\d{0,15})(\.\d?[1-9])?$/.test(source.differenceTry)
        || source.differenceTry === '0' || source.differenceTry === '-0')
      || supplierPiecesPresent && (!pricePresent || !count(source.supplierPieceQuantity)
        || source.supplierPieceQuantity < 1)) return null
    if (choicePresent) {
      const invoiced = decodeSelections(source.invoiced)
      if (!isLineageKg(source.pendingKg) || invoiced === null
        || invoiced.reduce((sum, selected) => sum + kgThousandths(selected.quantityKg), kgThousandths(source.pendingKg))
          !== kgThousandths(source.quantityKg)) return null
    }
    kg += kgThousandths(source.quantityKg)
    pieces += piecePresent ? source.pieceCount as number : 0
  }
  const parts = value as readonly SupplierReturnLineagePart[]
  const own = parts.filter((part) => same(part.goodsAcceptanceId, header.goodsAcceptanceId))
  if (!distinct(parts.map((part) => part.goodsAcceptanceId)) || !distinct(parts.map((part) => part.returnLineId))
    || !distinct(parts.map((part) => part.inventoryMovementId)) || own.length !== 1
    || !same(own[0].returnLineId, header.returnLineId) || !same(own[0].inventoryMovementId, header.inventoryMovementId)
    || !same(own[0].purchaseOrderId, header.purchaseOrderId) || !same(own[0].purchaseOrderLineId, header.purchaseOrderLineId)
    || kg !== kgThousandths(header.quantityKg) || header.pieceCount !== undefined && pieces !== header.pieceCount) return null
  return parts
}

function decodePreviewPart(value: unknown, pieces: boolean): SupplierReturnLineagePreviewPart | null {
  const source = record(value)
  if (source === null || !exact(source, PREVIEW_PART_FIELDS) || !isPurchasingGuid(source.goodsAcceptanceId)
    || !isPurchasingGuid(source.purchaseOrderId) || !isPurchasingGuid(source.purchaseOrderLineId) || !positiveKg(source.quantityKg)
    || !(pieces ? count(source.pieceCount) : source.pieceCount === null)
    || typeof source.costSource !== 'string' || !SOURCES.includes(source.costSource)
    || !positiveKg(source.receivedKg) || !isLineageKg(source.openKg) || !isLineageKg(source.pendingRemainingKg)
    || typeof source.selectionRequired !== 'boolean' || !Array.isArray(source.invoiced) || source.invoiced.length > 100) return null
  for (const item of source.invoiced) {
    const invoiced = record(item)
    if (invoiced === null || !exact(invoiced, ['sourceEventId', 'invoiceId', 'invoiceLineId', 'remainingKg'])
      || !isPurchasingGuid(invoiced.sourceEventId) || !isPurchasingGuid(invoiced.invoiceId)
      || !isPurchasingGuid(invoiced.invoiceLineId) || !isLineageKg(invoiced.remainingKg)) return null
  }
  return distinct((source.invoiced as readonly SupplierReturnInvoicedPart[]).map((item) => item.sourceEventId))
    ? source as unknown as SupplierReturnLineagePreviewPart : null
}

/**
 * Strict decoder of the preview, fail closed: exactly its fields (refusal optional); this company, order, receipt and label; the
 * kg and pieces that were asked; a known mode and cost source; pieces on the label, the document and every part together; a
 * quantity return has exactly its chosen receipt; the parts add up to the asked kg and pieces.
 */
export function decodeSupplierReturnLineagePreview(value: unknown, asked: {
  readonly companyId: string; readonly orderId: string; readonly acceptanceId: string; readonly sourceLabelId: string
  readonly quantityKg: string; readonly pieceCount: number | null
}): SupplierReturnLineagePreview | null {
  const source = record(value)
  const refusalPresent = source !== null && Object.hasOwn(source, 'refusal')
  if (source === null || !exact(source, [...PREVIEW_FIELDS, ...(refusalPresent ? ['refusal'] : [])])
    || !same(source.companyId, asked.companyId) || !same(source.purchaseOrderId, asked.orderId)
    || !same(source.goodsAcceptanceId, asked.acceptanceId) || !same(source.sourceLabelId, asked.sourceLabelId)
    || typeof source.lineageMode !== 'string' || !MODES.includes(source.lineageMode)
    || source.quantityKg !== asked.quantityKg || source.pieceCount !== asked.pieceCount
    || typeof source.tracksPieces !== 'boolean' || source.tracksPieces !== (asked.pieceCount !== null)
    || !isLineageKg(source.freeKg) || !isLineageKg(source.blockedKg)
    || !(source.tracksPieces ? count(source.freePieces) && count(source.blockedPieces)
      : source.freePieces === null && source.blockedPieces === null)
    || refusalPresent && !(typeof source.refusal === 'string' && CODE.test(source.refusal))
    || !Array.isArray(source.parts) || source.parts.length < 1 || source.parts.length > MAXIMUM_PARTS) return null
  let kg = 0n
  let pieces = 0
  for (const item of source.parts) {
    const part = decodePreviewPart(item, asked.pieceCount !== null)
    if (part === null) return null
    kg += kgThousandths(part.quantityKg)
    pieces += part.pieceCount ?? 0
  }
  const parts = source.parts as readonly SupplierReturnLineagePreviewPart[]
  if (!distinct(parts.map((part) => part.goodsAcceptanceId)) || kg !== kgThousandths(asked.quantityKg)
    || asked.pieceCount !== null && pieces !== asked.pieceCount
    || source.lineageMode === 'Quantity' && (parts.length !== 1 || !same(parts[0].goodsAcceptanceId, asked.acceptanceId))) return null
  return source as unknown as SupplierReturnLineagePreview
}
