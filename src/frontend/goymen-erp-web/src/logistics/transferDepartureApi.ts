import type { ShellState } from '../shell/shellState'
import type { ViewOutcome } from '../shell/uiState'
import { isBoundUuid } from '../product/wireGuards'
import { API_BASE, logisticsHeaders } from './api'

export const TRANSFER_DEPARTURE_CAPABILITY = 'logistics.transfer-departures.record'
export const TRANSFER_DEPARTURE_ROUTE = '/lojistik/transfer-cikislari'
export const TRANSFER_DEPARTURE_PATH = '/logistics/transfer-departures'
export const TRANSFER_DEPARTURE_SOURCE = 'logistics.transfer-departures.v1'
export type TransferDepartureStatus = 'InTransit' | 'PartiallyAccepted' | 'Accepted' | 'DispatchCancelled' | 'Closed'
export interface TransferDepartureCandidate {
  readonly transferId: string; readonly version: string; readonly status: TransferDepartureStatus
  readonly sourceLocationId: string; readonly sourceLocationCode: string; readonly sourceLocationName: string
  readonly destinationLocationId: string; readonly destinationLocationCode: string; readonly destinationLocationName: string
  readonly dispatchedAtUtc: string
}
export interface TransferDepartureCandidates {
  readonly companyId: string; readonly items: readonly TransferDepartureCandidate[]; readonly windowFull: boolean
}
export interface TransferOfficialDocument {
  readonly companyId: string; readonly transferId: string; readonly officialDocumentId: string; readonly version: number
  readonly isLinked: true; readonly linkedAtUtc: string; readonly isSentToGib: boolean; readonly sentAtUtc: string | null
  readonly cancelConfirmationVersion: number | null
}
export interface TransferDepartureFacts {
  readonly companyId: string; readonly transferId: string; readonly sourceLocationId: string
  readonly association: 0 | 1 | 2; readonly version: number | null; readonly exitWeighingAtUtc: string | null
  readonly vehicleExitAtUtc: string | null; readonly noFurtherExit: boolean
  readonly officialDocument: TransferOfficialDocument | null; readonly sourceVersion: string
}
export interface RecordTransferDeparture {
  readonly expectedVersion: string; readonly kind: 1 | 2; readonly occurredAtUtc: string; readonly reason: string
  readonly weighing: { readonly direction: 1 | 2; readonly firstKg: string; readonly secondKg: string } | null
}
const obj = (value: unknown): value is Record<string, unknown> => value !== null && typeof value === 'object' && !Array.isArray(value)
const exact = (value: Record<string, unknown>, fields: readonly string[]) => Object.keys(value).length === fields.length && fields.every((field) => Object.hasOwn(value, field))
const text = (value: unknown, max: number): value is string => typeof value === 'string' && value.trim() !== '' && value.length <= max && !/[\u0000-\u001f\u007f-\u009f]/.test(value)
const id = (value: unknown, expected: string): value is string => isBoundUuid(value) && value.toLowerCase() === expected.toLowerCase()
const positive = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value > 0
const version = (value: unknown): value is string => typeof value === 'string' && /^[1-9]\d{0,18}$/.test(value) && BigInt(value) <= 9223372036854775807n
export const transferDepartureUtc = (value: unknown): value is string => typeof value === 'string'
  && /^[0-9]{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|\+00:00)$/.test(value)
  && !value.startsWith('0000-') && Number.isFinite(Date.parse(value)) && new Date(value).toISOString().slice(0, 10) === value.slice(0, 10)
