/**
 * AT-018 price lists (U-05 "Fiyatlar"), the wire shapes of `ProductPriceListContractsV1`:
 *
 *   GET  /api/v1/price-lists?pageSize&cursor            → PriceListPage
 *   POST /api/v1/price-lists                            → 201 PriceListResult
 *   GET  /api/v1/price-lists/{id}                       → PriceListView
 *   POST /api/v1/price-lists/{id}/draft-revision        → 200 PriceListResult
 *   POST /api/v1/price-lists/{id}/versions              → 200 PriceListResult (a new draft version)
 *   POST /api/v1/price-lists/{id}/publication           → 200 PriceListResult
 *   GET  /api/v1/price-lists/product-options?q          → PriceListProductOptions
 *
 * Money is exact decimal TEXT on the wire, never a JS number; the concurrency `version` is text too. The decoders
 * refuse anything off-contract (fail closed): a body we cannot read is never painted as data.
 */

export type PriceListVersionState = 'Draft' | 'Published'
export type PriceListVersionStatus = 'Draft' | 'Planned' | 'Active' | 'Ended'

export interface PriceListLine {
  readonly lineId: string
  readonly lineNo: number
  readonly productId: string
  readonly productCode: string | null
  readonly productName: string | null
  readonly priceUnitId: string
  readonly unitPrice: string
  readonly floorUnitPrice: string | null
  readonly lowerQuantity: string
  readonly upperQuantity: string | null
}

export interface PriceListVersion {
  readonly versionId: string
  readonly versionNo: number
  readonly state: PriceListVersionState
  readonly status: PriceListVersionStatus
  readonly validFrom: string
  readonly validThrough: string | null
  readonly createdAtUtc: string
  readonly createdBy: string
  readonly publishedAtUtc: string | null
  readonly publishedBy: string | null
  readonly lines: readonly PriceListLine[]
}

export interface PriceListView {
  readonly id: string
  readonly name: string
  readonly currencyCode: string
  readonly channel: string
  readonly vatBasis: string
  readonly version: string
  readonly createdAtUtc: string
  readonly updatedAtUtc: string
  readonly activeVersionNo: number | null
  readonly draftVersionNo: number | null
  readonly versions: readonly PriceListVersion[]
}

export interface PriceListSummary {
  readonly id: string
  readonly name: string
  readonly currencyCode: string
  readonly channel: string
  readonly vatBasis: string
  readonly version: string
  readonly activeVersionNo: number | null
  readonly activeValidFrom: string | null
  readonly draftVersionNo: number | null
  readonly versionCount: number
  readonly updatedAtUtc: string
}

export interface PriceListPage {
  readonly items: readonly PriceListSummary[]
  readonly nextCursor: string | null
}

export interface PriceListResult {
  readonly priceList: PriceListView
  readonly isReplay: boolean
  readonly originalCorrelationId: string
}

export interface PriceListProductUnit {
  readonly unitId: string
  readonly role: 'Sales' | 'Stock'
  readonly code: string | null
}

export interface PriceListProductOption {
  readonly productId: string
  readonly code: string
  readonly name: string
  readonly units: readonly PriceListProductUnit[]
}

export interface PriceListProductOptions {
  readonly items: readonly PriceListProductOption[]
  readonly hasMore: boolean
}

export interface PriceListLineRequest {
  readonly lineId: string
  readonly productId: string
  readonly priceUnitId: string
  readonly unitPrice: string
  readonly floorUnitPrice: string | null
  readonly lowerQuantity: string
  readonly upperQuantity: string | null
}

export interface CreatePriceListRequest {
  readonly priceListId: string
  readonly versionId: string
  readonly name: string
  readonly currencyCode: string
  readonly channel: 'Sales'
  readonly vatBasis: 'ExcludingVat'
  readonly validFrom: string
  readonly validThrough: string | null
  readonly lines: readonly PriceListLineRequest[]
}

export interface RevisePriceListDraftRequest {
  readonly expectedVersion: string
  readonly name: string
  readonly validFrom: string
  readonly validThrough: string | null
  readonly lines: readonly PriceListLineRequest[]
}

export interface StartPriceListVersionRequest {
  readonly expectedVersion: string
  readonly versionId: string
  readonly validFrom: string
  readonly validThrough: string | null
}

export interface PublishPriceListVersionRequest {
  readonly expectedVersion: string
  readonly versionNo: number
}

export interface PriceListFieldError {
  readonly field: string
  readonly code: string
}

export interface PriceListProblem {
  readonly code: string | null
  readonly errors: readonly PriceListFieldError[]
}

