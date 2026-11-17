import { describe, expect, it } from 'vitest'
import { cashTransferProfileSuggestion } from './cashTransferProfileSuggestion'
import { decodeMoneyProfiles, type MoneyProfileView } from './moneyProfileContract'
import { cashTransferProfileFixture } from './moneyProfileFixtures'
import { COMPANY_A, COMPANY_B } from './testHarness'

function decoded() {
  const result = decodeMoneyProfiles(cashTransferProfileFixture(), COMPANY_A)
  if (result === null) throw new Error('SENTETIK suggestion fixture must match the actual owner contract')
  return result
}

describe('FNS-D62 initial transfer profile suggestion', () => {
  it('uses the exact current source identity and values, including inactive and nullable source values', () => {
    const view = decoded()
    const source = view.profiles[0].versions[0]
    const result = cashTransferProfileSuggestion(view, COMPANY_A, view.profiles[1])
    expect(result).toMatchObject({ companyId: COMPANY_A, currencyId: view.profiles[1].currencyId,
      operation: 'finance.manual-bank-payment.v1', source: { profileId: source.profileId, version: source.version,
        scale: 4, unitPriceScale: 7, isActive: false } })
    const nullable: MoneyProfileView = { ...view, profiles: [{ ...view.profiles[0], versions: [{ ...source, unitPriceScale: null }] }, view.profiles[1]] }
    expect(cashTransferProfileSuggestion(nullable, COMPANY_A, nullable.profiles[1])?.source.unitPriceScale).toBeNull()
  })

  it('does not replace the current source with a scheduled latest version', () => {
    const view = decoded()
    const current = view.profiles[0].versions[0]
    const later = { ...current, profileId: 'dddddddd-0000-4000-8000-000000000002', version: '2',
      scale: 8, effectiveFrom: '2026-12-01', state: 'Scheduled' as const }
    const scheduled: MoneyProfileView = { ...view, profiles: [{ ...view.profiles[0], version: '2', versions: [current, later] }, view.profiles[1]] }
    expect(cashTransferProfileSuggestion(scheduled, COMPANY_A, scheduled.profiles[1])?.source).toEqual(current)
  })

  it('never offers a source for an existing target or an unrelated operation', () => {
    const view = decoded()
    const existing = { ...view.profiles[1], version: '1', versions: view.profiles[0].versions }
    expect(cashTransferProfileSuggestion({ ...view, profiles: [view.profiles[0], existing] }, COMPANY_A, existing)).toBeNull()
    expect(cashTransferProfileSuggestion(view, COMPANY_A, { ...view.profiles[1], operation: 'finance.credit-plan' })).toBeNull()
    // A stale fabricated empty target cannot bypass the actual existing target in the current response.
    expect(cashTransferProfileSuggestion({ ...view, profiles: [view.profiles[0], existing] }, COMPANY_A, view.profiles[1])).toBeNull()
  })

  it('does not use a different company, currency or absent source as a default', () => {
    const view = decoded()
    expect(cashTransferProfileSuggestion(view, COMPANY_B, view.profiles[1])).toBeNull()
    const foreignCurrency = { ...view.profiles[0], currencyId: '66a81432-19e6-55ee-b9af-3927cb887dea', currencyCode: 'USD' }
    expect(cashTransferProfileSuggestion({ ...view, profiles: [foreignCurrency, view.profiles[1]] }, COMPANY_A, view.profiles[1])).toBeNull()
    expect(cashTransferProfileSuggestion({ ...view, profiles: [view.profiles[1]] }, COMPANY_A, view.profiles[1])).toBeNull()
  })

  it('refuses ambiguous or out-of-window current sources instead of selecting the first one', () => {
    const view = decoded()
    const source = view.profiles[0]
    const current = source.versions[0]
    for (const versions of [[], [current, { ...current, version: '2' }],
      [{ ...current, effectiveFrom: '2026-12-01' }], [{ ...current, effectiveThrough: '2026-11-15' }]]) {
      expect(cashTransferProfileSuggestion({ ...view, profiles: [{ ...source, versions }, view.profiles[1]] }, COMPANY_A, view.profiles[1])).toBeNull()
    }
  })
})
