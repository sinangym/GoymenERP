import type { ViewOutcome } from '../../shell/uiState'
import { decodePaintCandidates, decodePaintCatalog, decodePaintMutation, decodePaintPreview, PAINT_CANDIDATE_LIMIT, type PaintCandidates, type PaintCatalog, type PaintCommand,
  type PaintMutation, type PaintPreview, type PaintValidateRequest } from './contract'

export const PAINT_API_BASE = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? '/api/v1'
const BASE = '/product-paint-memberships'
const DEV_USER = (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user'
const DEV_IDENTITY_ENABLED = import.meta.env.DEV || import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === 'true'
const headers = (companyId: string): Record<string, string> => ({
  ...(DEV_IDENTITY_ENABLED ? { 'X-Development-User': DEV_USER } : {}), 'X-Company-Id': companyId,
})

async function read<T>(path: string, companyId: string, decode: (body: unknown) => T | null, signal?: AbortSignal,
  payload?: PaintValidateRequest): Promise<ViewOutcome<T>> {
  let response: Response
  try {
    response = await fetch(`${PAINT_API_BASE}${BASE}${path}`, {
      method: payload === undefined ? 'GET' : 'POST', signal,
      headers: { ...headers(companyId), ...(payload === undefined ? {} : { 'Content-Type': 'application/json' }) },
      ...(payload === undefined ? {} : { body: JSON.stringify(payload) }),
    })
  } catch {
    if (signal?.aborted) return { type: 'fence-stale' }
    return { type: typeof navigator !== 'undefined' && !navigator.onLine ? 'offline' : 'network-error' }
  }
  if (response.status !== 200) {
    let body: unknown = null
    try { body = await response.json() } catch { /* A refusal is not a data envelope. */ }
    return { type: 'http', status: response.status, body }
  }
  try {
    const data = decode(await response.json())
    return data === null ? { type: 'malformed' } : { type: 'ok-single', data }
  } catch { return { type: 'malformed' } }
}
export function fetchPaintCatalog(companyId: string, signal?: AbortSignal): Promise<ViewOutcome<PaintCatalog>> {
  return read('/current', companyId, (body) => decodePaintCatalog(body, companyId), signal)
}
export function fetchPaintCandidates(companyId: string, q: string | null, signal?: AbortSignal): Promise<ViewOutcome<PaintCandidates>> {
  const parameters = new URLSearchParams({ limit: String(PAINT_CANDIDATE_LIMIT) })
  if (q !== null) parameters.set('q', q)
  return read(`/candidates?${parameters.toString()}`, companyId,
    (body) => decodePaintCandidates(body, companyId, q), signal)
}
export function validatePaintFile(companyId: string, request: PaintValidateRequest,
  sha256: string, signal?: AbortSignal): Promise<ViewOutcome<PaintPreview>> {
  return read('/imports/validate', companyId, (body) => decodePaintPreview(body, companyId,
    { sha256, column: request.productCodeColumn }), signal, request)
}
export type PaintCommandOutcome =
  | { readonly type: 'applied'; readonly data: PaintMutation }
  | { readonly type: 'refused'; readonly status: number; readonly body: unknown; readonly terminalReplay?: boolean }
  | { readonly type: 'unknown'; readonly reason: 'offline' | 'network-error' | 'service-unavailable' | 'malformed-response' }

/** Unknown results always retain the caller's exact key and payload. No transport retry or key generation. */
export async function sendPaintCommand(companyId: string, command: PaintCommand, key: string): Promise<PaintCommandOutcome> {
  const path = command.kind === 'import' ? '/imports' : `/products/${encodeURIComponent(command.productId)}`
  let response: Response
  try {
    response = await fetch(`${PAINT_API_BASE}${BASE}${path}`, {
      method: command.kind === 'import' ? 'POST' : 'PUT',
      headers: { ...headers(companyId), 'Content-Type': 'application/json', 'Idempotency-Key': key },
      body: JSON.stringify(command.payload),
    })
  } catch {
    return { type: 'unknown', reason: typeof navigator !== 'undefined' && !navigator.onLine ? 'offline' : 'network-error' }
  }
  if (response.status === 200) {
    try {
      const data = decodePaintMutation(await response.json(), companyId, command)
      const replay = response.headers.get('Idempotency-Replayed')
      const original = response.headers.get('Idempotency-Original-Correlation-Id')
      const consistent = data !== null && (data.isReplay
        ? replay === 'true' && original === data.originalCorrelationId : replay === null && original === null)
      return data === null || !consistent ? { type: 'unknown', reason: 'malformed-response' } : { type: 'applied', data }
    } catch { return { type: 'unknown', reason: 'malformed-response' } }
  }
  if (response.status >= 500 || response.status < 400) {
    return { type: 'unknown', reason: response.status >= 500 ? 'service-unavailable' : 'malformed-response' }
  }
  let body: unknown = null
  try { body = await response.json() } catch { /* Keep the definite HTTP status. */ }
  const code = typeof body === 'object' && body !== null && 'code' in body ? body.code : null
  const original = response.headers.get('Idempotency-Original-Correlation-Id')
  const terminalReplay = [400, 404, 409, 422].includes(response.status)
    && response.headers.get('Idempotency-Replayed') === 'true' && original !== null && original.trim() !== ''
    && typeof code === 'string' && code.startsWith('product_paint_membership_')
  return { type: 'refused', status: response.status, body, terminalReplay }
}
