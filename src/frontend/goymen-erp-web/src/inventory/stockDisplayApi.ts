/**
 * ST-01's read of the #597 stock positions in the OPT-IN shape (contract pin d821e6b8, handoff STOK-UX r7).
 *
 * - Sends `X-ERP-Inventory-Stock-Display: 1.0` exactly once and decodes only with `./stockDisplayContract`.
 * - Sends only the published query keys, each at most once: `q`, `limit`, `cursor`, `productId`, `locationId`,
 *   `classification`, and `ownerKind` + `ownerId` together or not at all. Anything else is a 400 on the server.
 * - The cursor is opaque and forward-only; the caller passes back exactly what the server issued.
 * - There is NO fallback to the previously published response: a 401/403/503 is answered as itself (the handoff
 *   forbids a silent retry that would show the same screen with less Product information or another authority).
 *   The legacy client in `./api` stays as it was for its other readers.
 */
import type { ViewOutcome } from '../shell/uiState'
import { INVENTORY_STOCK_POSITIONS_PATH, inventoryHeaders } from './api'
import {
  decodeStockDisplayPage,
  STOCK_CLASSIFICATIONS,
  STOCK_PARTY_KINDS,
  type StockClassification,
  type StockDisplayPage,
  type StockPartyKind,
} from './stockDisplayContract'

export const STOCK_DISPLAY_HEADER = 'X-ERP-Inventory-Stock-Display'
export const STOCK_DISPLAY_VERSION = '1.0'
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

/** The filters the server supports, as the page holds them. Every id comes from a row the server returned. */
export interface StockPositionFilters {
  readonly q: string
  readonly productId: string | null
  readonly locationId: string | null
  readonly classification: StockClassification | null
  readonly owner: StockOwnerFilter | null
}

export const NO_STOCK_FILTERS: StockPositionFilters = {
  q: '', productId: null, locationId: null, classification: null, owner: null,
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
    (filters.owner !== null && (!isStockPartyKind(filters.owner.kind) || !isStockId(filters.owner.id)))) return null
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
): Promise<ViewOutcome<StockDisplayPage>> {
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
    const decoded = decodeStockDisplayPage(body)
    if (decoded === null || decoded.items.some((item) => item.companyId !== companyId)) return { type: 'malformed' }
    return { type: 'ok-single', data: decoded }
  } catch {
    if (signal?.aborted) return { type: 'fence-stale' }
    return navigator.onLine ? { type: 'network-error' } : { type: 'offline' }
  }
}
