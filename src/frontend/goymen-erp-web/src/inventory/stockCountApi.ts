import type { ViewOutcome } from '../shell/uiState'
import { API_BASE, inventoryHeaders } from './api'
import {
  decodeLabelStockCounts,
  decodeStockCountView,
  describesDecision,
  describesRecordedCount,
  STOCK_COUNT_DEFINITE_UNAVAILABLE_CODES,
  type LabelStockCounts,
  type StockCountDecisionRequest,
  type StockCountRequest,
  type StockCountView,
} from './stockCountContract'

export const INVENTORY_STOCK_COUNTS_PATH = `${API_BASE}/inventory/stock-counts`

export function inventoryStockCountDecisionPath(countId: string): string {
  return `${INVENTORY_STOCK_COUNTS_PATH}/${encodeURIComponent(countId)}/decision`
}

export function inventoryLabelStockCountsPath(labelId: string): string {
  return `${API_BASE}/inventory/labels/${encodeURIComponent(labelId)}/stock-counts`
}

export type StockCountCommandOutcome =
  | { readonly type: 'applied'; readonly response: StockCountView; readonly replayed: boolean }
  | { readonly type: 'failed'; readonly status: number; readonly code: string | null }
  | { readonly type: 'unknown'; readonly reason: 'offline' | 'network-error' | 'service-unavailable' | 'undecodable' | 'unexpected-status' }

async function bodyOf(response: Response): Promise<unknown> {
  try { return await response.json() } catch { return null }
}

function problemCode(body: unknown): string | null {
  if (typeof body !== 'object' || body === null || Array.isArray(body)) return null
  const code = (body as Record<string, unknown>).code
  return typeof code === 'string' && code.length > 0 ? code : null
}

/**
 * One command attempt (R19 shape). 201 is a new record; 200 is the stored answer to the same key and content
 * (`Idempotency-Replayed: true` + the original correlation). An answer that does not describe exactly this request,
 * any code-less or other 5xx, and any transport failure, is UNKNOWN: the caller resends with the same key and
 * payload — never a new key. A 503 with one of STOCK_COUNT_DEFINITE_UNAVAILABLE_CODES is raised before any write.
 */
async function send(
  url: string,
  payload: unknown,
  context: { readonly companyId: string; readonly idempotencyKey: string },
  describes: (view: StockCountView) => boolean,
): Promise<StockCountCommandOutcome> {
  let response: Response
  try {
    response = await fetch(url, {
      method: 'POST',
      headers: {
        ...inventoryHeaders(context.companyId),
        'Content-Type': 'application/json',
        'Idempotency-Key': context.idempotencyKey,
      },
      body: JSON.stringify(payload),
    })
  } catch {
    return { type: 'unknown', reason: navigator.onLine ? 'network-error' : 'offline' }
  }
  const body = await bodyOf(response)
  if (response.status === 200 || response.status === 201) {
    const decoded = decodeStockCountView(body)
    const replayHeader = response.headers.get('Idempotency-Replayed')
    const originalHeader = response.headers.get('Idempotency-Original-Correlation-Id')
    const replayed = response.status === 200
    const headersMatch = replayed
      ? replayHeader?.trim().toLowerCase() === 'true' && typeof originalHeader === 'string' && originalHeader.length > 0
      : replayHeader === null && originalHeader === null
    return decoded === null || !headersMatch || !describes(decoded)
      ? { type: 'unknown', reason: 'undecodable' }
      : { type: 'applied', response: decoded, replayed }
  }
  if ([400, 401, 403, 404, 409, 415, 422].includes(response.status)) {
    return { type: 'failed', status: response.status, code: problemCode(body) }
  }
  const unavailableCode = problemCode(body)
  if (response.status === 503 && unavailableCode !== null && STOCK_COUNT_DEFINITE_UNAVAILABLE_CODES.has(unavailableCode)) {
    return { type: 'failed', status: 503, code: unavailableCode }
  }
  return response.status >= 500
    ? { type: 'unknown', reason: 'service-unavailable' }
    : { type: 'unknown', reason: 'unexpected-status' }
}

export function recordStockCount(
  request: StockCountRequest,
  context: { readonly companyId: string; readonly idempotencyKey: string },
): Promise<StockCountCommandOutcome> {
  return send(INVENTORY_STOCK_COUNTS_PATH, request, context,
    (view) => describesRecordedCount(view, request, context.companyId))
}

export function decideStockCount(
  countId: string,
  request: StockCountDecisionRequest,
  context: { readonly companyId: string; readonly idempotencyKey: string },
): Promise<StockCountCommandOutcome> {
  return send(inventoryStockCountDecisionPath(countId), request, context,
    (view) => describesDecision(view, countId, request, context.companyId))
}

export async function fetchLabelStockCounts(
  companyId: string,
  labelId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<LabelStockCounts>> {
  try {
    const response = await fetch(inventoryLabelStockCountsPath(labelId), {
      headers: inventoryHeaders(companyId), cache: 'no-store', signal,
    })
    const body = await bodyOf(response)
    if (response.status !== 200) return { type: 'http', status: response.status, body }
    const decoded = decodeLabelStockCounts(body)
    return decoded === null || decoded.labelId.toLowerCase() !== labelId.toLowerCase() ||
      decoded.items.some((item) => item.companyId.toLowerCase() !== companyId.toLowerCase())
      ? { type: 'malformed' }
      : { type: 'ok-single', data: decoded }
  } catch {
    if (signal?.aborted) return { type: 'fence-stale' }
    return navigator.onLine ? { type: 'network-error' } : { type: 'offline' }
  }
}
