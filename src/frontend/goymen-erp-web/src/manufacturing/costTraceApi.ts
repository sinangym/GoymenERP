/**
 * The order's cost trace transport (URT-S19, B-27): `GET /api/v1/manufacturing/orders/{orderId}/cost-trace`.
 *
 * A read under its own grant (`manufacturing.reports.read`, API-42) beside the order read. It answers a `ViewOutcome`
 * like the other reads: an exact 200 whose body is this contract for the order asked for, or the HTTP status as it came
 * (another company's order is the same 404 as an unknown id). Nothing is cached by the browser: the trace follows the
 * order's stored facts.
 */

import { MANUFACTURING_BASE, devHeaders } from './api'
import { decodeCostTrace, type CostTraceView } from './costTraceContract'
import type { ViewOutcome } from '../shell/uiState'

export async function fetchCostTrace(
  companyId: string,
  orderId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<CostTraceView>> {
  let response: Response
  try {
    response = await fetch(`${MANUFACTURING_BASE}/orders/${encodeURIComponent(orderId)}/cost-trace`, {
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
  const trace = decodeCostTrace(body, orderId)
  return trace === null ? { type: 'malformed' } : { type: 'ok-single', data: trace }
}
