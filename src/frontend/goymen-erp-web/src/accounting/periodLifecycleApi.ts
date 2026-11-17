import type { ViewOutcome } from '../shell/uiState'
import { ACCOUNTING_BASE, devHeaders } from './api'

export interface PeriodClosure {
  readonly id: string; readonly companyId: string; readonly periodId: string; readonly closedPeriodVersion: number
  readonly targetPeriodId: string; readonly carryVoucherId: string; readonly sourceHash: string; readonly lineCount: number
  readonly closedBy: string; readonly closedAtUtc: string; readonly reopeningId: string | null
  readonly reopenedBy: string | null; readonly reopenedAtUtc: string | null
}
export interface PeriodReopening {
  readonly id: string; readonly companyId: string; readonly periodId: string; readonly closureId: string
  readonly closedPeriodVersion: number; readonly proposedBy: string; readonly proposedAtUtc: string; readonly reason: string
  readonly approvedBy: string | null; readonly approvedAtUtc: string | null; readonly version: number
}
export interface PeriodLifecycle {
  readonly companyId: string; readonly periodId: string; readonly periodVersion: number
  readonly closures: readonly PeriodClosure[]; readonly reopenings: readonly PeriodReopening[]
}
export type PeriodReopeningAttempt = { readonly operation: 'propose'; readonly periodId: string; readonly key: string
  readonly request: { readonly expectedVersion: number; readonly proposalId: string; readonly reason: string } }
  | { readonly operation: 'approve'; readonly periodId: string; readonly proposalId: string; readonly key: string
    readonly request: { readonly expectedVersion: number; readonly expectedPeriodVersion: number } }
export type PeriodReopeningOutcome = { readonly type: 'applied'; readonly value: PeriodReopening }
  | { readonly type: 'refused'; readonly status: number; readonly code: string | null } | { readonly type: 'unknown' }
const record = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null && !Array.isArray(v)
const guid = (v: unknown): v is string => typeof v === 'string' && /^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(v)
  && v !== '00000000-0000-0000-0000-000000000000'
const same = (a: string, b: string) => a.toLowerCase() === b.toLowerCase()
const positive = (v: unknown): v is number => Number.isSafeInteger(v) && (v as number) > 0
const instant = (v: unknown): v is string => typeof v === 'string' && /(?:Z|\+00:00)$/.test(v) && Number.isFinite(Date.parse(v))
const scoped = (v: Record<string, unknown>, company: string, period: string) => guid(v.companyId) && same(v.companyId, company)
  && guid(v.periodId) && same(v.periodId, period)
