/**
 * KLN-D42 dated shortage warning (`GET /api/v1/inventory/shortage-warnings`, `InventoryShortageWarningsV1`).
 *
 * A strict, fail-closed decoder: exact keys, closed enums, canonical kilogram text and the owner's shape rules. The
 * figures come from the one backend calculator; this screen never recomputes a shortage, a cover or a date.
 */

export const SHORTAGE_STATUSES = ['Covered', 'CoverageDateUnknown', 'LateCovered', 'Shortage', 'Unconvertible'] as const
export type ShortageStatus = typeof SHORTAGE_STATUSES[number]

export const SHORTAGE_COVERAGES = ['Covered', 'CoverageDateUnknown', 'LateCovered', 'Uncovered'] as const
export type ShortageCoverage = typeof SHORTAGE_COVERAGES[number]

export const SHORTAGE_SUPPLY_DATES = ['Answered', 'NotAnswered'] as const
export type ShortageSupplyDates = typeof SHORTAGE_SUPPLY_DATES[number]

export const SHORTAGE_SIMILAR_STATUSES = ['NotConfigured', 'Answered', 'Unavailable'] as const
export type ShortageSimilarStatus = typeof SHORTAGE_SIMILAR_STATUSES[number]

export interface ShortageLine {
  readonly orderId: string
  readonly documentNo: string
  readonly lineId: string
  readonly lineNo: number
  readonly dueDate: string | null
  readonly openKg: string
  readonly reservedKg: string
  readonly fromStockKg: string
  readonly fromSupplyKg: string
  readonly uncoveredKg: string
  readonly coverage: ShortageCoverage
  readonly latestSupplyDate: string | null
}

export interface ShortageSimilarProduct {
  readonly productId: string
  readonly productCode: string
  readonly productName: string
  readonly freeStockKg: string
}

export interface ShortageSimilarStock {
  readonly status: ShortageSimilarStatus
  readonly items: readonly ShortageSimilarProduct[]
}

/** Every kilogram figure is null exactly when the status is `Unconvertible`; such a product lists no line. */
export interface ShortageProduct {
  readonly productId: string
  readonly productCode: string
  readonly productName: string
  readonly status: ShortageStatus
  readonly openDemandKg: string | null
  readonly reservedKg: string | null
  readonly netDemandKg: string | null
  readonly freeStockKg: string | null
  readonly openSupplyKg: string | null
  readonly shortageKg: string | null
  readonly earliestDueDate: string | null
  readonly lines: readonly ShortageLine[]
  readonly similarStock: ShortageSimilarStock
}

export interface ShortageWarnings {
  readonly companyId: string
  readonly supplyDates: ShortageSupplyDates
  readonly items: readonly ShortageProduct[]
}

type JsonObject = Record<string, unknown>
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const NIL_GUID = '00000000-0000-0000-0000-000000000000'
/** Canonical non-negative kg as the calculator writes it: no leading zeros, no trailing decimal zeros; `0` allowed. */
const CANONICAL_KG = /^(0|[1-9]\d*)(\.\d*[1-9])?$/
/** A similar product's free stock is passed through from its owner: any non-negative invariant decimal. */
const PLAIN_KG = /^\d+(\.\d+)?$/
const DATE = /^\d{4}-\d{2}-\d{2}$/
const MAX_TEXT_LENGTH = 256

const PRODUCT_KEYS = [
  'productId', 'productCode', 'productName', 'status', 'openDemandKg', 'reservedKg', 'netDemandKg', 'freeStockKg',
  'openSupplyKg', 'shortageKg', 'earliestDueDate', 'lines', 'similarStock',
] as const
const LINE_KEYS = [
  'orderId', 'documentNo', 'lineId', 'lineNo', 'dueDate', 'openKg', 'reservedKg', 'fromStockKg', 'fromSupplyKg',
  'uncoveredKg', 'coverage', 'latestSupplyDate',
] as const

