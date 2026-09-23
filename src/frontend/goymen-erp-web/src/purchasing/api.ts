import type { ViewOutcome } from '../shell/uiState'
import { fetchCounterparties } from '../cari/api'
import { PARTY_ROLE_FLAG } from '../cari/contract'
import {
  decodeProductPurchaseCandidates,
  decodePurchaseOrderCommandResponse,
  decodePurchaseOrderDetail,
  decodePurchaseOrderList,
  decodePurchaseRequestCommandResponse,
  decodePurchaseRequestDetail,
  decodePurchaseRequestList,
  type CreatePurchaseRequestRequest,
  type CreatePurchaseOrderRequest,
  type ProductPurchaseCandidates,
  type PurchaseOrderCommandResponse,
  type PurchasingSupplierOffer,
  type PurchaseOrderDetail,
  type PurchaseOrderList,
  type PurchaseOrderStatus,
  type PurchaseRequestCommandResponse,
  type PurchaseRequestDetail,
  type PurchaseRequestList,
  type PurchaseRequestStatus,
} from './contract'

export const API_BASE = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? '/api/v1'
const DEV_USER = (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user'
const DEV_IDENTITY_ENABLED = import.meta.env.DEV || import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === 'true'

export function purchasingHeaders(companyId: string): Record<string, string> {
  return {
    ...(DEV_IDENTITY_ENABLED ? { 'X-Development-User': DEV_USER } : {}),
    'X-Company-Id': companyId,
  }
}

export const PURCHASING_ORDERS_BASE = `${API_BASE}/purchasing/orders`
export const PURCHASING_REQUESTS_BASE = `${API_BASE}/purchasing/requests`
export const PURCHASING_PRODUCT_CANDIDATES_PATH = `${API_BASE}/purchasing/references/products`
const PRODUCT_CANDIDATE_LIMIT = 25

export type CreatePurchaseOrderOutcome =
  | { readonly type: 'applied'; readonly response: PurchaseOrderCommandResponse; readonly replayed: boolean }
  | { readonly type: 'failed'; readonly status: number; readonly code: string | null; readonly replayed: boolean }
  | { readonly type: 'unknown'; readonly reason: 'offline' | 'network-error' | 'service-unavailable' | 'undecodable' | 'unexpected-status' }

export type CreatePurchaseRequestOutcome =
  | { readonly type: 'applied'; readonly response: PurchaseRequestCommandResponse; readonly replayed: boolean }
  | { readonly type: 'failed'; readonly status: number; readonly code: string | null; readonly replayed: boolean }
  | { readonly type: 'unknown'; readonly reason: 'offline' | 'network-error' | 'service-unavailable' | 'undecodable' | 'unexpected-status' }

async function bodyOf(response: Response): Promise<unknown> {
  try { return await response.json() } catch { return null }
}

export async function fetchPurchaseOrders(
  companyId: string,
  query: { readonly cursor?: string | null; readonly status?: PurchaseOrderStatus | null } = {},
  signal?: AbortSignal,
): Promise<ViewOutcome<PurchaseOrderList>> {
  const search = new URLSearchParams()
  if (query.cursor) search.set('cursor', query.cursor)
  if (query.status) search.set('status', query.status)
  try {
    const suffix = search.toString()
    const response = await fetch(`${PURCHASING_ORDERS_BASE}${suffix ? `?${suffix}` : ''}`, {
      headers: purchasingHeaders(companyId), signal,
    })
    const body = await bodyOf(response)
    if (response.status !== 200) return { type: 'http', status: response.status, body }
    const page = decodePurchaseOrderList(body)
    return page === null || page.items.some((item) => item.companyId !== companyId)
      ? { type: 'malformed' }
      : { type: 'ok-single', data: page }
  } catch {
    if (signal?.aborted) return { type: 'fence-stale' }
    return navigator.onLine ? { type: 'network-error' } : { type: 'offline' }
  }
}

export async function fetchPurchaseOrder(
  companyId: string,
  orderId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<PurchaseOrderDetail>> {
  try {
    const response = await fetch(`${PURCHASING_ORDERS_BASE}/${encodeURIComponent(orderId)}`, {
      headers: purchasingHeaders(companyId), signal,
    })
    const body = await bodyOf(response)
    if (response.status !== 200) return { type: 'http', status: response.status, body }
    const detail = decodePurchaseOrderDetail(body)
    return detail === null || detail.order.companyId !== companyId || detail.order.id !== orderId
      ? { type: 'malformed' }
      : { type: 'ok-single', data: detail }
  } catch {
    if (signal?.aborted) return { type: 'fence-stale' }
    return navigator.onLine ? { type: 'network-error' } : { type: 'offline' }
  }
}

/**
 * Reads Product-owned purchase candidates for the exact business date. Selection remains a hint:
 * create/save must still send productId + productVersion + unitId + effectiveOn for fresh validation.
 * Central contract: #399 exact fc0264ba043c74ff540ec23ca19110ff02c95760,
 * merged as ec2d7424f3b46bddd7b6da326b111a741db70269.
 */
export async function fetchPurchasingProductCandidates(
  companyId: string,
  effectiveOn: string,
  q = '',
  signal?: AbortSignal,
): Promise<ViewOutcome<ProductPurchaseCandidates>> {
  const query = new URLSearchParams({ effectiveOn, limit: String(PRODUCT_CANDIDATE_LIMIT) })
  const search = q.trim()
  if (search !== '') query.set('q', search)
  try {
    const response = await fetch(`${PURCHASING_PRODUCT_CANDIDATES_PATH}?${query.toString()}`, {
      headers: purchasingHeaders(companyId), cache: 'no-store', signal,
    })
    const body = await bodyOf(response)
    if (response.status !== 200) return { type: 'http', status: response.status, body }
    const candidates = decodeProductPurchaseCandidates(body)
    return candidates === null ? { type: 'malformed' } : { type: 'ok-single', data: candidates }
  } catch {
    if (signal?.aborted) return { type: 'fence-stale' }
    return navigator.onLine ? { type: 'network-error' } : { type: 'offline' }
  }
}

/**
 * Reads the existing Cari owner list with the exact Purchasing selector filters. The list's numeric
 * versions are intentionally not projected: create revalidates current party/account state and does
 * not accept a new party-version concurrency policy.
 */
export async function fetchPurchasingSupplierOptions(
  companyId: string,
  query: { readonly q?: string; readonly cursor?: string } = {},
  signal?: AbortSignal,
): Promise<ViewOutcome<PurchasingSupplierOffer>> {
  const outcome = await fetchCounterparties(
    { mode: 'single', companyId },
    {
      q: query.q,
      role: ['Supplier'],
      partyState: 'active',
      accountState: 'active',
      pageSize: 50,
      cursor: query.cursor,
    },
    signal,
  )
  if (outcome.type === 'ok') return { type: 'malformed' }
  if (outcome.type !== 'ok-single') return outcome

  const items: PurchasingSupplierOffer['items'][number][] = []
  const seen = new Set<string>()
  for (const party of outcome.data.items) {
    const accounts = party.companyAccounts.filter((account) =>
      account.companyId === companyId && account.isActive && (account.roles & PARTY_ROLE_FLAG.Supplier) !== 0)
    if (!party.isActive || accounts.length !== 1) return { type: 'malformed' }
    const account = accounts[0]
    const key = `${party.id}|${account.id}`
    if (seen.has(key)) return { type: 'malformed' }
    seen.add(key)
    items.push({
      supplierPartyId: party.id,
      companyAccountId: account.id,
      label: `${party.code} — ${party.legalName}`,
    })
  }
  return { type: 'ok-single', data: { items, nextCursor: outcome.data.nextCursor } }
}

function problemCode(body: unknown): string | null {
  if (typeof body !== 'object' || body === null || Array.isArray(body)) return null
  const code = (body as Record<string, unknown>).code
  return typeof code === 'string' && code.length > 0 ? code : null
}

function describesCreate(response: PurchaseOrderCommandResponse, request: CreatePurchaseOrderRequest, companyId: string): boolean {
  const { order, lines } = response.detail
  return order.companyId === companyId && order.status === 'Draft' && order.supplierPartyId === request.supplierPartyId &&
    order.companyAccountId === request.companyAccountId && order.sourceRequestId === request.sourceRequestId &&
    order.effectiveOn === request.effectiveOn && lines.length === request.lines.length &&
    request.lines.every((line) => {
      const answered = lines.find((candidate) => candidate.lineId === line.lineId)
      return answered !== undefined && answered.productId === line.productId &&
        answered.productVersion === line.expectedProductVersion && answered.unitId === line.unitId &&
        answered.ordered === line.quantity && answered.sourceRequestLineId === line.sourceRequestLineId
    })
}

/** One immutable create attempt. Unknown outcomes are retried with the same payload and key. */
export async function createPurchaseOrder(
  request: CreatePurchaseOrderRequest,
  context: { readonly companyId: string; readonly idempotencyKey: string },
  signal?: AbortSignal,
): Promise<CreatePurchaseOrderOutcome> {
  let response: Response
  try {
    response = await fetch(PURCHASING_ORDERS_BASE, {
      method: 'POST',
      headers: {
        ...purchasingHeaders(context.companyId),
        'Content-Type': 'application/json',
        'Idempotency-Key': context.idempotencyKey,
      },
      body: JSON.stringify(request),
      signal,
    })
  } catch {
    return { type: 'unknown', reason: navigator.onLine ? 'network-error' : 'offline' }
  }
  let body: unknown = null
  try { body = await response.json() } catch { body = null }
  if (response.status === 201) {
    const decoded = decodePurchaseOrderCommandResponse(body)
    const replayHeader = response.headers.get('Idempotency-Replayed')
    const originalHeader = response.headers.get('Idempotency-Original-Correlation-Id')
    const replayHeadersMatch = decoded !== null && (decoded.isReplay
      ? replayHeader?.trim().toLowerCase() === 'true' && originalHeader === decoded.originalCorrelationId
      : replayHeader === null && originalHeader === null)
    const location = response.headers.get('Location')
    return decoded === null || !replayHeadersMatch || !describesCreate(decoded, request, context.companyId) ||
      location === null || !location.endsWith(`/api/v1/purchasing/orders/${decoded.detail.order.id}`)
      ? { type: 'unknown', reason: 'undecodable' }
      : { type: 'applied', response: decoded, replayed: decoded.isReplay }
  }
  if ([400, 401, 403, 409, 415, 422].includes(response.status)) {
    return {
      type: 'failed', status: response.status, code: problemCode(body),
      replayed: response.headers.get('Idempotency-Replayed')?.trim().toLowerCase() === 'true',
    }
  }
  return response.status >= 500
    ? { type: 'unknown', reason: 'service-unavailable' }
    : { type: 'unknown', reason: 'unexpected-status' }
}

export async function fetchPurchaseRequests(
  companyId: string,
  query: { readonly cursor?: string | null; readonly status?: PurchaseRequestStatus | null } = {},
  signal?: AbortSignal,
): Promise<ViewOutcome<PurchaseRequestList>> {
  const search = new URLSearchParams()
  if (query.cursor) search.set('cursor', query.cursor)
  if (query.status) search.set('status', query.status)
  try {
    const suffix = search.toString()
    const response = await fetch(`${PURCHASING_REQUESTS_BASE}${suffix ? `?${suffix}` : ''}`, {
      headers: purchasingHeaders(companyId), signal,
    })
    const body = await bodyOf(response)
    if (response.status !== 200) return { type: 'http', status: response.status, body }
    const page = decodePurchaseRequestList(body)
    return page === null || page.items.some((item) => item.companyId !== companyId)
      ? { type: 'malformed' }
      : { type: 'ok-single', data: page }
  } catch {
    if (signal?.aborted) return { type: 'fence-stale' }
    return navigator.onLine ? { type: 'network-error' } : { type: 'offline' }
  }
}

export async function fetchPurchaseRequest(
  companyId: string,
  requestId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<PurchaseRequestDetail>> {
  try {
    const response = await fetch(`${PURCHASING_REQUESTS_BASE}/${encodeURIComponent(requestId)}`, {
      headers: purchasingHeaders(companyId), signal,
    })
    const body = await bodyOf(response)
    if (response.status !== 200) return { type: 'http', status: response.status, body }
    const detail = decodePurchaseRequestDetail(body)
    return detail === null || detail.request.companyId !== companyId || detail.request.id !== requestId
      ? { type: 'malformed' }
      : { type: 'ok-single', data: detail }
  } catch {
    if (signal?.aborted) return { type: 'fence-stale' }
    return navigator.onLine ? { type: 'network-error' } : { type: 'offline' }
  }
}

function describesRequestCreate(
  response: PurchaseRequestCommandResponse,
  request: CreatePurchaseRequestRequest,
  companyId: string,
): boolean {
  const { request: answered, lines } = response.detail
  return answered.companyId === companyId && answered.status === 'Draft' && answered.effectiveOn === request.effectiveOn &&
    lines.length === request.lines.length && request.lines.every((line) => {
      const result = lines.find((candidate) => candidate.lineId === line.lineId)
      return result !== undefined && result.productId === line.productId &&
        result.productVersion === line.expectedProductVersion && result.unitId === line.unitId && result.quantity === line.quantity
    })
}

export async function createPurchaseRequest(
  request: CreatePurchaseRequestRequest,
  context: { readonly companyId: string; readonly idempotencyKey: string },
  signal?: AbortSignal,
): Promise<CreatePurchaseRequestOutcome> {
  let response: Response
  try {
    response = await fetch(PURCHASING_REQUESTS_BASE, {
      method: 'POST',
      headers: {
        ...purchasingHeaders(context.companyId),
        'Content-Type': 'application/json',
        'Idempotency-Key': context.idempotencyKey,
      },
      body: JSON.stringify(request), signal,
    })
  } catch {
    return { type: 'unknown', reason: navigator.onLine ? 'network-error' : 'offline' }
  }
  const body = await bodyOf(response)
  if (response.status === 201) {
    const decoded = decodePurchaseRequestCommandResponse(body)
    const replayHeader = response.headers.get('Idempotency-Replayed')
    const originalHeader = response.headers.get('Idempotency-Original-Correlation-Id')
    const replayHeadersMatch = decoded !== null && (decoded.isReplay
      ? replayHeader?.trim().toLowerCase() === 'true' && originalHeader === decoded.originalCorrelationId
      : replayHeader === null && originalHeader === null)
    const location = response.headers.get('Location')
    return decoded === null || !replayHeadersMatch || !describesRequestCreate(decoded, request, context.companyId) ||
      location === null || !location.endsWith(`/api/v1/purchasing/requests/${decoded.detail.request.id}`)
      ? { type: 'unknown', reason: 'undecodable' }
      : { type: 'applied', response: decoded, replayed: decoded.isReplay }
  }
  if ([400, 401, 403, 409, 415, 422].includes(response.status)) {
    return {
      type: 'failed', status: response.status, code: problemCode(body),
      replayed: response.headers.get('Idempotency-Replayed')?.trim().toLowerCase() === 'true',
    }
  }
  return response.status >= 500
    ? { type: 'unknown', reason: 'service-unavailable' }
    : { type: 'unknown', reason: 'unexpected-status' }
}
