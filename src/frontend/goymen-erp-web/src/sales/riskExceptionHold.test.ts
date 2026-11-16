/**
 * FNS-D30 / SAT-D310: the display-only hold category mirrors the server's `SalesOrderRiskWarnings.Classify` — within → none,
 * over → OverLimit, a credit limit with no source (or `credit_limit_not_defined` on it) → CreditLimitNotSet, any other
 * unavailable read → SourceUnavailable. Every input is synthetic.
 */
import { describe, expect, it } from 'vitest'
import type { RiskCheck, RiskFigure } from './riskCheck'
import { riskHoldCategory } from './riskExceptionHold'

const limit = (state: RiskFigure['state']): RiskFigure => ({
  kind: 'CreditLimit', source: 'finance.party-credit-limit', state, taxBasis: null, amounts: null, reasonCode: null,
})
const check = (
  result: RiskCheck['result'], creditLimit: RiskFigure, reasons: RiskCheck['reasons'] = [],
): Pick<RiskCheck, 'result' | 'reasons' | 'creditLimit'> => ({ result, reasons, creditLimit })

describe('risk hold category (FNS-D30, display only)', () => {
  it('needs no exception within the limit', () => {
    expect(riskHoldCategory(check('Within', limit('Available')))).toBeNull()
  })

  it('names an over-limit order OverLimit', () => {
    expect(riskHoldCategory(check('Over', limit('Available')))).toBe('OverLimit')
  })

  it('names a credit limit without a source CreditLimitNotSet, never unlimited or zero', () => {
    expect(riskHoldCategory(check('Unavailable', limit('NoSource')))).toBe('CreditLimitNotSet')
  })

  it('names the credit_limit_not_defined reason on the credit limit CreditLimitNotSet', () => {
    expect(riskHoldCategory(check('Unavailable', limit('Unavailable'), [
      { component: 'CreditLimit', code: 'credit_limit_not_defined' },
    ] as RiskCheck['reasons']))).toBe('CreditLimitNotSet')
  })

  it('keeps the same code on another component, or another credit-limit reason, SourceUnavailable', () => {
    expect(riskHoldCategory(check('Unavailable', limit('Available'), [
      { component: 'ShippedNotInvoiced', code: 'credit_limit_not_defined' },
    ] as RiskCheck['reasons']))).toBe('SourceUnavailable')
    expect(riskHoldCategory(check('Unavailable', limit('Unavailable'), [
      { component: 'CreditLimit', code: 'finance_credit_limit_unreadable' },
    ] as RiskCheck['reasons']))).toBe('SourceUnavailable')
  })
})