function hasControl(value: string): boolean {
  for (const character of value) {
    const code = character.codePointAt(0)!
    if (code <= 0x1f || (code >= 0x7f && code <= 0x9f)) return true
  }
  return false
}

function record(value: unknown): JsonObject | null {
  return typeof value === 'object' && value !== null && !Array.isArray(value) ? value as JsonObject : null
}

function exact(value: JsonObject, names: readonly string[]): boolean {
  const actual = Object.keys(value).sort()
  const expected = [...names].sort()
  return actual.length === expected.length && actual.every((name, index) => name === expected[index])
}

function guid(value: unknown): value is string {
  return typeof value === 'string' && GUID.test(value) && value.toLowerCase() !== NIL_GUID
}

function text(value: unknown): value is string {
  return typeof value === 'string' && value.length > 0 && value.length <= MAX_TEXT_LENGTH && value.trim() === value
    && !hasControl(value)
}

function kg(value: unknown): value is string {
  return typeof value === 'string' && CANONICAL_KG.test(value)
}

function isZero(value: string): boolean {
  return value === '0'
}

/** A real calendar date (no 2026-02-30) or null. */
function dateOrNull(value: unknown): value is string | null {
  if (value === null) return true
  if (typeof value !== 'string' || !DATE.test(value)) return false
  const [year, month, day] = value.split('-').map(Number)
  const parsed = new Date(Date.UTC(year, month - 1, day))
  return parsed.getUTCFullYear() === year && parsed.getUTCMonth() === month - 1 && parsed.getUTCDate() === day
}

function oneOf<T extends string>(values: readonly T[], value: unknown): value is T {
  return typeof value === 'string' && (values as readonly string[]).includes(value)
}

function decodeLine(value: unknown): ShortageLine | null {
  const line = record(value)
  if (line === null || !exact(line, LINE_KEYS)) return null
  if (!guid(line.orderId) || !text(line.documentNo) || !guid(line.lineId)) return null
  if (typeof line.lineNo !== 'number' || !Number.isSafeInteger(line.lineNo) || line.lineNo < 1) return null
  if (!dateOrNull(line.dueDate) || !dateOrNull(line.latestSupplyDate)) return null
  if (!kg(line.openKg) || !kg(line.reservedKg) || !kg(line.fromStockKg) || !kg(line.fromSupplyKg)
    || !kg(line.uncoveredKg)) return null
  if (isZero(line.openKg) || !oneOf(SHORTAGE_COVERAGES, line.coverage)) return null
  // The owner's coverage rules, checked as shape only: an uncovered line carries uncovered kg and only it does.
  if ((line.coverage === 'Uncovered') === isZero(line.uncoveredKg)) return null
  if (line.coverage === 'LateCovered' && (line.dueDate === null || line.latestSupplyDate === null
    || line.latestSupplyDate <= line.dueDate || isZero(line.fromSupplyKg))) return null
  if (line.coverage === 'CoverageDateUnknown' && isZero(line.fromSupplyKg)) return null
  if (isZero(line.fromSupplyKg) && line.latestSupplyDate !== null) return null
  return {
    orderId: line.orderId, documentNo: line.documentNo, lineId: line.lineId, lineNo: line.lineNo,
    dueDate: line.dueDate, openKg: line.openKg, reservedKg: line.reservedKg, fromStockKg: line.fromStockKg,
    fromSupplyKg: line.fromSupplyKg, uncoveredKg: line.uncoveredKg, coverage: line.coverage,
    latestSupplyDate: line.latestSupplyDate,
  }
}

