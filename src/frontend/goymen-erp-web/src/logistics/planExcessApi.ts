import { API_BASE, logisticsHeaders } from './api'
import { decodeProblem } from './contract'
import type { AttemptSnapshot, CommandOutcome } from './instructionAttempt'
import type { ViewOutcome } from '../shell/uiState'

/** Published mount ids, not grants. The server measures the purpose's original permission. */
export const PLAN_EXCESS_CAPABILITY = { prepare: 'logistics.shipments.plan-excess.prepare', approve: 'logistics.shipments.plan-excess.approve' } as const
export const PLAN_EXCESS_PATH = '/logistics/plan-excess'
export const proposePlanExcessPath = (instruction: string, line: string) => `/logistics/loading-instructions/${encodeURIComponent(instruction)}/lines/${encodeURIComponent(line)}/plan-excess`
export const approvePlanExcessPath = (exception: string) => `${PLAN_EXCESS_PATH}/${encodeURIComponent(exception)}/approve`
export type PlanExcessPurpose = 'approve' | 'load' | 'ship'
export interface PlanExcessUse { readonly exceptionId: string; readonly expectedVersion: string; readonly quantity: string }
export interface ProposePlanExcess { readonly exceptionId: string; readonly expectedInstructionVersion: string; readonly quantity: string; readonly reason: string }
export interface ApprovePlanExcess { readonly expectedVersion: string; readonly expectedInstructionVersion: string }
export type PlanExcessCommand = ProposePlanExcess | ApprovePlanExcess
export interface PlanExcessView {
  readonly companyId: string; readonly exceptionId: string; readonly instructionId: string; readonly lineId: string
  readonly salesOrderId: string; readonly salesOrderLineId: string; readonly unitId: string; readonly version: string; readonly instructionVersion: string
  readonly status: 'Pending' | 'Approved'; readonly instructed: string; readonly quantity: string; readonly loadedUsed: string; readonly shippedUsed: string
  readonly reason: string; readonly proposedBy: string; readonly proposedAtUtc: string; readonly approvedBy: string | null; readonly approvedAtUtc: string | null
  readonly availableToLoad: string; readonly availableToShip: string
}
export interface PlanExcessPage { readonly items: readonly PlanExcessView[]; readonly nextCursor: string | null }
export interface PlanExcessResult { readonly exception: PlanExcessView; readonly isReplay: boolean; readonly originalCorrelationId: string }
const object = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null && !Array.isArray(v)
const exact = (v: Record<string, unknown>, fields: readonly string[]) => Object.keys(v).length === fields.length && fields.every(f => Object.hasOwn(v, f))
const guid = (v: unknown): v is string => typeof v === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(v) && v !== '00000000-0000-0000-0000-000000000000'
const version = (v: unknown): v is string => typeof v === 'string' && /^[1-9]\d{0,18}$/.test(v) && BigInt(v) <= 9223372036854775807n
const text = (v: unknown, max: number): v is string => typeof v === 'string' && v.trim().length > 0 && v.length <= max && !/[\u0000-\u001f\u007f]/.test(v)
const instant = (v: unknown): v is string => typeof v === 'string' && v.length <= 64 && /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$/.test(v) && Number.isFinite(Date.parse(v))
const quantity = (v: unknown): v is string => typeof v === 'string' && /^(?:0|[1-9]\d{0,14})(?:\.\d{1,3})?$/.test(v)
const positive = (v: unknown): v is string => quantity(v) && /[1-9]/.test(v)
const VIEW_FIELDS = ['companyId', 'exceptionId', 'instructionId', 'lineId', 'salesOrderId', 'salesOrderLineId', 'unitId', 'version', 'instructionVersion',
  'status', 'instructed', 'quantity', 'loadedUsed', 'shippedUsed', 'reason', 'proposedBy', 'proposedAtUtc', 'approvedBy', 'approvedAtUtc', 'availableToLoad', 'availableToShip'] as const