const nullableUtc = (value: unknown): value is string | null => value === null || transferDepartureUtc(value)
const instant = (value: string) => value.replace(/\+00:00$/, 'Z').replace(/\.0+Z$/, 'Z').replace(/(\.\d*?[1-9])0+Z$/, '$1Z')
export const transferDepartureKilograms = (value: string) => /^(?:0|[1-9]\d{0,14})(?:\.\d{1,3})?$/.test(value) && /[1-9]/.test(value)
export function decodeTransferDepartureCandidates(raw: unknown, company: string): TransferDepartureCandidates | null {
  if (!obj(raw) || !exact(raw, ['companyId', 'items', 'windowFull']) || !id(raw.companyId, company)
    || !Array.isArray(raw.items) || raw.items.length > 50 || typeof raw.windowFull !== 'boolean') return null
  const items: TransferDepartureCandidate[] = []
  for (const value of raw.items) {
    if (!obj(value) || !exact(value, ['transferId', 'version', 'status', 'sourceLocationId', 'sourceLocationCode', 'sourceLocationName',
      'destinationLocationId', 'destinationLocationCode', 'destinationLocationName', 'dispatchedAtUtc'])
      || !isBoundUuid(value.transferId) || !version(value.version) || typeof value.status !== 'string'
      || !['InTransit', 'PartiallyAccepted', 'Accepted', 'DispatchCancelled', 'Closed'].includes(value.status)
      || !isBoundUuid(value.sourceLocationId) || !isBoundUuid(value.destinationLocationId) || !text(value.sourceLocationCode, 200)
      || !text(value.destinationLocationCode, 200) || !text(value.sourceLocationName, 2000) || !text(value.destinationLocationName, 2000)
      || !transferDepartureUtc(value.dispatchedAtUtc)) return null
    items.push(value as unknown as TransferDepartureCandidate)
  }
  return new Set(items.map((value) => value.transferId.toLowerCase())).size === items.length
    ? { companyId: raw.companyId, items, windowFull: raw.windowFull } : null
}
function official(raw: unknown, company: string, transfer: string): TransferOfficialDocument | null {
  if (!obj(raw) || !exact(raw, ['companyId', 'transferId', 'officialDocumentId', 'version', 'isLinked', 'linkedAtUtc', 'isSentToGib', 'sentAtUtc', 'cancelConfirmationVersion'])
    || !id(raw.companyId, company) || !id(raw.transferId, transfer) || !isBoundUuid(raw.officialDocumentId) || !positive(raw.version)
    || raw.isLinked !== true || !transferDepartureUtc(raw.linkedAtUtc) || typeof raw.isSentToGib !== 'boolean'
    || !(raw.cancelConfirmationVersion === null || positive(raw.cancelConfirmationVersion))) return null
  if (raw.isSentToGib ? !transferDepartureUtc(raw.sentAtUtc) || Date.parse(raw.sentAtUtc) < Date.parse(raw.linkedAtUtc) : raw.sentAtUtc !== null) return null
  return raw as unknown as TransferOfficialDocument
}
export function decodeTransferDepartureFacts(raw: unknown, company: string, transfer: string, location: string): TransferDepartureFacts | null {
  if (!obj(raw) || !exact(raw, ['companyId', 'transferId', 'sourceLocationId', 'association', 'version', 'exitWeighingAtUtc', 'vehicleExitAtUtc', 'noFurtherExit', 'officialDocument', 'sourceVersion'])
    || !id(raw.companyId, company) || !id(raw.transferId, transfer) || !id(raw.sourceLocationId, location)
    || !nullableUtc(raw.exitWeighingAtUtc) || !nullableUtc(raw.vehicleExitAtUtc) || typeof raw.noFurtherExit !== 'boolean') return null
  if (raw.association === 0) {
    if (raw.version !== null || raw.exitWeighingAtUtc !== null || raw.vehicleExitAtUtc !== null || raw.noFurtherExit !== false
      || raw.officialDocument !== null || raw.sourceVersion !== `${TRANSFER_DEPARTURE_SOURCE}/legacy-unknown`) return null
  } else {
    if (!positive(raw.version) || raw.sourceVersion !== `${TRANSFER_DEPARTURE_SOURCE}/${raw.version}`
      || raw.noFurtherExit && (raw.exitWeighingAtUtc !== null || raw.vehicleExitAtUtc !== null)) return null
    if (raw.association === 1 ? raw.officialDocument !== null : raw.association !== 2 || official(raw.officialDocument, company, transfer) === null) return null
  }
  return raw as unknown as TransferDepartureFacts
}
async function read<T>(path: string, company: string, decode: (raw: unknown) => T | null, signal?: AbortSignal): Promise<ViewOutcome<T>> {
  let response: Response
  try { response = await fetch(`${API_BASE}${path}`, { headers: logisticsHeaders(company), signal }) }
  catch { return signal?.aborted ? { type: 'fence-stale' } : navigator.onLine ? { type: 'network-error' } : { type: 'offline' } }
  let raw: unknown = null; try { raw = await response.json() } catch { /* Reject malformed success below. */ }
  if (signal?.aborted) return { type: 'fence-stale' }
  if (response.status !== 200) return { type: 'http', status: response.status, body: raw }
  const value = decode(raw); return value === null ? { type: 'malformed' } : { type: 'ok-single', data: value }
}
export const fetchTransferDepartureCandidates = (company: string, signal?: AbortSignal) => read(`${TRANSFER_DEPARTURE_PATH}/candidates`, company,
  (raw) => decodeTransferDepartureCandidates(raw, company), signal)
