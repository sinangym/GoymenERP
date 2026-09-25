import type { CreatePurchaseRequestRequest } from './contract'

export interface PendingPurchaseRequestCreate {
  readonly actorId: string
  readonly companyId: string
  readonly idempotencyKey: string
  readonly payload: CreatePurchaseRequestRequest
}

export interface PendingPurchaseRequestSubmission {
  readonly actorId: string
  readonly companyId: string
  readonly requestId: string
  readonly expectedVersion: string
  readonly idempotencyKey: string
}

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const VERSION = /^[1-9]\d*$/
const QUANTITY = /^(0|[1-9]\d*)(\.\d*[1-9])?$/

export function pendingPurchaseRequestCreateKey(sessionEpoch: number, actorId: string | null, companyId: string | null): string | null {
  return actorId === null || companyId === null ? null : `goymen.purchasing.request-create.v1:${sessionEpoch}:${actorId}:${companyId}`
}

export function pendingPurchaseRequestSubmissionKey(
  sessionEpoch: number,
  actorId: string | null,
  companyId: string | null,
  requestId: string,
): string | null {
  return actorId === null || companyId === null || !UUID.test(requestId)
    ? null
    : `goymen.purchasing.request-submit.v1:${sessionEpoch}:${actorId}:${companyId}:${requestId}`
}

function decodeCreate(value: unknown): PendingPurchaseRequestCreate | null {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return null
  const candidate = value as Partial<PendingPurchaseRequestCreate>
  const payload = candidate.payload
  if (typeof candidate.actorId !== 'string' || candidate.actorId.length === 0 ||
    typeof candidate.companyId !== 'string' || !UUID.test(candidate.companyId) ||
    typeof candidate.idempotencyKey !== 'string' || !UUID.test(candidate.idempotencyKey) ||
    typeof payload !== 'object' || payload === null || Array.isArray(payload) ||
    !/^\d{4}-\d{2}-\d{2}$/.test(payload.effectiveOn) || !Array.isArray(payload.lines) ||
    payload.lines.length < 1 || payload.lines.length > 500) return null
  const ids = new Set<string>()
  for (const line of payload.lines) {
    if (!UUID.test(line.lineId) || ids.has(line.lineId) || !UUID.test(line.productId) ||
      !VERSION.test(line.expectedProductVersion) || !UUID.test(line.unitId) ||
      !QUANTITY.test(line.quantity) || !/[1-9]/.test(line.quantity)) return null
    ids.add(line.lineId)
  }
  return candidate as PendingPurchaseRequestCreate
}

function decodeSubmission(value: unknown): PendingPurchaseRequestSubmission | null {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return null
  const candidate = value as Partial<PendingPurchaseRequestSubmission>
  return typeof candidate.actorId === 'string' && candidate.actorId.length > 0 &&
    typeof candidate.companyId === 'string' && UUID.test(candidate.companyId) &&
    typeof candidate.requestId === 'string' && UUID.test(candidate.requestId) &&
    typeof candidate.expectedVersion === 'string' && VERSION.test(candidate.expectedVersion) &&
    typeof candidate.idempotencyKey === 'string' && UUID.test(candidate.idempotencyKey)
    ? candidate as PendingPurchaseRequestSubmission
    : null
}

function recall<T>(key: string | null, decode: (value: unknown) => T | null): T | null {
  if (key === null) return null
  try {
    const raw = sessionStorage.getItem(key)
    return raw === null ? null : decode(JSON.parse(raw))
  } catch { return null }
}

function reserve(key: string | null, snapshot: unknown): void {
  if (key === null) return
  try { sessionStorage.setItem(key, JSON.stringify(snapshot)) } catch { /* component state remains fail-closed */ }
}

function release(key: string | null, idempotencyKey: string, current: { readonly idempotencyKey: string } | null): void {
  if (key === null || current?.idempotencyKey !== idempotencyKey) return
  try { sessionStorage.removeItem(key) } catch { /* no-op */ }
}

export const recallPendingPurchaseRequestCreate = (key: string | null) => recall(key, decodeCreate)
export const recallPendingPurchaseRequestSubmission = (key: string | null) => recall(key, decodeSubmission)
export const reservePendingPurchaseRequestCreate = (key: string | null, snapshot: PendingPurchaseRequestCreate) => reserve(key, snapshot)
export const reservePendingPurchaseRequestSubmission = (key: string | null, snapshot: PendingPurchaseRequestSubmission) => reserve(key, snapshot)
export const releasePendingPurchaseRequestCreate = (key: string | null, idempotencyKey: string) =>
  release(key, idempotencyKey, recallPendingPurchaseRequestCreate(key))
export const releasePendingPurchaseRequestSubmission = (key: string | null, idempotencyKey: string) =>
  release(key, idempotencyKey, recallPendingPurchaseRequestSubmission(key))
