import type { ShellState } from '../shell/shellState'
import type { ViewOutcome } from '../shell/uiState'
import { API_BASE, inventoryHeaders } from './api'
import { decodeTransferDetail, decodeTransferList, isTransferGuid, isTransferVersion, type TransferDetail, type TransferDisplayItem } from './transferContract'
import { transferDispatchUtc } from './transferDispatchApi'

export const TRANSFER_RECEIPT_CAPABILITIES = { dispatch: 'inventory.stock-transfer.write', receive: 'inventory.stock-transfer.write', 'overage-decision': 'inventory.stock-transfer.overage-approve',
  'loss-request': 'inventory.stock-transfer.loss-record', 'loss-decision': 'inventory.stock-transfer.loss-approve' } as const
export type ReceiptPurpose = keyof typeof TRANSFER_RECEIPT_CAPABILITIES
export const RECEIPT_DOCUMENT_PATH = '/inventory/transfer-receipt-documents'
export interface ReceiptDocumentLine { readonly documentLineId: string; readonly transferLineId: string; readonly labelId: string; readonly dispatchedKg: string; readonly receivedKg: string; readonly overageReason: string | null }
export interface ReceiptDocument {
  readonly companyId: string; readonly documentId: string; readonly transferId: string; readonly version: string; readonly status: 'Draft' | 'Effective'
  readonly sourceTransferVersion: string; readonly receivedAtUtc: string; readonly lines: readonly ReceiptDocumentLine[]; readonly createdBy: string; readonly createdAtUtc: string
  readonly finalizedBy: string | null; readonly finalizedAtUtc: string | null; readonly finalizationReason: string | null; readonly sourceVersion: 'inventory.transfer-receipt-document.v1'
}
export interface ReceiptDocuments { readonly companyId: string; readonly transferId: string; readonly items: readonly ReceiptDocument[]; readonly searchWindowFull: boolean; readonly nextCursor: string | null }
export interface ReceiptFact {
  readonly lineId: string; readonly dispatchedKg: string; readonly receivedKg: string; readonly shortageKg: string; readonly overageKg: string; readonly shortageMovementId: string | null
  readonly overageReason: string | null; readonly receivedAtUtc: string; readonly receivedBy: string; readonly overageStatus: 'None' | 'Pending' | 'Approved' | 'Rejected'
  readonly overageDecisionReason: string | null; readonly overageDecidedAtUtc: string | null; readonly overageDecidedBy: string | null; readonly overageMovementId: string | null
  /** KLN-D119 (AK-2b): the four piece figures of a piece-tracking line (all or none). */
  readonly dispatchedPieceCount?: number; readonly receivedPieceCount?: number; readonly shortagePieceCount?: number; readonly overagePieceCount?: number
}
export interface ReceiptLoss {
  readonly lineId: string; readonly requestNumber: number; readonly explanation: string; readonly requestedAtUtc: string; readonly requestedBy: string
  readonly status: 'Pending' | 'Approved' | 'Rejected' | 'Withdrawn'; readonly decisionReason: string | null; readonly decidedAtUtc: string | null; readonly decidedBy: string | null; readonly movementId: string | null
}
export interface ReceiptOverageRenewal {
  readonly lineId: string; readonly requestNumber: number; readonly overageKg: string; readonly reason: string; readonly requestedAtUtc: string; readonly requestedBy: string
  readonly status: 'Pending' | 'Approved' | 'Rejected'; readonly decisionReason: string | null; readonly decidedAtUtc: string | null; readonly decidedBy: string | null; readonly movementId: string | null
  /** KLN-D119: the re-requested excess pieces of a piece-tracking line; absent for a kg-only line. */
  readonly overagePieceCount?: number
}
export interface ReceiptFacts { readonly companyId: string; readonly transferId: string; readonly version: string; readonly status: string; readonly receipts: readonly ReceiptFact[]; readonly losses: readonly ReceiptLoss[]; readonly overageRenewals: readonly ReceiptOverageRenewal[] }
export interface ReceiptWork { readonly purpose: ReceiptPurpose; readonly transfer: TransferDetail; readonly facts: ReceiptFacts }
export interface ReceiptCandidate extends TransferDisplayItem { readonly labelCodes: readonly string[] }
export interface ReceiptCandidates { readonly companyId: string; readonly purpose: ReceiptPurpose; readonly query: string | null; readonly limit: 25; readonly items: readonly ReceiptCandidate[]; readonly searchWindowFull: boolean; readonly nextCursor: string | null }
interface CreateBody { readonly documentId: string; readonly transferId: string; readonly expectedTransferVersion: string; readonly receivedAtUtc: string; readonly lines: readonly { readonly documentLineId: string; readonly transferLineId: string; readonly receivedKg: string; readonly overageReason: string | null }[] }
interface ReceiveBody { readonly expectedTransferVersion: string; readonly receivedAtUtc: string; readonly incomingDocument: { readonly kind: 'inventory.transfer.incoming'; readonly id: string; readonly lineId: string }; readonly receivedKg: string; readonly overageReason: string | null; readonly expectedIncomingDocumentVersion: string
  /** KLN-D119 (AK-2b): the received pieces — sent exactly for a line dispatched with pieces (`dispatchedPieceCount`). */
  readonly receivedPieceCount?: number }