export const fetchTransferDepartureFacts = (company: string, item: TransferDepartureCandidate, signal?: AbortSignal) => read(`${TRANSFER_DEPARTURE_PATH}/${encodeURIComponent(item.transferId)}`, company,
  (raw) => decodeTransferDepartureFacts(raw, company, item.transferId, item.sourceLocationId), signal)
export type TransferDepartureCommandOutcome = { readonly type: 'applied'; readonly facts: TransferDepartureFacts; readonly replayed: boolean }
  | { readonly type: 'refused'; readonly status: number; readonly code: string | null }
  | { readonly type: 'unknown'; readonly reason: 'offline' | 'network-error' | 'service-unavailable' | 'malformed-response' }
export interface TransferDepartureSnapshot {
  readonly binding: string; readonly companyId: string; readonly transferId: string; readonly sourceLocationId: string
  readonly payload: RecordTransferDeparture; readonly fingerprint: string; readonly key: string
}
export async function recordTransferDeparture(snapshot: TransferDepartureSnapshot): Promise<TransferDepartureCommandOutcome> {
  let response: Response
  try { response = await fetch(`${API_BASE}${TRANSFER_DEPARTURE_PATH}/${encodeURIComponent(snapshot.transferId)}`, {
    method: 'POST', headers: { ...logisticsHeaders(snapshot.companyId), 'Content-Type': 'application/json', 'Idempotency-Key': snapshot.key }, body: JSON.stringify(snapshot.payload),
  }) } catch { return { type: 'unknown', reason: navigator.onLine ? 'network-error' : 'offline' } }
  if (response.status >= 500) return { type: 'unknown', reason: 'service-unavailable' }
  let raw: unknown = null; try { raw = await response.json() } catch { /* A lost success body stays unknown. */ }
  if (response.status >= 400) return { type: 'refused', status: response.status, code: obj(raw) && typeof raw.code === 'string' ? raw.code : null }
  if (response.status !== 200 || !obj(raw) || !exact(raw, ['facts', 'isReplay', 'originalCorrelationId']) || typeof raw.isReplay !== 'boolean'
    || !text(raw.originalCorrelationId, 128)) return { type: 'unknown', reason: 'malformed-response' }
  const replay = response.headers.get('Idempotency-Replayed'); const correlation = response.headers.get('Idempotency-Original-Correlation-Id')
  const facts = decodeTransferDepartureFacts(raw.facts, snapshot.companyId, snapshot.transferId, snapshot.sourceLocationId)
  const at = snapshot.payload.kind === 1 ? facts?.exitWeighingAtUtc : facts?.vehicleExitAtUtc
  if (facts === null || facts.version === null || !version(snapshot.payload.expectedVersion) || BigInt(facts.version) !== BigInt(snapshot.payload.expectedVersion) + 1n
    || at == null || instant(at) !== instant(snapshot.payload.occurredAtUtc) || facts.noFurtherExit
    || (raw.isReplay ? replay !== 'true' || correlation !== raw.originalCorrelationId : replay !== null || correlation !== null)) return { type: 'unknown', reason: 'malformed-response' }
  return { type: 'applied', facts, replayed: raw.isReplay }
}
export type TransferDepartureAttempt = { readonly phase: 'idle' }
  | { readonly phase: 'sending'; readonly snapshot: TransferDepartureSnapshot; readonly unresolved: boolean }
  | { readonly phase: 'unknown'; readonly snapshot: TransferDepartureSnapshot; readonly reason: Extract<TransferDepartureCommandOutcome, { type: 'unknown' }>['reason'] }
  | { readonly phase: 'failed'; readonly snapshot: TransferDepartureSnapshot; readonly status: number; readonly unresolved: boolean }
  | { readonly phase: 'completed'; readonly snapshot: TransferDepartureSnapshot; readonly replayed: boolean }
