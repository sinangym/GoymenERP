import { describe, expect, it } from 'vitest'
import { decodeCollateralTypes } from './collateralTypesContract'
const company = '11111111-1111-1111-1111-111111111111'
const item = { id: '22222222-2222-2222-2222-222222222222', code: 'DBS', name: 'DBS', kind: 'Dbs', active: false, ratePercent: '85.1234567890123456789', version: '2', recordedBy: 'synthetic', recordedAtUtc: '2026-11-16T12:00:00Z' }
describe('SC32a collateral catalog contract', () => {
  it('accepts an empty catalog and preserves inactive exact-text rates without calculation', () => {
    expect(decodeCollateralTypes({ companyId: company, entries: [] }, company)?.entries).toEqual([])
    expect(decodeCollateralTypes({ companyId: company, entries: [item] }, company)?.entries[0]).toEqual(item)
  })
  it.each([{ ...item, kind: 'Cheque' }, { ...item, version: '0' }, { ...item, active: 'false' }, { ...item, ratePercent: 85 }, { ...item, ratePercent: '8e1' }, { ...item, recordedAtUtc: 'invalid' }])('rejects malformed catalog data', invalid => {
    expect(decodeCollateralTypes({ companyId: company, entries: [invalid] }, company)).toBeNull()
  })
  it('rejects another company or duplicate stable identities and codes', () => {
    expect(decodeCollateralTypes({ companyId: 'other', entries: [item] }, company)).toBeNull()
    expect(decodeCollateralTypes({ companyId: company, entries: [item, item] }, company)).toBeNull()
  })
})
