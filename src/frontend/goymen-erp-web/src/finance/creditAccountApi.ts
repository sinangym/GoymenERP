import { API_BASE, devHeaders } from './http'
import { isBoundUuid, isInstant, isOpaqueVersion } from './contract'
import { isDateOnly } from './dateOnly'
import { isNonNegativeDecimalText, isPositiveDecimalText } from './decimalText'
import type { ViewOutcome } from '../shell/uiState'
import { COMMAND_RESULT_FIELDS, hasExactKeys, replayHeadersAgree } from './wireShape'
import { decodeCustomerInstrumentPage, type CustomerInstrumentPage } from './customerInstrumentContract'

export type CreditGroup = 'Cash' | 'NonCash' | 'InstrumentAndFactoring' | 'Leasing'
export interface CreditCharge { readonly description: string; readonly amount: string; readonly treatment: 'IncludedInInstallment' | 'CollectedSeparately'; readonly sourceDocumentReference: string }
export interface CreditInstallment { readonly sequence: number; readonly dueDate: string; readonly installment: string; readonly principal: string; readonly interest: string; readonly bsmv: string; readonly kkdf: string; readonly charges?: readonly CreditCharge[]; readonly vat?: string }
export interface CreditSourceDocument { readonly reference: string; readonly documentDate: string; readonly revision: string }
export interface CreditNonCashCommission { readonly amount: string; readonly commissionDate: string; readonly sourceDocument: CreditSourceDocument; readonly period?: string | null }
export interface CreditNonCashReturn { readonly returnedOn: string; readonly sourceDocument: CreditSourceDocument }
export interface CreditProfile {
  readonly lenderPartyId: string; readonly lenderCompanyPartyAccountId: string
  readonly sourceDocument: CreditSourceDocument
  readonly planNature: 'Installments' | 'SourceMovements'
  readonly cash?: { readonly kind: string; readonly maturityDate: string | null; readonly indexCurrencyId?: string | null; readonly exportCommitmentReference?: string | null } | null
  readonly nonCash?: { readonly kind: string; readonly beneficiaryReference: string; readonly isIndefinite: boolean; readonly expiryDate: string | null
    readonly riskAmount?: string | null; readonly commission?: CreditNonCashCommission | null; readonly relatedCommercialDocumentReference?: string | null; readonly returnEvidence?: CreditNonCashReturn | null } | null
  readonly instrumentAndFactoring?: { readonly kind: string; readonly withRecourse: boolean | null; readonly instrumentIds: readonly string[]; readonly receivableDocumentReferences: readonly string[]
    readonly maturityDate?: string | null; readonly deductions?: { readonly interest: string; readonly commission: string; readonly bsmv: string } | null } | null
  readonly leasing?: { readonly assetReference: string; readonly leaseStartDate: string; readonly leaseEndDate: string; readonly contractDate?: string | null; readonly purchaseOptionAmount?: string | null } | null
}
export interface CreditProposal {
  readonly group: CreditGroup; readonly lenderName: string; readonly contractReference: string; readonly currencyId: string
  readonly principalAmount: string; readonly startDate: string; readonly installments: readonly CreditInstallment[]; readonly profile: CreditProfile
}
export interface CreditAccount extends Omit<CreditProposal, 'profile'> {
  readonly id: string; readonly companyId: string; readonly version: string; readonly status: 'Proposed' | 'Approved' | 'Rejected'
  readonly isOwnProposal: boolean; readonly proposedAtUtc: string; readonly decidedAtUtc: string | null; readonly rejectionReason: string | null
  readonly profile: CreditProfile | null
  readonly tolerance: { readonly profileId: string; readonly profileVersion: number; readonly currencyScale: number; readonly smallestCurrencyUnit: string } | null
  readonly planTotals: { readonly vat?: string; readonly installment: string; readonly principal: string; readonly interest: string; readonly bsmv: string; readonly kkdf: string; readonly charges?: { readonly includedInInstallment: string; readonly collectedSeparately: string } } | null
}
export interface CreditCurrency { readonly currencyId: string; readonly code: string; readonly displayLabel: string }
export type CreditAttempt = { readonly operation: 'propose'; readonly key: string; readonly request: CreditProposal }
  | { readonly operation: 'approve' | 'reject'; readonly key: string; readonly id: string; readonly request: { readonly expectedVersion: string; readonly reason?: string } }
export type CreditCommandOutcome = { readonly type: 'applied'; readonly value: CreditAccount; readonly replay: boolean }
  | { readonly type: 'refused'; readonly status: number; readonly issues: readonly { readonly field: string; readonly code: string }[] }
  | { readonly type: 'unknown' }
