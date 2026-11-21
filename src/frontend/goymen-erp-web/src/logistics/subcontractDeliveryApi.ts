import type { ViewOutcome } from '../shell/uiState'
import { API_BASE, logisticsHeaders } from './api'
import { decodeInstructionMutation, decodePhysicalStockRefusalDetail } from './contract'
import { decodeSubcontractCandidates, decodeSubcontractInstruction, decodeSubcontractSalesLines, decodeSubcontractList,
  SUBCONTRACT_DELIVERY_PATH as PATH, type SubcontractCandidate, type SubcontractInstruction } from './subcontractDeliveryContract'

async function read<T>(path: string, company: string, decode: (v: unknown) => T | null, signal?: AbortSignal): Promise<ViewOutcome<T>> {
  let response: Response
  try { response = await fetch(`${API_BASE}${path}`, { headers: logisticsHeaders(company), signal }) }
  catch { return signal?.aborted ? { type: 'fence-stale' } : navigator.onLine ? { type: 'network-error' } : { type: 'offline' } }
  let body: unknown = null; try { body = await response.json() } catch { /* A malformed success is not an empty result. */ }
  if (signal?.aborted) return { type: 'fence-stale' }
  if (response.status !== 200) return { type: 'http', status: response.status, body }
  const value = decode(body); return value === null ? { type: 'malformed' } : { type: 'ok-single', data: value }
}
export const fetchSubcontractSalesLines = (company: string, cursor: string | null, signal?: AbortSignal) =>
  read(`${PATH}/sales-lines${cursor === null ? '' : `?cursor=${encodeURIComponent(cursor)}`}`, company, v => decodeSubcontractSalesLines(v, company), signal)
export const fetchSubcontractList = (company: string, cursor: string | null, signal?: AbortSignal) =>
  read(`${PATH}${cursor === null ? '' : `?cursor=${encodeURIComponent(cursor)}`}`, company, v => decodeSubcontractList(v, company), signal)
export const fetchSubcontractCandidates = (company: string, order: string, line: string, page: number, signal?: AbortSignal) =>
  read(`${PATH}/candidates/${encodeURIComponent(order)}/${encodeURIComponent(line)}?pageIndex=${page}`, company,
    v => decodeSubcontractCandidates(v, company, order, line, page), signal)
export const fetchSubcontractInstruction = (company: string, id: string, signal?: AbortSignal) =>
  read(`${PATH}/${encodeURIComponent(id)}`, company, v => decodeSubcontractInstruction(v, company, id), signal)

export interface SubcontractAttempt {
  readonly binding: string; readonly company: string; readonly instructionId: string; readonly path: string
  readonly family: 'subcontract' | 'standard'; readonly body: string; readonly key: string; readonly correlation: string
}
export type SubcontractResult = { readonly type: 'applied'; readonly replayed: boolean; readonly view: SubcontractInstruction | null }
  | { readonly type: 'refused'; readonly status: number; readonly code: string | null; readonly detail: string | null;
      readonly replayed: boolean; readonly originalCorrelationId: string | null }
  | { readonly type: 'unknown'; readonly reason: 'offline' | 'network-error' | 'service-unavailable' | 'malformed-response' | 'conflict' }
export type SubcontractAttemptState = { readonly phase: 'idle' }
  | { readonly phase: 'sending'; readonly snapshot: SubcontractAttempt }
  | { readonly phase: 'completed'; readonly snapshot: SubcontractAttempt; readonly outcome: SubcontractResult }
  | { readonly phase: 'unknown'; readonly snapshot: SubcontractAttempt; readonly outcome: Extract<SubcontractResult, { type: 'unknown' }> }
