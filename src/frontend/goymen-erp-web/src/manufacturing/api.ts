/**
 * Manufacturing read transport — one production order, one production plan.
 *
 * Thin and hand-written, like the other modules' `api.ts`. HANDOFF: when the generated
 * client lands (Issue #122) the fetch moves to `src/api/generated/**`; the decoder in
 * ./contract.ts survives that move.
 *
 * No client-contract header: that is a Counterparty compatibility mechanism, not this
 * module's requirement.
 */

import {
  decodeMassBalance,
  decodeManufacturingOutputLocationCandidates,
  decodeManufacturingOutputProductCandidates,
  decodeOrder,
  decodeOrderList,
  decodePlan,
  type MassBalanceView,
  type ManufacturingOutputLocationCandidates,
  type ManufacturingOutputProductCandidates,
  type OrderListView,
  type OrderStatus,
  type PlanStatus,
  type ProductionOrderView,
  type ProductionPlanView,
} from './contract'
import { decodePlanList, type PlanListView } from './planListContract'
import type { ViewOutcome } from '../shell/uiState'

export const API_BASE = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? '/api/v1'
const DEV_USER = (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user'
const DEV_IDENTITY_ENABLED =
  import.meta.env.DEV || import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === 'true'

export function devHeaders(): Record<string, string> {
  return DEV_IDENTITY_ENABLED ? { 'X-Development-User': DEV_USER } : {}
}

export const MANUFACTURING_BASE = `${API_BASE}/manufacturing`
const OUTPUT_CANDIDATE_LIMIT = 25

async function outputReference<T>(
  companyId: string,
  path: string,
  decode: (value: unknown) => T | null,
  signal?: AbortSignal,
): Promise<ViewOutcome<T>> {
  let response: Response
  try {
    response = await fetch(`${MANUFACTURING_BASE}/references/${path}`, {
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
  }
  if (response.status !== 200) return { type: 'http', status: response.status, body }
  const decoded = decode(body)
  return decoded === null ? { type: 'malformed' } : { type: 'ok-single', data: decoded }
}

export function fetchManufacturingOutputProducts(
  companyId: string,
  effectiveOn: string,
  q = '',
  signal?: AbortSignal,
): Promise<ViewOutcome<ManufacturingOutputProductCandidates>> {
  const query = new URLSearchParams({ effectiveOn, limit: String(OUTPUT_CANDIDATE_LIMIT) })
  const search = q.trim()
  if (search.length > 0) query.set('q', search)
  return outputReference(
    companyId,
    `output-products?${query.toString()}`,
    decodeManufacturingOutputProductCandidates,
    signal,
  )
}

export function fetchManufacturingOutputLocations(
  companyId: string,
  q = '',
  signal?: AbortSignal,
): Promise<ViewOutcome<ManufacturingOutputLocationCandidates>> {
  const query = new URLSearchParams({ limit: String(OUTPUT_CANDIDATE_LIMIT) })
  const search = q.trim()
  if (search.length > 0) query.set('q', search)
  return outputReference(
    companyId,
    `output-locations?${query.toString()}`,
    decodeManufacturingOutputLocationCandidates,
    signal,
  )
}

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

/**
 * `GET /api/v1/manufacturing/plans/{planId}` — the stored plan, reopened.
 *
 * Its own narrow grant (`manufacturing.plans.read`): every write grant in the module together does
 * not add up to it, so a caller who may plan is not thereby a caller who may reopen. Another
 * company's plan is the same 404 as an unknown id — never a 403, which would confirm the plan
 * exists — and this layer passes that status through rather than turning it into an empty view.
 *
 * A 503 is a real, retryable answer, as for {@link fetchOrder}: a read that keeps meeting a
 * concurrent commit says so instead of guessing (FİNANS #313 INFO-4).
 */
export async function fetchPlan(
  companyId: string,
  planId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<ProductionPlanView>> {
  let response: Response
  try {
    response = await fetch(`${MANUFACTURING_BASE}/plans/${encodeURIComponent(planId)}`, {
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
  const plan = decodePlan(body)
  return plan === null ? { type: 'malformed' } : { type: 'ok-single', data: plan }
}

/** What the list asks for; every part is optional and the server decides the page it answers. */
export interface OrderListQuery {
  readonly pageSize?: number
  readonly cursor?: string | null
  readonly status?: OrderStatus | null
}

/**
 * `GET /api/v1/manufacturing/orders` — one page of the company's orders, newest first (MFG-ORD).
 *
 * The same read grant as {@link fetchOrder}: another company's order is in no page. The cursor is the
 * server's own opaque value, sent back unchanged; this layer never builds, parses or guesses one, and a
 * cursor the server refuses is that 400, never a silent first page.
 */
export async function fetchOrders(
  companyId: string,
  query: OrderListQuery = {},
  signal?: AbortSignal,
): Promise<ViewOutcome<OrderListView>> {
  const search = new URLSearchParams()
  if (query.pageSize !== undefined) search.set('pageSize', String(query.pageSize))
  if (query.cursor !== undefined && query.cursor !== null) search.set('cursor', query.cursor)
  if (query.status !== undefined && query.status !== null) search.set('status', query.status)
  const suffix = search.toString()
  let response: Response
  try {
    response = await fetch(`${MANUFACTURING_BASE}/orders${suffix.length === 0 ? '' : `?${suffix}`}`, {
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
  const page = decodeOrderList(body)
  return page === null ? { type: 'malformed' } : { type: 'ok-single', data: page }
}

/** What the plan list asks for; every part is optional and the server decides the page it answers. */
export interface PlanListQuery {
  readonly pageSize?: number
  readonly cursor?: string | null
  readonly status?: PlanStatus | null
}

/**
 * `GET /api/v1/manufacturing/plans` — one page of the company's saved plans, newest first (MFG-PLAN-LIST).
 *
 * The plan read's own grant (`manufacturing.plans.read`), as {@link fetchPlan}: another company's plan is in no
 * page. The cursor is the server's own opaque value, sent back unchanged; this layer never builds, parses or guesses
 * one, and a cursor the server refuses is that 400, never a silent first page. A 503 is a real, retryable answer.
 */
export async function fetchPlans(
  companyId: string,
  query: PlanListQuery = {},
  signal?: AbortSignal,
): Promise<ViewOutcome<PlanListView>> {
  const search = new URLSearchParams()
  if (query.pageSize !== undefined) search.set('pageSize', String(query.pageSize))
  if (query.cursor !== undefined && query.cursor !== null) search.set('cursor', query.cursor)
  if (query.status !== undefined && query.status !== null) search.set('status', query.status)
  const suffix = search.toString()
  let response: Response
  try {
    response = await fetch(`${MANUFACTURING_BASE}/plans${suffix.length === 0 ? '' : `?${suffix}`}`, {
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
  const page = decodePlanList(body)
  return page === null ? { type: 'malformed' } : { type: 'ok-single', data: page }
}
