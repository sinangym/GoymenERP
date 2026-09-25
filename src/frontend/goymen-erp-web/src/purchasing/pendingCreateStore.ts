import type { CreatePurchaseOrderRequest } from './contract'

export interface PendingPurchaseOrderCreate {
  readonly actorId: string
  readonly companyId: string
  readonly idempotencyKey: string
  readonly payload: CreatePurchaseOrderRequest
}

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const VERSION = /^[1-9]\d*$/
const QUANTITY = /^(0|[1-9]\d*)(\.\d*[1-9])?$/

export function pendingPurchaseOrderCreateKey(sessionEpoch: number, actorId: string | null, companyId: string | null): string | null {
  return actorId === null || companyId === null ? null : `goymen.purchasing.create.v1:${sessionEpoch}:${actorId}:${companyId}`
}

function decode(value: unknown): PendingPurchaseOrderCreate | null {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return null
  const candidate = value as Partial<PendingPurchaseOrderCreate>
  const payload = candidate.payload
  if (typeof candidate.actorId !== 'string' || candidate.actorId.length === 0 ||
    typeof candidate.companyId !== 'string' || !UUID.test(candidate.companyId) ||
    typeof candidate.idempotencyKey !== 'string' || !UUID.test(candidate.idempotencyKey) ||
    typeof payload !== 'object' || payload === null || Array.isArray(payload) ||
    !UUID.test(payload.supplierPartyId) || !UUID.test(payload.companyAccountId) || payload.sourceRequestId !== null ||
    !/^\d{4}-\d{2}-\d{2}$/.test(payload.effectiveOn) || !Array.isArray(payload.lines) || payload.lines.length !== 1) return null
  const line = payload.lines[0]
  if (!UUID.test(line.lineId) || !UUID.test(line.productId) || !VERSION.test(line.expectedProductVersion) ||
    !UUID.test(line.unitId) || !QUANTITY.test(line.quantity) || !/[1-9]/.test(line.quantity) || line.sourceRequestLineId !== null) return null
  return candidate as PendingPurchaseOrderCreate
}

export function recallPendingPurchaseOrderCreate(key: string | null): PendingPurchaseOrderCreate | null {
  if (key === null) return null
  try {
    const raw = sessionStorage.getItem(key)
    return raw === null ? null : decode(JSON.parse(raw))
  } catch { return null }
}

export function reservePendingPurchaseOrderCreate(key: string | null, snapshot: PendingPurchaseOrderCreate): void {
  if (key === null) return
  try { sessionStorage.setItem(key, JSON.stringify(snapshot)) } catch { /* component state remains fail-closed */ }
}

export function releasePendingPurchaseOrderCreate(key: string | null, idempotencyKey: string): void {
  if (key === null) return
  if (recallPendingPurchaseOrderCreate(key)?.idempotencyKey !== idempotencyKey) return
  try { sessionStorage.removeItem(key) } catch { /* no-op */ }
}
