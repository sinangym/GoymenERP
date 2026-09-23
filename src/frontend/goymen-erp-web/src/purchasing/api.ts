import type { ViewOutcome } from '../shell/uiState'
import {
  decodeProductPurchaseCandidates,
  decodePurchaseOrderDetail,
  decodePurchaseOrderList,
  type ProductPurchaseCandidates,
  type PurchaseOrderDetail,
  type PurchaseOrderList,
  type PurchaseOrderStatus,
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
export const PURCHASING_PRODUCT_CANDIDATES_PATH = `${API_BASE}/purchasing/references/products`
const PRODUCT_CANDIDATE_LIMIT = 25

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