export type ReceiptCommand =
  | { readonly kind: 'create'; readonly payload: CreateBody }
  | { readonly kind: 'finalize'; readonly document: ReceiptDocument; readonly payload: { readonly expectedDocumentVersion: string; readonly expectedTransferVersion: string; readonly reason: string } }
  | { readonly kind: 'receive'; readonly lineId: string; readonly document: ReceiptDocument; readonly payload: ReceiveBody }
  | { readonly kind: 'overage-decision'; readonly lineId: string; readonly payload: { readonly decision: 'Approve' | 'Reject'; readonly decidedAtUtc: string; readonly reason: string; readonly requestNumber: number } }
  | { readonly kind: 'overage-renewal'; readonly lineId: string; readonly payload: { readonly expectedTransferVersion: string; readonly requestedAtUtc: string; readonly overageKg: string; readonly reason: string
    /** KLN-D119: the re-requested excess pieces — sent exactly for a piece-tracking line. */
    readonly overagePieceCount?: number } }
  | { readonly kind: 'loss-request'; readonly lineId: string; readonly payload: { readonly expectedTransferVersion: string; readonly requestedAtUtc: string; readonly explanation: string } }
  | { readonly kind: 'loss-decision'; readonly lineId: string; readonly payload: { readonly expectedTransferVersion: string; readonly decision: 'Approve' | 'Reject'; readonly decidedAtUtc: string; readonly reason: string; readonly requestNumber: number } }
  | { readonly kind: 'loss-withdrawal'; readonly lineId: string; readonly payload: { readonly expectedTransferVersion: string; readonly withdrawnAtUtc: string; readonly reason: string; readonly requestNumber: number } }
