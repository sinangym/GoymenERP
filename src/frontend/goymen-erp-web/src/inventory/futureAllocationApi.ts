import type { ViewOutcome } from '../shell/uiState'
import { API_BASE, inventoryHeaders } from './api'
import { decodeFutureAllocation, decodeFutureAllocationList, decodeFutureSupplyOptions,
  type CreateFutureAllocation, type ReleaseFutureAllocation, type FutureAllocation, type FutureAllocationList, type FutureSupplyOptions } from './futureAllocationContract'

const linePath = (order: string, line: string) => `/inventory/sales-orders/${encodeURIComponent(order)}/lines/${encodeURIComponent(line)}`
async function read<T>(path: string, company: string, order: string, line: string, decoder: (v: unknown) => T | null,
  signal?: AbortSignal): Promise<ViewOutcome<T>> {
  try {
    const response = await fetch(`${API_BASE}${path}`, { headers: inventoryHeaders(company), cache: 'no-store', signal })
    let body: unknown = null
    try { body = await response.json() } catch { /* malformed */ }
    if (response.status !== 200) return { type: 'http', status: response.status, body }
    const result = decoder(body)
    return result !== null && typeof result === 'object' && 'companyId' in result && result.companyId === company
      && 'salesOrderId' in result && result.salesOrderId === order && 'salesOrderLineId' in result && result.salesOrderLineId === line
      ? { type: 'ok-single', data: result } : { type: 'malformed' }
  } catch { return { type: signal?.aborted ? 'fence-stale' : navigator.onLine ? 'network-error' : 'offline' } }
}
export const fetchFutureAllocations = (company: string, order: string, line: string, signal?: AbortSignal) =>
  read<FutureAllocationList>(`${linePath(order, line)}/future-allocations`, company, order, line, decodeFutureAllocationList, signal)
export const fetchFutureSupplyOptions = (company: string, order: string, line: string, cursor: string | null, signal?: AbortSignal) =>
  read<FutureSupplyOptions>(`${linePath(order, line)}/future-supply-options${cursor === null ? '' : `?cursor=${encodeURIComponent(cursor)}`}`,
    company, order, line, decodeFutureSupplyOptions, signal)

export type FutureAllocationPending = { readonly key: string } & (
  | { readonly kind: 'create'; readonly payload: CreateFutureAllocation }
  | { readonly kind: 'release'; readonly allocationId: string; readonly payload: ReleaseFutureAllocation })
export type FutureAllocationCommandOutcome =
  | { readonly type: 'applied'; readonly allocation: FutureAllocation; readonly replayed: boolean }
  | { readonly type: 'failed'; readonly status: number; readonly code: string | null }
  | { readonly type: 'unknown'; readonly reason: 'service-unavailable' | 'malformed-response' | 'network-error' | 'offline' }
export async function sendFutureAllocation(company: string, order: string, line: string, pending: FutureAllocationPending): Promise<FutureAllocationCommandOutcome> {
  try {
    const response = await fetch(`${API_BASE}/inventory/future-allocations${pending.kind === 'release' ? `/${encodeURIComponent(pending.allocationId)}/release` : ''}`, {
      method: 'POST', headers: { ...inventoryHeaders(company), 'Content-Type': 'application/json', 'Idempotency-Key': pending.key }, body: JSON.stringify(pending.payload),
    })
    let body: unknown = null
    try { body = await response.json() } catch { /* malformed */ }
    const replay = response.headers.get('Idempotency-Replayed')
    const original = response.headers.get('Idempotency-Original-Correlation-Id')
    const validReplay = replay === 'true' && original !== null && original.trim().length > 0
    if (response.status === 201 || response.status === 200) {
      const allocation = decodeFutureAllocation(body)
      if (allocation === null || allocation.companyId !== company || allocation.salesOrderId !== order || allocation.salesOrderLineId !== line
        || allocation.allocationId !== (pending.kind === 'create' ? pending.payload.allocationId : pending.allocationId)
        || (response.status === 200 ? !validReplay : replay !== null || original !== null)) return { type: 'unknown', reason: 'malformed-response' }
      return { type: 'applied', allocation, replayed: response.status === 200 }
    }
    if ([400, 401, 403, 404, 409, 415, 422].includes(response.status)) {
      if (replay !== null || original !== null) {
        if (![409, 422].includes(response.status) || !validReplay) return { type: 'unknown', reason: 'malformed-response' }
      }
      const code = typeof body === 'object' && body !== null && 'code' in body && typeof body.code === 'string' ? body.code : null
      return { type: 'failed', status: response.status, code }
    }
    return { type: 'unknown', reason: response.status >= 500 ? 'service-unavailable' : 'malformed-response' }
  } catch { return { type: 'unknown', reason: navigator.onLine ? 'network-error' : 'offline' } }
}
