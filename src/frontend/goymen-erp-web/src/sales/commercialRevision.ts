import type { DraftCommercialRevisionRequest, DraftDetail } from './contract'

export interface CommercialRevisionRow {
  readonly lineId: string
  readonly lineNo: number
  readonly productName: string
  readonly orderUnitId: string
  readonly unitPrice: string
  readonly priceUnitId: string
  readonly discountPercent: string
}

export type CommercialRevisionBuild =
  | { readonly ok: true; readonly payload: DraftCommercialRevisionRequest }
  | { readonly ok: false; readonly lineNo: number; readonly reason: 'discount-without-price' | 'price-unit-required' }

export function commercialRowsFromDetail(detail: DraftDetail): readonly CommercialRevisionRow[] {
  return detail.lines.map((line) => ({
    lineId: line.lineId,
    lineNo: line.lineNo,
    productName: line.productName,
    orderUnitId: line.orderUnitId,
    unitPrice: line.unitPrice ?? '',
    priceUnitId: line.priceUnitId ?? line.orderUnitId,
    discountPercent: line.discountPercent ?? '',
  }))
}

const nullableText = (value: string): string | null => {
  const trimmed = value.trim()
  return trimmed === '' ? null : trimmed
}

export function buildCommercialRevisionRequest(
  expectedVersion: string,
  rows: readonly CommercialRevisionRow[],
): CommercialRevisionBuild {
  const lines: DraftCommercialRevisionRequest['lines'][number][] = []
  for (const row of rows) {
    const unitPrice = nullableText(row.unitPrice)
    const discountPercent = nullableText(row.discountPercent)
    if (unitPrice === null && discountPercent !== null) {
      return { ok: false, lineNo: row.lineNo, reason: 'discount-without-price' }
    }
    const selectedUnit = nullableText(row.priceUnitId)
    if (unitPrice !== null && selectedUnit === null) {
      return { ok: false, lineNo: row.lineNo, reason: 'price-unit-required' }
    }
    lines.push({
      lineId: row.lineId,
      unitPrice,
      priceUnitId: unitPrice === null ? null : selectedUnit,
      discountPercent,
    })
  }
  return { ok: true, payload: { expectedVersion, lines } }
}