export const receiptCommandPurpose = (kind: ReceiptCommand['kind']): ReceiptPurpose => kind === 'overage-decision' || kind === 'loss-decision' ? kind : kind === 'loss-request' || kind === 'loss-withdrawal' ? 'loss-request' : 'receive'
const obj = (value: unknown): value is Record<string, unknown> => value !== null && typeof value === 'object' && !Array.isArray(value)
const exact = (value: Record<string, unknown>, keys: readonly string[]) => Object.keys(value).length === keys.length && keys.every((key) => Object.hasOwn(value, key))
const text = (value: unknown, max: number): value is string => typeof value === 'string' && value !== '' && value === value.trim() && value.length <= max && !/[\u0000-\u001f\u007f-\u009f]/.test(value)
export const receiptReason = (value: unknown): value is string => text(value, 500)
export const receiptKg = (value: unknown): value is string => typeof value === 'string' && /^(?:0|[1-9]\d{0,14})(?:\.\d{0,2}[1-9])?$/.test(value)
export const receiptPositiveKg = (value: unknown): value is string => receiptKg(value) && value !== '0'
const id = (value: unknown, expected: string): value is string => isTransferGuid(value) && value.toLowerCase() === expected.toLowerCase()
const nullableId = (value: unknown) => value === null || isTransferGuid(value)
/** KLN-D119: a stored piece count (`int`, never text, null or a fraction). */
const pieceCount = (value: unknown): value is number => typeof value === 'number' && Number.isInteger(value) && value >= 0 && value <= 2147483647
const RECEIPT_PIECES = ['dispatchedPieceCount', 'receivedPieceCount', 'shortagePieceCount', 'overagePieceCount'] as const
/** The writer's piece check: all four or none; dispatched/received positive; the difference is one-sided and goes with a kg difference. */
function receiptPieces(row: Record<string, unknown>, present: readonly string[]): boolean {
  if (present.length === 0) return true
  if (present.length !== RECEIPT_PIECES.length) return false
  const { dispatchedPieceCount: d, receivedPieceCount: r, shortagePieceCount: s, overagePieceCount: o } = row
  return pieceCount(d) && pieceCount(r) && pieceCount(s) && pieceCount(o) && d > 0 && r > 0 && (s === 0 || o === 0)
    && r + s - o === d && (s === 0 || row.shortageKg !== '0') && (o === 0 || row.overageKg !== '0')
}
const numbered = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value > 0 && value <= 2147483647
const same = (a: unknown, b: unknown) => JSON.stringify(a) === JSON.stringify(b)
const instant = (value: string) => value.replace(/\+00:00$/, 'Z').replace(/\.0+Z$/, 'Z').replace(/(\.\d*?[1-9])0+Z$/, '$1Z')
const at = (a: string, b: string) => instant(a) === instant(b)
const unique = (values: readonly string[]) => new Set(values.map((value) => value.toLowerCase())).size === values.length
export function decodeReceiptDocument(raw: unknown, transfer: TransferDetail): ReceiptDocument | null {
  if (!isTransferVersion(transfer.version) || !obj(raw) || !exact(raw, ['companyId', 'documentId', 'transferId', 'version', 'status', 'sourceTransferVersion', 'receivedAtUtc', 'lines', 'createdBy', 'createdAtUtc', 'finalizedBy', 'finalizedAtUtc', 'finalizationReason', 'sourceVersion'])
    || !id(raw.companyId, transfer.companyId) || !id(raw.transferId, transfer.transferId) || !isTransferGuid(raw.documentId) || !isTransferVersion(raw.sourceTransferVersion)
    || BigInt(raw.sourceTransferVersion) > BigInt(transfer.version) || !transferDispatchUtc(raw.receivedAtUtc)
    || transfer.dispatchedAtUtc === null || Date.parse(raw.receivedAtUtc) < Date.parse(transfer.dispatchedAtUtc)
    || !text(raw.createdBy, 128) || !transferDispatchUtc(raw.createdAtUtc) || raw.sourceVersion !== 'inventory.transfer-receipt-document.v1'
    || !Array.isArray(raw.lines) || raw.lines.length < 1 || raw.lines.length > 100) return null
  if (!(raw.status === 'Draft' && raw.version === '1' && raw.finalizedBy === null && raw.finalizedAtUtc === null && raw.finalizationReason === null
    || raw.status === 'Effective' && raw.version === '2' && text(raw.finalizedBy, 128) && transferDispatchUtc(raw.finalizedAtUtc) && receiptReason(raw.finalizationReason)
      && Date.parse(raw.finalizedAtUtc) >= Date.parse(raw.createdAtUtc))) return null
  for (const line of raw.lines) {
    if (!obj(line) || !exact(line, ['documentLineId', 'transferLineId', 'labelId', 'dispatchedKg', 'receivedKg', 'overageReason']) || !isTransferGuid(line.documentLineId)
      || !isTransferGuid(line.transferLineId) || !isTransferGuid(line.labelId) || !receiptPositiveKg(line.dispatchedKg) || !receiptPositiveKg(line.receivedKg)
      || !(line.overageReason === null || receiptReason(line.overageReason)) || !transfer.lines.some((source) => id(line.transferLineId, source.lineId)
        && id(line.labelId, source.labelId) && line.dispatchedKg === source.actualShippedKg)) return null
  }
  if (!unique(raw.lines.map((line) => line.documentLineId)) || !unique(raw.lines.map((line) => line.transferLineId))) return null
  return raw as unknown as ReceiptDocument
}
export function decodeReceiptDocuments(raw: unknown, transfer: TransferDetail, cursor: string | null = null): ReceiptDocuments | null {
  if (!obj(raw) || !exact(raw, ['companyId', 'transferId', 'items', 'searchWindowFull', 'nextCursor']) || !id(raw.companyId, transfer.companyId)
    || !id(raw.transferId, transfer.transferId) || !Array.isArray(raw.items) || raw.items.length > 50 || typeof raw.searchWindowFull !== 'boolean'
    || !(raw.searchWindowFull ? raw.items.length === 50 && text(raw.nextCursor, 4096) && raw.nextCursor !== cursor : raw.nextCursor === null)) return null
  const items = raw.items.map((value) => decodeReceiptDocument(value, transfer))
  return items.some((value) => value === null) || !unique(items.map((value) => value!.documentId)) ? null
    : { companyId: raw.companyId, transferId: raw.transferId, items: items as ReceiptDocument[], searchWindowFull: raw.searchWindowFull, nextCursor: raw.nextCursor as string | null }
}
/** Repeated identical observations are deduplicated; changed versions and cursor loops require a fresh first page. */
export function mergeReceiptDocumentPages(pages: readonly ReceiptDocuments[]): readonly ReceiptDocument[] | null {
  const result = new Map<string, ReceiptDocument>(); const cursors = new Set<string>()
  for (const page of pages) {
    if (pages[0] && (!id(page.companyId, pages[0].companyId) || !id(page.transferId, pages[0].transferId))) return null
    if (page.nextCursor !== null) { if (cursors.has(page.nextCursor)) return null; cursors.add(page.nextCursor) }
    for (const document of page.items) { const prior = result.get(document.documentId.toLowerCase()); if (prior && !same(prior, document)) return null; result.set(document.documentId.toLowerCase(), document) }
  }
  return [...result.values()]
}
function decision(status: unknown, reason: unknown, time: unknown, actor: unknown, movement: unknown) {
  return status === 'Pending' || status === 'None' ? reason === null && time === null && actor === null && movement === null
    : (status === 'Approved' || status === 'Rejected' || status === 'Withdrawn') && receiptReason(reason) && transferDispatchUtc(time) && text(actor, 128)
      && (status === 'Approved' ? isTransferGuid(movement) : movement === null)
}
export function decodeReceiptFacts(raw: unknown, transfer: TransferDetail): ReceiptFacts | null {
  if (!obj(raw) || !exact(raw, ['companyId', 'transferId', 'version', 'status', 'receipts', 'losses', 'overageRenewals']) || !id(raw.companyId, transfer.companyId)
    || !id(raw.transferId, transfer.transferId) || raw.version !== transfer.version || raw.status !== transfer.status
    || !Array.isArray(raw.receipts) || !Array.isArray(raw.losses) || !Array.isArray(raw.overageRenewals)
    || raw.receipts.length > 100 || raw.losses.length > 10000 || raw.overageRenewals.length > 10000) return null
  const receipts = raw.receipts
  const sourceLine = (value: unknown) => isTransferGuid(value) && transfer.lines.some((line) => id(value, line.lineId))
  for (const row of raw.receipts) {
    const pieces = obj(row) ? RECEIPT_PIECES.filter((key) => Object.hasOwn(row, key)) : []
    if (!obj(row) || !exact(row, ['lineId', 'dispatchedKg', 'receivedKg', 'shortageKg', 'overageKg', 'shortageMovementId', 'overageReason', 'receivedAtUtc', 'receivedBy', 'overageStatus', 'overageDecisionReason', 'overageDecidedAtUtc', 'overageDecidedBy', 'overageMovementId', ...pieces])
      || !receiptPieces(row, pieces)
      || !sourceLine(row.lineId) || !receiptPositiveKg(row.dispatchedKg) || !receiptPositiveKg(row.receivedKg) || !receiptKg(row.shortageKg) || !receiptKg(row.overageKg)
      || !nullableId(row.shortageMovementId) || !(row.overageReason === null || receiptReason(row.overageReason)) || !transferDispatchUtc(row.receivedAtUtc) || !text(row.receivedBy, 128)
      || !['None', 'Pending', 'Approved', 'Rejected'].includes(String(row.overageStatus))
      || (row.overageStatus === 'None') !== (row.overageKg === '0') || (row.overageStatus === 'None' ? row.overageReason !== null : !receiptReason(row.overageReason))
      || (row.shortageKg === '0') !== (row.shortageMovementId === null)
      || !decision(row.overageStatus, row.overageDecisionReason, row.overageDecidedAtUtc, row.overageDecidedBy, row.overageMovementId)
      || !transfer.lines.some((line) => id(row.lineId, line.lineId) && row.dispatchedKg === line.actualShippedKg)) return null
  }
  for (const [rows, renewal] of [[raw.losses, false], [raw.overageRenewals, true]] as const) for (const row of rows) {
    // KLN-D119: only a renewal of a piece-tracking line names its excess pieces; a loss request never does.
    const renewalPieces = renewal && obj(row) && Object.hasOwn(row, 'overagePieceCount') ? ['overagePieceCount'] : []
    if (!obj(row) || !exact(row, ['lineId', 'requestNumber', ...(renewal ? ['overageKg', 'reason'] : ['explanation']), 'requestedAtUtc', 'requestedBy', 'status', 'decisionReason', 'decidedAtUtc', 'decidedBy', 'movementId', ...renewalPieces])
      || (renewalPieces.length > 0 && !pieceCount(row.overagePieceCount))
      || !sourceLine(row.lineId) || !numbered(row.requestNumber) || renewal && row.requestNumber < 2
      || !(renewal ? receiptPositiveKg(row.overageKg) && receiptReason(row.reason) : receiptReason(row.explanation))
      || !transferDispatchUtc(row.requestedAtUtc) || !text(row.requestedBy, 128)
      || !(renewal ? ['Pending', 'Approved', 'Rejected'] : ['Pending', 'Approved', 'Rejected', 'Withdrawn']).includes(String(row.status))
      || !decision(row.status, row.decisionReason, row.decidedAtUtc, row.decidedBy, row.movementId)) return null
  }
  if (!unique(raw.receipts.map((row) => row.lineId)) || !unique(raw.losses.map((row) => `${row.lineId}:${row.requestNumber}`))
    || !unique(raw.overageRenewals.map((row) => `${row.lineId}:${row.requestNumber}`))
    || !unique(raw.losses.filter((row) => row.status === 'Pending').map((row) => row.lineId))
    || !unique([...raw.receipts.filter((row) => row.overageStatus === 'Pending'), ...raw.overageRenewals.filter((row) => row.status === 'Pending')].map((row) => row.lineId))
    || raw.overageRenewals.some((row) => !receipts.some((original) => id(row.lineId, original.lineId) && original.overageStatus !== 'None'))) return null
  return raw as unknown as ReceiptFacts
}
export function decodeReceiptWork(raw: unknown, company: string, transferId: string, purpose: ReceiptPurpose): ReceiptWork | null {
  if (!obj(raw) || !exact(raw, ['purpose', 'transfer', 'facts']) || raw.purpose !== purpose) return null
  const transfer = decodeTransferDetail(raw.transfer)
  if (transfer === null || !id(transfer.companyId, company) || !id(transfer.transferId, transferId) || transfer.lines.length < 1 || transfer.lines.length > 100
    || !unique(transfer.lines.map((line) => line.lineId)) || !transferDispatchUtc(transfer.plannedAtUtc)
    || !(transfer.dispatchedAtUtc === null || transferDispatchUtc(transfer.dispatchedAtUtc))) return null
  const facts = decodeReceiptFacts(raw.facts, transfer)
  return facts === null ? null : { purpose, transfer, facts }
}
export function decodeReceiptCandidates(raw: unknown, company: string, purpose: ReceiptPurpose, query: string | null, cursor: string | null = null): ReceiptCandidates | null {
  if (!obj(raw) || !exact(raw, ['companyId', 'purpose', 'query', 'limit', 'items', 'searchWindowFull', 'nextCursor']) || !id(raw.companyId, company)
    || raw.purpose !== purpose || raw.query !== query || raw.limit !== 25 || !Array.isArray(raw.items) || raw.items.length > 25 || typeof raw.searchWindowFull !== 'boolean'
    || !(raw.searchWindowFull ? raw.items.length === 25 && text(raw.nextCursor, 4096) && raw.nextCursor !== cursor : raw.nextCursor === null)) return null
  for (const row of raw.items) {
    if (!obj(row) || !exact(row, ['transfer', 'labelCodes', 'sourceLocationCode', 'sourceLocationName', 'destinationLocationCode', 'destinationLocationName'])
      || !Array.isArray(row.labelCodes) || row.labelCodes.length < 1 || row.labelCodes.length > 100 || row.labelCodes.some((code) => !text(code, 64))
      || !text(row.sourceLocationCode, 2000) || !text(row.sourceLocationName, 2000) || !text(row.destinationLocationCode, 2000) || !text(row.destinationLocationName, 2000)) return null
    const { labelCodes, ...display } = row; void labelCodes
    const decoded = decodeTransferList({ companyId: raw.companyId, items: [display], windowFull: false })
    if (decoded === null || !transferDispatchUtc(decoded.items[0].transfer.plannedAtUtc)
      || !(decoded.items[0].transfer.dispatchedAtUtc === null || transferDispatchUtc(decoded.items[0].transfer.dispatchedAtUtc))) return null
  }
  if (!unique(raw.items.map((row) => row.transfer.transferId))) return null
  return raw as unknown as ReceiptCandidates
}
export function mergeReceiptCandidatePages(pages: readonly ReceiptCandidates[]): readonly ReceiptCandidate[] | null {
  const result = new Map<string, ReceiptCandidate>(); const cursors = new Set<string>()
  for (const page of pages) {
    const first = pages[0]
    if (first && (!id(page.companyId, first.companyId) || page.purpose !== first.purpose || page.query !== first.query)) return null
    if (page.nextCursor !== null) { if (cursors.has(page.nextCursor)) return null; cursors.add(page.nextCursor) }
    for (const row of page.items) { const previous = result.get(row.transfer.transferId.toLowerCase()); if (previous && !same(previous, row)) return null; result.set(row.transfer.transferId.toLowerCase(), row) }
  }
  return [...result.values()]
}
async function read<T>(path: string, company: string, decode: (raw: unknown) => T | null, signal?: AbortSignal): Promise<ViewOutcome<T>> {
  try {
    const response = await fetch(`${API_BASE}${path}`, { headers: inventoryHeaders(company), cache: 'no-store', signal }); let raw: unknown = null
    try { raw = await response.json() } catch { /* An unreadable response cannot supply owner proof. */ }
    if (signal?.aborted) return { type: 'fence-stale' }
    if (response.status !== 200) return { type: 'http', status: response.status, body: raw }
    const value = decode(raw); return value === null ? { type: 'malformed' } : { type: 'ok-single', data: value }
  } catch { return signal?.aborted ? { type: 'fence-stale' } : { type: navigator.onLine ? 'network-error' : 'offline' } }
}
export const fetchReceiptWork = (company: string, transferId: string, purpose: ReceiptPurpose, signal?: AbortSignal) => read(`/inventory/transfers/${encodeURIComponent(transferId)}/receipt-work?purpose=${purpose}`,
  company, (raw) => decodeReceiptWork(raw, company, transferId, purpose), signal)
