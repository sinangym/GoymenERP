import type { MoneyProfileSeries, MoneyProfileVersion, MoneyProfileView } from './moneyProfileContract'

export const CASH_TRANSFER_PROFILE_OPERATION = 'finance.cash-transfer.v1'
const MANUAL_BANK_PAYMENT_OPERATION = 'finance.manual-bank-payment.v1'

export interface CashTransferProfileSuggestion {
  readonly companyId: string
  readonly currencyId: string
  readonly currencyCode: string
  readonly operation: string
  readonly source: MoneyProfileVersion
}

export function isFirstCashTransferProfile(profile: MoneyProfileSeries): boolean {
  return profile.operation === CASH_TRANSFER_PROFILE_OPERATION && profile.version === '0' && profile.versions.length === 0
}

/** FNS-D62: values from the existing authorized owner response, never an active profile or a write.
 * Only a single current version in the same company/currency can be offered for an empty transfer target.
 * A future/latest version is not a substitute for the profile actually in force today. */
export function cashTransferProfileSuggestion(view: MoneyProfileView, companyId: string,
  target: MoneyProfileSeries): CashTransferProfileSuggestion | null {
  if (view.companyId !== companyId || !isFirstCashTransferProfile(target)) return null
  const targets = view.profiles.filter(profile => profile.currencyId === target.currencyId
    && profile.currencyCode === target.currencyCode && profile.operation === CASH_TRANSFER_PROFILE_OPERATION)
  if (targets.length !== 1 || !isFirstCashTransferProfile(targets[0])) return null
  const sources = view.profiles.filter(profile => profile.currencyId === target.currencyId
    && profile.currencyCode === target.currencyCode && profile.operation === MANUAL_BANK_PAYMENT_OPERATION)
  if (sources.length !== 1) return null
  const current = sources[0].versions.filter(entry => entry.state === 'InForce' && entry.effectiveFrom <= view.today
    && (entry.effectiveThrough === null || entry.effectiveThrough >= view.today))
  if (current.length !== 1) return null
  return { companyId, currencyId: target.currencyId, currencyCode: target.currencyCode,
    operation: MANUAL_BANK_PAYMENT_OPERATION, source: current[0] }
}
