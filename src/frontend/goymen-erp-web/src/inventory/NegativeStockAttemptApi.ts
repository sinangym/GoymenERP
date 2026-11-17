import type { ViewOutcome } from '../shell/uiState'
import { API_BASE, inventoryHeaders } from './api'
import { decodeNegativeStockAttemptPage, isNegativeStockAttemptId, NEGATIVE_STOCK_ATTEMPT_LIMIT,
  validNegativeStockAttemptQuery, type NegativeStockAttemptPage, type NegativeStockAttemptQuery } from './NegativeStockAttemptContract'

export const NEGATIVE_STOCK_ATTEMPT_PATH = `${API_BASE}/inventory/negative-stock-attempts`
export const NEGATIVE_STOCK_ATTEMPT_INVALID_QUERY = 'inventory_negative_stock_attempts_invalid_query'
export function negativeStockAttemptProblemCode(body: unknown): string | null {
  if (typeof body !== 'object' || body === null || Array.isArray(body)) return null
  return 'code' in body && typeof body.code === 'string' ? body.code : null
}
export async function fetchNegativeStockAttempts(companyId: string, query: NegativeStockAttemptQuery,
  signal?: AbortSignal): Promise<ViewOutcome<NegativeStockAttemptPage>> {
  if (!isNegativeStockAttemptId(companyId) || !validNegativeStockAttemptQuery(query)) {
    return { type: 'http', status: 400, body: { code: NEGATIVE_STOCK_ATTEMPT_INVALID_QUERY } }
  }
  const search = new URLSearchParams({ openOnly: query.openOnly ? 'true' : 'false', limit: String(NEGATIVE_STOCK_ATTEMPT_LIMIT) })
  if (query.productId !== null) search.set('productId', query.productId)
  if (query.locationId !== null) search.set('locationId', query.locationId)
  try {
    const response = await fetch(`${NEGATIVE_STOCK_ATTEMPT_PATH}?${search.toString()}`, { headers: inventoryHeaders(companyId), cache: 'no-store', signal })
    let body: unknown = null
    try { body = await response.json() } catch { /* A non-JSON success is drift, never an empty result. */ }
    if (signal?.aborted) return { type: 'fence-stale' }
    if (response.status !== 200) return { type: 'http', status: response.status, body }
    const data = decodeNegativeStockAttemptPage(body, companyId, query)
    return data === null ? { type: 'malformed' } : { type: 'ok-single', data }
  } catch {
    if (signal?.aborted) return { type: 'fence-stale' }
    return typeof navigator !== 'undefined' && !navigator.onLine ? { type: 'offline' } : { type: 'network-error' }
  }
}
