import { API_BASE, devHeaders } from './http'
import { COMMAND_RESULT_FIELDS, hasExactKeys, replayHeadersAgree } from './wireShape'
import { isBoundUuid, isInstant, isOpaqueVersion } from './contract'
import { isDateOnly } from './dateOnly'
import { isNonNegativeDecimalText } from './decimalText'
import type { ViewOutcome } from '../shell/uiState'
import type { CreditAccount, CreditGroup } from './creditAccountApi'

export interface CreditLimitInput { readonly creditAccountId: string; readonly expectedCreditVersion: string; readonly expectedCurrentLimitId: string | null; readonly expectedCurrentLimitVersion: string | null; readonly amount: string; readonly sourceDocument: { readonly reference: string; readonly documentDate: string; readonly revision: string }; readonly reason: string }
export interface CreditLimit { readonly id: string; readonly companyId: string; readonly lenderPartyId: string; readonly lenderCompanyPartyAccountId: string; readonly group: CreditGroup; readonly currencyId: string; readonly version: string; readonly status: 'Proposed' | 'Approved' | 'Rejected'; readonly source: CreditLimitInput; readonly isOwnProposal: boolean; readonly proposedAtUtc: string; readonly decidedAtUtc: string | null; readonly rejectionReason: string | null; readonly supersededById: string | null }
export interface CreditLimitWorkspace { readonly companyId: string; readonly creditAccountId: string; readonly items: readonly CreditLimit[]; readonly assessment: { readonly recordedPrincipalNet: string; readonly exceedsLimit: boolean | null; readonly proposedMovements: readonly { readonly movementId: string; readonly sourceReference: string; readonly projectedRecordedPrincipalNet: string; readonly exceedsLimit: boolean | null }[] } }
export type LimitAttempt = { readonly key: string; readonly operation: 'propose'; readonly request: CreditLimitInput } | { readonly key: string; readonly operation: 'decide'; readonly id: string; readonly request: { readonly expectedVersion: string; readonly approve: boolean; readonly reason: string | null } }
export type LimitOutcome = { readonly type: 'applied'; readonly value: CreditLimit } | { readonly type: 'unknown' } | { readonly type: 'refused'; readonly status: number }
const record = (value: unknown): value is Record<string, unknown> => value !== null && typeof value === 'object' && !Array.isArray(value)
const text = (value: unknown): value is string => typeof value === 'string' && value.trim() !== ''
const nullableId = (value: unknown) => value === null || isBoundUuid(value)
const signed = (value: unknown) => typeof value === 'string' && isNonNegativeDecimalText(value.startsWith('-') ? value.slice(1) : value)
const nullableBool = (value: unknown) => value === null || typeof value === 'boolean'
const canonical = (value: unknown): unknown => Array.isArray(value) ? value.map(canonical) : record(value) ? Object.fromEntries(Object.keys(value).sort().map(k => [k, canonical(value[k])])) : value
// Exact OpenAPI property sets — K3 P2-3.
const LIMIT_FIELDS = ['companyId', 'currencyId', 'decidedAtUtc', 'group', 'id', 'isOwnProposal', 'lenderCompanyPartyAccountId',
  'lenderPartyId', 'proposedAtUtc', 'rejectionReason', 'source', 'status', 'supersededById', 'version']
const SOURCE_FIELDS = ['amount', 'creditAccountId', 'expectedCreditVersion', 'expectedCurrentLimitId', 'expectedCurrentLimitVersion',
  'reason', 'sourceDocument']
