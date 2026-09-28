import type { DraftDetail, DraftQuantityRevisionRequest } from './contract'

/**
 * DEC-SAL-002 / AT-023: the editable rows of a quantity revision — one per current line, in line order, prefilled with the
 * stored quantity text. The form checks only the SHAPE (positive plain decimal text, a reason, at least one change); Ö2 in
 * the line's unit, the delivery plan and every state rule are the server's.
 */
export interface QuantityRevisionRow {
  readonly lineId: string
  readonly lineNo: number
  readonly productName: string
  readonly orderUnitId: string
  readonly current: string
  readonly quantity: string
}

export type QuantityRevisionBuild =
  | { readonly ok: true; readonly payload: DraftQuantityRevisionRequest }
  | { readonly ok: false; readonly lineNo: number | null; readonly reason: 'reason-required' | 'quantity-format' | 'no-change' }

const QUANTITY_RE = /^[0-9]+(?:\.[0-9]+)?$/

export function quantityRowsFromDetail(detail: DraftDetail): readonly QuantityRevisionRow[] {
  return detail.lines.map((line) => ({
    lineId: line.lineId,
    lineNo: line.lineNo,
    productName: line.productName,
    orderUnitId: line.orderUnitId,
    current: line.quantity,
    quantity: line.quantity,
  }))
}

/** Plain decimal text compared by value without floating point: "1.250" and "1.25" are the same quantity. */
export function sameQuantity(left: string, right: string): boolean {
  const canonical = (text: string) => {
    const [integer, fraction = ''] = text.split('.')
    const whole = integer.replace(/^0+(?=\d)/, '')
    const decimals = fraction.replace(/0+$/, '')
    return decimals === '' ? whole : `${whole}.${decimals}`
  }
  return canonical(left) === canonical(right)
}

export function buildQuantityRevisionRequest(
  expectedVersion: string,
  reason: string,
  rows: readonly QuantityRevisionRow[],
): QuantityRevisionBuild {
  if (reason.trim() === '') return { ok: false, lineNo: null, reason: 'reason-required' }
  const lines: DraftQuantityRevisionRequest['lines'][number][] = []
  for (const row of rows) {
    const quantity = row.quantity.trim()
    if (!QUANTITY_RE.test(quantity) || !/[1-9]/.test(quantity)) {
      return { ok: false, lineNo: row.lineNo, reason: 'quantity-format' }
    }
    lines.push({ lineId: row.lineId, quantity })
  }
  if (rows.every((row) => sameQuantity(row.quantity.trim(), row.current))) {
    return { ok: false, lineNo: null, reason: 'no-change' }
  }
  return { ok: true, payload: { expectedVersion, reason, lines } }
}
