import type { ViewOutcome } from '../shell/uiState'
import { PURCHASING_ORDERS_BASE, purchasingHeaders } from './api'
import { post, type AdjustmentOutcome } from './receiptAdjustmentApi'
import { decodeReturnResponse, type SupplierReturnResponse } from './receiptAdjustmentContract'
import { decodeSupplierReturnLineagePreview, type SupplierReturnLineagePreview, type SupplierReturnLineageRequest } from './supplierReturnLineageContract'

export interface SupplierReturnLineagePreviewQuery {
  readonly companyId: string
  readonly orderId: string
  readonly acceptanceId: string
  readonly sourceLabelId: string
  readonly quantityKg: string
  readonly pieceCount: number | null
}

/**
 * AK-8b: GET …/goods-receipts/{acceptanceId}/supplier-return-lineage-preview — the price-free plan of a return from a source
 * label. A 200 must decode strictly and answer exactly what was asked; any other status is handed to the caller with its body
 * (401/403/404, a 409/422 with its code, a 503 while the lineage cannot be read); no answer is offline or a network error, an
 * aborted read is fence-stale.
 */
export async function fetchSupplierReturnLineagePreview(query: SupplierReturnLineagePreviewQuery, signal?: AbortSignal):
Promise<ViewOutcome<SupplierReturnLineagePreview>> {
  try {
    const search = new URLSearchParams({ sourceLabelId: query.sourceLabelId, quantityKg: query.quantityKg })
    if (query.pieceCount !== null) search.set('pieceCount', String(query.pieceCount))
    const response = await fetch(`${PURCHASING_ORDERS_BASE}/${encodeURIComponent(query.orderId)}/goods-receipts/${
      encodeURIComponent(query.acceptanceId)}/supplier-return-lineage-preview?${search.toString()}`,
    { headers: purchasingHeaders(query.companyId), signal })
    let body: unknown = null
    try { body = await response.json() } catch { /* A malformed success never enables a command. */ }
    if (response.status !== 200) return { type: 'http', status: response.status, body }
    const preview = decodeSupplierReturnLineagePreview(body, query)
    return preview === null ? { type: 'malformed' } : { type: 'ok-single', data: preview }
  } catch {
    if (signal?.aborted) return { type: 'fence-stale' }
    return navigator.onLine ? { type: 'network-error' } : { type: 'offline' }
  }
}

/**
 * AK-8b: POST …/supplier-returns with a source label. The 201 must be this return, from the posted label, with the posted kg and
 * pieces; anything else stays unresolved (the same key is retried, never a new one).
 */
export function createLineageSupplierReturn(company: string, order: string, request: SupplierReturnLineageRequest, key: string):
Promise<AdjustmentOutcome<SupplierReturnResponse>> {
  return post(company, `${PURCHASING_ORDERS_BASE}/${encodeURIComponent(order)}/supplier-returns`, request, key, 201, (body, replay) => {
    const answer = decodeReturnResponse(body, company, order, request.returnId, replay)
    return answer === null || answer.return.sourceLabelId?.toLowerCase() !== request.sourceLabelId.toLowerCase()
      || answer.return.quantityKg !== request.quantityKg || answer.return.pieceCount !== request.pieceCount
      || answer.return.goodsAcceptanceId.toLowerCase() !== request.goodsAcceptanceId.toLowerCase() ? null : answer
  })
}
