import type { DraftDetail, DraftLineRequest, DraftLineRevisionRequest, DraftLineRemovalRequest } from './contract'
import type { OrderDraftOptions } from './orderDraftForm'
import { parseDecimalInput, parseQuantityInput } from './wirePrimitives'

/**
 * SAT-D167 (EK24): the editable state of a line revision on S-04 — what happens to each current line (kept, removed, or its
 * product changed) and the lines to add. The form checks only the SHAPE (a reason, a chosen product and unit the Product owner
 * offers, a positive quantity, a price only with the price-write control and with its unit, a product change to ANOTHER product,
 * at least one change, one line left); whether a line is unshipped, the create's line rules, the fason rules (service, kg, no
 * price list), the line tax and every state rule are the server's.
 */
export interface NewLineValues {
  /** Client-owned, minted once when the row appears and kept for the life of the form (a retry sends the same id). */
  readonly lineId: string
  readonly productId: string
  readonly orderUnitId: string
  readonly quantityText: string
  /** '' = no price (null on the wire, never zero); read only with the price-write control. */
  readonly unitPriceText: string
  readonly priceUnitId: string
}

export type LineAction = 'keep' | 'remove' | 'replace'

export interface ExistingLineRow {
  readonly lineId: string
  readonly lineNo: number
  readonly productId: string
  readonly label: string
  readonly quantity: string
  readonly requestedDeliveryDate: string | null
  readonly action: LineAction
  /** The replacing line (used only when `action` is `replace`), minted with the row. */
  readonly replacement: NewLineValues
}

export interface LineRevisionValues {
  readonly existing: readonly ExistingLineRow[]
  readonly added: readonly NewLineValues[]
  readonly reason: string
}

export type LineRevisionProblem =
  | 'reason-required'
  | 'no-change'
  | 'no-lines-left'
  | 'product-required'
  | 'unit-required'
  | 'quantity-format'
  | 'price-format'
  | 'price-unit-required'
  | 'same-product'

export type LineRevisionBuild =
  | { readonly ok: true; readonly payload: DraftLineRevisionRequest }
  | { readonly ok: false; readonly problem: LineRevisionProblem; readonly row: string | null }

export const emptyNewLine = (lineId: string): NewLineValues => ({
  lineId, productId: '', orderUnitId: '', quantityText: '', unitPriceText: '', priceUnitId: '',
})

export function lineRevisionFromDetail(detail: DraftDetail, mint: () => string): LineRevisionValues {
  return {
    existing: detail.lines.map((line) => ({
      lineId: line.lineId,
      lineNo: line.lineNo,
      productId: line.productId,
      label: `${line.productCode} — ${line.productName}`,
      quantity: line.quantity,
      requestedDeliveryDate: line.requestedDeliveryDate,
      action: 'keep',
      replacement: { ...emptyNewLine(mint()), quantityText: line.quantity },
    })),
    added: [],
    reason: '',
  }
}

type NewLineBuild =
  | { readonly ok: true; readonly line: DraftLineRequest }
  | { readonly ok: false; readonly problem: LineRevisionProblem }

function buildNewLine(
  values: NewLineValues,
  products: OrderDraftOptions['products'],
  priceWrite: boolean,
  requestedDeliveryDate: string | null,
): NewLineBuild {
  const product = products.find((candidate) => candidate.productId === values.productId)
  if (product === undefined) return { ok: false, problem: 'product-required' }
  if (!product.orderUnits.some((unit) => unit.unitId === values.orderUnitId)) return { ok: false, problem: 'unit-required' }
  const quantity = parseQuantityInput(values.quantityText)
  if (!quantity.ok) return { ok: false, problem: 'quantity-format' }
  let unitPrice: string | null = null
  let priceUnitId: string | null = null
  // UI-14: without the price-write control no price member is read or sent, whatever the row holds.
  if (priceWrite && values.unitPriceText.trim() !== '') {
    const price = parseDecimalInput(values.unitPriceText)
    if (!price.ok) return { ok: false, problem: 'price-format' }
    if (!product.orderUnits.some((unit) => unit.unitId === values.priceUnitId)) return { ok: false, problem: 'price-unit-required' }
    unitPrice = price.value
    priceUnitId = values.priceUnitId
  }
  return {
    ok: true,
    line: {
      lineId: values.lineId,
      productId: product.productId,
      expectedProductVersion: product.productVersion,
      orderUnitId: values.orderUnitId,
      quantity: quantity.value,
      unitPrice,
      priceUnitId,
      requestedDeliveryDate,
      note: null,
      requestedDeliverySlices: null,
      discountPercent: null,
    },
  }
}

/**
 * The one body of a line revision, or the first local problem (in screen order: the current lines, the added lines, then the
 * reason). A replacement keeps the replaced line's requested delivery date; its slices are planned afterwards like any line's.
 */
export function buildLineRevisionRequest(
  expectedVersion: string,
  values: LineRevisionValues,
  products: OrderDraftOptions['products'],
  priceWrite: boolean,
): LineRevisionBuild {
  const added: DraftLineRequest[] = []
  const removed: DraftLineRemovalRequest[] = []
  for (const row of values.existing) {
    if (row.action === 'keep') continue
    if (row.action === 'remove') {
      removed.push({ lineId: row.lineId, replacedByLineId: null })
      continue
    }
    const label = `${row.lineNo}. ${row.label}`
    if (row.replacement.productId === row.productId) return { ok: false, problem: 'same-product', row: label }
    const built = buildNewLine(row.replacement, products, priceWrite, row.requestedDeliveryDate)
    if (!built.ok) return { ok: false, problem: built.problem, row: label }
    added.push(built.line)
    removed.push({ lineId: row.lineId, replacedByLineId: row.replacement.lineId })
  }
  for (const [index, row] of values.added.entries()) {
    const built = buildNewLine(row, products, priceWrite, null)
    if (!built.ok) return { ok: false, problem: built.problem, row: `+${index + 1}` }
    added.push(built.line)
  }
  if (added.length === 0 && removed.length === 0) return { ok: false, problem: 'no-change', row: null }
  if (values.existing.length - removed.length + added.length < 1) return { ok: false, problem: 'no-lines-left', row: null }
  if (values.reason.trim() === '') return { ok: false, problem: 'reason-required', row: null }
  return { ok: true, payload: { expectedVersion, reason: values.reason, added, removed } }
}

/**
 * The row a server field error names (`added[i]…` / `removed[i]…`), as the screen labels it: a current line's "N. code — name"
 * or an added line's "+k". null when the path names no row of this body.
 */
export function rowOfServerField(field: string, payload: DraftLineRevisionRequest, values: LineRevisionValues): string | null {
  const match = /^(added|removed)\[(\d+)\]/.exec(field)
  if (match === null) return null
  const index = Number(match[2])
  if (match[1] === 'removed') {
    const lineId = payload.removed[index]?.lineId
    const row = values.existing.find((candidate) => candidate.lineId === lineId)
    return row === undefined ? null : `${row.lineNo}. ${row.label}`
  }
  const lineId = payload.added[index]?.lineId
  const replaced = values.existing.find((candidate) => candidate.action === 'replace' && candidate.replacement.lineId === lineId)
  if (replaced !== undefined) return `${replaced.lineNo}. ${replaced.label}`
  const addedIndex = values.added.findIndex((candidate) => candidate.lineId === lineId)
  return addedIndex < 0 ? null : `+${addedIndex + 1}`
}
