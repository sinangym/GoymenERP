import { describe, expect, it } from 'vitest'
import { decodeSubcontractTaxOptions, decodeSubcontractTaxPolicy, selectedSubcontractTax, type SubcontractTaxOptions } from './subcontractTaxContract'
const company = '7e000000-0000-0000-0000-000000000001'
const value = { vatApplies: true, vatPercent: '20', exemptionCode: null, withholdingApplies: false, withholdingPercent: null, withholdingCode: null }
const options = (): SubcontractTaxOptions => ({ companyId: company, side: 2, on: '2026-11-17', defaultVersion: '1', default: value,
  vatRates: [{ code: '20', name: 'SENTETIK KDV', percent: '20', taxCode: null }, { code: '0', name: 'SENTETIK sıfır', percent: '0', taxCode: null }],
  withholdingRatios: [{ code: '5/10', name: 'SENTETIK yarı', percent: '50', taxCode: '601' }], exemptionCodes: [{ code: '301', name: 'SENTETIK istisna' }],
  withholdingCodes: [{ code: '601', name: 'SENTETIK tevkifat' }], sourceVersion: 'SENTETIK-lists:1' })
describe('subcontract tax owner contracts (synthetic sources; runtime NOT_RUN)', () => {
  it('keeps no source separate from a published zero tax and binds side company and business date', () => {
    const source = options()
    expect(decodeSubcontractTaxOptions(source, company, 'purchasing', source.on)).toEqual(source)
    expect(decodeSubcontractTaxOptions(source, company, 'sales')).toBeNull()
    expect(decodeSubcontractTaxOptions(source, 'foreign', 'purchasing')).toBeNull()
    expect(decodeSubcontractTaxOptions(source, company, 'purchasing', '2026-11-18')).toBeNull()
    expect(decodeSubcontractTaxOptions({ ...source, defaultVersion: '0', default: null }, company, 'purchasing')?.default).toBeNull()
    expect(decodeSubcontractTaxOptions({ ...source, defaultVersion: '0' }, company, 'purchasing')).toBeNull()
  })
  it('rejects corrupt rates duplicate list identities and free client fields', () => {
    const source = options()
    for (const invalid of [{ ...source, vatRates: [...source.vatRates, source.vatRates[0]] },
      { ...source, vatRates: [{ ...source.vatRates[0], percent: '20.0000' }] }, { ...source, permission: 'made-up' },
      { ...source, withholdingRatios: [{ ...source.withholdingRatios[0], percent: '0' }] }])
      expect(decodeSubcontractTaxOptions(invalid, company, 'purchasing')).toBeNull()
  })
  it('takes exact actual list values and their mapped codes without deriving percentages', () => {
    expect(selectedSubcontractTax(options(), { useDefault: false, vatRateCode: '20', withholdingRatioCode: '5/10', exemptionCode: '' }))
      .toEqual({ ...value, withholdingApplies: true, withholdingPercent: '50', withholdingCode: '601' })
    expect(selectedSubcontractTax(options(), { useDefault: false, vatRateCode: 'unknown', withholdingRatioCode: '', exemptionCode: '' })).toBeNull()
    expect(selectedSubcontractTax(options(), { useDefault: false, vatRateCode: '0', withholdingRatioCode: '5/10', exemptionCode: '301' })).toBeNull()
    expect(selectedSubcontractTax(options(), { useDefault: false, vatRateCode: '0', withholdingRatioCode: '', exemptionCode: '301' }))
      .toEqual({ vatApplies: false, vatPercent: null, exemptionCode: '301', withholdingApplies: false, withholdingPercent: null, withholdingCode: null })
  })
  it('requires actual ordered history and refuses inconsistent current snapshots', () => {
    const current = { companyId: company, current: value, version: '1', versions: [{ version: '1', value, recordedBy: 'SENTETIK', recordedAtUtc: '2026-11-17T00:00:00Z', reason: 'SENTETIK' }] }
    expect(decodeSubcontractTaxPolicy(current, company)).toEqual(current)
    expect(decodeSubcontractTaxPolicy({ ...current, current: { ...value, vatPercent: '10' } }, company)).toBeNull()
    expect(decodeSubcontractTaxPolicy({ ...current, versions: [...current.versions, current.versions[0]] }, company)).toBeNull()
    expect(decodeSubcontractTaxPolicy({ companyId: company, current: null, version: '0', versions: [] }, company)?.current).toBeNull()
  })
})