const idle: SubcontractAttemptState = Object.freeze({ phase: 'idle' })
const attempts = new Map<string, SubcontractAttemptState>()
const listeners = new Map<string, Set<() => void>>()
export function resetSubcontractAttemptsForTests() { attempts.clear(); listeners.clear() }
export const subcontractAttempt = (binding: string) => attempts.get(binding) ?? idle
export const subcontractFrozen = (state: SubcontractAttemptState) => state.phase === 'sending' || state.phase === 'unknown'
const emit = (binding: string) => listeners.get(binding)?.forEach(callback => callback())
export function subscribeSubcontractAttempt(binding: string, callback: () => void) {
  const set = listeners.get(binding) ?? new Set(); set.add(callback); listeners.set(binding, set)
  return () => { set.delete(callback); if (set.size === 0) listeners.delete(binding) }
}
export function beginSubcontractAttempt(binding: string, company: string, instructionId: string, path: string,
  family: SubcontractAttempt['family'], body: object): SubcontractAttempt | null {
  if (subcontractFrozen(subcontractAttempt(binding))) return null
  const snapshot: SubcontractAttempt = Object.freeze({ binding, company, instructionId, path, family, body: JSON.stringify(body), key: crypto.randomUUID(), correlation: crypto.randomUUID() })
  attempts.set(binding, { phase: 'sending', snapshot }); emit(binding); return snapshot
}
export function retrySubcontractAttempt(binding: string): SubcontractAttempt | null {
  const state = subcontractAttempt(binding); if (state.phase !== 'unknown') return null
  attempts.set(binding, { phase: 'sending', snapshot: state.snapshot }); emit(binding); return state.snapshot
}
export function finishSubcontractAttempt(snapshot: SubcontractAttempt, outcome: SubcontractResult) {
  const state = subcontractAttempt(snapshot.binding)
  if (state.phase !== 'sending' || state.snapshot !== snapshot) return
  attempts.set(snapshot.binding, outcome.type === 'unknown' ? { phase: 'unknown', snapshot, outcome } : { phase: 'completed', snapshot, outcome }); emit(snapshot.binding)
}
export function leaveSubcontractAttempt(binding: string) {
  const state = subcontractAttempt(binding)
  if (state.phase === 'sending') finishSubcontractAttempt(state.snapshot, { type: 'unknown', reason: 'network-error' })
}
export async function sendSubcontractAttempt(snapshot: SubcontractAttempt): Promise<SubcontractResult> {
  let response: Response
  try { response = await fetch(`${API_BASE}${snapshot.path}`, { method: 'POST', headers: { ...logisticsHeaders(snapshot.company),
    'Content-Type': 'application/json', 'Idempotency-Key': snapshot.key, 'X-Correlation-Id': snapshot.correlation }, body: snapshot.body }) }
  catch { return { type: 'unknown', reason: navigator.onLine ? 'network-error' : 'offline' } }
  let body: unknown = null; try { body = await response.json() } catch { /* Preserve unknown outcome and frozen key. */ }
  const replayHeader = response.headers.get('Idempotency-Replayed'); const replayed = replayHeader === 'true'
  if (response.status >= 200 && response.status < 300) {
    if ((replayHeader !== null && replayHeader !== 'true' && replayHeader !== 'false') || ![200, 201].includes(response.status)) return { type: 'unknown', reason: 'malformed-response' }
    if (snapshot.family === 'standard') {
      const decoded = decodeInstructionMutation(body)
      if (decoded === null || decoded.detail.instruction.companyId !== snapshot.company || decoded.detail.instruction.id !== snapshot.instructionId || decoded.isReplay !== replayed)
        return { type: 'unknown', reason: 'malformed-response' }
      const sent = JSON.parse(snapshot.body) as Record<string, unknown>
      if (snapshot.path.endsWith('/issue') && decoded.detail.instruction.status !== 'Issued'
        || snapshot.path.endsWith('/loads') && !decoded.detail.shares.some(share => share.shareId === sent.loadId && share.quantity === sent.quantity
          && share.measurement === sent.measurement && snapshot.path.includes(`/lines/${share.lineId}/`))
        || snapshot.path.endsWith('/weighings') && (decoded.detail.weighing === null || decoded.detail.weighing.weighingId !== sent.weighingId || decoded.detail.weighing.direction !== sent.direction
          || decoded.detail.weighing.firstWeighingKg !== sent.firstWeighingKg || decoded.detail.weighing.secondWeighingKg !== sent.secondWeighingKg))
        return { type: 'unknown', reason: 'malformed-response' }
      return { type: 'applied', replayed, view: null }
    }
    const decoded = decodeSubcontractInstruction(body, snapshot.company, snapshot.instructionId)
    if (decoded === null || decoded.isReplay !== replayed
      || (replayed ? response.headers.get('Idempotency-Original-Correlation-Id') !== decoded.originalCorrelationId : decoded.originalCorrelationId !== snapshot.correlation))
      return { type: 'unknown', reason: 'malformed-response' }
    const sent = JSON.parse(snapshot.body) as Record<string, unknown>
    if (snapshot.path === PATH) {
      const lines = sent.lines as readonly Record<string, unknown>[]
      if (decoded.status !== 'Draft' || decoded.lines.length !== lines.length || lines.some(line => !decoded.lines.some(actual => actual.lineId === line.lineId
        && actual.allocationId === line.allocationId && actual.instructedKg === line.quantityKg && actual.source.labelId === line.labelId
        && actual.source.outputFactId === line.outputFactId && actual.source.salesOrderId === line.salesOrderId && actual.source.salesOrderLineId === line.salesOrderLineId)))
        return { type: 'unknown', reason: 'malformed-response' }
    } else if (snapshot.path.endsWith('/loads')) {
      if (decoded.recordedLoadId !== sent.loadId || !decoded.lines.some(line => snapshot.path.includes(`/lines/${line.lineId}/`) && line.loadedKg !== '0'))
        return { type: 'unknown', reason: 'malformed-response' }
    } else if (snapshot.path.endsWith('/ship')) {
      const lines = sent.lines as readonly Record<string, unknown>[]
      if (lines.some(line => !decoded.lines.some(actual => actual.lineId === line.lineId && actual.shippedKg !== '0' && actual.source.labelId === line.labelId
        && actual.source.outputFactId === line.outputFactId && actual.source.manufacturingOrderId === line.manufacturingOrderId
        && actual.source.salesOrderId === line.salesOrderId && actual.source.salesOrderLineId === line.salesOrderLineId)))
        return { type: 'unknown', reason: 'malformed-response' }
    } else if (snapshot.path.endsWith('/reserve')) {
      const match = /\/lines\/([^/]+)\/plan-excess\/([^/]+)\/reserve$/.exec(snapshot.path)
      const line = match === null ? undefined : decoded.lines.find(item => item.lineId === match[1])
      const allocation = line?.additionalAllocations?.find(item => item.allocationId === sent.allocationId)
      if (match === null || line === undefined || allocation === undefined || allocation.exceptionId !== match[2]
        || allocation.version !== '1' || allocation.allocatedKg !== sent.quantityKg || allocation.remainingKg !== sent.quantityKg
        || decoded.version !== sent.expectedInstructionVersion) return { type: 'unknown', reason: 'malformed-response' }
    } else if (snapshot.path.endsWith('/weighing-corrections') && !decoded.corrections.some(value => value.correctionId === sent.correctionId
      && value.firstKg === sent.firstWeighingKg && value.secondKg === sent.secondWeighingKg && value.direction === sent.direction && value.reason === sent.reason))
      return { type: 'unknown', reason: 'malformed-response' }
    return { type: 'applied', replayed, view: decoded }
  }
  const problem = typeof body === 'object' && body !== null && !Array.isArray(body) ? body as Record<string, unknown> : null
  const code = typeof problem?.code === 'string' ? problem.code : null
  if (response.status >= 500) return { type: 'unknown', reason: 'service-unavailable' }
  if ([401, 403, 404].includes(response.status)) return { type: 'refused', status: response.status, code: null,
    detail: null, replayed: false, originalCorrelationId: null }
  if (response.status === 409 && (code === null || code.includes('concurrent') || code.includes('busy'))) return { type: 'unknown', reason: 'conflict' }
  const original = response.headers.get('Idempotency-Original-Correlation-Id')
  if (replayHeader !== null && replayHeader !== 'true' && replayHeader !== 'false'
    || replayed && (original === null || original.length < 1 || original.length > 128 || original.trim() !== original || /[\u0000-\u001f\u007f]/u.test(original))
    || !replayed && original !== null) return { type: 'unknown', reason: 'malformed-response' }
  let detail: string | null = null
  if (response.status === 409 && code === 'inventory_physical_stock_insufficient') {
    if (problem?.status !== 409) return { type: 'unknown', reason: 'malformed-response' }
    if (problem.detail !== null && problem.detail !== undefined) {
      detail = decodePhysicalStockRefusalDetail(problem.detail, response.status)
      if (detail === null) return { type: 'unknown', reason: 'malformed-response' }
    }
  }
  return { type: 'refused', status: response.status, code, detail, replayed, originalCorrelationId: replayed ? original : null }
}
export const subcontractCreateLine = (item: SubcontractCandidate, quantityKg: string, reason: string) => ({
  lineId: crypto.randomUUID(), allocationId: crypto.randomUUID(), salesOrderId: item.salesOrderId, salesOrderLineId: item.salesOrderLineId,
  expectedSalesOrderVersion: item.salesOrderVersion, salesLineRevision: item.salesLineRevision, manufacturingOrderId: item.manufacturingOrderId,
  expectedManufacturingOrderVersion: item.manufacturingOrderVersion, expectedPlanVersion: item.planVersion, expectedSalesLinkRevision: item.salesLinkRevision,
  outputFactId: item.outputFactId, labelId: item.labelId, expectedLabelVersion: item.labelVersion, quantityKg, reason,
})