export const fetchReceiptCandidates = (company: string, purpose: ReceiptPurpose, query: string | null, cursor: string | null, signal?: AbortSignal) => {
  const parameters = new URLSearchParams({ purpose, limit: '25' }); if (query !== null) parameters.set('q', query); if (cursor !== null) parameters.set('cursor', cursor)
  return read(`/inventory/transfer-receipt-work/candidates?${parameters}`, company, (raw) => decodeReceiptCandidates(raw, company, purpose, query, cursor), signal)
}
export const fetchReceiptDocuments = (transfer: TransferDetail, cursor: string | null, signal?: AbortSignal) => read(`${RECEIPT_DOCUMENT_PATH}/by-transfer/${encodeURIComponent(transfer.transferId)}${cursor === null ? '' : `?cursor=${encodeURIComponent(cursor)}`}`,
  transfer.companyId, (raw) => decodeReceiptDocuments(raw, transfer, cursor), signal)
export class ReceiptReadError extends Error {
  readonly outcome: Exclude<ViewOutcome<never>, { type: 'ok' | 'ok-single' }>
  constructor(outcome: Exclude<ViewOutcome<never>, { type: 'ok' | 'ok-single' }>) { super('Receipt owner read refused'); this.outcome = outcome }
}
export interface ReceiptSnapshot { readonly binding: string; readonly actorId: string; readonly work: ReceiptWork; readonly command: ReceiptCommand; readonly key: string; readonly fingerprint: string }
export type ReceiptOutcome = { readonly type: 'applied'; readonly replayed: boolean; readonly document: ReceiptDocument | null; readonly work: ReceiptWork | null }
  | { readonly type: 'refused'; readonly status: number; readonly code: string | null }
  | { readonly type: 'unknown'; readonly reason: 'offline' | 'network-error' | 'service-unavailable' | 'malformed-response' }
