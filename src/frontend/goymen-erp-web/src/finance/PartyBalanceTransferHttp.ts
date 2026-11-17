import type { ViewOutcome } from '../shell/uiState'
import { API_BASE, devHeaders } from './http'
import { decodeBalanceTransferPreview, decodeBalanceTransferResult, decodeBalanceTransferView,
  type BalanceTransferPreview, type BalanceTransferRecord, type BalanceTransferResult,
  type BalanceTransferSelection, type BalanceTransferView } from './PartyBalanceTransferContract'
const path = `${API_BASE}/finance/party-balance-transfers`
const headers = (companyId: string) => ({ ...devHeaders(), 'X-Company-Id': companyId, Accept: 'application/json' })
export async function fetchBalanceTransferPreview(companyId: string, selection: BalanceTransferSelection,
  signal?: AbortSignal): Promise<ViewOutcome<BalanceTransferPreview>> {
  try {
    const response = await fetch(`${path}/preview?${new URLSearchParams({ ...selection })}`, { headers: headers(companyId), cache: 'no-store', signal })
    if (response.status !== 200) return { type: 'http', status: response.status }
    let body: unknown
    try { body = await response.json() } catch { return { type: 'malformed' } }
    if (signal?.aborted) return { type: 'fence-stale' }
    const decoded = decodeBalanceTransferPreview(body, companyId, selection)
    return decoded.ok ? { type: 'ok-single', data: decoded.value } : { type: 'malformed' }
  } catch { return signal?.aborted ? { type: 'fence-stale' } : navigator.onLine ? { type: 'network-error' } : { type: 'offline' } }
}
export async function fetchBalanceTransfer(companyId: string, id: string,
  signal?: AbortSignal): Promise<ViewOutcome<BalanceTransferView>> {
  try {
    const response = await fetch(`${path}/${encodeURIComponent(id)}`, { headers: headers(companyId), cache: 'no-store', signal })
    if (response.status !== 200) return { type: 'http', status: response.status }
    let body: unknown
    try { body = await response.json() } catch { return { type: 'malformed' } }
    if (signal?.aborted) return { type: 'fence-stale' }
    if (typeof body !== 'object' || body === null || Object.keys(body).sort().join('|') !== 'outcome|preview|transfer'
      || (body as { outcome: unknown }).outcome !== 'Found' || (body as { preview: unknown }).preview !== null) return { type: 'malformed' }
    const decoded = decodeBalanceTransferView((body as { transfer: unknown }).transfer, companyId)
    return decoded.ok && decoded.value.id === id ? { type: 'ok-single', data: decoded.value } : { type: 'malformed' }
  } catch { return signal?.aborted ? { type: 'fence-stale' } : navigator.onLine ? { type: 'network-error' } : { type: 'offline' } }
}
export type BalanceTransferReply = { readonly kind: 'answered'; readonly result: BalanceTransferResult }
  | { readonly kind: 'refused'; readonly status: number } | { readonly kind: 'unknown' }
export async function recordBalanceTransfer(companyId: string, key: string, request: BalanceTransferRecord): Promise<BalanceTransferReply> {
  try {
    const response = await fetch(path, { method: 'POST', cache: 'no-store',
      headers: { ...headers(companyId), 'Content-Type': 'application/json', 'Idempotency-Key': key }, body: JSON.stringify(request) })
    if ([400, 401, 403, 404, 415].includes(response.status)) return { kind: 'refused', status: response.status }
    if (![200, 409, 422].includes(response.status)) return { kind: 'unknown' }
    const decoded = decodeBalanceTransferResult(await response.json(), companyId, request)
    if (!decoded.ok || decoded.value.replayed !== (response.headers.get('Idempotency-Replayed') === 'true')
      || decoded.value.outcome === 'Unavailable' || decoded.value.outcome === 'IdempotencyConflict'
      || (response.status === 200) !== (decoded.value.outcome === 'Succeeded')
      || (response.status === 422) !== (decoded.value.outcome === 'ValidationFailed')) return { kind: 'unknown' }
    return { kind: 'answered', result: decoded.value }
  } catch { return { kind: 'unknown' } }
}
