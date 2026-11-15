// SENTETIK — synthetic identities and amounts only.
import { describe, expect, it } from 'vitest'
import { decodeCreditPanel } from './CreditPanelContract'

const COMPANY = 'aaaaaaaa-0000-4000-8000-000000000001'
const PARTY = '11111111-1111-4111-8111-111111111111'
const ACCOUNT = '44444444-4444-4444-8444-444444444444'
const TRY = '22222222-2222-4222-8222-222222222222'
const USD = '33333333-3333-4333-8333-333333333333'

const available = (...amounts: (readonly [string, string])[]) => ({
  state: 'Available',
  amounts: amounts.map(([currencyId, amount]) => ({ currencyId, amount })),
  reasonCode: null,
})
const notShown = (state: string, reasonCode: string) => ({ state, amounts: [], reasonCode })

const panel = (over: Record<string, unknown> = {}) => ({
  companyId: COMPANY,
  partyId: PARTY,
  companyAccountId: ACCOUNT,
  asOf: '2026-11-15',
  creditLimit: available([TRY, '250000.5']),
  openReceivables: available([TRY, '1500.00'], [USD, '20']),
  heldCollateral: available([TRY, '1000']),
  weightedCollateral: notShown('Unavailable', 'finance_collateral_type_rate_unavailable'),
  collateralUtilization: notShown('Unavailable', 'finance_collateral_utilization_source_unavailable'),
  documentaryRisk: available(),
  ...over,
})

const decode = (value: unknown) => decodeCreditPanel(value, COMPANY, PARTY, ACCOUNT)

describe('decodeCreditPanel', () => {
  it('decodes every figure with its own state and keeps the amounts as text', () => {
    const decoded = decode(panel())
    expect(decoded.ok).toBe(true)
    if (!decoded.ok) return
    expect(decoded.value.creditLimit).toEqual({ state: 'Available', amounts: [{ currencyId: TRY, amount: '250000.5' }], reasonCode: null })
    expect(decoded.value.openReceivables.amounts).toHaveLength(2)
    expect(decoded.value.weightedCollateral).toEqual(
      { state: 'Unavailable', amounts: [], reasonCode: 'finance_collateral_type_rate_unavailable' })
    expect(decoded.value.documentaryRisk).toEqual({ state: 'Available', amounts: [], reasonCode: null })
    expect(decoded.value.asOf).toBe('2026-11-15')
  })

  it('accepts a not-set credit limit and withheld figures', () => {
    const decoded = decode(panel({
      creditLimit: notShown('NotSet', 'finance_credit_limit_not_set'),
      heldCollateral: notShown('Withheld', 'finance_collateral_withheld'),
    }))
    expect(decoded.ok).toBe(true)
  })

  it.each([
    ['another company', { companyId: 'aaaaaaaa-0000-4000-8000-000000000002' }],
    ['another party', { partyId: '11111111-1111-4111-8111-111111111112' }],
    ['another account', { companyAccountId: '44444444-4444-4444-8444-444444444445' }],
    ['a date that is not a calendar day', { asOf: '2026-02-30' }],
    ['an instant for the date', { asOf: '2026-11-15T00:00:00Z' }],
    ['a figure that is missing', { documentaryRisk: undefined }],
    ['a null figure', { documentaryRisk: null }],
    ['NotSet on a figure other than the limit', { openReceivables: notShown('NotSet', 'finance_credit_limit_not_set') }],
    ['an unknown state', { heldCollateral: notShown('Partial', 'x') }],
    ['an unavailable figure without a reason', { heldCollateral: notShown('Unavailable', '') }],
    ['an unavailable figure with an amount', {
      heldCollateral: { state: 'Unavailable', amounts: [{ currencyId: TRY, amount: '1' }], reasonCode: 'r' },
    }],
    ['an available figure with a reason', { heldCollateral: { ...available([TRY, '1']), reasonCode: 'r' } }],
    ['a negative amount', { openReceivables: available([TRY, '-1']) }],
    ['a numeric amount', { openReceivables: { state: 'Available', amounts: [{ currencyId: TRY, amount: 1 }], reasonCode: null } }],
    ['the same currency twice', { openReceivables: available([TRY, '1'], [TRY, '2']) }],
    ['an empty currency id', { openReceivables: available(['00000000-0000-0000-0000-000000000000', '1']) }],
    ['an extra amount member', {
      openReceivables: { state: 'Available', amounts: [{ currencyId: TRY, amount: '1', rate: '1' }], reasonCode: null },
    }],
    ['an extra figure member', { openReceivables: { ...available(), total: '0' } }],
  ])('refuses %s', (_name, over) => {
    const value: Record<string, unknown> = panel(over)
    for (const [key, member] of Object.entries(over)) if (member === undefined) delete value[key]
    expect(decode(value).ok).toBe(false)
  })

  it('refuses an extra top-level member such as a gross risk', () => {
    expect(decode({ ...panel(), grossRisk: available([TRY, '1']) }).ok).toBe(false)
  })
})
