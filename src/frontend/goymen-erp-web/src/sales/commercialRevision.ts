import type { DraftCommercialRevisionRequest, DraftDetail } from './contract'
import type { DraftLinePricing, LinePriceReference } from './linePricing'

export type AdjustmentKind = 'percent' | 'amount'

/**
 * One line of the price form. AT-018 / AT-021: `reference` is the chosen list price (shown, and the default price until the
 * person types another); the discount and the maturity surcharge each have a kind (percent / amount) and a value.
 */
export interface CommercialRevisionRow {
  readonly lineId: string
  readonly lineNo: number
  readonly productName: string
  readonly orderUnitId: string
  readonly unitPrice: string
  readonly priceUnitId: string
  readonly discountPercent: string
  readonly reference: LinePriceReference | null
  readonly discountKind: AdjustmentKind
  readonly surchargeKind: AdjustmentKind
  readonly surchargeValue: string
}

export type CommercialRevisionBuild =
  | { readonly ok: true; readonly payload: DraftCommercialRevisionRequest }
  | { readonly ok: false; readonly lineNo: number; readonly reason: 'discount-without-price' | 'price-unit-required' }

/** The form's rows from the detail and, when readable, the line pricing (reference, amounts, surcharge). */
export function commercialRowsFromDetail(detail: DraftDetail, pricing: DraftLinePricing | null = null): readonly CommercialRevisionRow[] {
  return detail.lines.map((line) => {
    const priced = pricing?.lines.find((candidate) => candidate.lineId === line.lineId) ?? null
    const discountIsAmount = priced?.discountAmount !== null && priced?.discountAmount !== undefined
    const surchargeIsAmount = priced?.surchargeAmount !== null && priced?.surchargeAmount !== undefined
    return {
      lineId: line.lineId,
      lineNo: line.lineNo,
      productName: line.productName,
      orderUnitId: line.orderUnitId,
      unitPrice: line.unitPrice ?? '',
      priceUnitId: line.priceUnitId ?? line.orderUnitId,
      discountPercent: discountIsAmount ? priced!.discountAmount! : (line.discountPercent ?? ''),
      reference: priced?.priceReference ?? null,
      discountKind: discountIsAmount ? 'amount' : 'percent',
      surchargeKind: surchargeIsAmount ? 'amount' : 'percent',
      surchargeValue: surchargeIsAmount ? priced!.surchargeAmount! : (priced?.surchargePercent ?? ''),
    }
  })
}

/** A writable commercial snapshot must describe this exact draft version and every current line exactly once. */
export function commercialRowsFromFreshPricing(detail: DraftDetail, pricing: DraftLinePricing | null): readonly CommercialRevisionRow[] | null {
  if (pricing === null || pricing.version !== detail.draft.version || pricing.lines.length !== detail.lines.length) return null
  const currentIds = new Set(detail.lines.map((line) => line.lineId))
  const pricedIds = new Set(pricing.lines.map((line) => line.lineId))
  if (pricedIds.size !== pricing.lines.length || pricedIds.size !== currentIds.size) return null
  for (const lineId of currentIds) if (!pricedIds.has(lineId)) return null
  return commercialRowsFromDetail(detail, pricing)
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
    const discount = nullableText(row.discountPercent)
    const surcharge = nullableText(row.surchargeValue)
    if (unitPrice === null && (discount !== null || surcharge !== null || row.reference !== null)) {
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
      discountPercent: row.discountKind === 'percent' ? discount : null,
      priceReference: row.reference === null ? null : { priceListId: row.reference.priceListId, versionNo: row.reference.versionNo },
      discountAmount: row.discountKind === 'amount' ? discount : null,
      surchargePercent: row.surchargeKind === 'percent' ? surcharge : null,
      surchargeAmount: row.surchargeKind === 'amount' ? surcharge : null,
    })
  }
  return { ok: true, payload: { expectedVersion, lines } }
}
