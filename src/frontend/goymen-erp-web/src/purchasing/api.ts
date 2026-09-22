import type { ViewOutcome } from '../shell/uiState'
import { decodePurchaseOrderDetail, decodePurchaseOrderList, type PurchaseOrderDetail, type PurchaseOrderList, type PurchaseOrderStatus } from './contract'

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
