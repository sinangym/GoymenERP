import { purchasingHeaders, PURCHASING_ORDERS_BASE } from './api'
import { decodeReceiptReversal, decodeReturnResponse, type ReceiptReversalRequest, type ReceiptReversalResponse,
  type SupplierReturnDecision, type SupplierReturnRequest, type SupplierReturnResponse } from './receiptAdjustmentContract'

export type AdjustmentOutcome<T> = { readonly type: 'applied'; readonly response: T }
  | { readonly type: 'failed'; readonly status: number; readonly code: string | null; readonly isReplay?: boolean; readonly detail?: string }
  | { readonly type: 'unknown'; readonly reason: 'offline' | 'network-error' | 'malformed' | 'service-unavailable'; readonly status?: number }

export async function post<T>(company: string, path: string, payload: unknown, key: string, expectedStatus: number,
  decode: (body: unknown, replay: boolean) => T | null): Promise<AdjustmentOutcome<T>> {
  try {
    const response = await fetch(path, { method: 'POST',
      headers: { ...purchasingHeaders(company), 'Content-Type': 'application/json', 'Idempotency-Key': key }, body: JSON.stringify(payload) })
    let body: unknown = null
    try { body = await response.json() } catch { /* A successful malformed answer stays unresolved. */ }
    if (response.status >= 500) return { type: 'unknown', reason: 'service-unavailable', status: response.status }
    if (response.status !== expectedStatus) {
      if (response.status >= 200 && response.status < 300) return { type: 'unknown', reason: 'malformed' }
      const detail = physicalStockRefusalDetail(body, response.status)
      return { type: 'failed', status: response.status, isReplay: response.headers.get('Idempotency-Replayed') === 'true',
        code: typeof body === 'object' && body !== null && 'code' in body && typeof body.code === 'string' ? body.code : null,
        ...(detail === null ? {} : { detail }) }
    }
    const decoded = decode(body, response.headers.get('Idempotency-Replayed') === 'true')
    return decoded === null ? { type: 'unknown', reason: 'malformed' } : { type: 'applied', response: decoded }
  } catch { return { type: 'unknown', reason: navigator.onLine ? 'network-error' : 'offline' } }
}
function physicalStockRefusalDetail(body: unknown, status: number): string | null {
  if (status !== 409 || typeof body !== 'object' || body === null || Array.isArray(body)
    || 'status' in body && body.status !== status || !('detail' in body)
    || typeof body.detail !== 'string' || body.detail.length > 128) return null
  const value = body.detail
  const match = /^Sistemde (-?(?:0|[1-9][0-9]{0,14})(?:\.[0-9]{0,2}[1-9])?) kg var, ((?:0|[1-9][0-9]{0,14})(?:\.[0-9]{0,2}[1-9])?) kg istendi\.$/u.exec(value)
  return match !== null && match[0] === value && match[1] !== '-0' && match[2] !== '0' ? value : null
}
const base = (order: string) => `${PURCHASING_ORDERS_BASE}/${encodeURIComponent(order)}`
export function reverseReceipt(company: string, order: string, acceptance: string, request: ReceiptReversalRequest, key: string): Promise<AdjustmentOutcome<ReceiptReversalResponse>> {
  return post(company, `${base(order)}/goods-receipts/${encodeURIComponent(acceptance)}/reversal`, request, key, 200,
    (body, replay) => decodeReceiptReversal(body, company, order, acceptance, request, replay))
}
export function createSupplierReturn(company: string, order: string, request: SupplierReturnRequest, key: string): Promise<AdjustmentOutcome<SupplierReturnResponse>> {
  return post(company, `${base(order)}/supplier-returns`, request, key, 201,
    (body, replay) => decodeReturnResponse(body, company, order, request.returnId, replay))
}
export function decideSupplierReturn(company: string, order: string, id: string, approve: boolean, request: SupplierReturnDecision, key: string): Promise<AdjustmentOutcome<SupplierReturnResponse>> {
  return post(company, `${base(order)}/supplier-returns/${encodeURIComponent(id)}/cost-exception/${approve ? 'approval' : 'rejection'}`, request, key, 200,
    (body, replay) => decodeReturnResponse(body, company, order, id, replay))
}
