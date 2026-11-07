/**
 * A1-3 (plan API-10): the unresolved cancellation attempt of one order, kept for an explicit same-payload resend (the same
 * Idempotency-Key, expected version and reason). Bound to the session epoch, actor, company and order; a snapshot that does
 * not decode is ignored (fail-closed: a fresh attempt starts with a fresh key).
 */
export interface PendingOrderCancellation {
  readonly actorId: string
  readonly companyId: string
  readonly orderId: string
  readonly expectedVersion: string
  readonly idempotencyKey: string
  readonly reason: string
}

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const VERSION = /^[1-9]\d*$/
const MAXIMUM_REASON_LENGTH = 4000

export function pendingOrderCancellationKey(
  sessionEpoch: number,
  actorId: string | null,
  companyId: string | null,
  orderId: string,
): string | null {
  return actorId === null || companyId === null || !UUID.test(orderId)
    ? null
    : `goymen.purchasing.order-cancellation.v1:${sessionEpoch}:${actorId}:${companyId}:${orderId}`
}

function decode(value: unknown): PendingOrderCancellation | null {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return null
  const candidate = value as Partial<PendingOrderCancellation>
  return typeof candidate.actorId === 'string' && candidate.actorId.length > 0 &&
    typeof candidate.companyId === 'string' && UUID.test(candidate.companyId) &&
    typeof candidate.orderId === 'string' && UUID.test(candidate.orderId) &&
    typeof candidate.expectedVersion === 'string' && VERSION.test(candidate.expectedVersion) &&
    typeof candidate.idempotencyKey === 'string' && UUID.test(candidate.idempotencyKey) &&
    typeof candidate.reason === 'string' && candidate.reason.trim().length > 0 && candidate.reason.length <= MAXIMUM_REASON_LENGTH
    ? candidate as PendingOrderCancellation
    : null
}

export function recallPendingOrderCancellation(key: string | null): PendingOrderCancellation | null {
  if (key === null) return null
  let raw: string | null
  try { raw = sessionStorage.getItem(key) } catch { return null }
  if (raw === null) return null
  try { return decode(JSON.parse(raw)) } catch { return null }
}

export function reservePendingOrderCancellation(key: string | null, snapshot: PendingOrderCancellation): void {
  if (key === null) return
  try { sessionStorage.setItem(key, JSON.stringify(snapshot)) } catch { /* fail closed in component state */ }
}

export function releasePendingOrderCancellation(key: string | null, idempotencyKey: string): void {
  if (key === null) return
  if (recallPendingOrderCancellation(key)?.idempotencyKey !== idempotencyKey) return
  try { sessionStorage.removeItem(key) } catch { /* no-op */ }
}
