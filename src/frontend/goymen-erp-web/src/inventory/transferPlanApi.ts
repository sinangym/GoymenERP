import type { ShellState } from '../shell/shellState'
import type { ViewOutcome } from '../shell/uiState'
import { API_BASE, inventoryHeaders } from './api'
import { decodeTransferDetail, isTransferGuid, isTransferVersion, type TransferDetail } from './transferContract'
import { transferDispatchUtc } from './transferDispatchApi'

export const TRANSFER_PLAN_CAPABILITY = 'inventory.stock-transfer.write'
export const TRANSFER_PLAN_CANDIDATES_PATH = '/inventory/transfer-plan-candidates'
export interface TransferPlanLabel {
  readonly labelId: string; readonly labelCode: string; readonly labelVersion: string; readonly productId: string; readonly productVersion: string | null
  readonly sourceLocationId: string; readonly sourceCustodianKind: 'Company' | 'Counterparty'; readonly sourceCustodianId: string
  readonly physicalKg: string; readonly blockedPhysicalKg: string; readonly activeAllocationKg: string; readonly documentStatus: 'Complete' | 'PendingDocuments'
}
export interface TransferPlanDestination {
  readonly locationId: string; readonly locationCode: string; readonly locationName: string; readonly siteId: string; readonly siteCode: string; readonly siteName: string
  readonly locationVersion: number; readonly custodianKind: 'Company' | 'Counterparty'; readonly custodianId: string
}
export interface TransferPlanCandidates<T> { readonly companyId: string; readonly query: string | null; readonly limit: 25; readonly items: readonly T[]; readonly searchWindowFull: boolean }
export interface PlanTransferRequest {
  readonly transferId: string; readonly sourceLocationId: string; readonly sourceCustodianKind: 'Company' | 'Counterparty'; readonly sourceCustodianId: string
  readonly destinationLocationId: string; readonly destinationCustodianKind: 'Company' | 'Counterparty'; readonly destinationCustodianId: string; readonly plannedAtUtc: string
  readonly lines: readonly { readonly lineId: string; readonly labelId: string; readonly expectedLabelVersion: string; readonly plannedKg: string }[]
}
const obj = (value: unknown): value is Record<string, unknown> => value !== null && typeof value === 'object' && !Array.isArray(value)
const exact = (value: Record<string, unknown>, fields: readonly string[]) => Object.keys(value).length === fields.length && fields.every((field) => Object.hasOwn(value, field))
const text = (value: unknown, max: number): value is string => typeof value === 'string' && value.trim() !== '' && value.length <= max && !/[\u0000-\u001f\u007f-\u009f]/.test(value)
const id = (value: unknown, expected: string): value is string => isTransferGuid(value) && value.toLowerCase() === expected.toLowerCase()
const kind = (value: unknown): value is 'Company' | 'Counterparty' => value === 'Company' || value === 'Counterparty'
const kg = (value: unknown): value is string => typeof value === 'string' && /^(?:0|[1-9]\d{0,14})(?:\.\d{1,3})?$/.test(value)
export const positiveTransferPlanKg = (value: string) => kg(value) && /[1-9]/.test(value)
const instant = (value: string) => value.replace(/\+00:00$/, 'Z').replace(/\.0+Z$/, 'Z').replace(/(\.\d*?[1-9])0+Z$/, '$1Z')
function label(raw: unknown): TransferPlanLabel | null {
  if (!obj(raw) || !exact(raw, ['labelId', 'labelCode', 'labelVersion', 'productId', 'productVersion', 'sourceLocationId', 'sourceCustodianKind', 'sourceCustodianId',
    'physicalKg', 'blockedPhysicalKg', 'activeAllocationKg', 'documentStatus']) || !isTransferGuid(raw.labelId) || !text(raw.labelCode, 64)
    || !isTransferVersion(raw.labelVersion) || !isTransferGuid(raw.productId) || !(raw.productVersion === null || text(raw.productVersion, 128))
    || !isTransferGuid(raw.sourceLocationId) || !kind(raw.sourceCustodianKind) || !isTransferGuid(raw.sourceCustodianId)
    || !kg(raw.physicalKg) || !kg(raw.blockedPhysicalKg) || !kg(raw.activeAllocationKg)
    || (raw.documentStatus !== 'Complete' && raw.documentStatus !== 'PendingDocuments')) return null
  return raw as unknown as TransferPlanLabel
}
function destination(raw: unknown): TransferPlanDestination | null {
  if (!obj(raw) || !exact(raw, ['locationId', 'locationCode', 'locationName', 'siteId', 'siteCode', 'siteName', 'locationVersion', 'custodianKind', 'custodianId'])
    || !isTransferGuid(raw.locationId) || !isTransferGuid(raw.siteId) || !text(raw.locationCode, 2000) || !text(raw.locationName, 2000)
    || !text(raw.siteCode, 2000) || !text(raw.siteName, 2000) || !kind(raw.custodianKind) || !isTransferGuid(raw.custodianId)
    || typeof raw.locationVersion !== 'number' || !Number.isSafeInteger(raw.locationVersion) || raw.locationVersion <= 0) return null
  return raw as unknown as TransferPlanDestination
}
function frame(raw: unknown, company: string, query: string | null): raw is Record<string, unknown> & { companyId: string; items: unknown[]; searchWindowFull: boolean } {
  return obj(raw) && exact(raw, ['companyId', 'query', 'limit', 'items', 'searchWindowFull']) && id(raw.companyId, company)
    && raw.query === query && raw.limit === 25 && Array.isArray(raw.items) && raw.items.length <= 25 && typeof raw.searchWindowFull === 'boolean'
}
export function decodeTransferPlanLabels(raw: unknown, company: string, query: string | null): TransferPlanCandidates<TransferPlanLabel> | null {
  if (!frame(raw, company, query)) return null
  const items = raw.items.map(label); if (items.some((value) => value === null)) return null
  const values = items as TransferPlanLabel[]
  return new Set(values.map((value) => value.labelId.toLowerCase())).size === values.length
    ? { companyId: raw.companyId, query, limit: 25, items: values, searchWindowFull: raw.searchWindowFull } : null
}
export function decodeTransferPlanDestinations(raw: unknown, company: string, query: string | null): TransferPlanCandidates<TransferPlanDestination> | null {
  if (!frame(raw, company, query) || raw.searchWindowFull && raw.items.length !== 25) return null
  const items = raw.items.map(destination); if (items.some((value) => value === null)) return null
  const values = items as TransferPlanDestination[]
  return new Set(values.map((value) => value.locationId.toLowerCase())).size === values.length
    ? { companyId: raw.companyId, query, limit: 25, items: values, searchWindowFull: raw.searchWindowFull } : null
}
async function read<T>(path: string, company: string, query: string | null, decode: (raw: unknown) => T | null, signal?: AbortSignal): Promise<ViewOutcome<T>> {
  const params = new URLSearchParams({ limit: '25' }); if (query !== null) params.set('q', query)
  let response: Response
  try { response = await fetch(`${API_BASE}${path}?${params.toString()}`, { headers: inventoryHeaders(company), cache: 'no-store', signal }) }
  catch { return signal?.aborted ? { type: 'fence-stale' } : { type: navigator.onLine ? 'network-error' : 'offline' } }
  let raw: unknown = null; try { raw = await response.json() } catch { /* Malformed success never enables planning. */ }
  if (signal?.aborted) return { type: 'fence-stale' }
  if (response.status !== 200) return { type: 'http', status: response.status, body: raw }
  const value = decode(raw); return value === null ? { type: 'malformed' } : { type: 'ok-single', data: value }
}
export const fetchTransferPlanLabels = (company: string, query: string | null, signal?: AbortSignal) => read(TRANSFER_PLAN_CANDIDATES_PATH,
  company, query, (raw) => decodeTransferPlanLabels(raw, company, query), signal)
