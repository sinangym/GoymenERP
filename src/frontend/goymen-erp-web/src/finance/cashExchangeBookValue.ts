import { decodeCashTransferView, type CashTransferView } from './cashTransferContract'
import { isBoundUuid, isInstant } from './contract'
import { API_BASE, devHeaders } from './http'
import type { ViewOutcome } from '../shell/uiState'

export interface BookValueEvidence { readonly documentId: string; readonly versionNo: number; readonly sha256: string }
export interface CashExchangeBookValue {
  readonly id: string; readonly companyId: string; readonly transferId: string; readonly transferVersion: '1'
  readonly transferSourceHash: string; readonly sourceBookValueTl: string; readonly evidence: BookValueEvidence
  readonly reason: string; readonly preparedBy: string; readonly preparedAtUtc: string
  readonly status: 'Pending' | 'Approved' | 'Rejected'; readonly version: '1' | '2'
  readonly decidedBy: string | null; readonly decidedAtUtc: string | null; readonly decisionReason: string | null
}
export interface BookValueWorkspace { readonly companyId: string; readonly transfer: CashTransferView; readonly proposals: readonly CashExchangeBookValue[] }
export interface BookValueCandidates { readonly companyId: string; readonly items: readonly CashTransferView[]; readonly nextCursor: string | null }
export interface ProposeBookValue { readonly transferId: string; readonly expectedTransferVersion: string; readonly sourceBookValueTl: string; readonly evidence: BookValueEvidence; readonly reason: string }
export interface DecideBookValue { readonly proposalId: string; readonly expectedVersion: '1'; readonly approve: boolean; readonly reason: string | null }
export interface BookValueCommandResult {
  readonly outcome: 'Succeeded' | 'VersionConflict' | 'IdempotencyConflict' | 'ValidationFailed'
  readonly resource: CashExchangeBookValue | null; readonly isReplay: boolean; readonly originalCorrelationId: string; readonly code: string | null
}
export interface BookValueAttempt { readonly key: string; readonly body: ProposeBookValue | DecideBookValue; readonly decision: boolean }
export type BookValueCommandOutcome = { readonly kind: 'completed'; readonly result: BookValueCommandResult }
  | { readonly kind: 'refused'; readonly status: number; readonly code: string | null }
  | { readonly kind: 'unknown' }

const PATH = '/finance/cash-exchanges/book-values'
const HASH = /^[a-fA-F0-9]{64}$/
const object = (x: unknown): x is Record<string, unknown> => x !== null && typeof x === 'object' && !Array.isArray(x)
const exact = (x: Record<string, unknown>, keys: readonly string[]) => Object.keys(x).length === keys.length && keys.every(k => Object.hasOwn(x, k))
const text = (x: unknown, max: number): x is string => typeof x === 'string' && x.length > 0 && x.length <= max && x === x.trim() && !/[\u0000-\u001f\u007f]/.test(x)
const positive = (x: unknown): x is string => text(x, 128) && /^(?:0|[1-9]\d*)(?:\.\d+)?$/.test(x) && /[1-9]/.test(x)

export function decodeBookValue(value: unknown, companyId: string, transferId?: string): CashExchangeBookValue | null {
  if (!object(value) || !exact(value, ['id', 'companyId', 'transferId', 'transferVersion', 'transferSourceHash', 'sourceBookValueTl', 'evidence', 'reason',
    'preparedBy', 'preparedAtUtc', 'status', 'version', 'decidedBy', 'decidedAtUtc', 'decisionReason'])
    || !isBoundUuid(value.id) || value.companyId !== companyId || !isBoundUuid(value.transferId) || transferId !== undefined && transferId !== value.transferId
    || value.transferVersion !== '1' || typeof value.transferSourceHash !== 'string' || !HASH.test(value.transferSourceHash)
    || !positive(value.sourceBookValueTl) || !text(value.reason, 1000) || !text(value.preparedBy, 128) || !isInstant(value.preparedAtUtc)) return null
  const e = value.evidence
  if (!object(e) || !exact(e, ['documentId', 'versionNo', 'sha256']) || !isBoundUuid(e.documentId)
    || !Number.isSafeInteger(e.versionNo) || (e.versionNo as number) < 1 || typeof e.sha256 !== 'string' || !HASH.test(e.sha256)) return null
  if (value.status === 'Pending') {
    if (value.version !== '1' || value.decidedBy !== null || value.decidedAtUtc !== null || value.decisionReason !== null) return null
  } else if (value.status === 'Approved' || value.status === 'Rejected') {
    if (value.version !== '2' || !text(value.decidedBy, 128) || value.decidedBy === value.preparedBy || !isInstant(value.decidedAtUtc)
      || Date.parse(value.decidedAtUtc) < Date.parse(value.preparedAtUtc)
      || (value.status === 'Approved' ? value.decisionReason !== null : !text(value.decisionReason, 1000))) return null
  } else return null
  return value as unknown as CashExchangeBookValue
}

