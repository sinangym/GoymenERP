import { describe, expect, it } from 'vitest'
import { decodeMoneyProfiles } from './moneyProfileContract'
import { moneyProfileFixture } from './moneyProfileFixtures'
import { COMPANY_A, COMPANY_B } from './testHarness'

describe('F029-S3 money profile decoder', () => {
  it('keeps the unit-price scale nullable and rejects foreign missing extra and mistyped data', () => {
    const fixture = moneyProfileFixture()
    expect(decodeMoneyProfiles(fixture, COMPANY_A)).toEqual(fixture)
    expect(decodeMoneyProfiles(fixture, COMPANY_B)).toBeNull()
    const series = fixture.profiles[0]
    const row = series.versions[0]
    const { unitPriceScale: _missing, ...missing } = row
    for (const value of [missing, { ...row, extra: true }, { ...row, scale: '2' }, { ...row, unitPriceScale: 29 }, { ...row, version: '0' }])
      expect(decodeMoneyProfiles({ ...fixture, profiles: [{ ...series, versions: [value] }] }, COMPANY_A)).toBeNull()
    const nullable = { ...fixture, profiles: [{ ...series, versions: [{ ...row, unitPriceScale: null }] }] }
    expect(decodeMoneyProfiles(nullable, COMPANY_A)).toEqual(nullable)
    expect(decodeMoneyProfiles({ ...fixture, profiles: [series, series] }, COMPANY_A)).toBeNull()
  })
})

describe('money profile initial authoring contract', () => {
  it('accepts only version zero for an empty series and refuses missing history for an existing series', () => {
    const fixture = moneyProfileFixture()
    const empty = { ...fixture, profiles: [{ ...fixture.profiles[0], version: '0', versions: [] }] }
    expect(decodeMoneyProfiles(empty, fixture.companyId)?.profiles[0].version).toBe('0')
    expect(decodeMoneyProfiles({ ...empty, profiles: [{ ...empty.profiles[0], version: '1' }] }, fixture.companyId)).toBeNull()
    expect(decodeMoneyProfiles({ ...fixture, profiles: [{ ...fixture.profiles[0], version: '0' }] }, fixture.companyId)).toBeNull()
  })
})
