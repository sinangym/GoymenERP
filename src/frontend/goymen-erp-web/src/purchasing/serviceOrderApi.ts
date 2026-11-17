import { API_BASE, purchasingHeaders } from './api'
import type { AdjustmentOutcome } from './receiptAdjustmentApi'
import type { ViewOutcome } from '../shell/uiState'
import { decodeServiceOrder, decodeServiceOrders, decodeServiceReferences, decodeServiceReceipt, type ServiceOrderReceipt } from './serviceOrderContract'
const base = `${API_BASE}/purchasing/service-orders`
async function read<T>(company: string, path: string, signal: AbortSignal, decode: (v: unknown) => T | null): Promise<ViewOutcome<T>> {
  try {
    const response = await fetch(path, { headers: purchasingHeaders(company), signal, cache: 'no-store' })
    if (!response.ok) return { type: 'http', status: response.status }
    if (response.status !== 200) return { type: 'malformed' }
    let body: unknown; try { body = await response.json() } catch { return { type: 'malformed' } }
    const value = decode(body); return value === null ? { type: 'malformed' } : { type: 'ok-single', data: value }
  } catch { return signal.aborted ? { type: 'fence-stale' } : { type: navigator.onLine ? 'network-error' : 'offline' } }
}
export const fetchServiceOrders = (company: string, signal: AbortSignal) => read(company, base, signal, v => decodeServiceOrders(v, company))
export const fetchServiceOrder = (company: string, id: string, signal: AbortSignal) => read(company, `${base}/${encodeURIComponent(id)}`, signal, v => decodeServiceOrder(v, company, id))
export const fetchServiceOrderReferences = (company: string, signal: AbortSignal) => read(company, `${base}/references`, signal, v => decodeServiceReferences(v, company))
export async function submitServiceOrder(company: string, id: string, action: 'create' | 'approve' | 'accept' | 'prices', payload: unknown, key: string): Promise<AdjustmentOutcome<ServiceOrderReceipt>> {
  try {
    const path = action === 'create' ? base : `${base}/${encodeURIComponent(id)}/${action === 'approve' ? 'approval' : action === 'prices' ? 'price-revisions' : 'service-acceptances'}`
    const response = await fetch(path, { method: 'POST', headers: { ...purchasingHeaders(company), 'Content-Type': 'application/json', 'Idempotency-Key': key }, body: JSON.stringify(payload) })
    let body: unknown = null; try { body = await response.json() } catch { /* Unknown outcomes keep the same request. */ }
    if (response.status >= 500) return { type: 'unknown', reason: 'service-unavailable', status: response.status }
    if (!response.ok && [400, 401, 403, 404, 409, 415, 422].includes(response.status)) return { type: 'failed', status: response.status,
      isReplay: response.headers.get('Idempotency-Replayed') === 'true', code: typeof body === 'object' && body !== null && 'code' in body && typeof body.code === 'string' ? body.code : null }
    if (response.status !== 200) return { type: 'unknown', reason: 'malformed' }
    const replay = response.headers.get('Idempotency-Replayed') === 'true', value = decodeServiceReceipt(body, company, id, replay)
    if (value === null || replay && response.headers.get('Idempotency-Original-Correlation-Id') !== value.originalCorrelationId)
      return { type: 'unknown', reason: 'malformed' }
    return { type: 'applied', response: value }
  } catch { return { type: 'unknown', reason: navigator.onLine ? 'network-error' : 'offline' } }
}
