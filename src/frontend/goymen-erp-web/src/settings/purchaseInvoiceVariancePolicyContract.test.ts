import { describe, expect, it } from 'vitest'
import { decodePurchaseInvoiceVariancePolicy, invoiceToleranceInput } from './purchaseInvoiceVariancePolicyContract'

const company = 'aaaaaaaa-0000-4000-8000-000000000001'
const row = { policyId: '11111111-1111-4111-8111-111111111111', version: '1', amountTolerance: '0', rateTolerance: '0.000001', publishedBy: 'SENTETIK', publishedAtUtc: '2026-11-17T09:00:00+00:00', reason: 'SENTETIK yayın' }
const fixture = () => ({ companyId: company, version: '1', current: { ...row }, versions: [{ ...row }] })
describe('API-97 strict company policy contract', () => {
  it('distinguishes no publication from an actual published zero without calculating tolerances', () => {
    expect(decodePurchaseInvoiceVariancePolicy({ companyId: company, version: '0', current: null, versions: [] }, company)?.current).toBeNull()
    expect(decodePurchaseInvoiceVariancePolicy(fixture(), company)?.current?.amountTolerance).toBe('0')
    expect(invoiceToleranceInput('999999999999999999.999999')).toBe(true)
    expect(invoiceToleranceInput('0.0000001')).toBe(false)
  })
  it('rejects foreign company, malformed decimals, missing current, extra fields and contradictory history', () => {
    expect(decodePurchaseInvoiceVariancePolicy(fixture(), 'bbbbbbbb-0000-4000-8000-000000000002')).toBeNull()
    expect(decodePurchaseInvoiceVariancePolicy({ ...fixture(), current: null }, company)).toBeNull()
    expect(decodePurchaseInvoiceVariancePolicy({ ...fixture(), extra: true }, company)).toBeNull()
    expect(decodePurchaseInvoiceVariancePolicy({ ...fixture(), versions: [] }, company)).toBeNull()
    for (const amountTolerance of [0, '-1', '1e2', '0.0000001', '0.00']) {
      const bad = { ...row, amountTolerance }
      expect(decodePurchaseInvoiceVariancePolicy({ ...fixture(), current: bad, versions: [bad] }, company)).toBeNull()
    }
    expect(decodePurchaseInvoiceVariancePolicy({ ...fixture(), current: { ...row, rateTolerance: '1' } }, company)).toBeNull()
  })
})
