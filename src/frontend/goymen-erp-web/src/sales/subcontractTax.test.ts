import { describe, expect, it } from 'vitest'
import detailRaw from './examples/detail-response.json?raw'
import { decodeDraftDetail } from './contract'
import { decodeSubcontractTaxOptions } from './subcontractTax'
const id = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'
const detail = decodeDraftDetail(JSON.parse(detailRaw), id)!
const source = () => ({ version: detail.draft.version, businessDate: detail.draft.businessDate, buyerSituation: 'Normal', options: {
  on: detail.draft.businessDate, buyerSituationsState: 'Resolved', buyerSituationsReason: null, buyerSituationListVersion: 0,
  buyerSituations: [{ code: 'Normal', name: 'SENTETIK alıcı', withholdingApplies: true, vatMode: 'Normal', documentRequired: false }],
  exemptionCodesState: 'Unresolved', exemptionCodesReason: 'code_list_missing', exemptionCodes: [], itemTypes: ['Service'],
  vatRatesState: 'Resolved', vatRatesReason: null, vatRates: [{ code: '10', name: 'SENTETIK oran', percent: '10', taxCode: null }],
  withholdingRatiosState: 'Unresolved', withholdingRatiosReason: 'rate_list_missing', withholdingRatios: [] } })
describe('Sales subcontract tax strict source contract', () => {
  it('binds source to the actual order version and date and retains actual list values', () => {
    expect(decodeSubcontractTaxOptions(source(), detail)?.options.vatRates[0].percent).toBe('10')
    expect(decodeSubcontractTaxOptions({ ...source(), version: '999' }, detail)).toBeNull()
    expect(decodeSubcontractTaxOptions({ ...source(), businessDate: '2026-01-01' }, detail)).toBeNull()
    expect(decodeSubcontractTaxOptions({ ...source(), companyId: 'foreign' }, detail)).toBeNull()
  })
  it('does not turn corrupt list success into a usable tax source', () => {
    expect(decodeSubcontractTaxOptions({ ...source(), options: { ...source().options, vatRates: [{ code: '10', name: 'SENTETIK', percent: '101', taxCode: null }] } }, detail)).toBeNull()
  })
})
