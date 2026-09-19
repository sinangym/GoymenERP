/**
 * Manufacturing read transport — one production order.
 *
 * Thin and hand-written, like the other modules' `api.ts`. HANDOFF: when the generated
 * client lands (Issue #122) the fetch moves to `src/api/generated/**`; the decoder in
 * ./contract.ts survives that move.
 *
 * No client-contract header: that is a Counterparty compatibility mechanism, not this
 * module's requirement.
 */

import { decodeMassBalance, decodeOrder, type MassBalanceView, type ProductionOrderView } from './contract'
import type { ViewOutcome } from '../shell/uiState'

export const API_BASE = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? '/api/v1'
const DEV_USER = (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user'
const DEV_IDENTITY_ENABLED =
  import.meta.env.DEV || import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === 'true'

export function devHeaders(): Record<string, string> {
  return DEV_IDENTITY_ENABLED ? { 'X-Development-User': DEV_USER } : {}
}

export const MANUFACTURING_BASE = `${API_BASE}/manufacturing`

/**
 * `GET /api/v1/manufacturing/orders/{orderId}` — the order rebuilt from its stored facts.
 *
 * This is also the read that runs after an accepted fact. A command's 200 body is the
 * order as it was when THAT command was first answered — a replay repeats it verbatim —
 * so the current figures always come from here, never from a command answer.
 *
 * A 503 is a real, retryable answer here: the server re-reads an order whose facts were
 * committed apart and, if it still meets a commit, says so (FİNANS #313 INFO-4) instead
 * of guessing a balance.
 */
export async function fetchOrder(
  companyId: string,
  orderId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<ProductionOrderView>> {
  let response: Response
  try {
    response = await fetch(`${MANUFACTURING_BASE}/orders/${encodeURIComponent(orderId)}`, {
      headers: { ...devHeaders(), 'X-Company-Id': companyId },
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
  const order = decodeOrder(body)
  return order === null ? { type: 'malformed' } : { type: 'ok-single', data: order }
}

/**
 * `GET /api/v1/manufacturing/orders/{orderId}/mass-balance` — the order through GDC-BAL/1, the one calculator the
 * close uses (BAL slice 2). Same read permission, company context and answers as {@link fetchOrder}; a read, so it
 * runs again after every accepted fact, beside the order read.
 */
export async function fetchMassBalance(
  companyId: string,
  orderId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<MassBalanceView>> {
  let response: Response
  try {
    response = await fetch(`${MANUFACTURING_BASE}/orders/${encodeURIComponent(orderId)}/mass-balance`, {
      headers: { ...devHeaders(), 'X-Company-Id': companyId },
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
  if (response.status !== 200) return { type: 'http', status: response.status, body }
  const balance = decodeMassBalance(body)
  return balance === null ? { type: 'malformed' } : { type: 'ok-single', data: balance }
}