function decodeSimilar(value: unknown, productId: string): ShortageSimilarStock | null {
  const similar = record(value)
  if (similar === null || !exact(similar, ['status', 'items']) || !oneOf(SHORTAGE_SIMILAR_STATUSES, similar.status)
    || !Array.isArray(similar.items)) return null
  if (similar.status !== 'Answered' && similar.items.length > 0) return null
  const items: ShortageSimilarProduct[] = []
  const seen = new Set<string>()
  for (const raw of similar.items) {
    const item = record(raw)
    if (item === null || !exact(item, ['productId', 'productCode', 'productName', 'freeStockKg'])) return null
    if (!guid(item.productId) || item.productId.toLowerCase() === productId.toLowerCase()
      || seen.has(item.productId.toLowerCase())) return null
    if (!text(item.productCode) || !text(item.productName)) return null
    if (typeof item.freeStockKg !== 'string' || !PLAIN_KG.test(item.freeStockKg)) return null
    seen.add(item.productId.toLowerCase())
    items.push({
      productId: item.productId, productCode: item.productCode, productName: item.productName,
      freeStockKg: item.freeStockKg,
    })
  }
  return { status: similar.status, items }
}

function decodeProduct(value: unknown): ShortageProduct | null {
  const product = record(value)
  if (product === null || !exact(product, PRODUCT_KEYS)) return null
  if (!guid(product.productId) || !text(product.productCode) || !text(product.productName)) return null
  if (!oneOf(SHORTAGE_STATUSES, product.status) || !dateOrNull(product.earliestDueDate)
    || !Array.isArray(product.lines)) return null
  const figures = [product.openDemandKg, product.reservedKg, product.netDemandKg, product.freeStockKg,
    product.openSupplyKg, product.shortageKg]
  const unconvertible = product.status === 'Unconvertible'
  if (unconvertible) {
    // Nothing is summed without a conversion owner: no figure, no line.
    if (figures.some((figure) => figure !== null) || product.lines.length > 0) return null
  } else if (!figures.every(kg)) {
    return null
  }
  const lines: ShortageLine[] = []
  const seen = new Set<string>()
  for (const raw of product.lines) {
    const line = decodeLine(raw)
    if (line === null || seen.has(line.lineId.toLowerCase())) return null
    seen.add(line.lineId.toLowerCase())
    lines.push(line)
  }
  if (!unconvertible && lines.length === 0 && product.status !== 'Covered') return null
  // A shortage is announced exactly when the calculator left uncovered kilograms.
  if (!unconvertible && (product.status === 'Shortage') === isZero(product.shortageKg as string)) return null
  const similarStock = decodeSimilar(product.similarStock, product.productId)
  if (similarStock === null) return null
  return {
    productId: product.productId, productCode: product.productCode, productName: product.productName,
    status: product.status,
    openDemandKg: product.openDemandKg as string | null, reservedKg: product.reservedKg as string | null,
    netDemandKg: product.netDemandKg as string | null, freeStockKg: product.freeStockKg as string | null,
    openSupplyKg: product.openSupplyKg as string | null, shortageKg: product.shortageKg as string | null,
    earliestDueDate: product.earliestDueDate, lines, similarStock,
  }
}

/**
 * Null on any deviation. `companyId` must be the context company the request was sent for, and when `supplyDates` is
 * `NotAnswered` no line may claim a purchase date (the owner could not answer one).
 */
export function decodeShortageWarnings(value: unknown, companyId: string): ShortageWarnings | null {
  const body = record(value)
  if (body === null || !exact(body, ['companyId', 'supplyDates', 'items'])) return null
  if (!guid(body.companyId) || body.companyId.toLowerCase() !== companyId.toLowerCase()) return null
  if (!oneOf(SHORTAGE_SUPPLY_DATES, body.supplyDates) || !Array.isArray(body.items)) return null
  const items: ShortageProduct[] = []
  const seen = new Set<string>()
  for (const raw of body.items) {
    const product = decodeProduct(raw)
    if (product === null || seen.has(product.productId.toLowerCase())) return null
    if (body.supplyDates === 'NotAnswered' && product.lines.some((line) =>
      line.latestSupplyDate !== null || line.coverage === 'LateCovered')) return null
    seen.add(product.productId.toLowerCase())
    items.push(product)
  }
  return { companyId: body.companyId, supplyDates: body.supplyDates, items }
}
