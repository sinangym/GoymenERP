import type { ViewOutcome } from '../shell/uiState'
import { purchasingHeaders, PURCHASING_ORDERS_BASE } from './api'
import { decodeSupplierReturnCostSources, type SupplierReturnCostSources } from './supplierReturnSourceContract'

export async function fetchSupplierReturnCostSources(companyId: string, orderId: string, acceptanceId: string,
  labelId: string, signal?: AbortSignal): Promise<ViewOutcome<SupplierReturnCostSources>> {
  try {
    const response = await fetch(`${PURCHASING_ORDERS_BASE}/${encodeURIComponent(orderId)}/goods-receipts/${encodeURIComponent(acceptanceId)}/supplier-return-cost-sources`,
      { headers: purchasingHeaders(companyId), signal })
    let body: unknown = null
    try { body = await response.json() } catch { /* A malformed success never enables a command. */ }
    if (response.status !== 200) return { type: 'http', status: response.status, body }
    const value = decodeSupplierReturnCostSources(body, companyId, acceptanceId, labelId)
    return value === null ? { type: 'malformed' } : { type: 'ok-single', data: value }
  } catch {
    if (signal?.aborted) return { type: 'fence-stale' }
    return navigator.onLine ? { type: 'network-error' } : { type: 'offline' }
  }
}
