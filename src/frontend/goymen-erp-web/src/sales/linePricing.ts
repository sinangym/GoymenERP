/**
 * AT-018 / AT-021 on the order line (user decision 2026-09-30): the Product price list price is the line's REFERENCE and
 * its default until the person changes it; a discount (percent or amount) and a maturity surcharge (vade farkı, percent or
 * amount) are applied on it; the reference and the adjustments stay visible, and the net is the SERVER's calculator's
 * (`SalesLinePriceCalculator`) — this module never computes a price.
 *
 *   GET /api/v1/sales/drafts/{id}/line-pricing                          → SalesOrderDraftLinePricingResponse (price read)
 *   GET /api/v1/sales/drafts/{id}/lines/{lineId}/price-references       → SalesOrderDraftPriceReferencesResponse (price write)
 *
 * MOD-02 (SAT-D9 / SAT-D10): the Product owner finds the list price along the tier chain agreement > customer-specific >
 * customer group > company for the draft's customer, on the Europe/Istanbul business day; every reference names the tier it
 * answered at, and the price-references read says how the chain resolved (one list, an ambiguous tier, or no price at all).
 *
 * Exact-member decoders (the S-04 rule): a missing nullable is as wrong as an extra member; a body we cannot read is never
 * painted as data.
 */

import { salesDraftDetailPath } from './contract'

export const salesDraftLinePricingPath = (id: string) => `${salesDraftDetailPath(id)}/line-pricing`
export const salesDraftLinePriceReferencesPath = (id: string, lineId: string) =>
  `${salesDraftDetailPath(id)}/lines/${encodeURIComponent(lineId)}/price-references`

/** SAT-D9 tiers in chain order. */
export const PRICE_TIERS = ['Agreement', 'Customer', 'CustomerGroup', 'Company'] as const
export type PriceTier = (typeof PRICE_TIERS)[number]

export const PRICE_TIER_STATUSES = ['Matched', 'Ambiguous', 'NoMatch', 'NotRequested', 'Unavailable', 'NotReached'] as const
export type PriceTierStatus = (typeof PRICE_TIER_STATUSES)[number]

export type PriceResolutionOutcome = 'Resolved' | 'Ambiguous' | 'NoPrice'

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
  readonly vatBasis: 'ExcludingVat'
  readonly lowerQuantity: string
  readonly upperQuantity: string | null
  /** The tier the list answered at; null on a line priced before the tier chain existed. */
  readonly tier: PriceTier | null
}

export interface PriceTierEvaluation {
  readonly tier: PriceTier
  readonly status: PriceTierStatus
  readonly reason: string | null
}

/** How the owner's tier chain resolved the line (`priceListId` / `versionNo` only when Resolved). */
export interface LinePriceResolution {
  readonly outcome: PriceResolutionOutcome
  readonly effectiveOn: string
  readonly businessTimeZone: string
  readonly matchedTier: PriceTier | null
  readonly priceListId: string | null
  readonly versionNo: number | null
  readonly tiers: readonly PriceTierEvaluation[]
  readonly packageStandard: { readonly status: 'Unavailable'; readonly reason: string }
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
  readonly floorException: PriceFloorException | null
}

