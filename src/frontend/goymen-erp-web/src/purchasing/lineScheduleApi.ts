import type { ViewOutcome } from '../shell/uiState'
import { PURCHASING_ORDERS_BASE, purchasingHeaders } from './api'
import { decodePurchaseOrderLineScheduleHistory, type PurchaseOrderLineScheduleHistory } from './lineScheduleContract'

/**
 * F-027 (ALS-D94): this company's order line schedule history. A 200 must decode strictly and be the order and line asked for;
 * any other status is handed to the caller with its body (the shell classifies 401/403/404/5xx); no answer is offline or a
 * network error, an aborted read is fence-stale.
 */
export async function fetchPurchaseOrderLineScheduleHistory(
  companyId: string,
  orderId: string,
  lineId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<PurchaseOrderLineScheduleHistory>> {
  try {
    const response = await fetch(
      `${PURCHASING_ORDERS_BASE}/${encodeURIComponent(orderId)}/lines/${encodeURIComponent(lineId)}/schedule-history`,
      { headers: purchasingHeaders(companyId), signal },
    )
    let body: unknown = null
    try { body = await response.json() } catch { body = null }
    if (response.status !== 200) return { type: 'http', status: response.status, body }
    const history = decodePurchaseOrderLineScheduleHistory(body, orderId, lineId)
    return history === null ? { type: 'malformed' } : { type: 'ok-single', data: history }
  } catch {
    if (signal?.aborted) return { type: 'fence-stale' }
    return navigator.onLine ? { type: 'network-error' } : { type: 'offline' }
  }
}
