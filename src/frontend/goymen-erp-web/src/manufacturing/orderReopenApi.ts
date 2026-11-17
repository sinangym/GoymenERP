import { MANUFACTURING_BASE, devHeaders } from './api'
import { decodeOrder } from './contract'

export interface OrderReopenAttempt { readonly idempotencyKey: string; readonly expectedVersion: number; readonly reason: string }
export type OrderReopenResult = { readonly type: 'applied' } | { readonly type: 'failed'; readonly status: number; readonly code: string | null } | { readonly type: 'unknown' }
const guid = /^(?!00000000-0000-0000-0000-000000000000)[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
export function decodeReopenAttempt(value: unknown): OrderReopenAttempt | null {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return null
  const v = value as Record<string, unknown>
  return Object.keys(v).sort().join(',') === 'expectedVersion,idempotencyKey,reason'
    && typeof v.idempotencyKey === 'string' && guid.test(v.idempotencyKey)
    && Number.isSafeInteger(v.expectedVersion) && (v.expectedVersion as number) >= 1
    && typeof v.reason === 'string' && v.reason.trim() === v.reason && v.reason.length > 0 && v.reason.length <= 256
    && !/[\u0000-\u001f\u007f-\u009f]/.test(v.reason)
    ? v as unknown as OrderReopenAttempt : null
}
export function readReopenAttempt(key: string): OrderReopenAttempt | 'blocked' | null {
  try { const raw = sessionStorage.getItem(key); return raw === null ? null : decodeReopenAttempt(JSON.parse(raw)) ?? 'blocked' } catch { return 'blocked' }
}
export function persistReopenAttempt(key: string, value: OrderReopenAttempt): boolean {
  try { const raw = JSON.stringify(value); sessionStorage.setItem(key, raw); return sessionStorage.getItem(key) === raw } catch { return false }
}
export function clearReopenAttempt(key: string): boolean {
  try { sessionStorage.removeItem(key); return sessionStorage.getItem(key) === null } catch { return false }
}
export async function reopenOrder(company: string, orderId: string, attempt: OrderReopenAttempt): Promise<OrderReopenResult> {
  if (decodeReopenAttempt(attempt) === null) return { type: 'unknown' }
  let response: Response
  try {
    response = await fetch(`${MANUFACTURING_BASE}/orders/${encodeURIComponent(orderId)}/reopening`, { method: 'POST',
      headers: { ...devHeaders(), 'Content-Type': 'application/json', 'X-Company-Id': company, 'Idempotency-Key': attempt.idempotencyKey },
      body: JSON.stringify({ expectedVersion: attempt.expectedVersion, reason: attempt.reason }) })
  } catch { return { type: 'unknown' } }
  let body: unknown = null; try { body = await response.json() } catch { /* An unreadable committed answer remains unknown. */ }
  if (response.status === 200) {
    const replay = response.headers.get('Idempotency-Replayed')?.trim().toLowerCase()
    if (replay !== undefined && replay !== 'true' && replay !== 'false'
      || replay === 'true' && !response.headers.get('Idempotency-Original-Correlation-Id')?.trim()) return { type: 'unknown' }
    const order = decodeOrder(body)
    const history = order?.reopenings?.at(-1)
    return order !== null && order.companyId.toLowerCase() === company.toLowerCase() && order.orderId.toLowerCase() === orderId.toLowerCase()
      && order.status === 'Open' && order.version === attempt.expectedVersion + 1 && history?.closedOrderVersion === attempt.expectedVersion
      && history.reason === attempt.reason ? { type: 'applied' } : { type: 'unknown' }
  }
  const code = typeof body === 'object' && body !== null && typeof (body as Record<string, unknown>).code === 'string'
    ? (body as { code: string }).code : null
  if ((response.status === 409 || response.status === 422) && !code?.trim()) return { type: 'unknown' }
  return [400, 401, 403, 404, 409, 415, 422].includes(response.status) ? { type: 'failed', status: response.status, code } : { type: 'unknown' }
}
