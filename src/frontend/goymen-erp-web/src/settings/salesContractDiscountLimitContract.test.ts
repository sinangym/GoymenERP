import { describe, expect, it } from 'vitest'
import { decodeContractDiscountLimit } from './salesContractDiscountLimitContract'
const company = '11111111-1111-1111-1111-111111111111'
const item = { version: '1', limitPercent: '0', recordedBy: 'synthetic', recordedAtUtc: '2026-11-16T12:00:00Z', reason: 'synthetic' }
describe('SC32b separate contract discount limit decoder', () => {
  it('distinguishes missing from explicit zero', () => {
    expect(decodeContractDiscountLimit({ companyId: company, limitPercent: null, version: '0', versions: [] }, company)?.limitPercent).toBeNull()
    expect(decodeContractDiscountLimit({ companyId: company, limitPercent: '0', version: '1', versions: [item] }, company)?.limitPercent).toBe('0')
  })
  it.each(['100.0001','101','-1','1,5','1e1','0.12345'])('refuses invalid published percentage text %s', rate => {
    expect(decodeContractDiscountLimit({ companyId: company, limitPercent: rate, version: '1', versions: [{ ...item, limitPercent: rate }] }, company)).toBeNull()
  })
  it('refuses invented defaults, another company, missing history and duplicate versions', () => {
    expect(decodeContractDiscountLimit({ companyId: company, limitPercent: '0', version: '0', versions: [] }, company)).toBeNull()
    expect(decodeContractDiscountLimit({ companyId: 'other', limitPercent: null, version: '0', versions: [] }, company)).toBeNull()
    expect(decodeContractDiscountLimit({ companyId: company, limitPercent: '0', version: '1', versions: [] }, company)).toBeNull()
    expect(decodeContractDiscountLimit({ companyId: company, limitPercent: '0', version: '1', versions: [item,item] }, company)).toBeNull()
  })
  it('K3 P2: accepts only the exact key sets of the view and of each version', () => {
    const view = { companyId: company, limitPercent: '0', version: '1', versions: [item] }
    const without = (value: object, key: string) => Object.fromEntries(Object.entries(value).filter(([name]) => name !== key))
    expect(decodeContractDiscountLimit(view, company)).toEqual(view)
    expect(decodeContractDiscountLimit({ ...view, extra: 'synthetic' }, company)).toBeNull()
    expect(decodeContractDiscountLimit({ ...view, versions: [{ ...item, extra: 'synthetic' }] }, company)).toBeNull()
    for (const key of Object.keys(view)) expect(decodeContractDiscountLimit(without(view, key), company)).toBeNull()
    for (const key of Object.keys(item)) expect(decodeContractDiscountLimit({ ...view, versions: [without(item, key)] }, company)).toBeNull()
  })
})
