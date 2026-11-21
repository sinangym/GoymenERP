/**
 * The order cost transport (URT-S17, SAT-D317): `GET /api/v1/manufacturing/orders/{orderId}/production-cost`.
 *
 * A read under the reports grant (`manufacturing.reports.read`) beside the order read. It answers a `ViewOutcome` like
 * the other reads: an exact 200 whose body is this contract for the order asked for, or the HTTP status as it came
 * (another company's order is the same 404 as an unknown id). Nothing is cached by the browser: the cost follows the
 * order's stored facts.
 */

import { MANUFACTURING_BASE, devHeaders } from './api'
import { decodeOrderCost, type OrderCostView } from './orderCostContract'
import type { ViewOutcome } from '../shell/uiState'

export async function fetchOrderCost(
  companyId: string,
  orderId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<OrderCostView>> {
  let response: Response
  try {
    response = await fetch(`${MANUFACTURING_BASE}/orders/${encodeURIComponent(orderId)}/production-cost`, {
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
  const cost = decodeOrderCost(body, orderId)
  return cost === null ? { type: 'malformed' } : { type: 'ok-single', data: cost }
}
