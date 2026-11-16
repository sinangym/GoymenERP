/**
 * FNS-D30 (Sinan 2026-11-12) / SAT-D310 on S-04: the reason an order's shipment is held at the Logistics risk gate, as a display
 * category only. The API-06 risk read carries no category of its own, so the screen names it from the same facts the server's
 * `SalesOrderRiskWarnings.Classify` reads: `Within` → no hold; `Over` → over the limit; `Unavailable` with the credit limit
 * having no source (or the `credit_limit_not_defined` reason on the credit limit) → no valid limit; any other `Unavailable` → the
 * risk could not be read. The category decides nothing: the Logistics gate and the server decide the hold, and every exception or
 * credit-limit command is measured afresh by the server. It never carries or derives an amount (SAT-D162).
 */
import type { RiskCheck } from './riskCheck'

export const RISK_HOLD_CATEGORIES = ['OverLimit', 'CreditLimitNotSet', 'SourceUnavailable'] as const
export type RiskHoldCategory = (typeof RISK_HOLD_CATEGORIES)[number]

/** The credit-limit component and the reason code the server classifies as "no valid limit" (mirrors the domain constants). */
const CREDIT_LIMIT_COMPONENT = 'CreditLimit'
const CREDIT_LIMIT_NOT_DEFINED = 'credit_limit_not_defined'

/** The hold category of a decoded risk read; null when the customer is within the limit (no exception needed). */
export function riskHoldCategory(check: Pick<RiskCheck, 'result' | 'reasons' | 'creditLimit'>): RiskHoldCategory | null {
  switch (check.result) {
    case 'Within':
      return null
    case 'Over':
      return 'OverLimit'
    case 'Unavailable':
      return check.creditLimit.state === 'NoSource'
        || check.reasons.some((reason) => reason.component === CREDIT_LIMIT_COMPONENT && reason.code === CREDIT_LIMIT_NOT_DEFINED)
        ? 'CreditLimitNotSet'
        : 'SourceUnavailable'
  }
}
