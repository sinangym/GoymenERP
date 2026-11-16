import { describe, expect, it } from 'vitest'
import {
  decodeOpeningBalance,
  decodeOpeningBalanceCommandResult,
  decodeOpeningBalanceCurrencyCandidates,
  decodeOpeningBalanceListPage,
  decodeOpeningPeriod,
} from './OpeningBalanceContract'
import {
  approved,
  currencyCandidates,
  listPage,
  OB_COMPANY,
  OB_OTHER_COMPANY,
  OB_PARTY,
  partyProposed,
  period,
  proposed,
  refusal,
  rejected,
  succeeded,
} from './openingBalanceFixtures'

// SENTETIK decoder evidence (NOT_ENV): exact member sets, the verified company and the FNS-D23 record invariants.

describe('decodeOpeningBalance', () => {
  it('decodes a cash proposal, a party proposal and both decisions', () => {
    for (const raw of [proposed(), partyProposed(), approved(), rejected()]) {
      const decoded = decodeOpeningBalance(raw, OB_COMPANY)
      expect(decoded.ok).toBe(true)
    }
    const party = decodeOpeningBalance(partyProposed(), OB_COMPANY)
    expect(party.ok && party.value.partyId).toBe(OB_PARTY)
    expect(party.ok && party.value.direction).toBe('Debit')
  })

  it('refuses another company, an extra or missing member and an unknown enum', () => {
    expect(decodeOpeningBalance(proposed({ companyId: OB_OTHER_COMPANY }), OB_COMPANY).ok).toBe(false)
    expect(decodeOpeningBalance({ ...proposed(), extra: 1 }, OB_COMPANY).ok).toBe(false)
    const { version: _dropped, ...missing } = proposed()
    expect(decodeOpeningBalance(missing, OB_COMPANY).ok).toBe(false)
    expect(decodeOpeningBalance(proposed({ targetKind: 'Warehouse' }), OB_COMPANY).ok).toBe(false)
    expect(decodeOpeningBalance(proposed({ status: 'Posted' }), OB_COMPANY).ok).toBe(false)
  })

  it('keeps the amount as positive decimal text and the version as a positive long', () => {
    expect(decodeOpeningBalance(proposed({ amount: 150000.5 }), OB_COMPANY).ok).toBe(false)
    expect(decodeOpeningBalance(proposed({ amount: '0' }), OB_COMPANY).ok).toBe(false)
    expect(decodeOpeningBalance(proposed({ amount: '-5' }), OB_COMPANY).ok).toBe(false)
    expect(decodeOpeningBalance(proposed({ version: '0' }), OB_COMPANY).ok).toBe(false)
    expect(decodeOpeningBalance(proposed({ asOfDate: '2026-13-01' }), OB_COMPANY).ok).toBe(false)
  })

  it('requires party and direction exactly for a party opening', () => {
    expect(decodeOpeningBalance(proposed({ partyId: OB_PARTY }), OB_COMPANY).ok).toBe(false)
    expect(decodeOpeningBalance(proposed({ direction: 'Credit' }), OB_COMPANY).ok).toBe(false)
    expect(decodeOpeningBalance(partyProposed({ partyId: null }), OB_COMPANY).ok).toBe(false)
    expect(decodeOpeningBalance(partyProposed({ direction: null }), OB_COMPANY).ok).toBe(false)
    expect(decodeOpeningBalance(partyProposed({ direction: 'Sideways' }), OB_COMPANY).ok).toBe(false)
  })

  it('ties the decision members to the status', () => {
    expect(decodeOpeningBalance(proposed({ decidedAtUtc: '2026-11-15T09:00:00+00:00' }), OB_COMPANY).ok).toBe(false)
    expect(decodeOpeningBalance(proposed({ rejectionReason: 'x' }), OB_COMPANY).ok).toBe(false)
    expect(decodeOpeningBalance(approved({ decidedAtUtc: null }), OB_COMPANY).ok).toBe(false)
    expect(decodeOpeningBalance(approved({ rejectionReason: 'x' }), OB_COMPANY).ok).toBe(false)
    expect(decodeOpeningBalance(rejected({ rejectionReason: null }), OB_COMPANY).ok).toBe(false)
    expect(decodeOpeningBalance(rejected({ rejectionReason: ' padded ' }), OB_COMPANY).ok).toBe(false)
  })
})