export function validReceiptCommand(work: ReceiptWork, command: ReceiptCommand): boolean {
  if (decodeReceiptWork(work, work.transfer.companyId, work.transfer.transferId, work.purpose) === null || work.purpose !== receiptCommandPurpose(command.kind)) return false
  const payload = command.payload; const line = 'lineId' in command ? work.transfer.lines.find((row) => id(command.lineId, row.lineId)) : null
  if ('expectedTransferVersion' in payload && payload.expectedTransferVersion !== work.transfer.version) return false
  if ('reason' in payload && !receiptReason(payload.reason) || 'explanation' in payload && !receiptReason(payload.explanation)) return false
  if ('decision' in payload && payload.decision !== 'Approve' && payload.decision !== 'Reject') return false
  if ('requestNumber' in payload && !numbered(payload.requestNumber)) return false
  if ('decidedAtUtc' in payload && !transferDispatchUtc(payload.decidedAtUtc) || 'requestedAtUtc' in payload && !transferDispatchUtc(payload.requestedAtUtc)
    || 'withdrawnAtUtc' in payload && !transferDispatchUtc(payload.withdrawnAtUtc)) return false
  if ('lineId' in command && !line) return false
  if (command.kind === 'create') return isTransferGuid(command.payload.documentId) && id(command.payload.transferId, work.transfer.transferId)
    && transferDispatchUtc(command.payload.receivedAtUtc) && command.payload.lines.length > 0 && command.payload.lines.length <= 100
    && unique(command.payload.lines.map((row) => row.documentLineId)) && unique(command.payload.lines.map((row) => row.transferLineId))
    && command.payload.lines.every((row) => isTransferGuid(row.documentLineId) && receiptPositiveKg(row.receivedKg) && (row.overageReason === null || receiptReason(row.overageReason))
      && work.transfer.lines.some((source) => id(row.transferLineId, source.lineId) && source.status === 'InTransit'))
  if (command.kind === 'finalize') return decodeReceiptDocument(command.document, work.transfer) !== null && command.document.status === 'Draft'
    && command.payload.expectedDocumentVersion === command.document.version
  if (command.kind === 'receive') return decodeReceiptDocument(command.document, work.transfer) !== null && command.document.status === 'Effective' && line?.status === 'InTransit'
    && command.payload.expectedIncomingDocumentVersion === command.document.version && command.payload.incomingDocument.kind === 'inventory.transfer.incoming'
    && id(command.payload.incomingDocument.id, command.document.documentId) && at(command.payload.receivedAtUtc, command.document.receivedAtUtc)
    && !work.facts.losses.some((row) => id(row.lineId, command.lineId) && row.status === 'Pending')
    && command.document.lines.some((row) => id(row.documentLineId, command.payload.incomingDocument.lineId) && id(row.transferLineId, command.lineId)
      && row.receivedKg === command.payload.receivedKg && row.overageReason === command.payload.overageReason)
    // KLN-D119: a line dispatched with pieces is received with its pieces (≥ 1); a kg-only line never names any.
    && (line.dispatchedPieceCount === undefined ? command.payload.receivedPieceCount === undefined
      : pieceCount(command.payload.receivedPieceCount) && command.payload.receivedPieceCount > 0)
  if (command.kind === 'overage-renewal') {
    const original = work.facts.receipts.find((row) => id(row.lineId, command.lineId))
    const latest = work.facts.overageRenewals.filter((row) => id(row.lineId, command.lineId)).reduce<ReceiptOverageRenewal | null>((prior, row) => prior === null || row.requestNumber > prior.requestNumber ? row : prior, null)
    const pieceLine = work.transfer.lines.some((source) => id(source.lineId, command.lineId) && source.dispatchedPieceCount !== undefined)
    return receiptPositiveKg(command.payload.overageKg) && (latest?.status ?? original?.overageStatus) === 'Rejected'
      && (pieceLine ? pieceCount(command.payload.overagePieceCount) : command.payload.overagePieceCount === undefined)
  }
  if (command.kind === 'overage-decision') return command.payload.requestNumber === 1
    ? work.facts.receipts.some((row) => id(row.lineId, command.lineId) && row.overageStatus === 'Pending')
    : work.facts.overageRenewals.some((row) => id(row.lineId, command.lineId) && row.requestNumber === command.payload.requestNumber && row.status === 'Pending')
  if (command.kind === 'loss-decision' || command.kind === 'loss-withdrawal') return work.facts.losses.some((row) => id(row.lineId, command.lineId) && row.requestNumber === command.payload.requestNumber && row.status === 'Pending')
  return line?.status === 'InTransit'
}
function commandPath(snapshot: ReceiptSnapshot) {
  const command = snapshot.command; const base = `/inventory/transfers/${snapshot.work.transfer.transferId}`
  if (command.kind === 'create') return RECEIPT_DOCUMENT_PATH
  if (command.kind === 'finalize') return `${RECEIPT_DOCUMENT_PATH}/${command.document.documentId}/finalize`
  if (command.kind === 'receive' || command.kind === 'overage-decision' || command.kind === 'overage-renewal') return `${base}/receive-lines/${command.lineId}${command.kind === 'receive' ? '' : `/${command.kind}`}`
  return `${base}/lines/${command.lineId}/${command.kind}`
}
function immutableTransfer(before: TransferDetail, after: TransferDetail) {
  const strip = (value: TransferDetail) => ({ ...value, version: null, status: null, lines: value.lines.map((line) => ({ ...line, status: null })) })
  return same(strip(before), strip(after))
}
function commandProved(snapshot: ReceiptSnapshot, result: ReceiptWork): boolean {
  const { command, actorId, work: prior } = snapshot; const facts = result.facts
  if (!immutableTransfer(prior.transfer, result.transfer)) return false
  if (command.kind === 'receive') return BigInt(result.transfer.version) === BigInt(prior.transfer.version) + 1n
    && facts.receipts.some((row) => id(row.lineId, command.lineId) && row.receivedBy === actorId && at(row.receivedAtUtc, command.payload.receivedAtUtc)
      && row.receivedKg === command.payload.receivedKg && row.overageReason === command.payload.overageReason
      && row.receivedPieceCount === command.payload.receivedPieceCount)
    && result.transfer.lines.some((line) => id(line.lineId, command.lineId) && line.status === 'Accepted')
  if (command.kind === 'loss-request') return result.transfer.version === prior.transfer.version && facts.losses.some((row) => id(row.lineId, command.lineId)
    && row.status === 'Pending' && row.requestedBy === actorId && at(row.requestedAtUtc, command.payload.requestedAtUtc) && row.explanation === command.payload.explanation
    && !prior.facts.losses.some((old) => id(old.lineId, row.lineId) && old.requestNumber === row.requestNumber))
  if (command.kind === 'overage-renewal') return result.transfer.version === prior.transfer.version && facts.overageRenewals.some((row) => id(row.lineId, command.lineId)
    && row.status === 'Pending' && row.requestedBy === actorId && at(row.requestedAtUtc, command.payload.requestedAtUtc) && row.reason === command.payload.reason && row.overageKg === command.payload.overageKg
    && row.overagePieceCount === command.payload.overagePieceCount
    && !prior.facts.overageRenewals.some((old) => id(old.lineId, row.lineId) && old.requestNumber === row.requestNumber))
  if (command.kind === 'overage-decision') {
    const receiver = prior.facts.receipts.find((value) => id(value.lineId, command.lineId))
    const requester = prior.facts.overageRenewals.find((value) => id(value.lineId, command.lineId) && value.requestNumber === command.payload.requestNumber)
    if (receiver?.receivedBy === actorId || requester?.requestedBy === actorId) return false
    const status = command.payload.decision === 'Approve' ? 'Approved' : 'Rejected'
    const row = command.payload.requestNumber === 1 ? facts.receipts.find((value) => id(value.lineId, command.lineId)) : facts.overageRenewals.find((value) => id(value.lineId, command.lineId) && value.requestNumber === command.payload.requestNumber)
    return BigInt(result.transfer.version) >= BigInt(prior.transfer.version) && row !== undefined && ('overageStatus' in row ? row.overageStatus === status && row.overageDecidedBy === actorId
      && row.overageDecisionReason === command.payload.reason && row.overageDecidedAtUtc !== null && at(row.overageDecidedAtUtc, command.payload.decidedAtUtc)
      : row.status === status && row.decidedBy === actorId && row.decisionReason === command.payload.reason && row.decidedAtUtc !== null && at(row.decidedAtUtc, command.payload.decidedAtUtc))
  }
  if (command.kind === 'loss-decision' || command.kind === 'loss-withdrawal') {
    if (command.kind === 'loss-decision' && prior.facts.losses.some((value) => id(value.lineId, command.lineId) && value.requestNumber === command.payload.requestNumber && value.requestedBy === actorId)) return false
    const status = command.kind === 'loss-withdrawal' ? 'Withdrawn' : command.payload.decision === 'Approve' ? 'Approved' : 'Rejected'
    const expected = command.kind === 'loss-decision' && command.payload.decision === 'Approve' ? BigInt(prior.transfer.version) + 1n : BigInt(prior.transfer.version)
    return BigInt(result.transfer.version) === expected && (status !== 'Approved' || result.transfer.lines.some((line) => id(line.lineId, command.lineId) && line.status === 'WrittenOff'))
      && facts.losses.some((row) => id(row.lineId, command.lineId) && row.requestNumber === command.payload.requestNumber
      && row.status === status && row.decidedBy === actorId && row.decisionReason === command.payload.reason && row.decidedAtUtc !== null
      && at(row.decidedAtUtc, command.kind === 'loss-withdrawal' ? command.payload.withdrawnAtUtc : command.payload.decidedAtUtc))
  }
  return false
}
export async function sendReceiptCommand(snapshot: ReceiptSnapshot): Promise<ReceiptOutcome> {
  let response: Response
  try { response = await fetch(`${API_BASE}${commandPath(snapshot)}`, { method: 'POST', cache: 'no-store', headers: { ...inventoryHeaders(snapshot.work.transfer.companyId), 'Content-Type': 'application/json', 'Idempotency-Key': snapshot.key }, body: JSON.stringify(snapshot.command.payload) }) }
  catch { return { type: 'unknown', reason: navigator.onLine ? 'network-error' : 'offline' } }
  if (response.status >= 500) return { type: 'unknown', reason: 'service-unavailable' }
  let raw: unknown = null; try { raw = await response.json() } catch { /* Never free the intent on unproved success. */ }
  if ([400, 401, 403, 404, 409, 415, 422].includes(response.status)) return { type: 'refused', status: response.status, code: obj(raw) && typeof raw.code === 'string' ? raw.code : null }
  const command = snapshot.command; const replayHeader = response.headers.get('Idempotency-Replayed'); const correlation = response.headers.get('Idempotency-Original-Correlation-Id')
  const replayed = replayHeader === 'true'; const unknown = (): ReceiptOutcome => ({ type: 'unknown', reason: 'malformed-response' })
  if (replayed ? !text(correlation, 128) || response.status !== 200 : replayHeader !== null || correlation !== null || response.status !== (command.kind === 'finalize' ? 200 : 201)) return unknown()
  if (command.kind === 'create' || command.kind === 'finalize') {
    if (!obj(raw) || !exact(raw, ['document', 'isReplay', 'originalCorrelationId']) || raw.isReplay !== replayed || !text(raw.originalCorrelationId, 128) || replayed && raw.originalCorrelationId !== correlation) return unknown()
    const document = decodeReceiptDocument(raw.document, snapshot.work.transfer)
    if (document === null) return unknown()
    if (command.kind === 'create') {
      if (!id(document.documentId, command.payload.documentId) || document.status !== 'Draft' || document.createdBy !== snapshot.actorId
        || document.sourceTransferVersion !== command.payload.expectedTransferVersion || !at(document.receivedAtUtc, command.payload.receivedAtUtc)
        || document.lines.length !== command.payload.lines.length || document.lines.some((row, index) => { const line = command.payload.lines[index]; return !id(row.documentLineId, line.documentLineId) || !id(row.transferLineId, line.transferLineId) || row.receivedKg !== line.receivedKg || row.overageReason !== line.overageReason })) return unknown()
    } else {
      const strip = (value: ReceiptDocument) => ({ ...value, version: null, status: null, finalizedBy: null, finalizedAtUtc: null, finalizationReason: null })
      if (!same(strip(document), strip(command.document)) || document.status !== 'Effective' || document.finalizedBy !== snapshot.actorId || document.finalizationReason !== command.payload.reason) return unknown()
    }
    return { type: 'applied', replayed, document, work: null }
  }
  if (!obj(raw) || !exact(raw, ['transfer', 'facts'])) return unknown()
  const work = decodeReceiptWork({ purpose: snapshot.work.purpose, ...raw }, snapshot.work.transfer.companyId, snapshot.work.transfer.transferId, snapshot.work.purpose)
  return work !== null && commandProved(snapshot, work) ? { type: 'applied', replayed, document: null, work } : unknown()
}
export type ReceiptAttempt = { readonly phase: 'idle' } | { readonly phase: 'sending'; readonly snapshot: ReceiptSnapshot; readonly unresolved: boolean }
  | { readonly phase: 'unknown'; readonly snapshot: ReceiptSnapshot; readonly reason: Extract<ReceiptOutcome, { type: 'unknown' }>['reason'] }
  | { readonly phase: 'failed'; readonly snapshot: ReceiptSnapshot; readonly status: number; readonly code: string | null; readonly unresolved: boolean }
  | { readonly phase: 'completed'; readonly snapshot: ReceiptSnapshot; readonly replayed: boolean; readonly result: Extract<ReceiptOutcome, { type: 'applied' }> }