const DOCUMENT_FIELDS = ['documentDate', 'reference', 'revision']
const WORKSPACE_FIELDS = ['assessment', 'companyId', 'creditAccountId', 'items']
const ASSESSMENT_FIELDS = ['exceedsLimit', 'proposedMovements', 'recordedPrincipalNet']
const MOVEMENT_ASSESSMENT_FIELDS = ['exceedsLimit', 'movementId', 'projectedRecordedPrincipalNet', 'sourceReference']
const ISSUE_FIELDS = ['code', 'field']
export function decodeCreditLimit(value: unknown, account: CreditAccount): CreditLimit | null {
  if (!record(value) || !hasExactKeys(value, LIMIT_FIELDS) || !isBoundUuid(value.id) || value.companyId !== account.companyId || value.currencyId !== account.currencyId || value.group !== account.group
    || value.lenderPartyId !== account.profile?.lenderPartyId || value.lenderCompanyPartyAccountId !== account.profile?.lenderCompanyPartyAccountId
    || !isOpaqueVersion(value.version) || !['Proposed', 'Approved', 'Rejected'].includes(value.status as string) || typeof value.isOwnProposal !== 'boolean'
    || !isInstant(value.proposedAtUtc) || value.decidedAtUtc !== null && !isInstant(value.decidedAtUtc) || !nullableId(value.supersededById)
    || value.rejectionReason !== null && !text(value.rejectionReason)) return null
  if (value.status === 'Proposed' ? value.decidedAtUtc !== null || value.rejectionReason !== null : value.decidedAtUtc === null
    || value.status === 'Rejected' && !text(value.rejectionReason) || value.status === 'Approved' && value.rejectionReason !== null) return null
  const s = value.source
  if (!record(s) || !hasExactKeys(s, SOURCE_FIELDS) || !isBoundUuid(s.creditAccountId) || !isOpaqueVersion(s.expectedCreditVersion) || !nullableId(s.expectedCurrentLimitId)
    || (s.expectedCurrentLimitId === null ? s.expectedCurrentLimitVersion !== null : !isOpaqueVersion(s.expectedCurrentLimitVersion))
    || !isNonNegativeDecimalText(s.amount) || !text(s.reason) || !record(s.sourceDocument) || !hasExactKeys(s.sourceDocument, DOCUMENT_FIELDS) || !text(s.sourceDocument.reference)
    || !text(s.sourceDocument.revision) || typeof s.sourceDocument.documentDate !== 'string' || !isDateOnly(s.sourceDocument.documentDate)) return null
  return value as unknown as CreditLimit
}
export function decodeCreditLimitWorkspace(body: unknown, account: CreditAccount): CreditLimitWorkspace | null {
  if (!record(body) || !hasExactKeys(body, WORKSPACE_FIELDS) || body.companyId !== account.companyId || body.creditAccountId !== account.id || !Array.isArray(body.items)) return null
  const rows = body.items.map(x => decodeCreditLimit(x, account)); const a = body.assessment
  if (rows.some(x => x === null) || new Set(rows.map(x => x?.id)).size !== rows.length || !record(a) || !hasExactKeys(a, ASSESSMENT_FIELDS) || !signed(a.recordedPrincipalNet) || !nullableBool(a.exceedsLimit)
    || !Array.isArray(a.proposedMovements) || a.proposedMovements.some(x => !record(x) || !hasExactKeys(x, MOVEMENT_ASSESSMENT_FIELDS) || !isBoundUuid(x.movementId) || !text(x.sourceReference) || !signed(x.projectedRecordedPrincipalNet) || !nullableBool(x.exceedsLimit))
    || new Set(a.proposedMovements.map(x => x.movementId)).size !== a.proposedMovements.length) return null
  const current = rows.filter(x => x?.status === 'Approved' && x.supersededById === null)
  if (current.length > 1 || (current.length === 0) !== (a.exceedsLimit === null) || a.proposedMovements.some(x => (current.length === 0) !== (x.exceedsLimit === null))) return null
  return body as unknown as CreditLimitWorkspace
}
export async function fetchCreditLimits(account: CreditAccount, signal?: AbortSignal): Promise<ViewOutcome<CreditLimitWorkspace>> {
  try { const response = await fetch(`${API_BASE}/finance/credit-bank-limits?${new URLSearchParams({ creditAccountId: account.id })}`, { headers: { ...devHeaders(), 'X-Company-Id': account.companyId }, cache: 'no-store', signal }); const body: unknown = await response.json().catch(() => null)
    if (response.status !== 200) return { type: 'http', status: response.status, body }; const value = decodeCreditLimitWorkspace(body, account)
    return value === null ? { type: 'malformed' } : { type: 'ok-single', data: value }
  } catch { return signal?.aborted ? { type: 'fence-stale' } : navigator.onLine ? { type: 'network-error' } : { type: 'offline' } }
}
export async function sendCreditLimit(account: CreditAccount, attempt: LimitAttempt): Promise<LimitOutcome> {
  try { const response = await fetch(`${API_BASE}/finance/credit-bank-limits${attempt.operation === 'decide' ? `/${encodeURIComponent(attempt.id)}/decision` : ''}`, { method: 'POST', headers: { ...devHeaders(), 'X-Company-Id': account.companyId, 'Content-Type': 'application/json', 'Idempotency-Key': attempt.key }, body: JSON.stringify(attempt.request) }); const body: unknown = await response.json().catch(() => null)
    if (response.status === 200) { const value = record(body) ? decodeCreditLimit(body.resource, account) : null
      return record(body) && hasExactKeys(body, COMMAND_RESULT_FIELDS) && body.outcome === 'Succeeded' && typeof body.isReplay === 'boolean' && text(body.originalCorrelationId)
        && replayHeadersAgree(response.headers, body.isReplay, body.originalCorrelationId) && Array.isArray(body.issues) && body.issues.length === 0
        && value !== null && value.status === (attempt.operation === 'propose' ? 'Proposed' : attempt.request.approve ? 'Approved' : 'Rejected')
        && (attempt.operation === 'propose' ? JSON.stringify(canonical(value.source)) === JSON.stringify(canonical(attempt.request)) : value.id === attempt.id) ? { type: 'applied', value } : { type: 'unknown' }
    }
    if ([400, 401, 403, 404, 415].includes(response.status)) return { type: 'refused', status: response.status }
    if ([409, 422].includes(response.status) && record(body) && hasExactKeys(body, COMMAND_RESULT_FIELDS) && body.resource === null && Array.isArray(body.issues)
      && body.issues.every(x => record(x) && hasExactKeys(x, ISSUE_FIELDS) && text(x.code) && text(x.field))
      && (response.status === 422 ? body.outcome === 'ValidationFailed' : ['VersionConflict', 'IdempotencyConflict'].includes(body.outcome as string))) return { type: 'refused', status: response.status }
    return { type: 'unknown' }
  } catch { return { type: 'unknown' } }
}
