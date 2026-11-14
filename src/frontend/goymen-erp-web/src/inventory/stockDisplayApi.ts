/**
 * ST-01's read of the #597 stock positions in the OPT-IN shape (contract pin d821e6b8, handoff STOK-UX r7).
 *
 * - P03.2: sends `X-ERP-Inventory-Stock-Display: 4.0` exactly once (#738: the #664 Product + Location + Owner display
 *   with the Product card's nominal length) and decodes only with the `4.0` decoder of `./stockDisplayContract`. The
 *   `1.0`–`3.0` decoders stay for their readers; a `4.0` answer is never decoded as an older shape, and a failed `4.0`
 *   read is never retried as any of them.
 * - Sends only the published query keys, each at most once: `q`, `limit`, `cursor`, `productId`, `locationId`,
 *   `classification`, `ownerKind` + `ownerId` together or not at all, and the typed filters of #690:
 *   `productFamilyId`, `thicknessMm` or `thicknessMinMm`/`thicknessMaxMm`, `widthMm` or `widthMinMm`/`widthMaxMm`.
 *   Anything else is a 400 on the server. The server filters before it pages; nothing is filtered here.
 * - STOCK-TYPED-FILTER-UI-01: the family choices are the Product owner's authorized list (#695,
 *   `GET /inventory/references/product-families`), never a list collected from the rows on screen.
 * - The cursor is opaque and forward-only; the caller passes back exactly what the server issued.
 * - There is NO fallback to the previously published response: a 401/403/503 is answered as itself (the handoff
 *   forbids a silent retry that would show the same screen with less Product information or another authority).
 *   The legacy client in `./api` stays as it was for its other readers.
 */
import type { ViewOutcome } from '../shell/uiState'
import { API_BASE, INVENTORY_STOCK_POSITIONS_PATH, inventoryHeaders } from './api'
import {
  decodeStockLengthDisplayPage,
  STOCK_CLASSIFICATIONS,
  STOCK_PARTY_KINDS,
  type StockClassification,
  type StockLengthDisplayPage,
  type StockPartyKind,
} from './stockDisplayContract'

export const STOCK_DISPLAY_HEADER = 'X-ERP-Inventory-Stock-Display'
/** The Product-enriched shape (#597); its decoder is kept for its readers. */
export const STOCK_DISPLAY_VERSION_PRODUCT = '1.0'
/** The Product + Location shape (#607); its decoder is kept for its readers. */
export const STOCK_DISPLAY_VERSION_LOCATION = '2.0'
/** The Product + Location + Owner shape (#664); its decoder is kept for its readers. */
export const STOCK_DISPLAY_VERSION_OWNER = '3.0'
/** The same shape with the Product card's nominal length (#738) — the one this screen selects. */
export const STOCK_DISPLAY_VERSION = '4.0'
/** The server's default page size; changing it would only reset the cursor. */
export const STOCK_PAGE_LIMIT = 25
/** The Inventory owner's label-code limit after trimming (`MaterialLabel.MaximumCodeLength`). */
export const STOCK_LABEL_SEARCH_MAX_LENGTH = 64

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const NIL_GUID = '00000000-0000-0000-0000-000000000000'

export interface StockOwnerFilter {
  readonly kind: StockPartyKind
  readonly id: string
}

/**
 * The two nominal measures the server filters on (#690). The nominal length is read and shown (`4.0`), but the server
 * publishes no length filter, so none is offered or sent.
 */
export const STOCK_MEASURES = ['thickness', 'width'] as const
export type StockMeasure = (typeof STOCK_MEASURES)[number]

/**
 * The filters the server supports, as the page holds them. A row id comes from a row the server returned, the family
 * id from the authorized family list, and a measure is the wire decimal the server reads (`1.5`, never `1,5`).
 */
export interface StockPositionFilters {
  readonly q: string
  readonly productId: string | null
  readonly locationId: string | null
  readonly classification: StockClassification | null
  readonly owner: StockOwnerFilter | null
  readonly productFamilyId: string | null
  readonly thicknessMm: string | null
  readonly thicknessMinMm: string | null
  readonly thicknessMaxMm: string | null
  readonly widthMm: string | null
  readonly widthMinMm: string | null
  readonly widthMaxMm: string | null
}

export const NO_STOCK_FILTERS: StockPositionFilters = {
  q: '', productId: null, locationId: null, classification: null, owner: null,
  productFamilyId: null,
  thicknessMm: null, thicknessMinMm: null, thicknessMaxMm: null,
  widthMm: null, widthMinMm: null, widthMaxMm: null,
}

/** The server's own measure grammar (`TryReadOptionalMeasure`): plain digits, at most three fraction digits. */
const WIRE_MEASURE = /^(?:0|[1-9][0-9]*)(?:\.[0-9]{1,3})?$/
const WIRE_MEASURE_MAX_LENGTH = 32

/** A measure the server accepts: its grammar, and more than zero. */
export function isStockMeasure(value: unknown): value is string {
  return typeof value === 'string' && value.length <= WIRE_MEASURE_MAX_LENGTH && WIRE_MEASURE.test(value) && /[1-9]/.test(value)
}

