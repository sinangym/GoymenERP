import type { ViewOutcome } from '../shell/uiState'
import { PURCHASING_ORDERS_BASE, purchasingHeaders } from './api'
import {
  decodePurchaseFutureAllocationReleaseResponse,
  decodePurchaseOrderFutureAllocationLinks,
  type PurchaseFutureAllocationReleaseResponse,
  type PurchaseOrderFutureAllocationLinks,
} from './allocationLinkContract'
import { decodePurchaseOrderCommandResponse, type PurchaseOrderCommandResponse } from './contract'

/**
 * F-017 §3.5 F17-4: the commands on a purchase order's future allocation links. `applied` is a 200 whose decoded body is the
 * record asked for with replay headers that agree with the body; a definite refusal is `failed`; anything else is `unknown` —
 * the caller keeps the same key and body for an explicit resend.
 */
export type AllocationLinkOutcome<T> =
  | { readonly type: 'applied'; readonly response: T; readonly replayed: boolean }
  | { readonly type: 'failed'; readonly status: number; readonly code: string | null }
  | { readonly type: 'unknown'; readonly reason: 'offline' | 'network-error' | 'service-unavailable' | 'undecodable' | 'unexpected-status' }

export interface SeenAllocation {
  readonly allocationId: string
  readonly expectedAllocationVersion: string
}

const DEFINITE = [400, 401, 403, 404, 409, 415, 422]

function codeOf(body: unknown): string | null {
  if (typeof body !== 'object' || body === null || Array.isArray(body)) return null
  const code = (body as Record<string, unknown>).code
  return typeof code === 'string' && code.length > 0 ? code : null
}

function replayHeadersMatch(response: Response, isReplay: boolean, originalCorrelationId: string): boolean {
  const replayHeader = response.headers.get('Idempotency-Replayed')
  const originalHeader = response.headers.get('Idempotency-Original-Correlation-Id')
  return isReplay
    ? replayHeader?.trim().toLowerCase() === 'true' && originalHeader === originalCorrelationId
    : replayHeader === null && originalHeader === null
}

async function post<T>(
  companyId: string,
  path: string,
  idempotencyKey: string,
  payload: unknown,
  accept: (body: unknown, response: Response) => { readonly response: T; readonly replayed: boolean } | null,
): Promise<AllocationLinkOutcome<T>> {
  let response: Response
  try {
    response = await fetch(`${PURCHASING_ORDERS_BASE}/${path}`, {
      method: 'POST',
      headers: { ...purchasingHeaders(companyId), 'Content-Type': 'application/json', 'Idempotency-Key': idempotencyKey },
      body: JSON.stringify(payload),
    })
  } catch {
    return { type: 'unknown', reason: navigator.onLine ? 'network-error' : 'offline' }
  }
  let body: unknown = null
  try { body = await response.json() } catch { body = null }
  if (response.status === 200) {
    const accepted = accept(body, response)
    return accepted === null ? { type: 'unknown', reason: 'undecodable' } : { type: 'applied', ...accepted }
  }
  if (DEFINITE.includes(response.status)) return { type: 'failed', status: response.status, code: codeOf(body) }
  return response.status >= 500 ? { type: 'unknown', reason: 'service-unavailable' } : { type: 'unknown', reason: 'unexpected-status' }
}

/** ALS-D83: drops one link with a required reason; the order version must come back unchanged. */
export function releasePurchaseOrderFutureAllocation(
  companyId: string,
  orderId: string,
  orderVersion: string,
  allocation: SeenAllocation,
  idempotencyKey: string,
  reasonText: string,
): Promise<AllocationLinkOutcome<PurchaseFutureAllocationReleaseResponse>> {
  const path = `${encodeURIComponent(orderId)}/future-allocations/${encodeURIComponent(allocation.allocationId)}/release`
  return post(companyId, path, idempotencyKey, { expectedAllocationVersion: allocation.expectedAllocationVersion, reasonText }, (body, response) => {
    const decoded = decodePurchaseFutureAllocationReleaseResponse(body)
    return decoded === null || decoded.orderId !== orderId || decoded.allocationId.toLowerCase() !== allocation.allocationId.toLowerCase()
      || (!decoded.isReplay && decoded.orderVersion !== orderVersion)
      || !replayHeadersMatch(response, decoded.isReplay, decoded.originalCorrelationId)
      ? null
      : { response: decoded, replayed: decoded.isReplay }
  })
}

/** ALS-D85: cancels the order's remainder together with exactly the allocations the actor confirmed. */
export function cancelPurchaseOrderWithAllocationRelease(
  companyId: string,
  orderId: string,
  expectedVersion: string,
  seenAllocations: readonly SeenAllocation[],
  idempotencyKey: string,
  reason: string,
): Promise<AllocationLinkOutcome<PurchaseOrderCommandResponse>> {
  return post(companyId, `${encodeURIComponent(orderId)}/cancellation-with-allocation-release`, idempotencyKey,
    { expectedVersion, reason, seenAllocations }, (body, response) => {
      const decoded = decodePurchaseOrderCommandResponse(body)
      return decoded === null || decoded.detail.order.companyId !== companyId || decoded.detail.order.id !== orderId
        || decoded.detail.order.status !== 'Cancelled'
        || !replayHeadersMatch(response, decoded.isReplay, decoded.originalCorrelationId)
        ? null
        : { response: decoded, replayed: decoded.isReplay }
    })
}

/** F17-4: the active links of this company's order; the record must be the one asked for. */
export async function fetchPurchaseOrderFutureAllocations(
  companyId: string,
  orderId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<PurchaseOrderFutureAllocationLinks>> {
  try {
    const response = await fetch(`${PURCHASING_ORDERS_BASE}/${encodeURIComponent(orderId)}/future-allocations`, {
      headers: purchasingHeaders(companyId), signal,
    })
    let body: unknown = null
    try { body = await response.json() } catch { body = null }
    if (response.status !== 200) return { type: 'http', status: response.status, body }
    const links = decodePurchaseOrderFutureAllocationLinks(body)
    return links === null || links.orderId !== orderId ? { type: 'malformed' } : { type: 'ok-single', data: links }
  } catch {
    if (signal?.aborted) return { type: 'fence-stale' }
    return navigator.onLine ? { type: 'network-error' } : { type: 'offline' }
  }
}