const idle: TransferDepartureAttempt = Object.freeze({ phase: 'idle' })
const attempts = new Map<string, TransferDepartureAttempt>(); const listeners = new Map<string, Set<() => void>>()
export function transferDepartureBinding(shell: ShellState): string | null {
  const context = shell.activeContext
  return context?.scopeMode === 'single' && context.companyId !== null && !shell.sessionInvalid
    ? JSON.stringify([API_BASE, shell.sessionEpoch, context.userId, context.companyId.toLowerCase(), 'transfer-departure']) : null
}
export const transferDepartureAttempt = (binding: string | null): TransferDepartureAttempt => binding === null ? idle : attempts.get(binding) ?? idle
export function subscribeTransferDepartureAttempt(binding: string | null, callback: () => void): () => void {
  if (binding === null) return () => {}
  const group = listeners.get(binding) ?? new Set<() => void>(); group.add(callback); listeners.set(binding, group)
  return () => { group.delete(callback); if (group.size === 0) listeners.delete(binding) }
}
function publish(binding: string, value: TransferDepartureAttempt) { attempts.set(binding, value); listeners.get(binding)?.forEach((callback) => callback()) }
export const transferDepartureFrozen = (value: TransferDepartureAttempt) => value.phase === 'sending' || value.phase === 'unknown' || value.phase === 'failed' && value.unresolved
export function beginTransferDepartureAttempt(binding: string | null, companyId: string, transferId: string, sourceLocationId: string,
  payload: RecordTransferDeparture, key: () => string = () => `transfer-departure-${crypto.randomUUID()}`): TransferDepartureSnapshot | null {
  if (binding === null) return null
  const current = transferDepartureAttempt(binding); if (current.phase === 'sending') return null
  const fingerprint = JSON.stringify([companyId.toLowerCase(), transferId.toLowerCase(), sourceLocationId.toLowerCase(), payload])
  if (transferDepartureFrozen(current) && current.phase !== 'idle') {
    if (current.snapshot.fingerprint !== fingerprint) return null
    publish(binding, { phase: 'sending', snapshot: current.snapshot, unresolved: true }); return current.snapshot
  }
  const frozenPayload = Object.freeze({ ...payload, weighing: payload.weighing === null ? null : Object.freeze({ ...payload.weighing }) })
  const snapshot = Object.freeze({ binding, companyId, transferId, sourceLocationId, payload: frozenPayload, fingerprint, key: key() })
  publish(binding, { phase: 'sending', snapshot, unresolved: false }); return snapshot
}
export function finishTransferDepartureAttempt(snapshot: TransferDepartureSnapshot, outcome: TransferDepartureCommandOutcome) {
  const current = transferDepartureAttempt(snapshot.binding)
  if (current.phase === 'idle' || current.phase === 'completed' || current.snapshot.key !== snapshot.key || current.phase === 'failed' && !current.unresolved) return
  const unresolved = current.phase === 'unknown' || current.unresolved
  if (outcome.type === 'applied') publish(snapshot.binding, { phase: 'completed', snapshot, replayed: outcome.replayed })
  else if (outcome.type === 'unknown') publish(snapshot.binding, { phase: 'unknown', snapshot, reason: outcome.reason })
  // This owner persists success only. A subsequent refusal cannot disprove a former unknown commit.
  else publish(snapshot.binding, { phase: 'failed', snapshot, status: outcome.status, unresolved })
}
export function leaveTransferDepartureAttempt(binding: string | null) {
  const current = transferDepartureAttempt(binding)
  if (binding !== null && current.phase === 'sending') publish(binding, { phase: 'unknown', snapshot: current.snapshot, reason: 'network-error' })
}
export function resetTransferDepartureAttemptsForTests() { attempts.clear(); listeners.clear() }
