import { MANUFACTURING_BASE, devHeaders } from './api'
import type { ViewOutcome } from '../shell/uiState'

export interface PlanPriority {
  readonly companyId: string; readonly planId: string; readonly planVersion: number
  readonly priorityScore: number; readonly priorityRevision: number
  readonly recordedBy: string | null; readonly recordedAtUtc: string | null; readonly reason: string | null
}
export interface PlanQueueLine {
  readonly planId: string; readonly dueDate: string | null; readonly priorityScore: number; readonly priorityRevision: number
  readonly plannedKg: string; readonly processedKg: string; readonly remainingKg: string
  readonly isOverPlanned: boolean; readonly isQueued: boolean
}
export interface PlanQueue {
  readonly contractVersion: 1; readonly companyId: string; readonly workCenterId: string
  readonly targetPlanId: string; readonly targetPlanVersion: number; readonly items: readonly PlanQueueLine[]
}
export interface PriorityAttempt {
  readonly idempotencyKey: string; readonly expectedVersion: number; readonly expectedPriorityRevision: number
  readonly priorityScore: number; readonly reason: string
}
export type PriorityOutcome = { readonly type: 'applied'; readonly resource: PlanPriority; readonly replayed: boolean }
  | { readonly type: 'failed'; readonly status: number; readonly code: string | null }
  | { readonly type: 'unknown' }
const GUID = /^(?!00000000-0000-0000-0000-000000000000)[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const KG = /^(0|[1-9][0-9]*)(\.[0-9]{1,3})?$/
const obj = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null && !Array.isArray(v)
const guid = (v: unknown): v is string => typeof v === 'string' && GUID.test(v)
const revision = (v: unknown): v is number => Number.isSafeInteger(v) && (v as number) >= 0
const score = (v: unknown): v is number => Number.isInteger(v) && (v as number) >= -2147483648 && (v as number) <= 2147483647
export function decodePlanPriority(v: unknown): PlanPriority | null {
  if (!obj(v) || !guid(v.companyId) || !guid(v.planId) || !revision(v.planVersion) || v.planVersion === 0
    || !score(v.priorityScore) || !revision(v.priorityRevision)) return null
  if (v.priorityRevision === 0) {
    if (v.priorityScore !== 0 || v.recordedBy !== null || v.recordedAtUtc !== null || v.reason !== null) return null
  } else if (typeof v.recordedBy !== 'string' || !v.recordedBy.trim() || typeof v.reason !== 'string' || !v.reason.trim()
    || typeof v.recordedAtUtc !== 'string' || !Number.isFinite(Date.parse(v.recordedAtUtc))) return null
  return v as unknown as PlanPriority
}
export function decodePlanQueue(v: unknown): PlanQueue | null {
  if (!obj(v) || v.contractVersion !== 1 || !guid(v.companyId) || !guid(v.workCenterId) || !guid(v.targetPlanId)
    || !revision(v.targetPlanVersion) || v.targetPlanVersion === 0 || !Array.isArray(v.items) || !v.items.length) return null
  const ids = new Set<string>()
  for (const line of v.items) {
    if (!obj(line) || !guid(line.planId) || ids.has(line.planId.toLowerCase()) || !score(line.priorityScore)
      || !revision(line.priorityRevision) || typeof line.isOverPlanned !== 'boolean' || typeof line.isQueued !== 'boolean'
      || (line.dueDate !== null && (typeof line.dueDate !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(line.dueDate)
        || !Number.isFinite(Date.parse(line.dueDate))))
      || ['plannedKg', 'processedKg', 'remainingKg'].some((key) => typeof line[key] !== 'string' || !KG.test(line[key] as string))) return null
    ids.add(line.planId.toLowerCase())
  }
  return ids.has(v.targetPlanId.toLowerCase()) ? v as unknown as PlanQueue : null
}
async function read<T>(companyId: string, path: string, decode: (v: unknown) => T | null,
  matches: (v: T) => boolean, signal?: AbortSignal): Promise<ViewOutcome<T>> {
  let response: Response
  try { response = await fetch(`${MANUFACTURING_BASE}${path}`, { headers: { ...devHeaders(), 'X-Company-Id': companyId }, cache: 'no-store', signal }) }
  catch { return signal?.aborted ? { type: 'fence-stale' } : navigator.onLine ? { type: 'network-error' } : { type: 'offline' } }
  let body: unknown = null
  try { body = await response.json() } catch { /* A successful malformed response stays distinct. */ }
  if (signal?.aborted) return { type: 'fence-stale' }
  if (response.status !== 200) return { type: 'http', status: response.status, body }
  const value = decode(body)
  return value !== null && matches(value) ? { type: 'ok-single', data: value } : { type: 'malformed' }
}
export const fetchPlanPriority = (company: string, plan: string, version: number, signal?: AbortSignal) =>
  read(company, `/plans/${encodeURIComponent(plan)}/priority`, decodePlanPriority,
    (v) => v.companyId.toLowerCase() === company.toLowerCase() && v.planId.toLowerCase() === plan.toLowerCase() && v.planVersion === version, signal)
export const fetchPlanQueue = (company: string, plan: string, version: number, signal?: AbortSignal) =>
  read(company, `/plans/${encodeURIComponent(plan)}/work-center-queue`, decodePlanQueue,
    (v) => v.companyId.toLowerCase() === company.toLowerCase() && v.targetPlanId.toLowerCase() === plan.toLowerCase() && v.targetPlanVersion === version, signal)

/** A lost, 503 or undecodable answer retains the exact frozen attempt; this function never retries. */
export async function changePlanPriority(company: string, plan: string, attempt: PriorityAttempt): Promise<PriorityOutcome> {
  let response: Response
  try { response = await fetch(`${MANUFACTURING_BASE}/plans/${encodeURIComponent(plan)}/priority`, {
    method: 'POST', headers: { ...devHeaders(), 'Content-Type': 'application/json', 'X-Company-Id': company,
      'Idempotency-Key': attempt.idempotencyKey },
    body: JSON.stringify({ expectedVersion: attempt.expectedVersion, expectedPriorityRevision: attempt.expectedPriorityRevision,
      priorityScore: attempt.priorityScore, reason: attempt.reason }),
  }) } catch { return { type: 'unknown' } }
  let body: unknown = null
  try { body = await response.json() } catch { /* A write may already have committed. */ }
  if (response.status === 200) {
    const value = decodePlanPriority(body)
    return value !== null && value.companyId.toLowerCase() === company.toLowerCase() && value.planId.toLowerCase() === plan.toLowerCase()
      && value.planVersion === attempt.expectedVersion && value.priorityRevision === attempt.expectedPriorityRevision + 1
      && value.priorityScore === attempt.priorityScore && value.reason === attempt.reason.trim()
      ? { type: 'applied', resource: value, replayed: response.headers.get('Idempotency-Replayed') === 'true' } : { type: 'unknown' }
  }
  return [400, 401, 403, 404, 409, 422].includes(response.status)
    ? { type: 'failed', status: response.status, code: obj(body) && typeof body.code === 'string' ? body.code : null }
    : { type: 'unknown' }
}
