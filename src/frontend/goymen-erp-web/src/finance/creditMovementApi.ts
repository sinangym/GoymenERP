import { API_BASE, devHeaders } from './http'
import { isBoundUuid, isInstant, isOpaqueVersion } from './contract'
import { isDateOnly } from './dateOnly'
import { isNonNegativeDecimalText, isPositiveDecimalText } from './decimalText'
import type { ViewOutcome } from '../shell/uiState'
import { decodeBankAccountMasterPage } from './bankAccountContract'

export type CreditMovementKind = 'Drawdown' | 'Repayment' | 'InterestAccrual' | 'OpeningBalance'
export type CreditComponentKind = 'principal' | 'interest' | 'bsmv' | 'kkdf' | 'commission' | 'fee'
export interface CreditMovementInput {
  readonly creditAccountId: string; readonly expectedCreditVersion: string; readonly kind: CreditMovementKind
  readonly sourceDocument: { readonly reference: string; readonly documentDate: string; readonly revision: string }
  readonly transactionDate: string; readonly accountingDate: string
  readonly components: readonly { readonly kind: CreditComponentKind; readonly amount: string; readonly functionalAmount: string | null }[]
  readonly bankEffect: { readonly bankAccountId: string; readonly amount: string; readonly sourceReference: string } | null
  readonly replacesOpeningMovementId: string | null; readonly expectedOpeningMovementVersion: string | null; readonly reason: string
}
export interface CreditMovement {
  readonly id: string; readonly companyId: string; readonly version: string; readonly status: 'Proposed' | 'Approved' | 'Rejected'
  readonly source: CreditMovementInput; readonly currencyId: string; readonly isOwnProposal: boolean; readonly proposedAtUtc: string
  readonly decidedAtUtc: string | null; readonly rejectionReason: string | null; readonly reversedByMovementId: string | null
  readonly openingPeriodVersion: string | null
  readonly moneyProfile: { readonly profileId: string; readonly profileVersion: number; readonly currencyScale: number; readonly smallestCurrencyUnit: string }
}
export type MovementAttempt = { readonly key: string; readonly operation: 'propose'; readonly request: CreditMovementInput }
  | { readonly key: string; readonly operation: 'decide'; readonly id: string; readonly request: { readonly expectedVersion: string; readonly approve: boolean; readonly reason: string | null } }
export type MovementOutcome = { readonly type: 'applied'; readonly value: CreditMovement } | { readonly type: 'unknown' }
  | { readonly type: 'refused'; readonly status: number; readonly codes: readonly string[] }
const record = (value: unknown): value is Record<string, unknown> => value !== null && typeof value === 'object' && !Array.isArray(value)
const text = (value: unknown): value is string => typeof value === 'string' && value.trim() !== ''
const date = (value: unknown): value is string => typeof value === 'string' && isDateOnly(value)
const nullableId = (value: unknown) => value === null || isBoundUuid(value)
const canonical = (value: unknown): unknown => Array.isArray(value) ? value.map(canonical) : record(value)
  ? Object.fromEntries(Object.keys(value).sort().map(key => [key, canonical(value[key])])) : value