export interface PriceFloorException {
  readonly requestedBy: string
  readonly requestedAtUtc: string
  readonly approvedBy: string | null
  readonly approvedAtUtc: string | null
  readonly reason: string | null
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
  readonly resolution: LinePriceResolution
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
const QUANTITY = /^(0|[1-9][0-9]{0,14})(?:\.([0-9]{1,3}))?$/
const isText = (v: unknown): v is string => typeof v === 'string' && v.length > 0 && v.length <= 256
const isApprovalReason = (v: unknown): v is string => typeof v === 'string' && v.trim().length > 0 && v.length <= 4000
const isDecimal = (v: unknown): v is string => typeof v === 'string' && v.length <= 64 && DECIMAL.test(v)
const isNullableDecimal = (v: unknown): v is string | null => v === null || isDecimal(v)
const isDate = (v: unknown): v is string => typeof v === 'string' && DATE.test(v)
const isCount = (v: unknown): v is number => typeof v === 'number' && Number.isInteger(v) && v >= 1

/** Product's decimal(18,3) quantity shape as an exact lexicographically comparable key; no JS Number conversion or rounding. */
function quantityKey(value: unknown): string | null {
  if (typeof value !== 'string') return null
  const match = QUANTITY.exec(value)
  if (match === null) return null
  return `${match[1].padStart(15, '0')}${(match[2] ?? '').padEnd(3, '0')}`
}

const REFERENCE_KEYS = ['priceListId', 'priceListName', 'versionNo', 'validFrom', 'validThrough', 'currencyCode', 'priceUnitId', 'unitPrice',
  'floorUnitPrice', 'vatBasis', 'lowerQuantity', 'upperQuantity', 'tier'] as const

const isTier = (v: unknown): v is PriceTier => typeof v === 'string' && (PRICE_TIERS as readonly string[]).includes(v)
const isTierStatus = (v: unknown): v is PriceTierStatus => typeof v === 'string' && (PRICE_TIER_STATUSES as readonly string[]).includes(v)

export function decodeReference(value: unknown): LinePriceReference | null {
  const o = exact(value, REFERENCE_KEYS)
  const lowerQuantity = o?.lowerQuantity
  const upperQuantity = o?.upperQuantity
  const lowerKey = quantityKey(lowerQuantity)
  const upperKey = upperQuantity === null ? null : quantityKey(upperQuantity)
  if (o === null || !isText(o.priceListId) || !isText(o.priceListName) || !isCount(o.versionNo) || !isDate(o.validFrom)
    || !(o.validThrough === null || isDate(o.validThrough)) || !isText(o.currencyCode) || !isText(o.priceUnitId)
    || !isDecimal(o.unitPrice) || !isNullableDecimal(o.floorUnitPrice) || o.vatBasis !== 'ExcludingVat'
    || typeof lowerQuantity !== 'string' || !(upperQuantity === null || typeof upperQuantity === 'string')
    || lowerKey === null || (upperQuantity !== null && (upperKey === null || upperKey <= lowerKey))
    || !(o.tier === null || isTier(o.tier))) return null
  return {
    priceListId: o.priceListId, priceListName: o.priceListName, versionNo: o.versionNo, validFrom: o.validFrom,
    validThrough: o.validThrough, currencyCode: o.currencyCode, priceUnitId: o.priceUnitId, unitPrice: o.unitPrice,
    floorUnitPrice: o.floorUnitPrice, vatBasis: o.vatBasis, lowerQuantity, upperQuantity, tier: o.tier,
  }
}

function decodeTierEvaluation(value: unknown): PriceTierEvaluation | null {
  const o = exact(value, ['tier', 'status', 'reason'])
  if (o === null || !isTier(o.tier) || !isTierStatus(o.status) || !(o.reason === null || isText(o.reason))) return null
  return { tier: o.tier, status: o.status, reason: o.reason }
}

/**
 * The chain's answer, checked against the items it explains: every tier once in chain order; Resolved = exactly one item of
 * the matched tier and its list/version; Ambiguous = several items of the matched tier; NoPrice = no item and no tier.
 */
function decodeResolution(value: unknown, on: string, items: readonly LinePriceReference[]): LinePriceResolution | null {
  const o = exact(value, ['outcome', 'effectiveOn', 'businessTimeZone', 'matchedTier', 'priceListId', 'versionNo', 'tiers', 'packageStandard'])
  if (o === null || o.effectiveOn !== on || !isText(o.businessTimeZone) || !Array.isArray(o.tiers)
    || !(o.matchedTier === null || isTier(o.matchedTier)) || !(o.priceListId === null || isText(o.priceListId))
    || !(o.versionNo === null || isCount(o.versionNo))) return null
  const standard = exact(o.packageStandard, ['status', 'reason'])
  if (standard === null || standard.status !== 'Unavailable' || !isText(standard.reason)) return null
  const tiers: PriceTierEvaluation[] = []
  for (const raw of o.tiers) {
    const tier = decodeTierEvaluation(raw)
    if (tier === null) return null
    tiers.push(tier)
  }
  if (tiers.map((tier) => tier.tier).join() !== PRICE_TIERS.join()) return null
  const matchedTier = o.matchedTier
  if (items.some((item) => item.tier !== matchedTier)) return null
  const fits = o.outcome === 'Resolved'
    ? matchedTier !== null && items.length === 1 && o.priceListId === items[0].priceListId && o.versionNo === items[0].versionNo
    : o.outcome === 'Ambiguous'
      ? matchedTier !== null && items.length > 1 && o.priceListId === null && o.versionNo === null
      : o.outcome === 'NoPrice' && matchedTier === null && items.length === 0 && o.priceListId === null && o.versionNo === null
  if (!fits) return null
  return {
    outcome: o.outcome as PriceResolutionOutcome, effectiveOn: o.effectiveOn, businessTimeZone: o.businessTimeZone, matchedTier,
    priceListId: o.priceListId, versionNo: o.versionNo, tiers,
    packageStandard: { status: 'Unavailable', reason: standard.reason },
  }
}

const PRICING_KEYS = ['lineId', 'lineNo', 'unitPrice', 'priceUnitId', 'priceSource', 'priceReference', 'discountPercent', 'discountAmount',
  'surchargePercent', 'surchargeAmount', 'netUnitPrice', 'floorException'] as const

function decodeFloorException(value: unknown): PriceFloorException | null {
  const o = exact(value, ['requestedBy', 'requestedAtUtc', 'approvedBy', 'approvedAtUtc', 'reason'])
  if (o === null || !isText(o.requestedBy) || typeof o.requestedAtUtc !== 'string' || Number.isNaN(Date.parse(o.requestedAtUtc))) return null
  const pending = o.approvedBy === null && o.approvedAtUtc === null && o.reason === null
  const approved = isText(o.approvedBy) && typeof o.approvedAtUtc === 'string' && !Number.isNaN(Date.parse(o.approvedAtUtc))
    && isApprovalReason(o.reason)
  return pending || approved ? o as unknown as PriceFloorException : null
}

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
    const floorException = o.floorException === null ? null : decodeFloorException(o.floorException)
    if (o.floorException !== null && floorException === null) return null
    // A price and its net travel together; a source without a price is off-contract.
    if ((o.unitPrice === null) !== (o.netUnitPrice === null) || (o.unitPrice === null) !== (o.priceSource === null)) return null
    lines.push({
      lineId: o.lineId, lineNo: o.lineNo, unitPrice: o.unitPrice, priceUnitId: o.priceUnitId, priceSource: o.priceSource,
      priceReference: reference, discountPercent: o.discountPercent, discountAmount: o.discountAmount, surchargePercent: o.surchargePercent,
      surchargeAmount: o.surchargeAmount, netUnitPrice: o.netUnitPrice, floorException,
    })
  }
  return { version: response.version, lines }
}

export function decodeLinePriceReferences(value: unknown, expectedLineId: string): LinePriceReferences | null {
  const o = exact(value, ['lineId', 'currencyCode', 'on', 'items', 'resolution'])
  if (o === null || o.lineId !== expectedLineId || !isText(o.currencyCode) || !isDate(o.on) || !Array.isArray(o.items)) return null
  const items: LinePriceReference[] = []
  for (const raw of o.items) {
    const reference = decodeReference(raw)
    if (reference === null || reference.tier === null) return null
    items.push(reference)
  }
  const resolution = decodeResolution(o.resolution, o.on, items)
  if (resolution === null) return null
  return { lineId: o.lineId, currencyCode: o.currencyCode, on: o.on, items, resolution }
}