/* ── decoders ──────────────────────────────────────────────────────────────────────── */

type Json = Record<string, unknown>

const isObject = (value: unknown): value is Json => typeof value === 'object' && value !== null && !Array.isArray(value)
const isText = (value: unknown): value is string => typeof value === 'string'
const isNullableText = (value: unknown): value is string | null => value === null || typeof value === 'string'
const isCount = (value: unknown): value is number => typeof value === 'number' && Number.isInteger(value) && value >= 0
const isNullableCount = (value: unknown): value is number | null => value === null || isCount(value)
const MONEY = /^(0|[1-9][0-9]{0,14})(\.[0-9]{1,4})?$/
const QUANTITY = /^(0|[1-9][0-9]{0,14})(\.[0-9]{1,3})?$/
const DATE = /^[0-9]{4}-[0-9]{2}-[0-9]{2}$/
const VERSION = /^[1-9][0-9]{0,18}$/
const isMoney = (value: unknown): value is string => isText(value) && MONEY.test(value)
const isQuantity = (value: unknown): value is string => isText(value) && QUANTITY.test(value)
const isDate = (value: unknown): value is string => isText(value) && DATE.test(value)

function decodeLine(value: unknown): PriceListLine | null {
  if (!isObject(value)) return null
  const { lineId, lineNo, productId, productCode, productName, priceUnitId, unitPrice, floorUnitPrice, lowerQuantity, upperQuantity } = value
  if (!isText(lineId) || !isCount(lineNo) || !isText(productId) || !isNullableText(productCode) || !isNullableText(productName)
    || !isText(priceUnitId) || !isMoney(unitPrice) || !(floorUnitPrice === null || isMoney(floorUnitPrice))
    || !isQuantity(lowerQuantity) || !(upperQuantity === null || isQuantity(upperQuantity))) return null
  return { lineId, lineNo, productId, productCode, productName, priceUnitId, unitPrice, floorUnitPrice, lowerQuantity, upperQuantity }
}

function decodeVersion(value: unknown): PriceListVersion | null {
  if (!isObject(value)) return null
  const { versionId, versionNo, state, status, validFrom, validThrough, createdAtUtc, createdBy, publishedAtUtc, publishedBy, lines } = value
  if (!isText(versionId) || !isCount(versionNo) || (state !== 'Draft' && state !== 'Published')
    || (status !== 'Draft' && status !== 'Planned' && status !== 'Active' && status !== 'Ended')
    || !isDate(validFrom) || !(validThrough === null || isDate(validThrough)) || !isText(createdAtUtc) || !isText(createdBy)
    || !isNullableText(publishedAtUtc) || !isNullableText(publishedBy) || !Array.isArray(lines)) return null
  const decoded = lines.map(decodeLine)
  if (decoded.some((line) => line === null)) return null
  return {
    versionId, versionNo, state, status, validFrom, validThrough, createdAtUtc, createdBy, publishedAtUtc, publishedBy,
    lines: decoded as PriceListLine[],
  }
}

export function decodePriceListView(value: unknown): PriceListView | null {
  if (!isObject(value)) return null
  const { id, name, currencyCode, channel, vatBasis, version, createdAtUtc, updatedAtUtc, activeVersionNo, draftVersionNo, versions } = value
  if (!isText(id) || !isText(name) || !isText(currencyCode) || !isText(channel) || !isText(vatBasis)
    || !isText(version) || !VERSION.test(version) || !isText(createdAtUtc) || !isText(updatedAtUtc)
    || !isNullableCount(activeVersionNo) || !isNullableCount(draftVersionNo) || !Array.isArray(versions) || versions.length === 0) return null
  const decoded = versions.map(decodeVersion)
  if (decoded.some((item) => item === null)) return null
  return {
    id, name, currencyCode, channel, vatBasis, version, createdAtUtc, updatedAtUtc, activeVersionNo, draftVersionNo,
    versions: decoded as PriceListVersion[],
  }
}

function decodeSummary(value: unknown): PriceListSummary | null {
  if (!isObject(value)) return null
  const { id, name, currencyCode, channel, vatBasis, version, activeVersionNo, activeValidFrom, draftVersionNo, versionCount, updatedAtUtc } = value
  if (!isText(id) || !isText(name) || !isText(currencyCode) || !isText(channel) || !isText(vatBasis) || !isText(version)
    || !isNullableCount(activeVersionNo) || !(activeValidFrom === null || isDate(activeValidFrom)) || !isNullableCount(draftVersionNo)
    || !isCount(versionCount) || !isText(updatedAtUtc)) return null
  return { id, name, currencyCode, channel, vatBasis, version, activeVersionNo, activeValidFrom, draftVersionNo, versionCount, updatedAtUtc }
}

