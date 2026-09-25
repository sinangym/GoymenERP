import type { ViewOutcome } from '../shell/uiState'
import {
  decodeInventoryStockPositions,
  decodeOpeningCount,
  decodeOrganizationLocationCandidates,
  decodeProductStockCandidates,
  type OpeningCountRequest,
  type OpeningCountView,
  type OrganizationLocationCandidates,
  type ProductStockCandidates,
  type InventoryStockPositions,
} from './contract'

export const API_BASE = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? '/api/v1'
const DEV_USER = (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user'
const DEV_IDENTITY_ENABLED = import.meta.env.DEV || import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === 'true'
const CANDIDATE_LIMIT = 25

export const INVENTORY_OPENING_COUNTS_PATH = `${API_BASE}/inventory/opening-counts`
export const INVENTORY_PRODUCT_CANDIDATES_PATH = `${API_BASE}/inventory/references/products`
export const INVENTORY_LOCATION_CANDIDATES_PATH = `${API_BASE}/inventory/references/locations`
export const INVENTORY_STOCK_POSITIONS_PATH = `${API_BASE}/inventory/positions`

export function inventoryHeaders(companyId: string): Record<string, string> {
  return {
    ...(DEV_IDENTITY_ENABLED ? { 'X-Development-User': DEV_USER } : {}),
    'X-Company-Id': companyId,
  }
}

async function bodyOf(response: Response): Promise<unknown> {
  try { return await response.json() } catch { return null }
}

function problemCode(body: unknown): string | null {
  if (typeof body !== 'object' || body === null || Array.isArray(body)) return null
  const code = (body as Record<string, unknown>).code
  return typeof code === 'string' && code.length > 0 ? code : null
}

export async function fetchOpeningProducts(
  companyId: string,
  effectiveOn: string,
  q = '',
  signal?: AbortSignal,
): Promise<ViewOutcome<ProductStockCandidates>> {
  const query = new URLSearchParams({ effectiveOn, limit: String(CANDIDATE_LIMIT) })
  const search = q.trim()
  if (search !== '') query.set('q', search)
  try {
    const response = await fetch(`${INVENTORY_PRODUCT_CANDIDATES_PATH}?${query.toString()}`, {
      headers: inventoryHeaders(companyId), cache: 'no-store', signal,
    })
    const body = await bodyOf(response)
    if (response.status !== 200) return { type: 'http', status: response.status, body }
    const decoded = decodeProductStockCandidates(body)
    return decoded === null ? { type: 'malformed' } : { type: 'ok-single', data: decoded }
  } catch {
    if (signal?.aborted) return { type: 'fence-stale' }
    return navigator.onLine ? { type: 'network-error' } : { type: 'offline' }
  }
}

export async function fetchOpeningLocations(
  companyId: string,
  q = '',
  signal?: AbortSignal,
): Promise<ViewOutcome<OrganizationLocationCandidates>> {
  const query = new URLSearchParams({ limit: String(CANDIDATE_LIMIT) })
  const search = q.trim()
  if (search !== '') query.set('q', search)
  try {
    const response = await fetch(`${INVENTORY_LOCATION_CANDIDATES_PATH}?${query.toString()}`, {
      headers: inventoryHeaders(companyId), cache: 'no-store', signal,
    })
    const body = await bodyOf(response)
    if (response.status !== 200) return { type: 'http', status: response.status, body }
    const decoded = decodeOrganizationLocationCandidates(body)
    return decoded === null ? { type: 'malformed' } : { type: 'ok-single', data: decoded }
  } catch {
    if (signal?.aborted) return { type: 'fence-stale' }
    return navigator.onLine ? { type: 'network-error' } : { type: 'offline' }
  }
}

export async function fetchStockPositions(
  companyId: string,
  q = '',
  signal?: AbortSignal,
): Promise<ViewOutcome<InventoryStockPositions>> {
  const query = new URLSearchParams({ limit: String(CANDIDATE_LIMIT) })
  const search = q.trim()
  if (search !== '') query.set('q', search)
  try {
    const response = await fetch(`${INVENTORY_STOCK_POSITIONS_PATH}?${query.toString()}`, {
      headers: inventoryHeaders(companyId), cache: 'no-store', signal,
    })
    const body = await bodyOf(response)
    if (response.status !== 200) return { type: 'http', status: response.status, body }
    const decoded = decodeInventoryStockPositions(body)
    if (decoded === null || decoded.items.some((item) => item.companyId !== companyId)) return { type: 'malformed' }
    return { type: 'ok-single', data: decoded }
  } catch {
    if (signal?.aborted) return { type: 'fence-stale' }
    return navigator.onLine ? { type: 'network-error' } : { type: 'offline' }
  }
}

export type RecordOpeningCountOutcome =
  | { readonly type: 'applied'; readonly response: OpeningCountView; readonly replayed: boolean }
  | { readonly type: 'failed'; readonly status: number; readonly code: string | null; readonly replayed: boolean }
  | { readonly type: 'unknown'; readonly reason: 'offline' | 'network-error' | 'service-unavailable' | 'undecodable' | 'unexpected-status' }

function describesOpening(response: OpeningCountView, request: OpeningCountRequest, companyId: string): boolean {
  return response.companyId === companyId && response.countId === request.countId &&
    response.locationId === request.locationId && response.countedOn === request.countedOn &&
    response.sheetReference === request.sheetReference && response.lines.length === request.lines.length &&
    request.lines.every((line) => {
      const answered = response.lines.find((candidate) => candidate.lineId === line.lineId)
      return answered !== undefined && answered.labelId === line.labelId && answered.labelCode === line.labelCode &&
        answered.lotId === line.lotId && answered.heatNumber === line.heatNumber &&
        answered.coilNumber === line.coilNumber && answered.productId === line.productId &&
        answered.quantityKg === line.quantityKg
    })
}

export async function recordOpeningCount(
  request: OpeningCountRequest,
  context: { readonly companyId: string; readonly idempotencyKey: string },
): Promise<RecordOpeningCountOutcome> {
  let response: Response
  try {
    response = await fetch(INVENTORY_OPENING_COUNTS_PATH, {
      method: 'POST',
      headers: {
        ...inventoryHeaders(context.companyId),
        'Content-Type': 'application/json',
        'Idempotency-Key': context.idempotencyKey,
      },
      body: JSON.stringify(request),
    })
  } catch {
    return { type: 'unknown', reason: navigator.onLine ? 'network-error' : 'offline' }
  }
  const body = await bodyOf(response)
  if (response.status === 200 || response.status === 201) {
    const decoded = decodeOpeningCount(body)
    const replayHeader = response.headers.get('Idempotency-Replayed')
    const originalHeader = response.headers.get('Idempotency-Original-Correlation-Id')
    const replayed = response.status === 200
    const headersMatch = replayed
      ? replayHeader?.trim().toLowerCase() === 'true' && typeof originalHeader === 'string' && originalHeader.length > 0
      : replayHeader === null && originalHeader === null
    return decoded === null || !headersMatch || !describesOpening(decoded, request, context.companyId)
      ? { type: 'unknown', reason: 'undecodable' }
      : { type: 'applied', response: decoded, replayed }
  }
  if ([400, 401, 403, 409, 415, 422].includes(response.status)) {
    return {
      type: 'failed', status: response.status, code: problemCode(body),
      replayed: response.headers.get('Idempotency-Replayed')?.trim().toLowerCase() === 'true',
    }
  }
  return response.status >= 500
    ? { type: 'unknown', reason: 'service-unavailable' }
    : { type: 'unknown', reason: 'unexpected-status' }
}

export async function fetchOpeningCount(
  companyId: string,
  countId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<OpeningCountView>> {
  try {
    const response = await fetch(`${INVENTORY_OPENING_COUNTS_PATH}/${encodeURIComponent(countId)}`, {
      headers: inventoryHeaders(companyId), cache: 'no-store', signal,
    })
    const body = await bodyOf(response)
    if (response.status !== 200) return { type: 'http', status: response.status, body }
    const decoded = decodeOpeningCount(body)
    return decoded === null || decoded.companyId !== companyId || decoded.countId !== countId
      ? { type: 'malformed' }
      : { type: 'ok-single', data: decoded }
  } catch {
    if (signal?.aborted) return { type: 'fence-stale' }
    return navigator.onLine ? { type: 'network-error' } : { type: 'offline' }
  }
}
