/**
 * AT-018 / AT-021 on the order line (user decision 2026-09-30): the Product price list price is the line's REFERENCE and
 * its default until the person changes it; a discount (percent or amount) and a maturity surcharge (vade farkı, percent or
 * amount) are applied on it; the reference and the adjustments stay visible, and the net is the SERVER's calculator's
 * (`SalesLinePriceCalculator`) — this module never computes a price.
 *
 *   GET /api/v1/sales/drafts/{id}/line-pricing                          → SalesOrderDraftLinePricingResponse (price read)
 *   GET /api/v1/sales/drafts/{id}/lines/{lineId}/price-references       → SalesOrderDraftPriceReferencesResponse (price write)
 *
 * Exact-member decoders (the S-04 rule): a missing nullable is as wrong as an extra member; a body we cannot read is never
 * painted as data.
 */

import { salesDraftDetailPath } from './contract'

export const salesDraftLinePricingPath = (id: string) => `${salesDraftDetailPath(id)}/line-pricing`
export const salesDraftLinePriceReferencesPath = (id: string, lineId: string) =>
  `${salesDraftDetailPath(id)}/lines/${encodeURIComponent(lineId)}/price-references`

export interface LinePriceReference {
  readonly priceListId: string
  readonly priceListName: string
  readonly versionNo: number
  readonly validFrom: string
  readonly validThrough: string | null
  readonly currencyCode: string
  readonly priceUnitId: string
  readonly unitPrice: string
  readonly floorUnitPrice: string | null
  readonly vatBasis: string
  readonly lowerQuantity: string
  readonly upperQuantity: string | null
}

export type PriceSource = 'Reference' | 'Manual'

export interface LinePricing {
  readonly lineId: string
  readonly lineNo: number
  readonly unitPrice: string | null
  readonly priceUnitId: string | null
  readonly priceSource: PriceSource | null
  readonly priceReference: LinePriceReference | null
  readonly discountPercent: string | null
  readonly discountAmount: string | null
  readonly surchargePercent: string | null
  readonly surchargeAmount: string | null
  readonly netUnitPrice: string | null
}

export interface DraftLinePricing {
  readonly version: string
  readonly lines: readonly LinePricing[]
}

export interface LinePriceReferences {
  readonly lineId: string
  readonly currencyCode: string
  readonly on: string
  readonly items: readonly LinePriceReference[]
}

type Obj = Record<string, unknown>

function exact(value: unknown, keys: readonly string[]): Obj | null {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return null
  const obj = value as Obj
  if (Object.keys(obj).length !== keys.length) return null
  for (const key of keys) if (!Object.hasOwn(obj, key)) return null
  return obj
}

const DECIMAL = /^[0-9]+(?:\.[0-9]+)?$/
const DATE = /^\d{4}-\d{2}-\d{2}$/
const isText = (v: unknown): v is string => typeof v === 'string' && v.length > 0 && v.length <= 256
const isDecimal = (v: unknown): v is string => typeof v === 'string' && v.length <= 64 && DECIMAL.test(v)
const isNullableDecimal = (v: unknown): v is string | null => v === null || isDecimal(v)
const isDate = (v: unknown): v is string => typeof v === 'string' && DATE.test(v)
const isCount = (v: unknown): v is number => typeof v === 'number' && Number.isInteger(v) && v >= 1

const REFERENCE_KEYS = ['priceListId', 'priceListName', 'versionNo', 'validFrom', 'validThrough', 'currencyCode', 'priceUnitId', 'unitPrice',
  'floorUnitPrice', 'vatBasis', 'lowerQuantity', 'upperQuantity'] as const

export function decodeReference(value: unknown): LinePriceReference | null {
  const o = exact(value, REFERENCE_KEYS)
  if (o === null || !isText(o.priceListId) || !isText(o.priceListName) || !isCount(o.versionNo) || !isDate(o.validFrom)
    || !(o.validThrough === null || isDate(o.validThrough)) || !isText(o.currencyCode) || !isText(o.priceUnitId)
    || !isDecimal(o.unitPrice) || !isNullableDecimal(o.floorUnitPrice) || !isText(o.vatBasis)
    || !isDecimal(o.lowerQuantity) || !isNullableDecimal(o.upperQuantity)) return null
  return {
    priceListId: o.priceListId, priceListName: o.priceListName, versionNo: o.versionNo, validFrom: o.validFrom,
    validThrough: o.validThrough, currencyCode: o.currencyCode, priceUnitId: o.priceUnitId, unitPrice: o.unitPrice,
    floorUnitPrice: o.floorUnitPrice, vatBasis: o.vatBasis, lowerQuantity: o.lowerQuantity, upperQuantity: o.upperQuantity,
  }
}

const PRICING_KEYS = ['lineId', 'lineNo', 'unitPrice', 'priceUnitId', 'priceSource', 'priceReference', 'discountPercent', 'discountAmount',
  'surchargePercent', 'surchargeAmount', 'netUnitPrice'] as const

export function decodeDraftLinePricing(value: unknown): DraftLinePricing | null {
  const response = exact(value, ['version', 'lines'])
  if (response === null || typeof response.version !== 'string' || !/^[1-9][0-9]{0,18}$/.test(response.version)
    || !Array.isArray(response.lines)) return null
  const lines: LinePricing[] = []
  for (const raw of response.lines) {
    const o = exact(raw, PRICING_KEYS)
    if (o === null || !isText(o.lineId) || !isCount(o.lineNo) || !isNullableDecimal(o.unitPrice)
      || !(o.priceUnitId === null || isText(o.priceUnitId))
      || !(o.priceSource === null || o.priceSource === 'Reference' || o.priceSource === 'Manual')
      || !isNullableDecimal(o.discountPercent) || !isNullableDecimal(o.discountAmount) || !isNullableDecimal(o.surchargePercent)
      || !isNullableDecimal(o.surchargeAmount) || !isNullableDecimal(o.netUnitPrice)) return null
    const reference = o.priceReference === null ? null : decodeReference(o.priceReference)
    if (o.priceReference !== null && reference === null) return null
    // A price and its net travel together; a source without a price is off-contract.
    if ((o.unitPrice === null) !== (o.netUnitPrice === null) || (o.unitPrice === null) !== (o.priceSource === null)) return null
    lines.push({
      lineId: o.lineId, lineNo: o.lineNo, unitPrice: o.unitPrice, priceUnitId: o.priceUnitId, priceSource: o.priceSource,
      priceReference: reference, discountPercent: o.discountPercent, discountAmount: o.discountAmount, surchargePercent: o.surchargePercent,
      surchargeAmount: o.surchargeAmount, netUnitPrice: o.netUnitPrice,
    })
  }
  return { version: response.version, lines }
}

export function decodeLinePriceReferences(value: unknown, expectedLineId: string): LinePriceReferences | null {
  const o = exact(value, ['lineId', 'currencyCode', 'on', 'items'])
  if (o === null || o.lineId !== expectedLineId || !isText(o.currencyCode) || !isDate(o.on) || !Array.isArray(o.items)) return null
  const items: LinePriceReference[] = []
  for (const raw of o.items) {
    const reference = decodeReference(raw)
    if (reference === null) return null
    items.push(reference)
  }
  return { lineId: o.lineId, currencyCode: o.currencyCode, on: o.on, items }
}