const idle: ReceiptAttempt = Object.freeze({ phase: 'idle' }); const attempts = new Map<string, ReceiptAttempt>(); const listeners = new Map<string, Set<() => void>>()
const selections = new Map<string, { readonly purpose: ReceiptPurpose; readonly transferId: string | null }>()
export const recallReceiptSelection = (binding: string | null) => binding === null ? null : selections.get(binding) ?? null
/** A locator only: route reentry always performs fresh purpose authorization and fresh owner reads. */
export function rememberReceiptSelection(binding: string | null, purpose: ReceiptPurpose, transferId: string | null) {
  if (binding !== null && (transferId === null || isTransferGuid(transferId))) selections.set(binding, { purpose, transferId })
}
export const receiptBinding = (shell: ShellState) => shell.activeContext?.scopeMode === 'single' && shell.activeContext.companyId !== null && !shell.sessionInvalid
  ? JSON.stringify([API_BASE, shell.sessionEpoch, shell.activeContext.userId, shell.activeContext.companyId.toLowerCase(), 'inventory-transfer-receipt']) : null
export const receiptAttempt = (binding: string | null): ReceiptAttempt => binding === null ? idle : attempts.get(binding) ?? idle
export function subscribeReceiptAttempt(binding: string | null, callback: () => void) {
  if (binding === null) return () => {}
  const group = listeners.get(binding) ?? new Set<() => void>(); group.add(callback); listeners.set(binding, group)
  return () => { group.delete(callback); if (!group.size) listeners.delete(binding) }
}
function publish(binding: string, value: ReceiptAttempt) { attempts.set(binding, value); listeners.get(binding)?.forEach((callback) => callback()) }
export const receiptFrozen = (value: ReceiptAttempt) => value.phase === 'sending' || value.phase === 'unknown' || value.phase === 'failed' && value.unresolved
function freeze<T>(value: T): T { if (value !== null && typeof value === 'object') { Object.values(value).forEach((child) => freeze(child)); Object.freeze(value) } return value }
export function beginReceiptAttempt(binding: string | null, actorId: string, work: ReceiptWork, command: ReceiptCommand, key: () => string = () => crypto.randomUUID()): ReceiptSnapshot | null {
  if (binding === null || !text(actorId, 128) || !validReceiptCommand(work, command)) return null
  const prior = receiptAttempt(binding); if (prior.phase === 'sending') return null
  const fingerprint = JSON.stringify([actorId, work, command])
  if (receiptFrozen(prior) && prior.phase !== 'idle') { if (prior.snapshot.fingerprint !== fingerprint) return null; publish(binding, { phase: 'sending', snapshot: prior.snapshot, unresolved: true }); return prior.snapshot }
  const snapshot = freeze({ binding, actorId, work: structuredClone(work), command: structuredClone(command), key: `transfer-receipt-${key()}`, fingerprint })
  publish(binding, { phase: 'sending', snapshot, unresolved: false }); return snapshot
}
export function finishReceiptAttempt(snapshot: ReceiptSnapshot, outcome: ReceiptOutcome) {
  const prior = receiptAttempt(snapshot.binding)
  if (prior.phase === 'idle' || prior.phase === 'completed' || prior.snapshot.key !== snapshot.key || prior.phase === 'failed' && !prior.unresolved) return
  if (outcome.type === 'applied') publish(snapshot.binding, { phase: 'completed', snapshot, replayed: outcome.replayed, result: freeze(outcome) })
  else if (outcome.type === 'unknown') publish(snapshot.binding, { phase: 'unknown', snapshot, reason: outcome.reason })
  else publish(snapshot.binding, { phase: 'failed', snapshot, status: outcome.status, code: outcome.code,
    unresolved: prior.phase === 'unknown' || prior.unresolved || outcome.status === 401 || outcome.status === 403 })
}
export function leaveReceiptAttempt(binding: string | null) { const prior = receiptAttempt(binding); if (binding !== null && prior.phase === 'sending') publish(binding, { phase: 'unknown', snapshot: prior.snapshot, reason: 'network-error' }) }
export function resetReceiptAttemptsForTests() { attempts.clear(); listeners.clear(); selections.clear() }