export function decodePriceListPage(value: unknown): PriceListPage | null {
  if (!isObject(value) || !Array.isArray(value.items) || !isNullableText(value.nextCursor)) return null
  const items = value.items.map(decodeSummary)
  if (items.some((item) => item === null)) return null
  return { items: items as PriceListSummary[], nextCursor: value.nextCursor }
}

export function decodePriceListResult(value: unknown): PriceListResult | null {
  if (!isObject(value) || typeof value.isReplay !== 'boolean' || !isText(value.originalCorrelationId)) return null
  const priceList = decodePriceListView(value.priceList)
  return priceList === null ? null : { priceList, isReplay: value.isReplay, originalCorrelationId: value.originalCorrelationId }
}

export function decodeProductOptions(value: unknown): PriceListProductOptions | null {
  if (!isObject(value) || !Array.isArray(value.items) || typeof value.hasMore !== 'boolean') return null
  const items: PriceListProductOption[] = []
  for (const item of value.items) {
    if (!isObject(item) || !isText(item.productId) || !isText(item.code) || !isText(item.name) || !Array.isArray(item.units)) return null
    const units: PriceListProductUnit[] = []
    for (const unit of item.units) {
      if (!isObject(unit) || !isText(unit.unitId) || (unit.role !== 'Sales' && unit.role !== 'Stock') || !isNullableText(unit.code)) return null
      units.push({ unitId: unit.unitId, role: unit.role, code: unit.code })
    }
    items.push({ productId: item.productId, code: item.code, name: item.name, units })
  }
  return { items, hasMore: value.hasMore }
}

export function decodeProblem(value: unknown): PriceListProblem {
  if (!isObject(value)) return { code: null, errors: [] }
  const errors = Array.isArray(value.errors)
    ? value.errors.filter((error): error is PriceListFieldError => isObject(error) && isText(error.field) && isText(error.code))
    : []
  return { code: isText(value.code) ? value.code : null, errors }
}

/* ── input ─────────────────────────────────────────────────────────────────────────── */

/**
 * What a person types as a price → the exact wire text, or null. Digits with ONE decimal separator (comma or dot),
 * at most 15 integer and 4 decimal digits; no thousands separators (an ambiguous "42.000" is not guessed), never
 * rounded. "42000,5" → "42000.5".
 */
export function parseMoneyInput(input: string): string | null {
  const trimmed = input.trim()
  if (!/^[0-9]+([.,][0-9]+)?$/.test(trimmed)) return null
  const normalized = trimmed.replace(',', '.')
  const [integer, fraction] = normalized.split('.')
  const canonicalInteger = integer.replace(/^0+(?=[0-9])/, '')
  const text = fraction === undefined ? canonicalInteger : `${canonicalInteger}.${fraction}`
  return MONEY.test(text) ? text : null
}

/** Quantity tier input uses the backend's decimal(18,3) wire scale. It is preserved as text; no conversion is inferred. */
export function parseQuantityInput(input: string): string | null {
  const trimmed = input.trim()
  if (!/^[0-9]+([.,][0-9]+)?$/.test(trimmed)) return null
  const normalized = trimmed.replace(',', '.')
  const [integer, fraction] = normalized.split('.')
  const canonicalInteger = integer.replace(/^0+(?=[0-9])/, '')
  const text = fraction === undefined ? canonicalInteger : `${canonicalInteger}.${fraction}`
  return QUANTITY.test(text) ? text : null
}

/** Exact decimal text shown the Turkish way: "42000.5" → "42.000,5". The digits are the server's, never recomputed. */
export function formatMoneyText(text: string): string {
  const [integer, fraction] = text.split('.')
  const grouped = integer.replace(/\B(?=(\d{3})+(?!\d))/g, '.')
  return fraction === undefined ? grouped : `${grouped},${fraction}`
}

/** "2026-10-01" → "01.10.2026". */
export function formatDate(text: string): string {
  const [year, month, day] = text.split('-')
  return `${day}.${month}.${year}`
}

/** Today's calendar date on the device, as the wire date. */
export function todayText(now: Date = new Date()): string {
  const month = String(now.getMonth() + 1).padStart(2, '0')
  const day = String(now.getDate()).padStart(2, '0')
  return `${now.getFullYear()}-${month}-${day}`
}

export function isWireDate(text: string): boolean {
  return DATE.test(text)
}