const base = `${API_BASE}/finance/credit-accounts`
const record = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null && !Array.isArray(value)
const text = (value: unknown): value is string => typeof value === 'string' && value.trim() !== ''
const date = (value: unknown): value is string => typeof value === 'string' && isDateOnly(value)
const nullableDate = (value: unknown) => value === null || date(value)
const inSet = (value: unknown, values: readonly string[]) => typeof value === 'string' && values.includes(value)
const CASH = ['Spot', 'Revolving', 'Installment', 'Overdraft', 'ForeignCurrencyIndexed', 'Export', 'CreditCard']
const NONCASH = ['Guarantee', 'LetterOfCredit', 'Acceptance', 'Aval']
const INSTRUMENT = ['ChequeCollateral', 'NoteCollateral', 'Factoring']
// Exact OpenAPI property sets (required first, then optional) — K3 P2-3.
const PROFILE_REQUIRED = ['lenderCompanyPartyAccountId', 'lenderPartyId', 'planNature', 'sourceDocument']
const PROFILE_OPTIONAL = ['cash', 'instrumentAndFactoring', 'leasing', 'nonCash']
const SOURCE_DOCUMENT_FIELDS = ['documentDate', 'reference', 'revision']
const CASH_FIELDS = ['kind', 'maturityDate']
const CASH_OPTIONAL = ['indexCurrencyId', 'exportCommitmentReference']
const NONCASH_FIELDS = ['beneficiaryReference', 'expiryDate', 'isIndefinite', 'kind']
const NONCASH_OPTIONAL = ['riskAmount', 'commission', 'relatedCommercialDocumentReference', 'returnEvidence']
const INSTRUMENT_FIELDS = ['instrumentIds', 'kind', 'receivableDocumentReferences', 'withRecourse']
const INSTRUMENT_OPTIONAL = ['maturityDate', 'deductions']
const LEASING_FIELDS = ['assetReference', 'leaseEndDate', 'leaseStartDate']
const LEASING_OPTIONAL = ['contractDate', 'purchaseOptionAmount']
const AMOUNT_FIELDS = ['bsmv', 'installment', 'interest', 'kkdf', 'principal']
const INSTALLMENT_REQUIRED = [...AMOUNT_FIELDS, 'dueDate', 'sequence']
const CHARGE_FIELDS = ['amount', 'description', 'sourceDocumentReference', 'treatment']
const CHARGE_TOTAL_FIELDS = ['collectedSeparately', 'includedInInstallment']
const TOLERANCE_FIELDS = ['currencyScale', 'profileId', 'profileVersion', 'smallestCurrencyUnit']
const ACCOUNT_REQUIRED = ['companyId', 'contractReference', 'currencyId', 'decidedAtUtc', 'group', 'id', 'installments', 'isOwnProposal',
  'lenderName', 'planTotals', 'principalAmount', 'proposedAtUtc', 'rejectionReason', 'startDate', 'status', 'version']
const ACCOUNT_OPTIONAL = ['profile', 'tolerance']
const LIST_FIELDS = ['companyId', 'items']
const CURRENCY_FIELDS = ['code', 'currencyId', 'displayLabel']
const ISSUE_FIELDS = ['code', 'field']
const optional = (value: Record<string, unknown>, key: string, valid: (item: unknown) => boolean) => value[key] === undefined || value[key] === null || valid(value[key])
const sourceDocument = (value: unknown) => record(value) && hasExactKeys(value, SOURCE_DOCUMENT_FIELDS)
  && text(value.reference) && date(value.documentDate) && text(value.revision)
const commission = (value: unknown) => record(value) && hasExactKeys(value, ['amount', 'commissionDate', 'sourceDocument'], ['period'])
  && isNonNegativeDecimalText(value.amount) && date(value.commissionDate) && sourceDocument(value.sourceDocument) && optional(value, 'period', text)
const returned = (value: unknown) => record(value) && hasExactKeys(value, ['returnedOn', 'sourceDocument']) && date(value.returnedOn) && sourceDocument(value.sourceDocument)
const deductions = (value: unknown) => record(value) && hasExactKeys(value, ['interest', 'commission', 'bsmv'])
  && ['interest', 'commission', 'bsmv'].every(key => isNonNegativeDecimalText(value[key]))