export const fetchTransferPlanDestinations = (company: string, query: string | null, signal?: AbortSignal) => read(`${TRANSFER_PLAN_CANDIDATES_PATH}/destinations`,
  company, query, (raw) => decodeTransferPlanDestinations(raw, company, query), signal)
export const sameTransferPlanSource = (a: TransferPlanLabel, b: TransferPlanLabel) => id(a.sourceLocationId, b.sourceLocationId)
  && a.sourceCustodianKind === b.sourceCustodianKind && id(a.sourceCustodianId, b.sourceCustodianId)
export const differentTransferPlanTarget = (source: TransferPlanLabel, target: TransferPlanDestination) => !id(source.sourceLocationId, target.locationId)
  || source.sourceCustodianKind !== target.custodianKind || !id(source.sourceCustodianId, target.custodianId)
export interface TransferPlanSnapshot {
  readonly binding: string; readonly companyId: string; readonly actorId: string; readonly labels: readonly TransferPlanLabel[]
  readonly destination: TransferPlanDestination; readonly payload: PlanTransferRequest; readonly key: string; readonly fingerprint: string
}
export type TransferPlanOutcome = { readonly type: 'applied'; readonly transfer: TransferDetail; readonly replayed: boolean }
  | { readonly type: 'refused'; readonly status: number; readonly code: string | null }
  | { readonly type: 'unknown'; readonly reason: 'offline' | 'network-error' | 'service-unavailable' | 'malformed-response' }
