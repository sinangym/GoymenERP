import type { ViewOutcome } from '../../shell/uiState'
import { decodeCogsMutation, decodeCogsView, type CogsMutation, type CogsView, type SetCogsRequest } from './contract'

export const COGS_API_BASE = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? '/api/v1'
const DEV_USER = (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user'
const DEV_IDENTITY_ENABLED = import.meta.env.DEV || import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === 'true'
const path = (productId: string) => `${COGS_API_BASE}/product-cogs-classifications/${encodeURIComponent(productId)}`
const headers = (companyId: string): Record<string, string> => ({
  ...(DEV_IDENTITY_ENABLED ? { 'X-Development-User': DEV_USER } : {}), 'X-Company-Id': companyId,
})
export async function fetchCogs(companyId: string, productId: string, signal?: AbortSignal): Promise<ViewOutcome<CogsView>> {
  let response: Response
  try { response = await fetch(path(productId), { headers: headers(companyId), signal }) }
  catch {
    if (signal?.aborted) return { type: 'fence-stale' }
    return { type: typeof navigator !== 'undefined' && !navigator.onLine ? 'offline' : 'network-error' }
  }
  if (response.status !== 200) {
    let body: unknown = null
    try { body = await response.json() } catch { /* No data envelope is released. */ }
    return { type: 'http', status: response.status, body }
  }
  try {
    const data = decodeCogsView(await response.json(), companyId, productId)
    return data === null ? { type: 'malformed' } : { type: 'ok-single', data }
  } catch { return { type: 'malformed' } }
}
export type CogsCommandOutcome =
  | { readonly type: 'applied'; readonly data: CogsMutation }
  | { readonly type: 'refused'; readonly status: number; readonly terminalReplay: boolean }
  | { readonly type: 'unknown'; readonly reason: 'offline' | 'network-error' | 'service-unavailable' | 'malformed-response' }

/** This transport never mints or rotates a key. An undecodable success may have committed. */
export async function setCogs(companyId: string, productId: string, request: SetCogsRequest, key: string): Promise<CogsCommandOutcome> {
  let response: Response
  try {
    response = await fetch(path(productId), { method: 'PUT', headers: { ...headers(companyId), 'Content-Type': 'application/json', 'Idempotency-Key': key },
      body: JSON.stringify(request) })
  } catch { return { type: 'unknown', reason: typeof navigator !== 'undefined' && !navigator.onLine ? 'offline' : 'network-error' } }
  if (response.status === 200) {
    try {
      const data = decodeCogsMutation(await response.json(), companyId, productId, request)
      const replay = response.headers.get('Idempotency-Replayed')
      const original = response.headers.get('Idempotency-Original-Correlation-Id')
      return data === null || !(data.isReplay ? replay === 'true' && original === data.originalCorrelationId : replay === null && original === null)
        ? { type: 'unknown', reason: 'malformed-response' } : { type: 'applied', data }
    } catch { return { type: 'unknown', reason: 'malformed-response' } }
  }
  if (response.status >= 500 || response.status < 400) return { type: 'unknown', reason: response.status >= 500 ? 'service-unavailable' : 'malformed-response' }
  let body: unknown = null
  try { body = await response.json() } catch { /* A later plain refusal cannot settle an earlier lost outcome. */ }
  const code = typeof body === 'object' && body !== null && 'code' in body ? body.code : null
  const original = response.headers.get('Idempotency-Original-Correlation-Id')
  const terminalReplay = ((response.status === 404 && code === 'product_cogs_classification_product_not_found')
    || (response.status === 409 && code === 'product_cogs_classification_version_conflict'))
    && response.headers.get('Idempotency-Replayed') === 'true' && original !== null && original.trim() !== '' && original.length <= 128
  return { type: 'refused', status: response.status, terminalReplay }
}
