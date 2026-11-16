/**
 * The order's lineage transport (URT-S22, API-28): `GET /api/v1/manufacturing/orders/{orderId}/lineage`.
 *
 * A read under the order read permission the order screen already needs (KLN-D44: no new permission). It answers a
 * `ViewOutcome` like the other reads: an exact 200 whose body is this contract for the order asked for, or the HTTP
 * status as it came (another company's order is the same 404 as an unknown id). Nothing is cached by the browser: the
 * lineage follows the order's stored facts.
 */

import { MANUFACTURING_BASE, devHeaders } from './api'
import { decodeOrderLineage, type OrderLineageView } from './lineageContract'
import type { ViewOutcome } from '../shell/uiState'

export async function fetchOrderLineage(
  companyId: string,
  orderId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<OrderLineageView>> {
  let response: Response
  try {
    response = await fetch(`${MANUFACTURING_BASE}/orders/${encodeURIComponent(orderId)}/lineage`, {
      headers: { ...devHeaders(), 'X-Company-Id': companyId },
      cache: 'no-store',
      signal,
    })
  } catch {
    if (signal?.aborted) return { type: 'fence-stale' }
    return navigator.onLine ? { type: 'network-error' } : { type: 'offline' }
  }
  let body: unknown = null
  try {
    body = await response.json()
  } catch {
    if (signal?.aborted) return { type: 'fence-stale' }
    body = null
  }
  // EXACT 200: `Response.ok` spans all of 2xx, which is not this contract.
  if (response.status !== 200) return { type: 'http', status: response.status, body }
  const lineage = decodeOrderLineage(body, orderId)
  return lineage === null ? { type: 'malformed' } : { type: 'ok-single', data: lineage }
}
