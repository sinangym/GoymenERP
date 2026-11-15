import { describe, expect, it } from 'vitest'
import {
  decodeCollateralAccount,
  decodeCollateralCommandResult,
  decodeCollateralHistoryPage,
  decodeCollateralTypeCandidates,
  isRatePercentText,
} from './CollateralContract'
import {
  account,
  available,
  changed,
  CO_ACCOUNT,
  CO_COMPANY,
  CO_CURRENCY,
  CO_OTHER_ACCOUNT,
  CO_OTHER_COMPANY,
  coId,
  historyPage,
  recorded,
  refusal,
  released,
  succeeded,
  superseded,
  typeCandidate,
  typeCandidates,
  unavailable,
} from './collateralFixtures'

// SENTETIK decoder evidence (NOT_ENV): exact member sets, the verified company, the record invariants of FNS-D57 and
// the separate figures of FNS-D31 / FNS-D58. Anything else is malformed, never data.

const decodeAccount = (value: unknown) => decodeCollateralAccount(value, CO_COMPANY, CO_ACCOUNT)

describe('decodeCollateralAccount', () => {
  it('decodes the held collaterals and both figures as given', () => {
    const decoded = decodeAccount(account())
    expect(decoded.ok).toBe(true)
    if (!decoded.ok) return
    expect(decoded.value.held).toHaveLength(1)
    expect(decoded.value.weightedCollateral).toEqual(available([{ currencyId: CO_CURRENCY, amount: '187500.375' }]))
    expect(decoded.value.documentaryRisk.state).toBe('Available')
  })

  it('refuses another company, another account or an extra member', () => {
    expect(decodeAccount(account({ companyId: CO_OTHER_COMPANY })).ok).toBe(false)
    expect(decodeAccount(account({ companyAccountId: CO_OTHER_ACCOUNT })).ok).toBe(false)
    expect(decodeAccount({ ...account(), grossRisk: '1' }).ok).toBe(false)
    expect(decodeAccount(account({ held: [recorded({ companyId: CO_OTHER_COMPANY })] })).ok).toBe(false)
    expect(decodeAccount(account({ held: [recorded({ companyAccountId: CO_OTHER_ACCOUNT })] })).ok).toBe(false)
  })

  it('holds only effective, received, unreleased collaterals, once each', () => {
    expect(decodeAccount(account({ held: [superseded()] })).ok).toBe(false)
    expect(decodeAccount(account({ held: [released()] })).ok).toBe(false)
    expect(decodeAccount(account({ held: [recorded({ direction: 'Given' })] })).ok).toBe(false)
    expect(decodeAccount(account({ held: [recorded(), recorded({ id: coId(9) })] })).ok).toBe(false)
    expect(decodeAccount(account({ held: [] })).ok).toBe(true)
  })

  it('never accepts a withheld weighted collateral, but a withheld documentary risk is a state', () => {
    expect(decodeAccount(account({ weightedCollateral: unavailable('finance_documentary_risk_withheld', 'Withheld') })).ok)
      .toBe(false)
    expect(decodeAccount(account({ documentaryRisk: unavailable('finance_documentary_risk_withheld', 'Withheld') })).ok)
      .toBe(true)
    expect(decodeAccount(account({ weightedCollateral: unavailable('finance_collateral_type_rate_unavailable') })).ok)
      .toBe(true)
  })

  it('refuses a figure that mixes a reason with amounts or repeats a currency', () => {
    expect(decodeAccount(account({ weightedCollateral: { state: 'Unavailable', amounts: [], reasonCode: null } })).ok)
      .toBe(false)
    expect(decodeAccount(account({ weightedCollateral: { state: 'Unavailable',
      amounts: [{ currencyId: CO_CURRENCY, amount: '1' }], reasonCode: 'finance_collateral_unreadable' } })).ok).toBe(false)
    expect(decodeAccount(account({ weightedCollateral: { state: 'Available', amounts: [], reasonCode: 'x' } })).ok)
      .toBe(false)
    expect(decodeAccount(account({ documentaryRisk: available([
      { currencyId: CO_CURRENCY, amount: '1' }, { currencyId: CO_CURRENCY, amount: '2' }]) })).ok).toBe(false)
    expect(decodeAccount(account({ weightedCollateral: available([{ currencyId: CO_CURRENCY, amount: '-1' }]) })).ok)
      .toBe(false)
    // A 0% rate weighs a collateral to zero: that is an answer.
    expect(decodeAccount(account({ weightedCollateral: available([{ currencyId: CO_CURRENCY, amount: '0' }]) })).ok)
      .toBe(true)
  })
})