/** Compares two valid wire measures as decimals, digit by digit — no floating point, so nothing is rounded. */
export function compareStockMeasures(left: string, right: string): number {
  const [leftWhole, leftFraction = ''] = left.split('.')
  const [rightWhole, rightFraction = ''] = right.split('.')
  if (leftWhole.length !== rightWhole.length) return leftWhole.length < rightWhole.length ? -1 : 1
  const a = leftWhole + leftFraction.padEnd(3, '0')
  const b = rightWhole + rightFraction.padEnd(3, '0')
  return a === b ? 0 : a < b ? -1 : 1
}

export type StockMeasureInputProblem = 'format' | 'leadingZero' | 'precision' | 'zero'

/**
 * What a person typed for a measure, as the wire decimal. Turkish writing: the fraction follows a comma. A dot is
 * refused rather than guessed, because "1.250" is 1250 in the table and 1,25 on a calculator. The digits are sent
 * as typed: nothing is rounded, cut or padded, and a fourth fraction digit is an error the person sees. A number
 * written with zeros in front ("03") is refused exactly as before; it only gets its own reason.
 */
export function stockMeasureFromInput(text: string): { readonly wire: string } | { readonly problem: StockMeasureInputProblem } {
  const trimmed = text.trim()
  if (/^[0-9]+,[0-9]{4,}$/.test(trimmed)) return { problem: 'precision' }
  if (!/^(?:0|[1-9][0-9]*)(?:,[0-9]{1,3})?$/.test(trimmed) || trimmed.length > WIRE_MEASURE_MAX_LENGTH) {
    const leadingZero = /^0[0-9]+(?:,[0-9]{1,3})?$/.test(trimmed) && trimmed.length <= WIRE_MEASURE_MAX_LENGTH
    return { problem: leadingZero ? 'leadingZero' : 'format' }
  }
  const wire = trimmed.replace(',', '.')
  return isStockMeasure(wire) ? { wire } : { problem: 'zero' }
}

/** A wire measure as the field shows it again: the same digits, the fraction after a comma. */
export function stockMeasureInputText(wire: string | null): string {
  return wire === null ? '' : wire.replace('.', ',')
}

function measureKeys(measure: StockMeasure) {
  return measure === 'thickness'
    ? { exact: 'thicknessMm', min: 'thicknessMinMm', max: 'thicknessMaxMm' } as const
    : { exact: 'widthMm', min: 'widthMinMm', max: 'widthMaxMm' } as const
}

export interface StockMeasureFilter {
  readonly exact: string | null
  readonly min: string | null
  readonly max: string | null
}

export const NO_STOCK_MEASURE: StockMeasureFilter = { exact: null, min: null, max: null }

/** One measure of a filter set: an exact value, or a range with either end open. */
export function stockMeasureFilter(filters: StockPositionFilters, measure: StockMeasure): StockMeasureFilter {
  const keys = measureKeys(measure)
  return { exact: filters[keys.exact], min: filters[keys.min], max: filters[keys.max] }
}

/** The same filter set with one measure replaced. */
export function withStockMeasure(filters: StockPositionFilters, measure: StockMeasure, value: StockMeasureFilter): StockPositionFilters {
  const keys = measureKeys(measure)
  return { ...filters, [keys.exact]: value.exact, [keys.min]: value.min, [keys.max]: value.max }
}

/** The server's rule for one measure: valid values, exact and range never together, and min ≤ max. */
export function isStockMeasureFilterValid({ exact, min, max }: StockMeasureFilter): boolean {
  if ([exact, min, max].some((value) => value !== null && !isStockMeasure(value))) return false
  if (exact !== null && (min !== null || max !== null)) return false
  return min === null || max === null || compareStockMeasures(min, max) <= 0
}

export function isStockId(value: unknown): value is string {
  return typeof value === 'string' && GUID.test(value) && value.toLowerCase() !== NIL_GUID
}

export function isStockClassification(value: unknown): value is StockClassification {
  return typeof value === 'string' && (STOCK_CLASSIFICATIONS as readonly string[]).includes(value)
}

export function isStockPartyKind(value: unknown): value is StockPartyKind {
  return typeof value === 'string' && (STOCK_PARTY_KINDS as readonly string[]).includes(value)
}

/** The label-code search as the server will read it, or null when it is longer than the server accepts. */
export function normalizedLabelSearch(value: string): string | null {
  const trimmed = value.trim()
  return trimmed.length <= STOCK_LABEL_SEARCH_MAX_LENGTH ? trimmed : null
}

/**
 * The exact query: published keys only, each once, in a stable order. Returns null for a filter the server would
 * refuse, so an invalid value is never sent to be rejected.
 */
