import type { ShellState } from '../shell/shellState'
import type { ViewOutcome } from '../shell/uiState'
import { API_BASE, inventoryHeaders } from './api'
import { decodeTransferDetail, isTransferGuid, isTransferVersion, type TransferDetail } from './transferContract'

export const TRANSFER_DISPATCH_CAPABILITIES = {
  read: 'logistics.transfer-outgoing-documents.read', create: 'logistics.dispatch-notes.create',
  finalize: 'logistics.dispatch-notes.approve', cancel: 'logistics.dispatch-notes.cancel', dispatch: 'inventory.stock-transfer.write',
} as const
export const TRANSFER_OUTGOING_PATH = '/logistics/transfer-outgoing-documents'
export interface TransferOutgoingDocument {
  readonly companyId: string; readonly documentId: string; readonly transferId: string; readonly orderedTransferLineIds: readonly string[]
  readonly eDispatchNumber: string | null; readonly status: 'Draft' | 'Effective' | 'Cancelled'; readonly version: number
  readonly createdBy: string; readonly createdAtUtc: string; readonly effectiveBy: string | null; readonly effectiveAtUtc: string | null
  readonly cancelledBy: string | null; readonly cancelledAtUtc: string | null; readonly cancellationReason: string | null
}
export interface TransferOutgoingRead {
  readonly document: TransferOutgoingDocument; readonly association: 0 | 1 | 2; readonly departureVersion: number | null
}
export type TransferDispatchCommand =
  | { readonly kind: 'create'; readonly payload: { readonly documentId: string; readonly transferId: string; readonly orderedTransferLineIds: readonly string[]; readonly eDispatchNumber: null; readonly eDispatchAssociation: 1 } }
  | { readonly kind: 'finalize'; readonly document: TransferOutgoingDocument; readonly payload: { readonly expectedVersion: number } }
  | { readonly kind: 'cancel'; readonly document: TransferOutgoingDocument; readonly payload: { readonly expectedVersion: number; readonly reason: string } }
  | { readonly kind: 'dispatch'; readonly document: TransferOutgoingDocument; readonly payload: { readonly expectedTransferVersion: string; readonly dispatchedAtUtc: string; readonly outgoingDocumentId: string; readonly expectedOutgoingDocumentVersion: string } }
export interface TransferDispatchSnapshot {
  readonly binding: string; readonly companyId: string; readonly actorId: string; readonly transfer: TransferDetail
  readonly command: TransferDispatchCommand; readonly key: string; readonly fingerprint: string
}
export type TransferDispatchOutcome = { readonly type: 'applied'; readonly replayed: boolean }
  | { readonly type: 'refused'; readonly status: number; readonly code: string | null }
  | { readonly type: 'unknown'; readonly reason: 'offline' | 'network-error' | 'service-unavailable' | 'malformed-response' }
const obj = (value: unknown): value is Record<string, unknown> => value !== null && typeof value === 'object' && !Array.isArray(value)
const exact = (value: Record<string, unknown>, fields: readonly string[]) => Object.keys(value).length === fields.length && fields.every((field) => Object.hasOwn(value, field))
const text = (value: unknown, max: number): value is string => typeof value === 'string' && value.trim() !== '' && value.length <= max && !/[\u0000-\u001f\u007f-\u009f]/.test(value)
const id = (value: unknown, expected: string): value is string => isTransferGuid(value) && value.toLowerCase() === expected.toLowerCase()
const positive = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value > 0
export const transferDispatchUtc = (value: unknown): value is string => typeof value === 'string'
  && /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|\+00:00)$/.test(value) && !value.startsWith('0000-')
  && Number.isFinite(Date.parse(value)) && new Date(value).toISOString().slice(0, 10) === value.slice(0, 10)