export function decodePlanExcessView(v: unknown, company: string, instruction?: string): PlanExcessView | null {
  if (!object(v) || !exact(v, VIEW_FIELDS) || v.companyId !== company || instruction !== undefined && v.instructionId !== instruction
    || !['companyId', 'exceptionId', 'instructionId', 'lineId', 'salesOrderId', 'salesOrderLineId', 'unitId'].every(k => guid(v[k]))
    || !version(v.version) || !version(v.instructionVersion) || v.status !== 'Pending' && v.status !== 'Approved'
    || !positive(v.instructed) || !positive(v.quantity)
    || !['loadedUsed', 'shippedUsed', 'availableToLoad', 'availableToShip'].every(k => quantity(v[k]))
    || !text(v.reason, 500) || !text(v.proposedBy, 128) || !instant(v.proposedAtUtc)
    || !(v.approvedBy === null || text(v.approvedBy, 128)) || !(v.approvedAtUtc === null || instant(v.approvedAtUtc))) return null
  if (v.status === 'Pending' ? v.approvedBy !== null || v.approvedAtUtc !== null : v.approvedBy === null || v.approvedAtUtc === null) return null
  return v as unknown as PlanExcessView
}
export function decodePlanExcessPage(v: unknown, company: string, instruction?: string): PlanExcessPage | null {
  if (!object(v) || !exact(v, ['items', 'nextCursor']) || !Array.isArray(v.items) || v.items.length > 50
    || !(v.nextCursor === null || text(v.nextCursor, 4000))) return null
  const items: PlanExcessView[] = []
  for (const raw of v.items) { const item = decodePlanExcessView(raw, company, instruction); if (item === null || items.some(x => x.exceptionId === item.exceptionId)) return null; items.push(item) }
  return { items, nextCursor: v.nextCursor }
}
export function decodePlanExcessResult(v: unknown, company: string): PlanExcessResult | null {
  if (!object(v) || !exact(v, ['exception', 'isReplay', 'originalCorrelationId']) || typeof v.isReplay !== 'boolean' || !text(v.originalCorrelationId, 128)) return null
  const exception = decodePlanExcessView(v.exception, company)
  return exception === null ? null : { exception, isReplay: v.isReplay, originalCorrelationId: v.originalCorrelationId }
}
const json = async (r: Response): Promise<unknown> => { try { return await r.json() } catch { return null } }
export async function fetchPlanExcess(company: string, purpose: PlanExcessPurpose, instruction?: string, cursor: string | null = null, signal?: AbortSignal): Promise<ViewOutcome<PlanExcessPage>> {
  try {
    const query = new URLSearchParams({ purpose }); if (instruction !== undefined) query.set('instructionId', instruction); if (cursor !== null) query.set('cursor', cursor)
    const response = await fetch(`${API_BASE}${PLAN_EXCESS_PATH}?${query}`, { headers: logisticsHeaders(company), cache: 'no-store', signal })
    if (signal?.aborted) return { type: 'fence-stale' }
    const body = await json(response); if (signal?.aborted) return { type: 'fence-stale' }
    if (response.status !== 200) return { type: 'http', status: response.status, body }
    const page = decodePlanExcessPage(body, company, instruction)
    return page === null ? { type: 'malformed' } : { type: 'ok-single', data: page }
  } catch { return { type: signal?.aborted ? 'fence-stale' : navigator.onLine ? 'network-error' : 'offline' } }
}
export async function sendPlanExcess(snapshot: AttemptSnapshot<PlanExcessCommand>, signal?: AbortSignal): Promise<CommandOutcome<PlanExcessResult>> {
  try {
    const response = await fetch(`${API_BASE}${snapshot.path}`, { method: 'POST', headers: { ...logisticsHeaders(snapshot.companyId), 'Content-Type': 'application/json', 'Idempotency-Key': snapshot.key }, body: JSON.stringify(snapshot.payload), signal })
    if (signal?.aborted) return { type: 'aborted' }
    const body = await json(response)
    if (signal?.aborted) return { type: 'aborted' }
    if (response.status !== 200) return response.status >= 200 && response.status < 300 ? { type: 'malformed' } : { type: 'http', status: response.status, problem: decodeProblem(body, response.status) }
    const result = decodePlanExcessResult(body, snapshot.companyId)
    const replay = response.headers.get('Idempotency-Replayed'), original = response.headers.get('Idempotency-Original-Correlation-Id')
    if (result === null || replay !== null && replay !== 'true' && replay !== 'false' || result.isReplay !== (replay === 'true')
      || (result.isReplay ? original !== result.originalCorrelationId : original !== null)) return { type: 'malformed' }
    const item = result.exception, payload = snapshot.payload
    if ('exceptionId' in payload) {
      if (snapshot.path !== proposePlanExcessPath(item.instructionId, item.lineId) || item.exceptionId !== payload.exceptionId
        || item.instructionVersion !== payload.expectedInstructionVersion || item.quantity !== payload.quantity || item.reason !== payload.reason
        || item.status !== 'Pending') return { type: 'malformed' }
    } else if (snapshot.path !== approvePlanExcessPath(item.exceptionId) || item.status !== 'Approved'
      || item.instructionVersion !== payload.expectedInstructionVersion || !version(payload.expectedVersion)
      || BigInt(item.version) !== BigInt(payload.expectedVersion) + 1n) return { type: 'malformed' }
    return { type: 'succeeded', data: result }
  } catch { return { type: signal?.aborted ? 'aborted' : navigator.onLine ? 'network-error' : 'offline' } }
}