describe('decodeOpeningBalanceListPage', () => {
  it('decodes the page for the verified company and refuses a duplicate id or another company', () => {
    const decoded = decodeOpeningBalanceListPage(listPage([proposed(), approved()]), OB_COMPANY)
    expect(decoded.ok && decoded.value.items).toHaveLength(2)
    expect(decodeOpeningBalanceListPage(listPage([proposed(), proposed()]), OB_COMPANY).ok).toBe(false)
    expect(decodeOpeningBalanceListPage(listPage([], OB_OTHER_COMPANY), OB_COMPANY).ok).toBe(false)
    expect(decodeOpeningBalanceListPage(listPage([proposed({ companyId: OB_OTHER_COMPANY })]), OB_COMPANY).ok)
      .toBe(false)
  })
})

describe('decodeOpeningPeriod', () => {
  it('decodes Open and Closed with a version and Unavailable without one', () => {
    expect(decodeOpeningPeriod(period('Open'), OB_COMPANY)).toEqual({
      ok: true, value: { companyId: OB_COMPANY, state: 'Open', version: '7' },
    })
    expect(decodeOpeningPeriod(period('Closed'), OB_COMPANY).ok).toBe(true)
    expect(decodeOpeningPeriod(period('Unavailable'), OB_COMPANY).ok).toBe(true)
  })

  it('refuses a version that does not fit the state, an unknown state or another company', () => {
    expect(decodeOpeningPeriod(period('Open', null), OB_COMPANY).ok).toBe(false)
    expect(decodeOpeningPeriod(period('Unavailable', '7'), OB_COMPANY).ok).toBe(false)
    expect(decodeOpeningPeriod(period('Ajar', '7'), OB_COMPANY).ok).toBe(false)
    expect(decodeOpeningPeriod(period('Open', '7', OB_OTHER_COMPANY), OB_COMPANY).ok).toBe(false)
  })
})

describe('decodeOpeningBalanceCommandResult', () => {
  it('decodes a success with its resource and a refusal without one', () => {
    expect(decodeOpeningBalanceCommandResult(succeeded(proposed()), OB_COMPANY).ok).toBe(true)
    const refused = decodeOpeningBalanceCommandResult(
      refusal('ValidationFailed', [{ field: 'amount', code: 'opening_amount_invalid' }]), OB_COMPANY)
    expect(refused.ok && refused.value.issues).toEqual([{ field: 'amount', code: 'opening_amount_invalid' }])
  })

  it('refuses a success without a resource, a refusal with one and a replay without its correlation', () => {
    expect(decodeOpeningBalanceCommandResult(succeeded(null), OB_COMPANY).ok).toBe(false)
    expect(decodeOpeningBalanceCommandResult({ ...refusal('VersionConflict'), resource: proposed() }, OB_COMPANY).ok)
      .toBe(false)
    expect(decodeOpeningBalanceCommandResult(
      succeeded(proposed(), { isReplay: true, originalCorrelationId: '' }), OB_COMPANY).ok).toBe(false)
    expect(decodeOpeningBalanceCommandResult(
      succeeded(proposed(), { issues: [{ field: 'x', code: 'y' }] }), OB_COMPANY).ok).toBe(false)
    expect(decodeOpeningBalanceCommandResult(refusal('Exploded'), OB_COMPANY).ok).toBe(false)
  })
})

describe('decodeOpeningBalanceCurrencyCandidates', () => {
  it('decodes the candidates as given and refuses another company', () => {
    expect(decodeOpeningBalanceCurrencyCandidates(currencyCandidates(), OB_COMPANY).ok).toBe(true)
    expect(decodeOpeningBalanceCurrencyCandidates(currencyCandidates(OB_OTHER_COMPANY), OB_COMPANY).ok).toBe(false)
  })
})
