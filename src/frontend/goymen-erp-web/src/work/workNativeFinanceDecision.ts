import { decodeCreditLimit, type CreditLimit } from '../finance/creditLimitApi'
import { decodeOpeningBalance, type OpeningBalanceView } from '../finance/OpeningBalanceContract'
import { hasExactKeys } from '../finance/wireShape'
import { isBoundUuid } from '../finance/contract'
import { isNonNegativeDecimalText } from '../finance/decimalText'
import { transferDispatchUtc } from '../inventory/transferDispatchApi'

export const NATIVE_FINANCE_WORK_CAPABILITY = {
  'finance.bank-credit-limit.v1': 'finance.bank-credit-limit.work-decision',
  'finance.opening-balance.v1': 'finance.opening-balance.work-decision',
  'finance.opening-balance-reversal.v1': 'finance.opening-balance-reversal.work-decision',
} as const
export type NativeFinanceDecisionKind = keyof typeof NATIVE_FINANCE_WORK_CAPABILITY
export const isNativeFinanceDecisionKind = (kind: string): kind is NativeFinanceDecisionKind => Object.hasOwn(NATIVE_FINANCE_WORK_CAPABILITY, kind)
export interface WorkBankCreditLimitPreview { readonly limit: CreditLimit; readonly proposedBy: string; readonly sourceFactsDigest: string }
export interface WorkOpeningBalancePreview { readonly openingBalance: OpeningBalanceView }
const record = (v: unknown): v is Record<string, unknown> => v !== null && typeof v === 'object' && !Array.isArray(v)
const same = (v: unknown, id: string) => typeof v === 'string' && v.toLowerCase() === id.toLowerCase()
const guid = (v: unknown): v is string => typeof v === 'string' && v.length === 36 && isBoundUuid(v)
const text = (v: unknown, max: number): v is string => typeof v === 'string' && v.trim().length > 0 && v.length <= max
const canonical = (v: unknown, max: number) => text(v,max) && v === v.trim()
const utc = (v: unknown): v is string => transferDispatchUtc(v) && v === v.trim()
const amount = (v: unknown): v is string => typeof v === 'string' && v === v.trim() && isNonNegativeDecimalText(v)
const digest = (v: unknown) => typeof v === 'string' && v.length === 64 && /^[0-9a-f]+$/.test(v)
// The server owner matcher remains the authority for the native formatter's rule amount; raw money is never recomputed.
export function validNativeFinancePreview(value: Record<string, unknown>, company: string, kind: NativeFinanceDecisionKind,
  id: string, dateValidator: (value: string) => boolean): boolean {
  if (value.approvalReasonRequired !== false || value.rejectionSupported !== true || !Array.isArray(value.rules)
    || value.rules.length !== 1 || !record(value.rules[0]) || !Array.isArray(value.references)) return false
  const rule = value.rules[0], refs = value.references
  if (rule.threshold !== null || rule.deviation !== null || rule.unit !== null || !amount(rule.actual)) return false
  let targets: readonly (readonly [string, string])[]
  if (kind === 'finance.bank-credit-limit.v1') {
    const human = value.bankCreditLimit
    if (!record(human) || !hasExactKeys(human,['limit','proposedBy','sourceFactsDigest']) || !text(human.proposedBy,128)
      || !digest(human.sourceFactsDigest) || !record(human.limit)) return false
    const v = human.limit
    if (!guid(v.companyId) || !same(v.companyId,company) || !guid(v.id) || !same(v.id,id) || !guid(v.currencyId)
      || !guid(v.lenderPartyId) || !guid(v.lenderCompanyPartyAccountId)
      || v.group !== 'Cash' && v.group !== 'NonCash' && v.group !== 'InstrumentAndFactoring' && v.group !== 'Leasing') return false
    const limit = decodeCreditLimit(v, { companyId: v.companyId, currencyId: v.currencyId, group: v.group,
      lenderPartyId: v.lenderPartyId, lenderCompanyPartyAccountId: v.lenderCompanyPartyAccountId }, dateValidator)
    if (limit === null || limit.isOwnProposal || !utc(limit.proposedAtUtc) || limit.decidedAtUtc !== null && !utc(limit.decidedAtUtc)
      || limit.version !== (limit.status === 'Proposed' ? '1' : '2') || value.sourceVersion !== limit.version
      || value.pending !== (limit.status === 'Proposed') || !guid(limit.source.creditAccountId)
      || !canonical(limit.source.expectedCreditVersion,64) || !canonical(limit.source.reason,1000)
      || limit.source.sourceDocument.documentDate === '0001-01-01'
      || !canonical(limit.source.sourceDocument.reference,200) || !canonical(limit.source.sourceDocument.revision,100)
      || !amount(limit.source.amount) || limit.source.amount.length > 128
      || limit.source.expectedCurrentLimitId !== null && !guid(limit.source.expectedCurrentLimitId)
      || limit.supersededById !== null && !guid(limit.supersededById)
      || limit.source.expectedCurrentLimitVersion !== null && !canonical(limit.source.expectedCurrentLimitVersion,64)
      || limit.rejectionReason !== null && !canonical(limit.rejectionReason,1000)) return false
    if (rule.ruleCode !== 'finance.bank-credit-limit-approval-required') return false
    targets = [['credit-account',limit.source.creditAccountId],['lender-party',limit.lenderPartyId],
      ['company-account',limit.lenderCompanyPartyAccountId],['currency',limit.currencyId]]
  } else {
    const human = value.openingBalance, reversal = kind === 'finance.opening-balance-reversal.v1'
    if (!record(human) || !hasExactKeys(human,['openingBalance'])) return false
    const decoded = decodeOpeningBalance(human.openingBalance,company,dateValidator,reversal ? 'retained-reversal-decision' : 'opening-decision')
    if (!decoded.ok) return false
    const v = decoded.value, target = reversal ? v.reversal : v
    if (target === null || !amount(v.amount) || !guid(v.companyId) || !guid(v.id) || !guid(v.targetId) || !guid(v.currencyId)
      || v.partyId !== null && !guid(v.partyId) || !same(target.id,id) || !utc(v.proposedAtUtc)
      || v.decidedAtUtc !== null && !utc(v.decidedAtUtc) || !utc(target.proposedAtUtc)
      || target.decidedAtUtc !== null && !utc(target.decidedAtUtc) || target.isOwnProposal
      || v.version !== (v.status === 'Proposed' ? '1' : v.status === 'Reversed' ? '3' : '2')
      || target.version !== (target.status === 'Proposed' ? '1' : target.status === 'Reversed' ? '3' : '2')
      || value.sourceVersion !== target.version || value.pending !== (target.status === 'Proposed')
      || v.openingPeriodVersion.length > 64 || v.rejectionReason !== null && !canonical(v.rejectionReason,500)
      || reversal && (v.reversal!.reason.length > 500 || v.reversal!.openingPeriodVersion.length > 64
        || v.reversal!.rejectionReason !== null && !canonical(v.reversal!.rejectionReason,500))) return false
    if (rule.ruleCode !== (reversal ? 'finance.opening-balance-reversal-approval-required' : 'finance.opening-balance-approval-required')) return false
    targets = [[v.targetKind === 'CashAccount' ? 'cash-account' : v.targetKind === 'BankAccount' ? 'bank-account' : 'company-account',v.targetId],['currency',v.currencyId],
      ...(v.partyId === null ? [] : [['party',v.partyId] as const]), ...(reversal ? [['opening-balance',v.id] as const] : [])]
  }
  return refs.length === targets.length && targets.every(([role,target]) => refs.filter(ref => record(ref)
    && ref.role === role && same(ref.recordId,target) && ref.link === null).length === 1)
}