export function decodePeriodReopening(value: unknown, company: string, period: string): PeriodReopening | null {
  if (!record(value) || !scoped(value, company, period) || !guid(value.id) || !guid(value.closureId)
    || !positive(value.closedPeriodVersion) || !guid(value.proposedBy) || !instant(value.proposedAtUtc)
    || typeof value.reason !== 'string' || !value.reason.trim() || value.reason !== value.reason.trim() || value.reason.length > 1000
    || ![1, 2].includes(value.version as number)) return null
  if (value.version === 1 ? value.approvedBy !== null || value.approvedAtUtc !== null
    : !guid(value.approvedBy) || same(value.approvedBy, value.proposedBy) || !instant(value.approvedAtUtc)
      || Date.parse(value.approvedAtUtc) < Date.parse(value.proposedAtUtc)) return null
  return value as unknown as PeriodReopening
}
export function decodePeriodLifecycle(value: unknown, company: string, period: string): PeriodLifecycle | null {
  if (!record(value) || !scoped(value, company, period) || !positive(value.periodVersion)
    || !Array.isArray(value.closures) || !Array.isArray(value.reopenings)) return null
  const closures: PeriodClosure[] = []
  for (const row of value.closures) {
    if (!record(row) || !scoped(row, company, period) || !guid(row.id) || !guid(row.targetPeriodId)
      || same(row.targetPeriodId, period) || !guid(row.carryVoucherId) || !positive(row.closedPeriodVersion)
      || row.closedPeriodVersion > value.periodVersion || !guid(row.closedBy) || !instant(row.closedAtUtc)
      || typeof row.sourceHash !== 'string' || !/^[a-f0-9]{64}$/i.test(row.sourceHash)
      || !Number.isSafeInteger(row.lineCount) || (row.lineCount as number) < 0) return null
    if (row.reopeningId === null ? row.reopenedBy !== null || row.reopenedAtUtc !== null
      : !guid(row.reopeningId) || !guid(row.reopenedBy) || !instant(row.reopenedAtUtc)
        || Date.parse(row.reopenedAtUtc) < Date.parse(row.closedAtUtc)) return null
    closures.push(row as unknown as PeriodClosure)
  }
  const reopenings = value.reopenings.map(row => decodePeriodReopening(row, company, period))
  if (reopenings.some(row => row === null) || new Set(closures.map(row => row.id.toLowerCase())).size !== closures.length
    || new Set(closures.map(row => row.closedPeriodVersion)).size !== closures.length
    || new Set(closures.map(row => row.carryVoucherId.toLowerCase())).size !== closures.length
    || new Set(reopenings.map(row => row?.id.toLowerCase())).size !== reopenings.length
    || new Set(reopenings.map(row => row?.closureId.toLowerCase())).size !== reopenings.length) return null
  for (const row of reopenings as PeriodReopening[]) {
    const closure = closures.find(close => same(close.id, row.closureId))
    if (closure === undefined || closure.closedPeriodVersion !== row.closedPeriodVersion
      || row.version === 1 && closure.reopeningId !== null
      || row.version === 2 && (closure.reopeningId === null || !same(closure.reopeningId, row.id)
        || closure.reopenedBy !== row.approvedBy || closure.reopenedAtUtc !== row.approvedAtUtc)) return null
  }
  if (closures.some(close => close.reopeningId !== null && !reopenings.some(row => row !== null && same(row.id, close.reopeningId!)))) return null
  return { companyId: company, periodId: period, periodVersion: value.periodVersion, closures, reopenings: reopenings as PeriodReopening[] }
}
export async function fetchPeriodLifecycle(company: string, period: string, signal?: AbortSignal): Promise<ViewOutcome<PeriodLifecycle>> {
  try {
    const response = await fetch(`${ACCOUNTING_BASE}/periods/${encodeURIComponent(period)}/lifecycle`, {
      headers: { ...devHeaders(), 'X-Company-Id': company }, signal })
    const body: unknown = await response.json().catch(() => null)
    if (response.status !== 200) return { type: 'http', status: response.status, body }
    const value = decodePeriodLifecycle(body, company, period)
    return value === null ? { type: 'malformed' } : { type: 'ok-single', data: value }
  } catch { return signal?.aborted ? { type: 'fence-stale' } : navigator.onLine ? { type: 'network-error' } : { type: 'offline' } }
}
const refusalCodes = new Set(['idempotency_conflict', 'accounting_version_conflict', 'accounting_concurrent_change',
  'accounting_period_reopening_exists', 'accounting_period_closure_proof_required', 'accounting_period_not_closed',
  'accounting_period_reopening_already_approved', 'accounting_invalid_reference', 'accounting_value_does_not_fit'])
export async function submitPeriodReopening(company: string, attempt: PeriodReopeningAttempt): Promise<PeriodReopeningOutcome> {
  try {
    const suffix = attempt.operation === 'propose' ? '' : `/${encodeURIComponent(attempt.proposalId)}/approval`
    const response = await fetch(`${ACCOUNTING_BASE}/periods/${encodeURIComponent(attempt.periodId)}/reopening-proposals${suffix}`, {
      method: 'POST', headers: { ...devHeaders(), 'X-Company-Id': company, 'Idempotency-Key': attempt.key, 'Content-Type': 'application/json' },
      body: JSON.stringify(attempt.request) })
    const body: unknown = await response.json().catch(() => null)
    if (response.status === 200) {
      const value = decodePeriodReopening(body, company, attempt.periodId)
      const matches = value !== null && (attempt.operation === 'propose'
        ? same(value.id, attempt.request.proposalId) && value.version === 1 && value.reason === attempt.request.reason
          && value.closedPeriodVersion === attempt.request.expectedVersion
        : same(value.id, attempt.proposalId) && value.version === attempt.request.expectedVersion + 1
          && value.closedPeriodVersion === attempt.request.expectedPeriodVersion)
      return matches && value !== null ? { type: 'applied', value } : { type: 'unknown' }
    }
    const code = record(body) && typeof body.code === 'string' ? body.code : null
    return [400, 401, 403, 404, 415].includes(response.status) || [409, 422].includes(response.status) && code !== null && refusalCodes.has(code)
      ? { type: 'refused', status: response.status, code } : { type: 'unknown' }
  } catch { return { type: 'unknown' } }
}