const instant = (value: string) => value.replace(/\+00:00$/, 'Z').replace(/\.0+Z$/, 'Z').replace(/(\.\d*?[1-9])0+Z$/, '$1Z')
const sameIds = (left: readonly string[], right: readonly string[]) => left.length === right.length && left.every((value, index) => id(value, right[index]))
const kilograms = (value: string) => /^(?:0|[1-9]\d{0,14})(?:\.\d{1,3})?$/.test(value) && /[1-9]/.test(value)
export function validDispatchTransfer(value: TransferDetail): boolean {
  return decodeTransferDetail(value) !== null && transferDispatchUtc(value.plannedAtUtc) && value.lines.length > 0 && value.lines.length <= 200
    && new Set(value.lines.map((line) => line.lineId.toLowerCase())).size === value.lines.length
    && typeof value.sourceCustodianKind === 'string' && typeof value.destinationCustodianKind === 'string'
    && value.lines.every((line) => typeof line.status === 'string' && kilograms(line.plannedKg)
      && (line.actualShippedKg === null || kilograms(line.actualShippedKg)))
}
export function decodeTransferOutgoingDocument(raw: unknown, transfer: TransferDetail): TransferOutgoingDocument | null {
  if (!obj(raw) || !exact(raw, ['companyId', 'documentId', 'transferId', 'orderedTransferLineIds', 'eDispatchNumber', 'status', 'version',
    'createdBy', 'createdAtUtc', 'effectiveBy', 'effectiveAtUtc', 'cancelledBy', 'cancelledAtUtc', 'cancellationReason'])
    || !validDispatchTransfer(transfer) || !id(raw.companyId, transfer.companyId) || !id(raw.transferId, transfer.transferId)
    || !isTransferGuid(raw.documentId) || !Array.isArray(raw.orderedTransferLineIds) || !sameIds(raw.orderedTransferLineIds, transfer.lines.map((line) => line.lineId))
    || !(raw.eDispatchNumber === null || text(raw.eDispatchNumber, 64)) || !positive(raw.version)
    || !text(raw.createdBy, 128) || !transferDispatchUtc(raw.createdAtUtc)) return null
  const effective = raw.status === 'Effective' || raw.status === 'Cancelled' && raw.version === 3
  if (effective ? !text(raw.effectiveBy, 128) || !transferDispatchUtc(raw.effectiveAtUtc) || Date.parse(raw.effectiveAtUtc) < Date.parse(raw.createdAtUtc)
    : raw.effectiveBy !== null || raw.effectiveAtUtc !== null) return null
  if (raw.status === 'Cancelled') {
    if (raw.version !== 2 && raw.version !== 3 || !text(raw.cancelledBy, 128) || !transferDispatchUtc(raw.cancelledAtUtc)
      || !text(raw.cancellationReason, 500) || Date.parse(raw.cancelledAtUtc) < Date.parse(effective ? raw.effectiveAtUtc as string : raw.createdAtUtc)) return null
  } else if (raw.cancelledBy !== null || raw.cancelledAtUtc !== null || raw.cancellationReason !== null
    || (raw.status === 'Draft' ? raw.version !== 1 : raw.status !== 'Effective' || raw.version !== 2)) return null
  return raw as unknown as TransferOutgoingDocument
}
export function decodeTransferOutgoingRead(raw: unknown, transfer: TransferDetail): TransferOutgoingRead | null {
  if (!obj(raw) || !exact(raw, ['document', 'association', 'departureVersion'])) return null
  const document = decodeTransferOutgoingDocument(raw.document, transfer); if (document === null) return null
  if (raw.association === 0 ? raw.departureVersion !== null : (raw.association !== 1 && raw.association !== 2) || !positive(raw.departureVersion)) return null
  if (raw.association === 1 && document.eDispatchNumber !== null) return null
  return { document, association: raw.association as 0 | 1 | 2, departureVersion: raw.departureVersion as number | null }
}
export async function fetchTransferOutgoing(transfer: TransferDetail, signal?: AbortSignal): Promise<ViewOutcome<TransferOutgoingRead>> {
  if (!validDispatchTransfer(transfer)) return { type: 'malformed' }
  let response: Response
  try { response = await fetch(`${API_BASE}${TRANSFER_OUTGOING_PATH}/by-transfer/${encodeURIComponent(transfer.transferId)}`,
    { headers: inventoryHeaders(transfer.companyId), cache: 'no-store', signal }) }
  catch { return signal?.aborted ? { type: 'fence-stale' } : { type: navigator.onLine ? 'network-error' : 'offline' } }
  let raw: unknown = null; try { raw = await response.json() } catch { /* A malformed read never enables a command. */ }
  if (signal?.aborted) return { type: 'fence-stale' }
  if (response.status !== 200) return { type: 'http', status: response.status, body: raw }
  const value = decodeTransferOutgoingRead(raw, transfer); return value === null ? { type: 'malformed' } : { type: 'ok-single', data: value }
}
function immutableDocument(command: Exclude<TransferDispatchCommand, { readonly kind: 'create' }>, document: TransferOutgoingDocument): boolean {
  const prior = command.document
  return id(document.documentId, prior.documentId) && document.createdBy === prior.createdBy && instant(document.createdAtUtc) === instant(prior.createdAtUtc)
    && document.eDispatchNumber === prior.eDispatchNumber && sameIds(document.orderedTransferLineIds, prior.orderedTransferLineIds)
}
function appliedDocument(raw: unknown, snapshot: TransferDispatchSnapshot): boolean {
  const { command, transfer } = snapshot; const document = decodeTransferOutgoingDocument(raw, transfer)
  if (document === null || command.kind === 'dispatch') return false
  if (command.kind === 'create') return id(document.documentId, command.payload.documentId) && document.status === 'Draft' && document.version === 1
    && document.createdBy === snapshot.actorId && document.eDispatchNumber === null
  if (!immutableDocument(command, document) || document.version !== command.payload.expectedVersion + 1) return false
  if (command.kind === 'finalize') return document.status === 'Effective' && document.effectiveBy === snapshot.actorId
  if (command.kind === 'cancel') return document.status === 'Cancelled' && document.cancelledBy === snapshot.actorId
    && document.cancellationReason === command.payload.reason && document.effectiveBy === command.document.effectiveBy
    && (document.effectiveAtUtc === null ? command.document.effectiveAtUtc === null
      : command.document.effectiveAtUtc !== null && instant(document.effectiveAtUtc) === instant(command.document.effectiveAtUtc))
  return false
}
function appliedDispatch(raw: unknown, snapshot: TransferDispatchSnapshot): boolean {
  const command = snapshot.command; if (command.kind !== 'dispatch') return false
  const result = decodeTransferDetail(raw); const original = snapshot.transfer
  if (result === null || !validDispatchTransfer(result) || !id(result.companyId, snapshot.companyId) || !id(result.transferId, original.transferId)
    || !id(result.sourceLocationId, original.sourceLocationId) || !id(result.destinationLocationId, original.destinationLocationId)
    || result.sourceCustodianKind !== original.sourceCustodianKind || !id(result.sourceCustodianId, original.sourceCustodianId)
    || result.destinationCustodianKind !== original.destinationCustodianKind || !id(result.destinationCustodianId, original.destinationCustodianId)
    || instant(result.plannedAtUtc) !== instant(original.plannedAtUtc) || result.status !== 'InTransit'
    || BigInt(result.version) !== BigInt(command.payload.expectedTransferVersion) + 1n || result.dispatchedAtUtc === null
    || !transferDispatchUtc(result.dispatchedAtUtc) || instant(result.dispatchedAtUtc) !== instant(command.payload.dispatchedAtUtc)
    || !sameIds(result.lines.map((line) => line.lineId), original.lines.map((line) => line.lineId))) return false
  return result.lines.every((line, index) => id(line.labelId, original.lines[index].labelId) && line.expectedLabelVersion === original.lines[index].expectedLabelVersion
    && line.plannedKg === original.lines[index].plannedKg && line.status === 'InTransit' && line.actualShippedKg !== null && line.differenceKg !== null)
}
export async function sendTransferDispatch(snapshot: TransferDispatchSnapshot): Promise<TransferDispatchOutcome> {
  const { command } = snapshot
  if (!id(snapshot.companyId, snapshot.transfer.companyId) || !validDispatchCommand(snapshot.transfer, command))
    return { type: 'unknown', reason: 'malformed-response' }
  const path = command.kind === 'create' ? TRANSFER_OUTGOING_PATH : command.kind === 'dispatch'
    ? `/inventory/transfers/${encodeURIComponent(snapshot.transfer.transferId)}/dispatch`
    : `${TRANSFER_OUTGOING_PATH}/${encodeURIComponent(command.document.documentId)}/${command.kind}`
  let response: Response
  try { response = await fetch(`${API_BASE}${path}`, { method: 'POST', cache: 'no-store',
    headers: { ...inventoryHeaders(snapshot.companyId), 'Content-Type': 'application/json', 'Idempotency-Key': snapshot.key }, body: JSON.stringify(command.payload) }) }
  catch { return { type: 'unknown', reason: navigator.onLine ? 'network-error' : 'offline' } }
  if (response.status >= 500) return { type: 'unknown', reason: 'service-unavailable' }
  let raw: unknown = null; try { raw = await response.json() } catch { /* An unproven success remains unknown. */ }
  if ([400, 401, 403, 404, 409, 415, 422].includes(response.status))
    return { type: 'refused', status: response.status, code: obj(raw) && typeof raw.code === 'string' ? raw.code : null }
  const replay = response.headers.get('Idempotency-Replayed'); const correlation = response.headers.get('Idempotency-Original-Correlation-Id')
  if (command.kind === 'dispatch') {
    const replayed = response.status === 200
    if ((response.status !== 201 && !replayed) || !appliedDispatch(raw, snapshot)
      || (replayed ? replay !== 'true' || !text(correlation, 128) : replay !== null || correlation !== null)) return { type: 'unknown', reason: 'malformed-response' }
    return { type: 'applied', replayed }
  }
  if (response.status !== (command.kind === 'create' ? 201 : 200) || !obj(raw) || !exact(raw, ['document', 'isReplay', 'originalCorrelationId'])
    || typeof raw.isReplay !== 'boolean' || !text(raw.originalCorrelationId, 128) || !appliedDocument(raw.document, snapshot)
    || (raw.isReplay ? replay !== 'true' || correlation !== raw.originalCorrelationId : replay !== null || correlation !== null)) return { type: 'unknown', reason: 'malformed-response' }
  return { type: 'applied', replayed: raw.isReplay }
}
export type TransferDispatchAttempt = { readonly phase: 'idle' }
  | { readonly phase: 'sending'; readonly snapshot: TransferDispatchSnapshot; readonly unresolved: boolean }
  | { readonly phase: 'unknown'; readonly snapshot: TransferDispatchSnapshot; readonly reason: Extract<TransferDispatchOutcome, { type: 'unknown' }>['reason'] }
  | { readonly phase: 'failed'; readonly snapshot: TransferDispatchSnapshot; readonly status: number; readonly unresolved: boolean }
  | { readonly phase: 'completed'; readonly snapshot: TransferDispatchSnapshot; readonly replayed: boolean }