export function decodeCreditProfile(value: unknown, group: CreditGroup): CreditProfile | null {
  if (!record(value) || !hasExactKeys(value, PROFILE_REQUIRED, PROFILE_OPTIONAL) || !isBoundUuid(value.lenderPartyId)
    || !isBoundUuid(value.lenderCompanyPartyAccountId) || !record(value.sourceDocument)
    || !hasExactKeys(value.sourceDocument, SOURCE_DOCUMENT_FIELDS) || !text(value.sourceDocument.reference) || !date(value.sourceDocument.documentDate)
    || !text(value.sourceDocument.revision) || !inSet(value.planNature, ['Installments', 'SourceMovements'])) return null
  const shapes = [value.cash, value.nonCash, value.instrumentAndFactoring, value.leasing].filter(item => item !== null && item !== undefined)
  if (shapes.length !== 1) return null
  if (group === 'Cash' && (!record(value.cash) || !hasExactKeys(value.cash, CASH_FIELDS, CASH_OPTIONAL) || !inSet(value.cash.kind, CASH) || !nullableDate(value.cash.maturityDate)
    || !optional(value.cash, 'indexCurrencyId', isBoundUuid) || !optional(value.cash, 'exportCommitmentReference', text))) return null
  if (group === 'NonCash' && (!record(value.nonCash) || !hasExactKeys(value.nonCash, NONCASH_FIELDS, NONCASH_OPTIONAL) || !inSet(value.nonCash.kind, NONCASH) || !text(value.nonCash.beneficiaryReference)
    || typeof value.nonCash.isIndefinite !== 'boolean' || !nullableDate(value.nonCash.expiryDate)
    || value.nonCash.isIndefinite !== (value.nonCash.expiryDate === null) || value.planNature !== 'SourceMovements'
    || !optional(value.nonCash, 'riskAmount', isNonNegativeDecimalText) || !optional(value.nonCash, 'commission', commission)
    || !optional(value.nonCash, 'relatedCommercialDocumentReference', text) || !optional(value.nonCash, 'returnEvidence', returned))) return null
  if (group === 'InstrumentAndFactoring') {
    const item = value.instrumentAndFactoring
    if (!record(item) || !hasExactKeys(item, INSTRUMENT_FIELDS, INSTRUMENT_OPTIONAL) || !inSet(item.kind, INSTRUMENT) || item.withRecourse !== null && typeof item.withRecourse !== 'boolean'
      || !Array.isArray(item.instrumentIds) || item.instrumentIds.some(id => !isBoundUuid(id))
      || new Set(item.instrumentIds).size !== item.instrumentIds.length || !Array.isArray(item.receivableDocumentReferences)
      || item.receivableDocumentReferences.some(reference => !text(reference))
      || (item.kind === 'Factoring' ? typeof item.withRecourse !== 'boolean' || item.receivableDocumentReferences.length === 0 : item.instrumentIds.length === 0)
      || !optional(item, 'maturityDate', date) || !optional(item, 'deductions', deductions)) return null
  }
  if (group === 'Leasing' && (!record(value.leasing) || !hasExactKeys(value.leasing, LEASING_FIELDS, LEASING_OPTIONAL) || !text(value.leasing.assetReference) || !date(value.leasing.leaseStartDate)
    || !date(value.leasing.leaseEndDate) || value.leasing.leaseEndDate < value.leasing.leaseStartDate
    || !optional(value.leasing, 'contractDate', date) || !optional(value.leasing, 'purchaseOptionAmount', isNonNegativeDecimalText))) return null
  return value as unknown as CreditProfile
}
function installment(value: unknown, index: number): value is CreditInstallment {
  return record(value) && hasExactKeys(value, INSTALLMENT_REQUIRED, ['charges', 'vat']) && value.sequence === index + 1 && date(value.dueDate)
    && AMOUNT_FIELDS.every(key => isNonNegativeDecimalText(value[key]))
    && (value.vat === undefined || isNonNegativeDecimalText(value.vat))
    && (value.charges === undefined || Array.isArray(value.charges) && value.charges.every(charge => record(charge)
      && hasExactKeys(charge, CHARGE_FIELDS) && text(charge.description) && isNonNegativeDecimalText(charge.amount) && text(charge.sourceDocumentReference)
      && inSet(charge.treatment, ['IncludedInInstallment', 'CollectedSeparately'])))
}
export function decodeCreditAccount(value: unknown, company: string): CreditAccount | null {
  if (!record(value) || !hasExactKeys(value, ACCOUNT_REQUIRED, ACCOUNT_OPTIONAL) || !isBoundUuid(value.id)
    || !isBoundUuid(value.companyId) || value.companyId.toLowerCase() !== company.toLowerCase()
    || !isOpaqueVersion(value.version) || !inSet(value.group, ['Cash', 'NonCash', 'InstrumentAndFactoring', 'Leasing'])
    || !text(value.lenderName) || !text(value.contractReference) || !isBoundUuid(value.currencyId) || !isPositiveDecimalText(value.principalAmount)
    || !date(value.startDate) || !inSet(value.status, ['Proposed', 'Approved', 'Rejected']) || typeof value.isOwnProposal !== 'boolean'
    || !isInstant(value.proposedAtUtc) || value.decidedAtUtc !== null && !isInstant(value.decidedAtUtc)
    || value.rejectionReason !== null && !text(value.rejectionReason) || !Array.isArray(value.installments)
    || !value.installments.every(installment)) return null
  if (value.status === 'Proposed' ? value.decidedAtUtc !== null || value.rejectionReason !== null
    : value.decidedAtUtc === null || value.status === 'Rejected' && !text(value.rejectionReason)) return null
  const profile = value.profile === undefined || value.profile === null ? null : decodeCreditProfile(value.profile, value.group as CreditGroup)
  if (value.profile !== undefined && value.profile !== null && profile === null) return null
  if (profile?.planNature === 'SourceMovements' && (value.installments.length !== 0 || value.planTotals !== null)) return null
  if (value.planTotals !== null && (!record(value.planTotals) || !hasExactKeys(value.planTotals, AMOUNT_FIELDS, ['charges', 'vat'])
    || !AMOUNT_FIELDS.every(key => isNonNegativeDecimalText((value.planTotals as Record<string, unknown>)[key]))
    || value.planTotals.vat !== undefined && !isNonNegativeDecimalText(value.planTotals.vat)
    || value.planTotals.charges !== undefined && (!record(value.planTotals.charges) || !hasExactKeys(value.planTotals.charges, CHARGE_TOTAL_FIELDS)
      || !isNonNegativeDecimalText(value.planTotals.charges.includedInInstallment) || !isNonNegativeDecimalText(value.planTotals.charges.collectedSeparately)))) return null
  if (profile?.planNature === 'Installments' && (value.installments.length === 0 || value.planTotals === null)) return null
  const tolerance = value.tolerance ?? null
  if (tolerance !== null && (!record(tolerance) || !hasExactKeys(tolerance, TOLERANCE_FIELDS) || !isBoundUuid(tolerance.profileId) || !Number.isSafeInteger(tolerance.profileVersion)
    || (tolerance.profileVersion as number) < 1 || !Number.isInteger(tolerance.currencyScale) || (tolerance.currencyScale as number) < 0
    || !isPositiveDecimalText(tolerance.smallestCurrencyUnit))) return null
  return { ...value, profile, tolerance } as unknown as CreditAccount
}
async function read<T>(company: string, path: string, decode: (body: unknown) => T | null, signal?: AbortSignal): Promise<ViewOutcome<T>> {
  try {
    const response = await fetch(base + path, { headers: { ...devHeaders(), 'X-Company-Id': company }, cache: 'no-store', signal })
    const body: unknown = await response.json().catch(() => null)
    if (response.status !== 200) return { type: 'http', status: response.status, body }
    const data = decode(body)
    return data === null ? { type: 'malformed' } : { type: 'ok-single', data }
  } catch { return signal?.aborted ? { type: 'fence-stale' } : navigator.onLine ? { type: 'network-error' } : { type: 'offline' } }
}
export const fetchCreditAccounts = (company: string, signal?: AbortSignal) => read(company, '', body => {
  if (!record(body) || !hasExactKeys(body, LIST_FIELDS) || typeof body.companyId !== 'string'
    || body.companyId.toLowerCase() !== company.toLowerCase() || !Array.isArray(body.items)) return null
  const items = body.items.map(item => decodeCreditAccount(item, company))
  return items.some(item => item === null) || new Set(items.map(item => item?.id)).size !== items.length ? null : items as CreditAccount[]
}, signal)
export const fetchCreditCurrencies = (company: string, signal?: AbortSignal) => read(company, '/currency-candidates', body => {
  if (!record(body) || !hasExactKeys(body, LIST_FIELDS) || typeof body.companyId !== 'string'
    || body.companyId.toLowerCase() !== company.toLowerCase() || !Array.isArray(body.items)
    || body.items.some(item => !record(item) || !hasExactKeys(item, CURRENCY_FIELDS) || !isBoundUuid(item.currencyId) || !text(item.code) || !text(item.displayLabel))
    || new Set(body.items.map(item => item.currencyId)).size !== body.items.length) return null
  return body.items as CreditCurrency[]
}, signal)
export async function sendCreditAccount(company: string, attempt: CreditAttempt): Promise<CreditCommandOutcome> {
  try {
    const response = await fetch(base + (attempt.operation === 'propose' ? '' : `/${encodeURIComponent(attempt.id)}/${attempt.operation}`), {
      method: 'POST', headers: { ...devHeaders(), 'X-Company-Id': company, 'Content-Type': 'application/json', 'Idempotency-Key': attempt.key }, body: JSON.stringify(attempt.request),
    })
    const body: unknown = await response.json().catch(() => null)
    if (response.status === 200) {
      const value = record(body) ? decodeCreditAccount(body.resource, company) : null
      const status = attempt.operation === 'propose' ? 'Proposed' : attempt.operation === 'approve' ? 'Approved' : 'Rejected'
      return record(body) && hasExactKeys(body, COMMAND_RESULT_FIELDS) && body.outcome === 'Succeeded' && typeof body.isReplay === 'boolean'
        && text(body.originalCorrelationId) && replayHeadersAgree(response.headers, body.isReplay, body.originalCorrelationId)
        && Array.isArray(body.issues) && body.issues.length === 0 && value !== null && value.status === status
        && (attempt.operation === 'propose' ? value.contractReference === attempt.request.contractReference && value.currencyId === attempt.request.currencyId
          : value.id.toLowerCase() === attempt.id.toLowerCase())
        ? { type: 'applied', value, replay: body.isReplay } : { type: 'unknown' }
    }
    if ([400, 401, 403, 404, 415].includes(response.status)) return { type: 'refused', status: response.status, issues: [] }
    if ([409, 422].includes(response.status) && record(body) && hasExactKeys(body, COMMAND_RESULT_FIELDS) && body.resource === null && Array.isArray(body.issues)
      && body.issues.every(issue => record(issue) && hasExactKeys(issue, ISSUE_FIELDS) && text(issue.field) && text(issue.code))
      && (response.status === 422 ? body.outcome === 'ValidationFailed' : ['VersionConflict', 'IdempotencyConflict'].includes(body.outcome as string)))
      return { type: 'refused', status: response.status, issues: body.issues as { field: string; code: string }[] }
    return { type: 'unknown' }
  } catch { return { type: 'unknown' } }
}

