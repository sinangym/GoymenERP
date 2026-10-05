import { parseMoneyInput, parseQuantityInput, type PriceListLine, type PriceListLineRequest, type PriceListProductUnit } from './contract'
import { PRICE_LIST_TEXT as T } from './priceListText'

/** One editable line. `units` is null for a line read from the server (its unit is kept as stored). */
export interface PriceListLineRow {
  readonly lineId: string
  readonly productId: string
  readonly productLabel: string
  readonly units: readonly PriceListProductUnit[] | null
  readonly priceUnitId: string
  readonly unitPrice: string
  readonly floorUnitPrice: string
  readonly lowerQuantity: string
  readonly upperQuantity: string
}

export function rowsFromLines(lines: readonly PriceListLine[]): PriceListLineRow[] {
  return lines.map((line) => ({
    lineId: line.lineId,
    productId: line.productId,
    productLabel: line.productCode === null ? line.productId : `${line.productCode} · ${line.productName ?? ''}`,
    units: null,
    priceUnitId: line.priceUnitId,
    unitPrice: line.unitPrice.replace('.', ','),
    floorUnitPrice: line.floorUnitPrice === null ? '' : line.floorUnitPrice.replace('.', ','),
    lowerQuantity: line.lowerQuantity.replace('.', ','),
    upperQuantity: line.upperQuantity === null ? '' : line.upperQuantity.replace('.', ','),
  }))
}

export type LinesBuild =
  | { readonly ok: true; readonly lines: PriceListLineRequest[] }
  | { readonly ok: false; readonly message: string }

/** The rows as the wire wants them, or the first local error (nothing is sent until every line reads). */
export function buildLines(rows: readonly PriceListLineRow[]): LinesBuild {
  const lines: PriceListLineRequest[] = []
  for (const [index, row] of rows.entries()) {
    const unitPrice = parseMoneyInput(row.unitPrice)
    if (unitPrice === null) return { ok: false, message: T.localErrors.price(index + 1) }
    let floorUnitPrice: string | null = null
    if (row.floorUnitPrice.trim() !== '') {
      floorUnitPrice = parseMoneyInput(row.floorUnitPrice)
      if (floorUnitPrice === null) return { ok: false, message: T.localErrors.floor(index + 1) }
    }
    if (row.priceUnitId === '') return { ok: false, message: T.localErrors.unit(index + 1) }
    const lowerQuantity = parseQuantityInput(row.lowerQuantity)
    if (lowerQuantity === null) return { ok: false, message: T.localErrors.lowerQuantity(index + 1) }
    let upperQuantity: string | null = null
    if (row.upperQuantity.trim() !== '') {
      upperQuantity = parseQuantityInput(row.upperQuantity)
      if (upperQuantity === null) return { ok: false, message: T.localErrors.upperQuantity(index + 1) }
    }
    lines.push({ lineId: row.lineId, productId: row.productId, priceUnitId: row.priceUnitId, unitPrice, floorUnitPrice,
      lowerQuantity, upperQuantity })
  }
  return { ok: true, lines }
}