export function stockPositionQuery(filters: StockPositionFilters, cursor: string | null): URLSearchParams | null {
  const q = normalizedLabelSearch(filters.q)
  if (q === null) return null
  if ((filters.productId !== null && !isStockId(filters.productId)) ||
    (filters.locationId !== null && !isStockId(filters.locationId)) ||
    (filters.classification !== null && !isStockClassification(filters.classification)) ||
    (filters.owner !== null && (!isStockPartyKind(filters.owner.kind) || !isStockId(filters.owner.id))) ||
    (filters.productFamilyId !== null && !isStockId(filters.productFamilyId)) ||
    !STOCK_MEASURES.every((measure) => isStockMeasureFilterValid(stockMeasureFilter(filters, measure)))) return null
  const query = new URLSearchParams()
  if (q !== '') query.set('q', q)
  query.set('limit', String(STOCK_PAGE_LIMIT))
  if (cursor !== null) query.set('cursor', cursor)
  if (filters.productId !== null) query.set('productId', filters.productId)
  if (filters.locationId !== null) query.set('locationId', filters.locationId)
  if (filters.classification !== null) query.set('classification', filters.classification)
  if (filters.owner !== null) {
    query.set('ownerKind', filters.owner.kind)
    query.set('ownerId', filters.owner.id)
  }
  if (filters.productFamilyId !== null) query.set('productFamilyId', filters.productFamilyId)
  for (const name of ['thicknessMm', 'thicknessMinMm', 'thicknessMaxMm', 'widthMm', 'widthMinMm', 'widthMaxMm'] as const) {
    const value = filters[name]
    if (value !== null) query.set(name, value)
  }
  return query
}

async function bodyOf(response: Response): Promise<unknown> {
  try { return await response.json() } catch { return null }
}

/**
 * One page of the opt-in read. A filter the server would refuse is answered `malformed` without a request; a row
 * of another company refuses the whole page (it can never be shown under this company).
 */
export async function fetchStockDisplayPage(
  companyId: string,
  filters: StockPositionFilters,
  cursor: string | null,
  signal?: AbortSignal,
): Promise<ViewOutcome<StockLengthDisplayPage>> {
  const query = stockPositionQuery(filters, cursor)
  if (query === null) return { type: 'malformed' }
  try {
    const response = await fetch(`${INVENTORY_STOCK_POSITIONS_PATH}?${query.toString()}`, {
      headers: { ...inventoryHeaders(companyId), [STOCK_DISPLAY_HEADER]: STOCK_DISPLAY_VERSION },
      cache: 'no-store',
      signal,
    })
    const body = await bodyOf(response)
    if (response.status !== 200) return { type: 'http', status: response.status, body }
    const decoded = decodeStockLengthDisplayPage(body)
    if (decoded === null || decoded.items.some((item) => item.companyId !== companyId)) return { type: 'malformed' }
    return { type: 'ok-single', data: decoded }
  } catch {
    if (signal?.aborted) return { type: 'fence-stale' }
    return navigator.onLine ? { type: 'network-error' } : { type: 'offline' }
  }
}

/** The Product owner's authorized family list for the stock filter (#695); it takes no query at all. */
export const STOCK_PRODUCT_FAMILIES_PATH = `${API_BASE}/inventory/references/product-families`

export interface StockProductFamilyOption {
  readonly familyId: string
  readonly code: string
  readonly displayName: string
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function requiredText(value: unknown): value is string {
  return typeof value === 'string' && value.trim() !== ''
}

/**
 * `{ items: [{ familyId, code, displayName }] }` exactly as the endpoint validates it before answering: at least
 * one family, real ids, non-blank code and name, no id or code twice. Anything else is refused whole — a partly
 * readable family list would be offered as "all families".
 */
export function decodeStockProductFamilyOptions(body: unknown): readonly StockProductFamilyOption[] | null {
  if (!isRecord(body) || !Array.isArray(body.items) || body.items.length === 0) return null
  const options: StockProductFamilyOption[] = []
  for (const item of body.items as readonly unknown[]) {
    if (!isRecord(item) || !isStockId(item.familyId) || !requiredText(item.code) || !requiredText(item.displayName)) return null
    options.push({ familyId: item.familyId.toLowerCase(), code: item.code, displayName: item.displayName })
  }
  if (new Set(options.map((option) => option.familyId)).size !== options.length ||
    new Set(options.map((option) => option.code)).size !== options.length) return null
  return options
}

/** The authorized family list: one request, never retried here and never replaced by a list built from rows. */
export async function fetchStockProductFamilyOptions(
  companyId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<readonly StockProductFamilyOption[]>> {
  try {
    const response = await fetch(STOCK_PRODUCT_FAMILIES_PATH, { headers: inventoryHeaders(companyId), cache: 'no-store', signal })
    const body = await bodyOf(response)
    if (response.status !== 200) return { type: 'http', status: response.status, body }
    const decoded = decodeStockProductFamilyOptions(body)
    return decoded === null ? { type: 'malformed' } : { type: 'ok-single', data: decoded }
  } catch {
    if (signal?.aborted) return { type: 'fence-stale' }
    return navigator.onLine ? { type: 'network-error' } : { type: 'offline' }
  }
}