export async function fetchCreditInstruments(company: string, cursor: string | null, signal?: AbortSignal): Promise<ViewOutcome<CustomerInstrumentPage>> {
  try {
    const response = await fetch(`${API_BASE}/finance/customer-instruments${cursor === null ? '' : `?${new URLSearchParams({ cursor })}`}`, {
      headers: { ...devHeaders(), 'X-Company-Id': company }, cache: 'no-store', signal,
    })
    const body: unknown = await response.json().catch(() => null)
    if (response.status !== 200) return { type: 'http', status: response.status, body }
    const decoded = decodeCustomerInstrumentPage(body, company)
    return decoded.ok ? { type: 'ok-single', data: decoded.value } : { type: 'malformed' }
  } catch { return signal?.aborted ? { type: 'fence-stale' } : navigator.onLine ? { type: 'network-error' } : { type: 'offline' } }
}

// This is transport parsing only. Totals, tolerance, principal and fee rules are checked by the backend.
export const CREDIT_PLAN_HEADER = 'sira;vade;taksit;anapara;faiz;bsmv;kkdf'
export function parseCreditPlanFile(contents: string): readonly CreditInstallment[] | null {
  const lines = contents.replace(/^\uFEFF/, '').replace(/\r\n/g, '\n').replace(/\n+$/, '').split('\n')
  const hasVat = lines[0] === CREDIT_PLAN_HEADER + ';kdv'
  if ((!hasVat && lines[0] !== CREDIT_PLAN_HEADER) || lines.length < 2 || lines.length > 601) return null
  const result = lines.slice(1).map(line => { const cells = line.split(';'); return cells.length === (hasVat ? 8 : 7) && /^[1-9][0-9]*$/.test(cells[0]!)
    ? { sequence: Number(cells[0]), dueDate: cells[1]!, installment: cells[2]!, principal: cells[3]!, interest: cells[4]!, bsmv: cells[5]!, kkdf: cells[6]!, ...(hasVat ? { vat: cells[7]! } : {}) } : null })
  return result.every((line, index) => line !== null && installment(line, index)) ? result as CreditInstallment[] : null
}
