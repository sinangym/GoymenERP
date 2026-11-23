import { MANUFACTURING_BASE, devHeaders } from './api'
import { decodePlanQueue, type PlanQueue } from './planQueueApi'

export interface QueueMoveAttempt {
  readonly idempotencyKey: string; readonly workCenterId: string; readonly expectedVersion: number
  readonly expectedQueueRevision: number; readonly movingPlanId: string; readonly beforePlanId: string | null; readonly reason: string
}
export type QueueMoveOutcome = { readonly type: 'applied'; readonly resource: PlanQueue }
  | { readonly type: 'failed'; readonly status: number } | { readonly type: 'unknown' }
const GUID = /^(?!00000000-0000-0000-0000-000000000000)[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
export function decodeQueueMoveAttempt(value: unknown): QueueMoveAttempt | null {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return null
  const v = value as Record<string, unknown>
  if (Object.keys(v).sort().join(',') !== 'beforePlanId,expectedQueueRevision,expectedVersion,idempotencyKey,movingPlanId,reason,workCenterId'
    || typeof v.idempotencyKey !== 'string' || !GUID.test(v.idempotencyKey)
    || typeof v.workCenterId !== 'string' || !GUID.test(v.workCenterId)
    || typeof v.movingPlanId !== 'string' || !GUID.test(v.movingPlanId)
    || (v.beforePlanId !== null && (typeof v.beforePlanId !== 'string' || !GUID.test(v.beforePlanId)
      || v.beforePlanId.toLowerCase() === v.movingPlanId.toLowerCase()))
    || !Number.isSafeInteger(v.expectedVersion) || (v.expectedVersion as number) < 1
    || !Number.isSafeInteger(v.expectedQueueRevision) || (v.expectedQueueRevision as number) < 0
    || typeof v.reason !== 'string' || v.reason.trim() !== v.reason || !v.reason.length || v.reason.length > 256) return null
  return v as unknown as QueueMoveAttempt
}

export function readQueueMove(key: string): { readonly kind: 'empty' } | { readonly kind: 'pending'; readonly attempt: QueueMoveAttempt } | { readonly kind: 'blocked' } {
  try {
    const raw = sessionStorage.getItem(key)
    if (raw === null) return { kind: 'empty' }
    const attempt = decodeQueueMoveAttempt(JSON.parse(raw))
    return attempt === null ? { kind: 'blocked' } : { kind: 'pending', attempt }
  } catch { return { kind: 'blocked' } }
}
export function persistQueueMove(key: string, attempt: QueueMoveAttempt): boolean {
  try { const value = JSON.stringify(attempt); sessionStorage.setItem(key, value); return sessionStorage.getItem(key) === value } catch { return false }
}
export function clearQueueMove(key: string): boolean {
  try { sessionStorage.removeItem(key); return sessionStorage.getItem(key) === null } catch { return false }
}

export async function moveQueuePlan(company: string, plan: string, attempt: QueueMoveAttempt): Promise<QueueMoveOutcome> {
  if (decodeQueueMoveAttempt(attempt) === null) return { type: 'unknown' }
  let response: Response
  try {
    response = await fetch(`${MANUFACTURING_BASE}/plans/${encodeURIComponent(plan)}/work-center-queue/reorder`, {
      method: 'POST', headers: { ...devHeaders(), 'Content-Type': 'application/json', 'X-Company-Id': company, 'Idempotency-Key': attempt.idempotencyKey },
      body: JSON.stringify({ expectedVersion: attempt.expectedVersion, expectedQueueRevision: attempt.expectedQueueRevision,
        movingPlanId: attempt.movingPlanId, beforePlanId: attempt.beforePlanId, reason: attempt.reason }),
    })
  } catch { return { type: 'unknown' } }
  let body: unknown = null
  try { body = await response.json() } catch { /* A committed write may have an unreadable response. */ }
  if (response.status === 200) {
    const replay = response.headers.get('Idempotency-Replayed')?.trim().toLowerCase()
    if (replay !== undefined && replay !== 'true' && replay !== 'false'
      || replay === 'true' && !response.headers.get('Idempotency-Original-Correlation-Id')?.trim()) return { type: 'unknown' }
    const value = decodePlanQueue(body)
    if (value === null || value.companyId.toLowerCase() !== company.toLowerCase() || value.targetPlanId.toLowerCase() !== plan.toLowerCase()
      || value.workCenterId.toLowerCase() !== attempt.workCenterId.toLowerCase() || value.targetPlanVersion !== attempt.expectedVersion
      || value.queueRevision !== attempt.expectedQueueRevision + 1 || !value.hasManualOrder) return { type: 'unknown' }
    const ids = value.items.filter(item => item.isQueued).map(item => item.planId.toLowerCase())
    const moving = ids.indexOf(attempt.movingPlanId.toLowerCase())
    if (moving < 0 || (attempt.beforePlanId === null ? moving !== ids.length - 1
      : ids[moving + 1] !== attempt.beforePlanId.toLowerCase())) return { type: 'unknown' }
    return { type: 'applied', resource: value }
  }
  if ((response.status === 409 || response.status === 422)
    && (typeof body !== 'object' || body === null || typeof (body as Record<string, unknown>).code !== 'string'
      || !(body as { code: string }).code.trim())) return { type: 'unknown' }
  return [400, 401, 403, 404, 409, 415, 422].includes(response.status)
    ? { type: 'failed', status: response.status } : { type: 'unknown' }
}
