import type { ViewOutcome } from '../shell/uiState'
import { API_BASE, inventoryHeaders } from './api'
import {
  decodeLabelStockScraps,
  decodeStockScrapView,
  describesScrapDecision,
  describesRecordedScrap,
  STOCK_SCRAP_DEFINITE_UNAVAILABLE_CODES,
  type LabelStockScraps,
  type StockScrapDecisionRequest,
  type StockScrapRequest,
  type StockScrapView,
} from './stockScrapContract'

export const INVENTORY_STOCK_SCRAPS_PATH = `${API_BASE}/inventory/stock-scraps`

export function inventoryStockScrapDecisionPath(scrapId: string): string {
  return `${INVENTORY_STOCK_SCRAPS_PATH}/${encodeURIComponent(scrapId)}/decision`
}

export function inventoryLabelStockScrapsPath(labelId: string): string {
  return `${API_BASE}/inventory/labels/${encodeURIComponent(labelId)}/stock-scraps`
}

export type StockScrapCommandOutcome =
  | { readonly type: 'applied'; readonly response: StockScrapView; readonly replayed: boolean }
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
 * payload — never a new key. A 503 with one of STOCK_SCRAP_DEFINITE_UNAVAILABLE_CODES is raised before any write.
 */
async function send(
  url: string,
  payload: unknown,
  context: { readonly companyId: string; readonly idempotencyKey: string },
  describes: (view: StockScrapView) => boolean,
): Promise<StockScrapCommandOutcome> {
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
    const decoded = decodeStockScrapView(body)
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
  if (response.status === 503 && unavailableCode !== null && STOCK_SCRAP_DEFINITE_UNAVAILABLE_CODES.has(unavailableCode)) {
    return { type: 'failed', status: 503, code: unavailableCode }
  }
  return response.status >= 500
    ? { type: 'unknown', reason: 'service-unavailable' }
    : { type: 'unknown', reason: 'unexpected-status' }
}

export function recordStockScrap(
  request: StockScrapRequest,
  context: { readonly companyId: string; readonly idempotencyKey: string },
): Promise<StockScrapCommandOutcome> {
  return send(INVENTORY_STOCK_SCRAPS_PATH, request, context,
    (view) => describesRecordedScrap(view, request, context.companyId))
}

export function decideStockScrap(
  scrapId: string,
  request: StockScrapDecisionRequest,
  context: { readonly companyId: string; readonly idempotencyKey: string },
): Promise<StockScrapCommandOutcome> {
  return send(inventoryStockScrapDecisionPath(scrapId), request, context,
    (view) => describesScrapDecision(view, scrapId, request, context.companyId))
}

export async function fetchLabelStockScraps(
  companyId: string,
  labelId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<LabelStockScraps>> {
  try {
    const response = await fetch(inventoryLabelStockScrapsPath(labelId), {
      headers: inventoryHeaders(companyId), cache: 'no-store', signal,
    })
    const body = await bodyOf(response)
    if (response.status !== 200) return { type: 'http', status: response.status, body }
    const decoded = decodeLabelStockScraps(body)
    return decoded === null || decoded.labelId.toLowerCase() !== labelId.toLowerCase() ||
      decoded.items.some((item) => item.companyId.toLowerCase() !== companyId.toLowerCase())
      ? { type: 'malformed' }
      : { type: 'ok-single', data: decoded }
  } catch {
    if (signal?.aborted) return { type: 'fence-stale' }
    return navigator.onLine ? { type: 'network-error' } : { type: 'offline' }
  }
}
