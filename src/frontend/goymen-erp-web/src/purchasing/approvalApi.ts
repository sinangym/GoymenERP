import { PURCHASING_ORDERS_BASE, purchasingHeaders } from './api'
import { decodePurchaseOrderCommandResponse, type PurchaseOrderCommandResponse } from './contract'

export type ApprovalOutcome =
  | { readonly type: 'applied'; readonly response: PurchaseOrderCommandResponse; readonly replayed: boolean }
  | { readonly type: 'failed'; readonly status: number; readonly code: string | null; readonly replayed: boolean }
  | { readonly type: 'unknown'; readonly reason: 'offline' | 'network-error' | 'service-unavailable' | 'undecodable' | 'unexpected-status' }

function codeOf(body: unknown): string | null {
  if (typeof body !== 'object' || body === null || Array.isArray(body)) return null
  const code = (body as Record<string, unknown>).code
  return typeof code === 'string' && code.length > 0 ? code : null
}

export async function approvePurchaseOrder(
  companyId: string,
  orderId: string,
  expectedVersion: string,
  idempotencyKey: string,
): Promise<ApprovalOutcome> {
  let response: Response
  try {
    response = await fetch(`${PURCHASING_ORDERS_BASE}/${encodeURIComponent(orderId)}/approval`, {
      method: 'POST',
      headers: {
        ...purchasingHeaders(companyId),
        'Content-Type': 'application/json',
        'Idempotency-Key': idempotencyKey,
      },
      body: JSON.stringify({ expectedVersion }),
    })
  } catch {
    return { type: 'unknown', reason: navigator.onLine ? 'network-error' : 'offline' }
  }
  let body: unknown = null
  try { body = await response.json() } catch { body = null }
  if (response.status === 200) {
    const decoded = decodePurchaseOrderCommandResponse(body)
    return decoded === null
      ? { type: 'unknown', reason: 'undecodable' }
      : { type: 'applied', response: decoded, replayed: response.headers.get('Idempotency-Replayed') === 'true' }
  }
  if ([400, 401, 403, 404, 409, 415, 422].includes(response.status)) {
    return { type: 'failed', status: response.status, code: codeOf(body), replayed: response.headers.get('Idempotency-Replayed') === 'true' }
  }
  return response.status >= 500
    ? { type: 'unknown', reason: 'service-unavailable' }
    : { type: 'unknown', reason: 'unexpected-status' }
}