const idle: TransferDispatchAttempt = Object.freeze({ phase: 'idle' })
const attempts = new Map<string, TransferDispatchAttempt>(); const listeners = new Map<string, Set<() => void>>()
export function transferDispatchBinding(shell: ShellState): string | null {
  const context = shell.activeContext
  return context?.scopeMode === 'single' && context.companyId !== null && !shell.sessionInvalid
    ? JSON.stringify([API_BASE, shell.sessionEpoch, context.userId, context.companyId.toLowerCase(), 'transfer-outgoing-dispatch']) : null
}
export const transferDispatchAttempt = (binding: string | null): TransferDispatchAttempt => binding === null ? idle : attempts.get(binding) ?? idle
export function subscribeTransferDispatchAttempt(binding: string | null, callback: () => void): () => void {
  if (binding === null) return () => {}
  const group = listeners.get(binding) ?? new Set<() => void>(); group.add(callback); listeners.set(binding, group)
  return () => { group.delete(callback); if (group.size === 0) listeners.delete(binding) }
}
function publish(binding: string, value: TransferDispatchAttempt) { attempts.set(binding, value); listeners.get(binding)?.forEach((callback) => callback()) }
export const transferDispatchFrozen = (value: TransferDispatchAttempt) => value.phase === 'sending' || value.phase === 'unknown' || value.phase === 'failed' && value.unresolved
function validDispatchCommand(transfer: TransferDetail, command: TransferDispatchCommand): boolean {
  if (!validDispatchTransfer(transfer)) return false
  const planned = transfer.status === 'Planned' && transfer.dispatchedAtUtc === null && transfer.lines.every((line) => line.status === 'Planned')
  if (command.kind === 'create') return planned && isTransferGuid(command.payload.documentId) && id(command.payload.transferId, transfer.transferId)
    && command.payload.eDispatchAssociation === 1 && command.payload.eDispatchNumber === null
    && sameIds(command.payload.orderedTransferLineIds, transfer.lines.map((line) => line.lineId))
  if (decodeTransferOutgoingDocument(command.document, transfer) === null) return false
  if (command.kind === 'dispatch') return planned && command.document.status === 'Effective' && command.document.eDispatchNumber === null
    && command.payload.expectedTransferVersion === transfer.version && isTransferVersion(command.payload.expectedTransferVersion)
    && id(command.payload.outgoingDocumentId, command.document.documentId) && command.payload.expectedOutgoingDocumentVersion === String(command.document.version)
    && transferDispatchUtc(command.payload.dispatchedAtUtc)
  return command.payload.expectedVersion === command.document.version && (command.kind === 'finalize'
    ? planned && command.document.status === 'Draft' && command.document.eDispatchNumber === null
    : command.document.status !== 'Cancelled' && text(command.payload.reason, 500) && command.payload.reason === command.payload.reason.trim())
}
export function beginTransferDispatchAttempt(binding: string | null, actorId: string, transfer: TransferDetail, command: TransferDispatchCommand,
  key: () => string = () => `transfer-flow-${crypto.randomUUID()}`): TransferDispatchSnapshot | null {
  if (binding === null || !text(actorId, 128) || !validDispatchCommand(transfer, command)) return null
  const current = transferDispatchAttempt(binding); if (current.phase === 'sending') return null
  const fingerprint = JSON.stringify([transfer.companyId.toLowerCase(), actorId, transfer, command])
  if (transferDispatchFrozen(current) && current.phase !== 'idle') {
    if (current.snapshot.fingerprint !== fingerprint) return null
    publish(binding, { phase: 'sending', snapshot: current.snapshot, unresolved: true }); return current.snapshot
  }
  // JSON DTOs only: clone first so freezing cannot mutate another panel's live owner response.
  const savedTransfer = freeze(JSON.parse(JSON.stringify(transfer)) as TransferDetail)
  const savedCommand = freeze(JSON.parse(JSON.stringify(command)) as TransferDispatchCommand)
  const snapshot = Object.freeze({ binding, companyId: transfer.companyId, actorId, transfer: savedTransfer, command: savedCommand, key: key(), fingerprint })
  publish(binding, { phase: 'sending', snapshot, unresolved: false }); return snapshot
}
function freeze<T>(value: T): T {
  if (value !== null && typeof value === 'object') { Object.values(value).forEach((child) => freeze(child)); Object.freeze(value) }
  return value
}
export function finishTransferDispatchAttempt(snapshot: TransferDispatchSnapshot, outcome: TransferDispatchOutcome) {
  const current = transferDispatchAttempt(snapshot.binding)
  if (current.phase === 'idle' || current.phase === 'completed' || current.snapshot.key !== snapshot.key || current.phase === 'failed' && !current.unresolved) return
  const unresolved = current.phase === 'unknown' || current.unresolved
  if (outcome.type === 'applied') publish(snapshot.binding, { phase: 'completed', snapshot, replayed: outcome.replayed })
  else if (outcome.type === 'unknown') publish(snapshot.binding, { phase: 'unknown', snapshot, reason: outcome.reason })
  else publish(snapshot.binding, { phase: 'failed', snapshot, status: outcome.status, unresolved: unresolved || outcome.status === 401 || outcome.status === 403 })
}
export function leaveTransferDispatchAttempt(binding: string | null) {
  const current = transferDispatchAttempt(binding)
  if (binding !== null && current.phase === 'sending') publish(binding, { phase: 'unknown', snapshot: current.snapshot, reason: 'network-error' })
}
export function resetTransferDispatchAttemptsForTests() { attempts.clear(); listeners.clear() }
