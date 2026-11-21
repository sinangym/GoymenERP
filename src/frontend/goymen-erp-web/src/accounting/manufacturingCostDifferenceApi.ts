import type { ViewOutcome } from '../shell/uiState'
import { isDateOnly } from '../finance/dateOnly'
import { ACCOUNTING_BASE, devHeaders } from './api'

export interface CostOutput {
  readonly outputId: string; readonly receiptMovementId: string; readonly labelId: string
  readonly netQuantityKg: string; readonly remainingQuantityKg: string; readonly consumedQuantityKg: string
  readonly tlUnitCostPerKg: string; readonly remainingTlValue: string; readonly consumedTlDifference: string
  readonly usdUnitCostPerKg: string | null; readonly originalTlAmount: string
}
export interface CostDifference {
  readonly proposalId: string; readonly companyId: string; readonly manufacturingOrderId: string
  readonly generationId: string; readonly generationVersion: string
  readonly status: 'AwaitingPreparation' | 'Prepared' | 'Posted' | 'NoDifference'
  readonly version: number; readonly signedTlDifference: string; readonly outputs: readonly CostOutput[]
  readonly sourceOccurredAtUtc: string; readonly periodId: string | null
  readonly accountingDate: string | null; readonly transactionDate: string | null
  readonly debitAccountId: string | null; readonly creditAccountId: string | null
  readonly preparedBy: string | null; readonly approvedBy: string | null; readonly journalId: string | null
  readonly reason: string | null
  readonly accountPlan: { readonly packageId: string; readonly packageVersion: number; readonly catalogVersion: number | null
    readonly sourceVersion: string; readonly sha256: string
    readonly debit: { readonly accountId: string; readonly code: string; readonly name: string }
    readonly credit: { readonly accountId: string; readonly code: string; readonly name: string } } | null
}
export interface CostPreparation {
  readonly proposalId: string; readonly expectedVersion: number; readonly periodId: string
  readonly accountingDate: string; readonly transactionDate: string
  readonly debitAccountId: string; readonly creditAccountId: string; readonly reason: string
}
export type CostAttempt = { readonly key: string; readonly id: string; readonly operation: 'prepare'; readonly request: CostPreparation }
  | { readonly key: string; readonly id: string; readonly operation: 'approve'; readonly request: { readonly expectedVersion: number } }
export type CostCommandOutcome = { readonly type: 'applied'; readonly value: CostDifference }
  | { readonly type: 'refused'; readonly status: number; readonly code: string | null }
  | { readonly type: 'unknown' }

const base = `${ACCOUNTING_BASE}/manufacturing-cost-differences`
const guid = (value: unknown): value is string => typeof value === 'string'
  && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value)
  && value !== '00000000-0000-0000-0000-000000000000'
const rec = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null && !Array.isArray(value)
const decimal = (value: unknown): value is string => typeof value === 'string' && /^-?(0|[1-9][0-9]*)(\.[0-9]+)?$/.test(value)
// Exact quantity text is rendered as supplied; the browser never divides or recomputes a physical share.
const quantity = (value: unknown): value is string => typeof value === 'string'
  && (/^(0|[1-9][0-9]*)(\.[0-9]+)?$/.test(value) || /^[1-9][0-9]* \/ [1-9][0-9]*$/.test(value))
const nullableGuid = (value: unknown) => value === null || guid(value)
const nullableDate = (value: unknown) => value === null || typeof value === 'string' && isDateOnly(value)

