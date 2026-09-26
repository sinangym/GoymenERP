import { purchasingHeaders, PURCHASING_ORDERS_BASE } from './api'
import type { PurchaseOrderDetail, PurchaseOrderLine } from './contract'
import type { ViewOutcome } from '../shell/uiState'
import { decodeGoodsReceiptOptions, decodeGoodsReceiptPage, decodeGoodsReceiptResponse,
  type GoodsReceiptOptions, type GoodsReceiptPage, type GoodsReceiptRequest, type GoodsReceiptResponse } from './goodsReceiptContract'

export type ReceiptOutcome = { readonly type: 'applied'; readonly response: GoodsReceiptResponse }
  | { readonly type: 'failed'; readonly status: number; readonly code: string | null }
  | { readonly type: 'unknown' }

const base = (orderId: string) => `${PURCHASING_ORDERS_BASE}/${encodeURIComponent(orderId)}`
const lineBase = (orderId: string, lineId: string) => `${base(orderId)}/lines/${encodeURIComponent(lineId)}`
async function bodyOf(response: Response): Promise<unknown> { try { return await response.json() } catch { return null } }

async function read<T>(path: string, companyId: string, decode: (body: unknown) => T | null, signal?: AbortSignal): Promise<ViewOutcome<T>> {
  try {
    const response = await fetch(path, { headers: purchasingHeaders(companyId), signal })
    const body = await bodyOf(response)
    if (response.status !== 200) return { type: 'http', status: response.status, body }
    const data = decode(body)
    return data === null ? { type: 'malformed' } : { type: 'ok-single', data }
  } catch {
    return signal?.aborted ? { type: 'fence-stale' } : navigator.onLine ? { type: 'network-error' } : { type: 'offline' }
  }
}

export function fetchGoodsReceipts(detail: PurchaseOrderDetail, signal?: AbortSignal): Promise<ViewOutcome<GoodsReceiptPage>> {
  return read(`${base(detail.order.id)}/goods-receipts`, detail.order.companyId, (body) => decodeGoodsReceiptPage(body, detail), signal)
}

export function fetchGoodsReceiptOptions(detail: PurchaseOrderDetail, line: PurchaseOrderLine, signal?: AbortSignal): Promise<ViewOutcome<GoodsReceiptOptions>> {
  return read(`${lineBase(detail.order.id, line.lineId)}/goods-receipt-options`, detail.order.companyId,
    (body) => decodeGoodsReceiptOptions(body, detail, line), signal)
}

export async function recordGoodsReceipt(detail: PurchaseOrderDetail, lineId: string, payload: GoodsReceiptRequest,
  idempotencyKey: string): Promise<ReceiptOutcome> {
  try {
    const response = await fetch(`${lineBase(detail.order.id, lineId)}/goods-receipts`, {
      method: 'POST', headers: { ...purchasingHeaders(detail.order.companyId), 'Content-Type': 'application/json', 'Idempotency-Key': idempotencyKey },
      body: JSON.stringify(payload),
    })
    const body = await bodyOf(response)
    if (response.status >= 500) return { type: 'unknown' }
    if (response.status !== 201) {
      if (response.status >= 200 && response.status < 300) return { type: 'unknown' }
      const code = typeof body === 'object' && body !== null && 'code' in body && typeof body.code === 'string' ? body.code : null
      return { type: 'failed', status: response.status, code }
    }
    const decoded = decodeGoodsReceiptResponse(body, detail, lineId, payload, response.headers.get('Idempotency-Replayed') === 'true')
    return decoded === null ? { type: 'unknown' } : { type: 'applied', response: decoded }
  } catch { return { type: 'unknown' } }
}