describe('collateral record invariants', () => {
  const one = (record: unknown) => decodeCollateralHistoryPage(historyPage([record]), CO_COMPANY, CO_ACCOUNT).ok

  it('accepts a record, a change and a release as the server writes them', () => {
    expect(one(recorded())).toBe(true)
    expect(one(changed())).toBe(true)
    expect(one(released())).toBe(true)
    expect(one(superseded())).toBe(true)
  })

  it('ties a Record to revision 1 and a reason to a Release only', () => {
    expect(one(recorded({ revision: '2' }))).toBe(false)
    expect(one(changed({ revision: '1' }))).toBe(false)
    expect(one(released({ releaseReason: null }))).toBe(false)
    expect(one(released({ releaseReason: ' padded' }))).toBe(false)
    expect(one(changed({ releaseReason: 'SENTETIK' }))).toBe(false)
  })

  it('ties the superseded stamp to the status and keeps the validity ordered', () => {
    expect(one(recorded({ supersededAtUtc: '2026-11-14T09:00:00+00:00' }))).toBe(false)
    expect(one(recorded({ status: 'Superseded' }))).toBe(false)
    expect(one(recorded({ validFrom: '2026-11-10', validUntil: '2026-11-09' }))).toBe(false)
    expect(one(recorded({ validFrom: '2026-11-10', validUntil: '2026-11-10' }))).toBe(true)
  })

  it('keeps amounts as positive decimal text and refuses unknown enums or a padded reference', () => {
    expect(one(recorded({ amount: '0' }))).toBe(false)
    expect(one(recorded({ amount: 250000.5 }))).toBe(false)
    expect(one(recorded({ amount: '1e3' }))).toBe(false)
    expect(one(recorded({ kind: 'Delete' }))).toBe(false)
    expect(one(recorded({ source: 'SalesOrder' }))).toBe(false)
    expect(one(recorded({ documentReference: 'SENTETIK ' }))).toBe(false)
    expect(one(recorded({ validFrom: '2026-13-01' }))).toBe(false)
  })

  it('allows at most one effective revision per collateral and one row per revision', () => {
    const page = (items: readonly unknown[]) => decodeCollateralHistoryPage(historyPage(items), CO_COMPANY, CO_ACCOUNT).ok
    expect(page([changed(), superseded()])).toBe(true)
    expect(page([changed(), recorded()])).toBe(false)
    expect(page([changed(), changed({ id: coId(8), status: 'Superseded', supersededAtUtc: '2026-11-14T11:00:00+00:00' })]))
      .toBe(false)
    expect(page([recorded(), recorded()])).toBe(false)
    expect(decodeCollateralHistoryPage(historyPage([], CO_OTHER_ACCOUNT), CO_COMPANY, CO_ACCOUNT).ok).toBe(false)
  })
})

describe('decodeCollateralCommandResult', () => {
  it('decodes a success carrying an effective revision and a refusal carrying issues', () => {
    expect(decodeCollateralCommandResult(succeeded(recorded()), CO_COMPANY).ok).toBe(true)
    const refused = decodeCollateralCommandResult(refusal('ValidationFailed',
      [{ field: 'collateralTypeId', code: 'collateral_type_is_instrument' }]), CO_COMPANY)
    expect(refused).toEqual({ ok: true, value: refusal('ValidationFailed',
      [{ field: 'collateralTypeId', code: 'collateral_type_is_instrument' }]) })
  })

  it('refuses a success without an effective resource, with issues or of another company', () => {
    expect(decodeCollateralCommandResult(succeeded(superseded()), CO_COMPANY).ok).toBe(false)
    expect(decodeCollateralCommandResult(succeeded(recorded(), { issues: [{ field: 'x', code: 'y' }] }), CO_COMPANY).ok)
      .toBe(false)
    expect(decodeCollateralCommandResult(succeeded(recorded({ companyId: CO_OTHER_COMPANY })), CO_COMPANY).ok).toBe(false)
    expect(decodeCollateralCommandResult(refusal('Denied', [], { resource: recorded() }), CO_COMPANY).ok).toBe(false)
    expect(decodeCollateralCommandResult(succeeded(recorded(), { isReplay: true, originalCorrelationId: '' }), CO_COMPANY).ok)
      .toBe(false)
    expect(decodeCollateralCommandResult(refusal('Pending'), CO_COMPANY).ok).toBe(false)
  })
})

describe('decodeCollateralTypeCandidates', () => {
  it('decodes Settings types as given, with the rate as text', () => {
    const decoded = decodeCollateralTypeCandidates(typeCandidates([typeCandidate({ ratePercent: '12.5' })]), CO_COMPANY)
    expect(decoded).toEqual({ ok: true, value: typeCandidates([typeCandidate({ ratePercent: '12.5' })]) })
  })

  it('refuses another company, a repeated type, a rate out of 0–100 or an extra member', () => {
    expect(decodeCollateralTypeCandidates(typeCandidates(undefined, CO_OTHER_COMPANY), CO_COMPANY).ok).toBe(false)
    expect(decodeCollateralTypeCandidates(typeCandidates([typeCandidate(), typeCandidate()]), CO_COMPANY).ok).toBe(false)
    expect(decodeCollateralTypeCandidates(typeCandidates([typeCandidate({ ratePercent: '100.01' })]), CO_COMPANY).ok)
      .toBe(false)
    expect(decodeCollateralTypeCandidates(typeCandidates([{ ...typeCandidate(), isNegotiableInstrument: false }]),
      CO_COMPANY).ok).toBe(false)
  })
})

describe('isRatePercentText', () => {
  it.each([
    ['0', true], ['100', true], ['100.00', true], ['99.999999', true], ['12.5', true],
    ['100.000001', false], ['101', false], ['-1', false], ['1e2', false], ['50,5', false], [' 5', false], ['', false],
  ])('%s → %s', (rate, expected) => {
    expect(isRatePercentText(rate)).toBe(expected)
  })
})