export function decodeCreditMovement(value: unknown, company: string, accountId: string): CreditMovement | null {
  if (!record(value) || !isBoundUuid(value.id) || value.companyId !== company || !isOpaqueVersion(value.version)
    || !['Proposed', 'Approved', 'Rejected'].includes(value.status as string) || !isBoundUuid(value.currencyId)
    || typeof value.isOwnProposal !== 'boolean' || !isInstant(value.proposedAtUtc)
    || value.decidedAtUtc !== null && !isInstant(value.decidedAtUtc) || value.rejectionReason !== null && !text(value.rejectionReason)
    || !nullableId(value.reversedByMovementId) || value.openingPeriodVersion !== null && !isOpaqueVersion(value.openingPeriodVersion)) return null
  if (value.status === 'Proposed' ? value.decidedAtUtc !== null || value.rejectionReason !== null
    : value.decidedAtUtc === null || value.status === 'Rejected' && !text(value.rejectionReason)) return null
  const source = value.source
  if (!record(source) || source.creditAccountId !== accountId || !isOpaqueVersion(source.expectedCreditVersion)
    || !['Drawdown', 'Repayment', 'InterestAccrual', 'OpeningBalance'].includes(source.kind as string)
    || !record(source.sourceDocument) || !text(source.sourceDocument.reference) || !date(source.sourceDocument.documentDate)
    || !text(source.sourceDocument.revision) || !date(source.transactionDate) || !date(source.accountingDate) || !text(source.reason)
    || !nullableId(source.replacesOpeningMovementId) || source.expectedOpeningMovementVersion !== null && !isOpaqueVersion(source.expectedOpeningMovementVersion)
    || !Array.isArray(source.components) || source.components.length === 0 || source.components.length > 6
    || source.components.some(x => !record(x) || !['principal', 'interest', 'bsmv', 'kkdf', 'commission', 'fee'].includes(x.kind as string)
      || !isNonNegativeDecimalText(x.amount) || x.functionalAmount !== null && !isNonNegativeDecimalText(x.functionalAmount))
    || new Set(source.components.map(x => x.kind)).size !== source.components.length) return null
  if (source.bankEffect !== null && (!record(source.bankEffect) || !isBoundUuid(source.bankEffect.bankAccountId)
    || !isPositiveDecimalText(source.bankEffect.amount) || !text(source.bankEffect.sourceReference)
    || !['Drawdown', 'Repayment'].includes(source.kind as string))) return null
  const profile = value.moneyProfile
  if (!record(profile) || !isBoundUuid(profile.profileId) || !Number.isSafeInteger(profile.profileVersion) || (profile.profileVersion as number) < 1
    || !Number.isInteger(profile.currencyScale) || (profile.currencyScale as number) < 0 || (profile.currencyScale as number) > 28
    || !isPositiveDecimalText(profile.smallestCurrencyUnit)) return null
  return value as unknown as CreditMovement
}
async function read<T>(company: string, path: string, decoder: (body: unknown) => T | null, signal?: AbortSignal): Promise<ViewOutcome<T>> {
  try { const response = await fetch(API_BASE + path, { headers: { ...devHeaders(), 'X-Company-Id': company }, cache: 'no-store', signal })
    const body: unknown = await response.json().catch(() => null)
    if (response.status !== 200) return { type: 'http', status: response.status, body }
    const data = decoder(body); return data === null ? { type: 'malformed' } : { type: 'ok-single', data }
  } catch { return signal?.aborted ? { type: 'fence-stale' } : navigator.onLine ? { type: 'network-error' } : { type: 'offline' } }
}
export const fetchCreditMovements = (company: string, accountId: string, signal?: AbortSignal) =>
  read(company, `/finance/credit-movements?${new URLSearchParams({ creditAccountId: accountId })}`, body => {
    if (!record(body) || body.companyId !== company || !Array.isArray(body.items)) return null
    const rows = body.items.map(x => decodeCreditMovement(x, company, accountId))
    return rows.some(x => x === null) || new Set(rows.map(x => x?.id)).size !== rows.length ? null : rows as CreditMovement[]
  }, signal)
export const fetchCreditBanks = (company: string, signal?: AbortSignal) => read(company, '/finance/bank-accounts/master', body => {
  const decoded = decodeBankAccountMasterPage(body, company); return decoded.ok ? decoded.value : null
}, signal)
export async function sendCreditMovement(company: string, accountId: string, attempt: MovementAttempt): Promise<MovementOutcome> {
  try { const response = await fetch(`${API_BASE}/finance/credit-movements${attempt.operation === 'decide' ? `/${encodeURIComponent(attempt.id)}/decision` : ''}`, {
    method: 'POST', headers: { ...devHeaders(), 'X-Company-Id': company, 'Content-Type': 'application/json', 'Idempotency-Key': attempt.key }, body: JSON.stringify(attempt.request),
  }); const body: unknown = await response.json().catch(() => null)
    if (response.status === 200) {
      const value = record(body) ? decodeCreditMovement(body.resource, company, accountId) : null
      const expectedStatus = attempt.operation === 'propose' ? 'Proposed' : attempt.request.approve ? 'Approved' : 'Rejected'
      return record(body) && body.outcome === 'Succeeded' && typeof body.isReplay === 'boolean' && text(body.originalCorrelationId)
        && Array.isArray(body.issues) && body.issues.length === 0 && value !== null && value.status === expectedStatus
        && (attempt.operation === 'propose' ? JSON.stringify(canonical(value.source)) === JSON.stringify(canonical(attempt.request)) : value.id === attempt.id)
        ? { type: 'applied', value } : { type: 'unknown' }
    }
    if ([400, 401, 403, 404, 415].includes(response.status)) return { type: 'refused', status: response.status, codes: [] }
    if ([409, 422].includes(response.status) && record(body) && body.resource === null && Array.isArray(body.issues)
      && body.issues.every(x => record(x) && text(x.code) && text(x.field)) && (response.status === 422 ? body.outcome === 'ValidationFailed'
        : body.outcome === 'VersionConflict' || body.outcome === 'IdempotencyConflict'))
      return { type: 'refused', status: response.status, codes: body.issues.map(x => x.code as string) }
    return { type: 'unknown' }
  } catch { return { type: 'unknown' } }
}