export async function sendTransferPlan(snapshot: TransferPlanSnapshot): Promise<TransferPlanOutcome> {
  let response: Response
  try { response = await fetch(`${API_BASE}/inventory/transfers`, { method: 'POST', cache: 'no-store', headers: {
    ...inventoryHeaders(snapshot.companyId), 'Content-Type': 'application/json', 'Idempotency-Key': snapshot.key }, body: JSON.stringify(snapshot.payload) }) }
  catch { return { type: 'unknown', reason: navigator.onLine ? 'network-error' : 'offline' } }
  if (response.status >= 500) return { type: 'unknown', reason: 'service-unavailable' }
  let raw: unknown = null; try { raw = await response.json() } catch { /* Keep unknown until the original result is proved. */ }
  if ([400, 401, 403, 404, 409, 415, 422].includes(response.status)) return { type: 'refused', status: response.status, code: obj(raw) && typeof raw.code === 'string' ? raw.code : null }
  const result = decodeTransferDetail(raw); const body = snapshot.payload
  const replayed = response.status === 200; const replay = response.headers.get('Idempotency-Replayed'); const correlation = response.headers.get('Idempotency-Original-Correlation-Id')
  if ((response.status !== 201 && !replayed) || result === null || !id(result.companyId, snapshot.companyId) || !id(result.transferId, body.transferId)
    || result.version !== '1' || result.status !== 'Planned' || result.dispatchedAtUtc !== null || !transferDispatchUtc(result.plannedAtUtc)
    || instant(result.plannedAtUtc) !== instant(body.plannedAtUtc) || !id(result.sourceLocationId, body.sourceLocationId)
    || result.sourceCustodianKind !== body.sourceCustodianKind || !id(result.sourceCustodianId, body.sourceCustodianId)
    || !id(result.destinationLocationId, body.destinationLocationId) || result.destinationCustodianKind !== body.destinationCustodianKind || !id(result.destinationCustodianId, body.destinationCustodianId)
    || result.lines.length !== body.lines.length || result.lines.some((line, index) => !id(line.lineId, body.lines[index].lineId) || !id(line.labelId, body.lines[index].labelId)
      || line.expectedLabelVersion !== body.lines[index].expectedLabelVersion || line.plannedKg !== body.lines[index].plannedKg || line.status !== 'Planned' || line.actualShippedKg !== null || line.differenceKg !== null)
    || (replayed ? replay !== 'true' || !text(correlation, 128) : replay !== null || correlation !== null)) return { type: 'unknown', reason: 'malformed-response' }
  return { type: 'applied', transfer: result, replayed }
}
export type TransferPlanAttempt = { readonly phase: 'idle' }
  | { readonly phase: 'sending'; readonly snapshot: TransferPlanSnapshot; readonly unresolved: boolean }
  | { readonly phase: 'unknown'; readonly snapshot: TransferPlanSnapshot; readonly reason: Extract<TransferPlanOutcome, { type: 'unknown' }>['reason'] }
  | { readonly phase: 'failed'; readonly snapshot: TransferPlanSnapshot; readonly status: number; readonly unresolved: boolean }
  | { readonly phase: 'completed'; readonly snapshot: TransferPlanSnapshot; readonly replayed: boolean }
