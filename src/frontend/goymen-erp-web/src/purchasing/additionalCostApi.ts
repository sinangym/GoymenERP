import { API_BASE, purchasingHeaders } from './api'
import type { ViewOutcome } from '../shell/uiState'
import type { AdjustmentOutcome } from './receiptAdjustmentApi'
import { decodeAdditionalCostSource, decodeAdditionalCostReferences, decodeAdditionalCosts, decodeAdditionalCostReceipt,
  type AdditionalCostKind, type RecordAdditionalCost, type AdditionalCostReceipt } from './additionalCostContract'
const base = API_BASE + '/purchasing/additional-costs'
const code = (kind: AdditionalCostKind) => kind === 'GoodsOrder' ? 1 : 2
async function read<T>(company: string, path: string, signal: AbortSignal, decode: (v: unknown) => T | null): Promise<ViewOutcome<T>> {
  try {
    const response = await fetch(path, { headers: purchasingHeaders(company), signal, cache: 'no-store' })
    if (response.status !== 200) return response.status >= 200 && response.status < 300
      ? { type: 'malformed' } : { type: 'http', status: response.status }
    let raw: unknown; try { raw = await response.json() } catch { return { type: 'malformed' } }
    const data = decode(raw); return data === null ? { type: 'malformed' } : { type: 'ok-single', data }
  } catch { return signal.aborted ? { type: 'fence-stale' } : { type: navigator.onLine ? 'network-error' : 'offline' } }
}
export const fetchAdditionalCostSource = (company: string, kind: AdditionalCostKind, id: string, signal: AbortSignal) =>
  read(company, `${base}/sources/${code(kind)}/${encodeURIComponent(id)}`, signal, v => decodeAdditionalCostSource(v, company, kind, id))
export const fetchAdditionalCosts = (company: string, kind: AdditionalCostKind, id: string, signal: AbortSignal) =>
  read(company, `${base}/sources/${code(kind)}/${encodeURIComponent(id)}/costs`, signal, v => decodeAdditionalCosts(v, company, kind, id))
export const fetchAdditionalCostReferences = (company: string, on: string, signal: AbortSignal) =>
  read(company, `${base}/references?on=${encodeURIComponent(on)}`, signal, v => decodeAdditionalCostReferences(v, company, on))
export async function recordAdditionalCost(company: string, kind: AdditionalCostKind, body: RecordAdditionalCost, key: string): Promise<AdjustmentOutcome<AdditionalCostReceipt>> {
  try {
    const response = await fetch(base, { method: 'POST', headers: { ...purchasingHeaders(company), 'Content-Type': 'application/json', 'Idempotency-Key': key }, body: JSON.stringify(body) })
    let raw: unknown = null; try { raw = await response.json() } catch { /* Keep unknown outcomes unresolved. */ }
    if (response.status >= 500) return { type: 'unknown', reason: 'service-unavailable', status: response.status }
    if ([400, 401, 403, 404, 409, 415, 422].includes(response.status)) return { type: 'failed', status: response.status,
      isReplay: response.headers.get('Idempotency-Replayed') === 'true', code: typeof raw === 'object' && raw !== null && 'code' in raw && typeof raw.code === 'string' ? raw.code : null }
    if (response.status !== 200) return { type: 'unknown', reason: 'malformed' }
    const replay = response.headers.get('Idempotency-Replayed') === 'true', data = decodeAdditionalCostReceipt(raw, company, kind, body.orderId, body.costId, replay)
    if (data === null || replay && response.headers.get('Idempotency-Original-Correlation-Id') !== data.originalCorrelationId) return { type: 'unknown', reason: 'malformed' }
    return { type: 'applied', response: data }
  } catch { return { type: 'unknown', reason: navigator.onLine ? 'network-error' : 'offline' } }
}
