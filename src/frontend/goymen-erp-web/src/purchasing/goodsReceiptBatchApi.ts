import type { ViewOutcome } from '../shell/uiState'
import { PURCHASING_ORDERS_BASE, purchasingHeaders } from './api'
import { decodeGoodsReceiptBatchPage, type GoodsReceiptBatchPage } from './goodsReceiptBatchContract'

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