export function decodeBookValueWorkspace(value: unknown, companyId: string, transferId: string): BookValueWorkspace | null {
  if (!object(value) || !exact(value, ['companyId', 'transfer', 'proposals']) || value.companyId !== companyId || !Array.isArray(value.proposals)) return null
  const source = decodeCashTransferView(value.transfer, companyId)
  if (!source.ok || source.value.id !== transferId || source.value.preparation.kind !== 'CurrencyExchange') return null
  const proposals = value.proposals.map(x => decodeBookValue(x, companyId, transferId))
  if (proposals.some(x => x === null) || new Set(proposals.map(x => x!.id)).size !== proposals.length
    || proposals.filter(x => x!.status === 'Approved').length > 1 || proposals.filter(x => x!.status === 'Pending').length > 1) return null
  return { companyId, transfer: source.value, proposals: proposals as CashExchangeBookValue[] }
}

export function decodeBookValueCandidates(value: unknown, companyId: string): BookValueCandidates | null {
  if (!object(value) || !exact(value, ['companyId', 'items', 'nextCursor']) || value.companyId !== companyId
    || !Array.isArray(value.items) || value.items.length > 25 || value.nextCursor !== null && !isBoundUuid(value.nextCursor)) return null
  const rows: CashTransferView[] = []
  for (const item of value.items) { const row = decodeCashTransferView(item, companyId); if (!row.ok || row.value.preparation.kind !== 'CurrencyExchange') return null; rows.push(row.value) }
  if (new Set(rows.map(x => x.id)).size !== rows.length || value.nextCursor !== null && rows.at(-1)?.id !== value.nextCursor) return null
  return { companyId, items: rows, nextCursor: value.nextCursor as string | null }
}

async function read<T>(path: string, companyId: string, decode: (x: unknown) => T | null, signal?: AbortSignal): Promise<ViewOutcome<T>> {
  try {
    const response = await fetch(`${API_BASE}${path}`, { headers: { ...devHeaders(), 'X-Company-Id': companyId, Accept: 'application/json' }, cache: 'no-store', signal })
    if (response.status !== 200) return { type: 'http', status: response.status }
    let value: unknown
    try { value = await response.json() } catch { return signal?.aborted ? { type: 'fence-stale' } : { type: 'malformed' } }
    if (signal?.aborted) return { type: 'fence-stale' }
    const decoded = decode(value)
    return decoded === null ? { type: 'malformed' } : { type: 'ok-single', data: decoded }
  } catch { return signal?.aborted ? { type: 'fence-stale' } : navigator.onLine ? { type: 'network-error' } : { type: 'offline' } }
}
export const fetchBookValueCandidates = (company: string, after: string | null, signal?: AbortSignal) =>
  read(`${PATH}${after === null ? '' : `?afterId=${encodeURIComponent(after)}`}`, company, x => decodeBookValueCandidates(x, company), signal)
export const fetchBookValueWorkspace = (company: string, id: string, signal?: AbortSignal) =>
  read(`${PATH}/${encodeURIComponent(id)}`, company, x => decodeBookValueWorkspace(x, company, id), signal)

export async function sendBookValueAttempt(companyId: string, actorId: string, attempt: BookValueAttempt, signal?: AbortSignal): Promise<BookValueCommandOutcome> {
  try {
    const response = await fetch(`${API_BASE}${PATH}${attempt.decision ? '/decision' : ''}`, { method: 'POST', headers: {
      ...devHeaders(), 'X-Company-Id': companyId, 'Content-Type': 'application/json', Accept: 'application/json', 'Idempotency-Key': attempt.key },
    body: JSON.stringify(attempt.body), signal })
    if ([400, 401, 403, 415].includes(response.status)) return { kind: 'refused', status: response.status, code: null }
    if (![200, 409, 422].includes(response.status)) return { kind: 'unknown' }
    const value: unknown = await response.json()
    if (!object(value) || !exact(value, ['outcome', 'resource', 'isReplay', 'originalCorrelationId', 'code'])
      || typeof value.isReplay !== 'boolean' || !text(value.originalCorrelationId, 128) || !(value.code === null || text(value.code, 128))) return { kind: 'unknown' }
    if (value.isReplay !== (response.headers.get('Idempotency-Replayed') === 'true')) return { kind: 'unknown' }
    if (response.status === 200 && value.outcome === 'Succeeded' && value.code === null) {
      const row = decodeBookValue(value.resource, companyId, 'transferId' in attempt.body ? attempt.body.transferId : undefined)
      if (row === null || ('proposalId' in attempt.body ? row.id !== attempt.body.proposalId || row.decidedBy !== actorId
        || row.status !== (attempt.body.approve ? 'Approved' : 'Rejected') || row.decisionReason !== attempt.body.reason
        : row.version !== '1' || row.status !== 'Pending' || row.preparedBy !== actorId || row.reason !== attempt.body.reason
          || row.sourceBookValueTl !== attempt.body.sourceBookValueTl || row.evidence.documentId !== attempt.body.evidence.documentId
          || row.evidence.versionNo !== attempt.body.evidence.versionNo || row.evidence.sha256 !== attempt.body.evidence.sha256)) return { kind: 'unknown' }
      return { kind: 'completed', result: { outcome: 'Succeeded', resource: row, isReplay: value.isReplay, originalCorrelationId: value.originalCorrelationId, code: null } }
    }
    if (value.resource !== null || value.isReplay || !(response.status === 409 && ['VersionConflict', 'IdempotencyConflict'].includes(value.outcome as string)
      || response.status === 422 && value.outcome === 'ValidationFailed')) return { kind: 'unknown' }
    return { kind: 'refused', status: response.status, code: value.code as string | null }
  } catch { return { kind: 'unknown' } }
}
