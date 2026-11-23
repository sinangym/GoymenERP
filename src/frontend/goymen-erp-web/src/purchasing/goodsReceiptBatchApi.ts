import type { ViewOutcome } from '../shell/uiState'
import { PURCHASING_ORDERS_BASE, purchasingHeaders } from './api'
import { decodeGoodsReceiptBatchPage, decodeGoodsReceiptBatchResponse, type GoodsReceiptBatchPage, type GoodsReceiptBatchResponse,
  type SubmitGoodsReceiptBatchRequest } from './goodsReceiptBatchContract'

/**
 * F-027 (ALC-S16): this company's bulk goods receipts of one order. A 200 must decode strictly and be this company's order;
 * any other status is handed to the caller with its body (the shell classifies 401/403/404/5xx); no answer is offline or a
 * network error, an aborted read is fence-stale.
 */
export async function fetchGoodsReceiptBatches(
  companyId: string,
  orderId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<GoodsReceiptBatchPage>> {
  try {
    const response = await fetch(`${PURCHASING_ORDERS_BASE}/${encodeURIComponent(orderId)}/goods-receipt-batches`,
      { headers: purchasingHeaders(companyId), signal })
    let body: unknown = null
    try { body = await response.json() } catch { body = null }
    if (response.status !== 200) return { type: 'http', status: response.status, body }
    const page = decodeGoodsReceiptBatchPage(body, companyId, orderId)
    return page === null ? { type: 'malformed' } : { type: 'ok-single', data: page }
  } catch {
    if (signal?.aborted) return { type: 'fence-stale' }
    return navigator.onLine ? { type: 'network-error' } : { type: 'offline' }
  }
}

/** One field refusal of a problem body (`errors[]`), e.g. `labels[2].acceptedQuantity`. */
export interface GoodsReceiptBatchFieldProblem { readonly field: string; readonly code: string }

export type GoodsReceiptBatchSubmitOutcome =
  | { readonly type: 'applied'; readonly response: GoodsReceiptBatchResponse }
  | { readonly type: 'failed'; readonly status: number; readonly code: string | null; readonly fields: readonly GoodsReceiptBatchFieldProblem[] }
  | { readonly type: 'unknown'; readonly reason: 'offline' | 'network-error' | 'service-unavailable' | 'malformed' }

function fieldProblems(body: unknown): readonly GoodsReceiptBatchFieldProblem[] {
  if (typeof body !== 'object' || body === null || Array.isArray(body)) return []
  const errors = (body as Record<string, unknown>).errors
  if (!Array.isArray(errors)) return []
  const fields: GoodsReceiptBatchFieldProblem[] = []
  for (const error of errors) {
    if (typeof error !== 'object' || error === null) continue
    const { field, code } = error as Record<string, unknown>
    if (typeof field === 'string' && field.length > 0 && typeof code === 'string' && code.length > 0) fields.push({ field, code })
  }
  return fields
}

/**
 * ALC-S16 / AK-3b: POST …/orders/{id}/lines/{lineId}/goods-receipt-batches with the caller's Idempotency-Key. The body is sent
 * exactly as built (`acceptedQuantity` only where the builder put it). 201 must decode strictly, agree with the replay header and
 * be the batch that was sent; a 5xx, another 2xx or an undecodable 201 is an UNKNOWN outcome (the caller retries with the same key
 * and body); any other status is a refusal with its code and field problems.
 */
export async function submitGoodsReceiptBatch(companyId: string, orderId: string, lineId: string,
  request: SubmitGoodsReceiptBatchRequest, idempotencyKey: string): Promise<GoodsReceiptBatchSubmitOutcome> {
  try {
    const response = await fetch(
      `${PURCHASING_ORDERS_BASE}/${encodeURIComponent(orderId)}/lines/${encodeURIComponent(lineId)}/goods-receipt-batches`,
      { method: 'POST', headers: { ...purchasingHeaders(companyId), 'Content-Type': 'application/json', 'Idempotency-Key': idempotencyKey },
        body: JSON.stringify(request) })
    let body: unknown = null
    try { body = await response.json() } catch { body = null }
    if (response.status >= 500) return { type: 'unknown', reason: 'service-unavailable' }
    if (response.status !== 201) {
      if (response.status >= 200 && response.status < 300) return { type: 'unknown', reason: 'malformed' }
      const code = typeof body === 'object' && body !== null && 'code' in body && typeof body.code === 'string' ? body.code : null
      return { type: 'failed', status: response.status, code, fields: fieldProblems(body) }
    }
    const decoded = decodeGoodsReceiptBatchResponse(body, companyId, orderId, lineId, request,
      response.headers.get('Idempotency-Replayed') === 'true')
    return decoded === null ? { type: 'unknown', reason: 'malformed' } : { type: 'applied', response: decoded }
  } catch {
    return { type: 'unknown', reason: navigator.onLine ? 'network-error' : 'offline' }
  }
}
