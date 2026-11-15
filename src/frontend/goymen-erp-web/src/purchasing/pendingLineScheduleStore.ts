/**
 * F-027 (UI-07, EK16 §2a; ALS-D94): the unresolved termin change of one order line, kept for an explicit same-payload resend (the
 * same Idempotency-Key, expected version and slices). Bound to the session epoch, actor, company, order and line; a snapshot that
 * does not decode is ignored (fail-closed: a fresh attempt starts with a fresh key). Only the shape is checked here — the totals,
 * the received-portion lock and every other schedule rule are the server's.
 */
export interface PendingLineScheduleSlice {
  readonly sliceId: string
  readonly quantity: string
  readonly deliveryDate: string
}

export interface PendingLineSchedule {
  readonly actorId: string
  readonly companyId: string
  readonly orderId: string
  readonly lineId: string
  readonly expectedVersion: string
  readonly idempotencyKey: string
  readonly slices: readonly PendingLineScheduleSlice[]
}

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const VERSION = /^[1-9]\d*$/
const DATE = /^\d{4}-\d{2}-\d{2}$/
const DECIMAL = /^(0|[1-9]\d*)(\.\d+)?$/
const KEYS = ['actorId', 'companyId', 'orderId', 'lineId', 'expectedVersion', 'idempotencyKey', 'slices'] as const
const SLICE_KEYS = ['sliceId', 'quantity', 'deliveryDate'] as const

export function pendingLineScheduleKey(
  sessionEpoch: number,
  actorId: string | null,
  companyId: string | null,
  orderId: string,
  lineId: string,
): string | null {
  return actorId === null || companyId === null || !UUID.test(orderId) || !UUID.test(lineId)
    ? null
    : `goymen.purchasing.order-line-schedule.v1:${sessionEpoch}:${actorId}:${companyId}:${orderId}:${lineId}`
}

function exactKeys(value: Record<string, unknown>, keys: readonly string[]): boolean {
  const present = Object.keys(value)
  return present.length === keys.length && keys.every((key) => Object.prototype.hasOwnProperty.call(value, key))
}

function decodeSlice(value: unknown): PendingLineScheduleSlice | null {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return null
  const slice = value as Record<string, unknown>
  return exactKeys(slice, SLICE_KEYS) &&
    typeof slice.sliceId === 'string' && UUID.test(slice.sliceId) &&
    typeof slice.quantity === 'string' && DECIMAL.test(slice.quantity) &&
    typeof slice.deliveryDate === 'string' && DATE.test(slice.deliveryDate)
    ? slice as unknown as PendingLineScheduleSlice
    : null
}

function decode(value: unknown): PendingLineSchedule | null {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return null
  const candidate = value as Record<string, unknown>
  if (
    !exactKeys(candidate, KEYS) ||
    typeof candidate.actorId !== 'string' || candidate.actorId.length === 0 ||
    typeof candidate.companyId !== 'string' || !UUID.test(candidate.companyId) ||
    typeof candidate.orderId !== 'string' || !UUID.test(candidate.orderId) ||
    typeof candidate.lineId !== 'string' || !UUID.test(candidate.lineId) ||
    typeof candidate.expectedVersion !== 'string' || !VERSION.test(candidate.expectedVersion) ||
    typeof candidate.idempotencyKey !== 'string' || !UUID.test(candidate.idempotencyKey) ||
    !Array.isArray(candidate.slices) || candidate.slices.length === 0
  ) return null
  const slices = candidate.slices.map(decodeSlice)
  return slices.every((slice) => slice !== null) ? candidate as unknown as PendingLineSchedule : null
}

export function recallPendingLineSchedule(key: string | null): PendingLineSchedule | null {
  if (key === null) return null
  let raw: string | null
  try { raw = sessionStorage.getItem(key) } catch { return null }
  if (raw === null) return null
  try { return decode(JSON.parse(raw)) } catch { return null }
}

export function reservePendingLineSchedule(key: string | null, snapshot: PendingLineSchedule): void {
  if (key === null) return
  try { sessionStorage.setItem(key, JSON.stringify(snapshot)) } catch { /* fail closed in component state */ }
}

export function releasePendingLineSchedule(key: string | null, idempotencyKey: string): void {
  if (key === null) return
  if (recallPendingLineSchedule(key)?.idempotencyKey !== idempotencyKey) return
  try { sessionStorage.removeItem(key) } catch { /* no-op */ }
}
