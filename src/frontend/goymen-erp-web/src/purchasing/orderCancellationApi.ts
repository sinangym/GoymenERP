import type { ViewOutcome } from '../shell/uiState'
import { PURCHASING_ORDERS_BASE, purchasingHeaders } from './api'
import {
  decodePurchaseOrderCancelImpact,
  decodePurchaseOrderCommandResponse,
  type PurchaseOrderCancelImpact,
  type PurchaseOrderCommandResponse,
} from './contract'

/**
 * A1-3 (plan API-10; No.161 §2): the cancellation of an approved order's whole unaccepted remainder. `applied` is a 200 whose
 * decoded body is this company's order, now Cancelled, with replay headers that agree with the body; a definite refusal is
 * `failed`; anything else (no answer, a 5xx, a body that cannot be trusted) is `unknown` — the caller keeps the same key,
 * version and reason for an explicit resend.
 */
export type OrderCancellationOutcome =
  | { readonly type: 'applied'; readonly response: PurchaseOrderCommandResponse; readonly replayed: boolean }
  | { readonly type: 'failed'; readonly status: number; readonly code: string | null; readonly replayed: boolean }
  | { readonly type: 'unknown'; readonly reason: 'offline' | 'network-error' | 'service-unavailable' | 'undecodable' | 'unexpected-status' }

function codeOf(body: unknown): string | null {
  if (typeof body !== 'object' || body === null || Array.isArray(body)) return null
  const code = (body as Record<string, unknown>).code
  return typeof code === 'string' && code.length > 0 ? code : null
}

export async function cancelPurchaseOrderRemainder(
  companyId: string,
  orderId: string,
  expectedVersion: string,
  idempotencyKey: string,
  reason: string,
): Promise<OrderCancellationOutcome> {
  let response: Response
  try {
    response = await fetch(`${PURCHASING_ORDERS_BASE}/${encodeURIComponent(orderId)}/cancellation`, {
      method: 'POST',
      headers: {
        ...purchasingHeaders(companyId),
        'Content-Type': 'application/json',
        'Idempotency-Key': idempotencyKey,
      },
      body: JSON.stringify({ expectedVersion, reason }),
    })
  } catch {
    return { type: 'unknown', reason: navigator.onLine ? 'network-error' : 'offline' }
  }
  let body: unknown = null
  try { body = await response.json() } catch { body = null }
  if (response.status === 200) {
    const decoded = decodePurchaseOrderCommandResponse(body)
    const replayHeader = response.headers.get('Idempotency-Replayed')
    const originalHeader = response.headers.get('Idempotency-Original-Correlation-Id')
    const replayHeadersMatch = decoded !== null && (
      decoded.isReplay
        ? replayHeader?.trim().toLowerCase() === 'true' && originalHeader === decoded.originalCorrelationId
        : replayHeader === null && originalHeader === null
    )
    return decoded === null || decoded.detail.order.companyId !== companyId || decoded.detail.order.id !== orderId
      || decoded.detail.order.status !== 'Cancelled' || !replayHeadersMatch
      ? { type: 'unknown', reason: 'undecodable' }
      : { type: 'applied', response: decoded, replayed: decoded.isReplay }
  }
  if ([400, 401, 403, 404, 409, 415, 422].includes(response.status)) {
    return { type: 'failed', status: response.status, code: codeOf(body), replayed: response.headers.get('Idempotency-Replayed') === 'true' }
  }
  return response.status >= 500
    ? { type: 'unknown', reason: 'service-unavailable' }
    : { type: 'unknown', reason: 'unexpected-status' }
}

/** A1-3 (plan API-10): the impact preview of this company's order; the record must be the one asked for. */
export async function fetchPurchaseOrderCancelImpact(
  companyId: string,
  orderId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<PurchaseOrderCancelImpact>> {
  try {
    const response = await fetch(`${PURCHASING_ORDERS_BASE}/${encodeURIComponent(orderId)}/cancel-impact`, {
      headers: purchasingHeaders(companyId), signal,
    })
    let body: unknown = null
    try { body = await response.json() } catch { body = null }
    if (response.status !== 200) return { type: 'http', status: response.status, body }
    const impact = decodePurchaseOrderCancelImpact(body)
    return impact === null || impact.orderId !== orderId ? { type: 'malformed' } : { type: 'ok-single', data: impact }
  } catch {
    if (signal?.aborted) return { type: 'fence-stale' }
    return navigator.onLine ? { type: 'network-error' } : { type: 'offline' }
  }
}