const idle: TransferPlanAttempt = Object.freeze({ phase: 'idle' })
const attempts = new Map<string, TransferPlanAttempt>(); const listeners = new Map<string, Set<() => void>>()
export function transferPlanBinding(shell: ShellState): string | null {
  const context = shell.activeContext
  return context?.scopeMode === 'single' && context.companyId !== null && !shell.sessionInvalid
    ? JSON.stringify([API_BASE, shell.sessionEpoch, context.userId, context.companyId.toLowerCase(), 'inventory-transfer-plan']) : null
}
export const transferPlanAttempt = (binding: string | null): TransferPlanAttempt => binding === null ? idle : attempts.get(binding) ?? idle
export function subscribeTransferPlanAttempt(binding: string | null, callback: () => void): () => void {
  if (binding === null) return () => {}
  const group = listeners.get(binding) ?? new Set<() => void>(); group.add(callback); listeners.set(binding, group)
  return () => { group.delete(callback); if (group.size === 0) listeners.delete(binding) }
}
function publish(binding: string, value: TransferPlanAttempt) { attempts.set(binding, value); listeners.get(binding)?.forEach((callback) => callback()) }
export const transferPlanFrozen = (value: TransferPlanAttempt) => value.phase === 'sending' || value.phase === 'unknown' || value.phase === 'failed' && value.unresolved
export function beginTransferPlanAttempt(binding: string | null, companyId: string, actorId: string, labels: readonly TransferPlanLabel[], target: TransferPlanDestination,
  plannedAtUtc: string, uuid: () => string = () => crypto.randomUUID()): TransferPlanSnapshot | null {
  if (binding === null || !isTransferGuid(companyId) || !text(actorId, 128) || !transferDispatchUtc(plannedAtUtc)
    || labels.length < 1 || labels.length > 100 || labels.some((value) => label(value) === null || !positiveTransferPlanKg(value.physicalKg) || !sameTransferPlanSource(labels[0], value))
    || new Set(labels.map((value) => value.labelId.toLowerCase())).size !== labels.length || destination(target) === null || !differentTransferPlanTarget(labels[0], target)) return null
  const current = transferPlanAttempt(binding); if (current.phase === 'sending') return null
  const fingerprint = JSON.stringify([companyId.toLowerCase(), actorId, labels, target, plannedAtUtc])
  if (transferPlanFrozen(current) && current.phase !== 'idle') {
    if (current.snapshot.fingerprint !== fingerprint) return null
    publish(binding, { phase: 'sending', snapshot: current.snapshot, unresolved: true }); return current.snapshot
  }
  const first = labels[0]; const payload: PlanTransferRequest = { transferId: uuid(), sourceLocationId: first.sourceLocationId,
    sourceCustodianKind: first.sourceCustodianKind, sourceCustodianId: first.sourceCustodianId, destinationLocationId: target.locationId,
    destinationCustodianKind: target.custodianKind, destinationCustodianId: target.custodianId, plannedAtUtc,
    lines: labels.map((value) => ({ lineId: uuid(), labelId: value.labelId, expectedLabelVersion: value.labelVersion, plannedKg: value.physicalKg })) }
  const generated = [payload.transferId, ...payload.lines.map((line) => line.lineId)]
  if (generated.some((value) => !isTransferGuid(value)) || new Set(generated.map((value) => value.toLowerCase())).size !== generated.length) return null
  const snapshot: TransferPlanSnapshot = freeze({ binding, companyId, actorId, labels: structuredClone(labels), destination: structuredClone(target),
    payload, fingerprint, key: `transfer-plan-${uuid()}` })
  publish(binding, { phase: 'sending', snapshot, unresolved: false }); return snapshot
}
function freeze<T>(value: T): T { if (value !== null && typeof value === 'object') { Object.values(value).forEach((child) => freeze(child)); Object.freeze(value) } return value }
export function finishTransferPlanAttempt(snapshot: TransferPlanSnapshot, outcome: TransferPlanOutcome) {
  const current = transferPlanAttempt(snapshot.binding)
  if (current.phase === 'idle' || current.phase === 'completed' || current.snapshot.key !== snapshot.key || current.phase === 'failed' && !current.unresolved) return
  const unresolved = current.phase === 'unknown' || current.unresolved
  if (outcome.type === 'applied') publish(snapshot.binding, { phase: 'completed', snapshot, replayed: outcome.replayed })
  else if (outcome.type === 'unknown') publish(snapshot.binding, { phase: 'unknown', snapshot, reason: outcome.reason })
  else publish(snapshot.binding, { phase: 'failed', snapshot, status: outcome.status, unresolved: unresolved || outcome.status === 401 || outcome.status === 403 })
}
export function leaveTransferPlanAttempt(binding: string | null) {
  const current = transferPlanAttempt(binding)
  if (binding !== null && current.phase === 'sending') publish(binding, { phase: 'unknown', snapshot: current.snapshot, reason: 'network-error' })
}
export function resetTransferPlanAttemptsForTests() { attempts.clear(); listeners.clear() }