export function decodeCostDifference(body: unknown, company: string): CostDifference | null {
  if (!rec(body) || !guid(body.proposalId) || !guid(body.companyId) || body.companyId.toLowerCase() !== company.toLowerCase()
    || !guid(body.manufacturingOrderId) || !guid(body.generationId) || typeof body.generationVersion !== 'string'
    || !/^[1-9][0-9]*$/.test(body.generationVersion) || !Number.isSafeInteger(body.version) || (body.version as number) < 1
    || !['AwaitingPreparation', 'Prepared', 'Posted', 'NoDifference'].includes(body.status as string)
    || !decimal(body.signedTlDifference) || typeof body.sourceOccurredAtUtc !== 'string' || !Number.isFinite(Date.parse(body.sourceOccurredAtUtc))
    || !Array.isArray(body.outputs) || body.outputs.length === 0 || body.outputs.some(output => !rec(output)
      || !['outputId', 'receiptMovementId', 'labelId'].every(key => guid(output[key]))
      || !['netQuantityKg', 'remainingQuantityKg', 'consumedQuantityKg'].every(key => quantity(output[key]))
      || !['tlUnitCostPerKg', 'remainingTlValue', 'consumedTlDifference', 'originalTlAmount'].every(key => decimal(output[key]))
      || output.usdUnitCostPerKg !== null && !decimal(output.usdUnitCostPerKg))
    || !['periodId', 'debitAccountId', 'creditAccountId', 'preparedBy', 'approvedBy', 'journalId'].every(key => nullableGuid(body[key]))
    || !nullableDate(body.accountingDate) || !nullableDate(body.transactionDate)
    || body.reason !== null && (typeof body.reason !== 'string' || body.reason.trim() === '')) return null
  if (new Set(body.outputs.map(output => output.outputId.toLowerCase())).size !== body.outputs.length) return null
  const prepared = body.status === 'Prepared' || body.status === 'Posted'
  const plan = body.accountPlan
  if (prepared && (!rec(plan) || !guid(plan.packageId) || !Number.isSafeInteger(plan.packageVersion)
    || (plan.packageVersion as number) < 1 || plan.catalogVersion !== null && (!Number.isSafeInteger(plan.catalogVersion) || (plan.catalogVersion as number) < 1)
    || typeof plan.sourceVersion !== 'string' || plan.sourceVersion.trim() === '' || typeof plan.sha256 !== 'string' || !/^[0-9a-f]{64}$/i.test(plan.sha256)
    || ![plan.debit, plan.credit].every(account => rec(account) && guid(account.accountId)
      && typeof account.code === 'string' && account.code.trim() !== '' && typeof account.name === 'string' && account.name.trim() !== '')
    || (plan.debit as Record<string, unknown>).accountId !== body.debitAccountId || (plan.credit as Record<string, unknown>).accountId !== body.creditAccountId)) return null
  if (!prepared && plan !== null) return null
  if (prepared && (!guid(body.periodId) || !guid(body.debitAccountId) || !guid(body.creditAccountId)
    || body.debitAccountId.toLowerCase() === body.creditAccountId.toLowerCase() || !guid(body.preparedBy)
    || body.accountingDate === null || body.transactionDate === null || body.reason === null)) return null
  if (!prepared && ['periodId', 'debitAccountId', 'creditAccountId', 'preparedBy', 'accountingDate', 'transactionDate', 'reason'].some(key => body[key] !== null)) return null
  if (body.status === 'Posted' ? !guid(body.approvedBy) || !guid(body.journalId)
    || (body.preparedBy as string).toLowerCase() === body.approvedBy.toLowerCase()
    : body.approvedBy !== null || body.journalId !== null) return null
  return body as unknown as CostDifference
}

export async function fetchCostDifferences(company: string, signal?: AbortSignal): Promise<ViewOutcome<readonly CostDifference[]>> {
  try {
    const response = await fetch(base, { headers: { ...devHeaders(), 'X-Company-Id': company }, signal })
    const body: unknown = await response.json().catch(() => null)
    if (response.status !== 200) return { type: 'http', status: response.status, body }
    if (!rec(body) || typeof body.companyId !== 'string' || body.companyId.toLowerCase() !== company.toLowerCase() || !Array.isArray(body.items)) return { type: 'malformed' }
    const items = body.items.map(item => decodeCostDifference(item, company))
    if (items.some(item => item === null) || new Set(items.map(item => item?.proposalId.toLowerCase())).size !== items.length) return { type: 'malformed' }
    return { type: 'ok-single', data: items as CostDifference[] }
  } catch {
    return signal?.aborted ? { type: 'fence-stale' } : navigator.onLine ? { type: 'network-error' } : { type: 'offline' }
  }
}

const definitiveCodes = new Set(['idempotency_conflict', 'accounting_version_conflict', 'accounting_concurrent_change',
  'cost_difference_source_changed', 'cost_difference_source_conflict', 'cost_difference_posting_conflict',
  'cost_difference_not_preparable', 'cost_difference_preparation_invalid', 'cost_difference_open_period_required',
  'cost_difference_account_plan_missing', 'cost_difference_account_not_in_plan', 'cost_difference_amount_unaccepted', 'cost_difference_posting_refused'])

export async function sendCostDifference(company: string, attempt: CostAttempt): Promise<CostCommandOutcome> {
  try {
    const response = await fetch(attempt.operation === 'prepare' ? `${base}/preparation` : `${base}/${encodeURIComponent(attempt.id)}/approval`, {
      method: 'POST', headers: { ...devHeaders(), 'X-Company-Id': company, 'Content-Type': 'application/json', 'Idempotency-Key': attempt.key },
      body: JSON.stringify(attempt.request),
    })
    const body: unknown = await response.json().catch(() => null)
    if (response.status === 200) {
      const value = decodeCostDifference(body, company)
      return value !== null && value.proposalId.toLowerCase() === attempt.id.toLowerCase()
        && value.status === (attempt.operation === 'prepare' ? 'Prepared' : 'Posted')
        ? { type: 'applied', value } : { type: 'unknown' }
    }
    const code = rec(body) && typeof body.code === 'string' ? body.code : null
    if ([400, 401, 403, 404, 415].includes(response.status)
      || [409, 422].includes(response.status) && code !== null && definitiveCodes.has(code)) return { type: 'refused', status: response.status, code }
    return { type: 'unknown' }
  } catch { return { type: 'unknown' } }
}
