import { describe, expect, it } from 'vitest'
import { decodeServiceOrder, decodeServiceOrders, decodeServiceReferences, decodeServiceReceipt } from './serviceOrderContract'
import { serviceId, serviceFixture } from './serviceOrderFixtures.testSupport'
describe('ALC-S7 strict owner contract (synthetic)', () => {
  it('accepts a genuine empty catalog without supplying a service or unit default', () => {
    expect(decodeServiceReferences({ companyId: serviceId(1), serviceTypes: [], units: [] }, serviceId(1))?.serviceTypes).toEqual([])
  })
  it('rejects stale company, duplicate identity, missing and extra fields', () => {
    const value = serviceFixture()
    expect(decodeServiceOrder(value, serviceId(1))).not.toBeNull()
    expect(decodeServiceOrder(value, serviceId(99))).toBeNull()
    expect(decodeServiceOrders([value, value], serviceId(1))).toBeNull()
    expect(decodeServiceOrder({ ...value, ownerIsCompany: true }, serviceId(1))).toBeNull()
    const { version: _version, ...missing } = value
    expect(decodeServiceOrder(missing, serviceId(1))).toBeNull()
  })
  it('rejects price disclosure under a withheld response and never upgrades it to zero', () => {
    const value = serviceFixture()
    expect(decodeServiceOrder({ ...value, lines: [{ ...value.lines[0], unitPrice: '0' }] }, serviceId(1))).toBeNull()
    expect(decodeServiceOrder(value, serviceId(1))?.lines[0].unitPrice).toBeNull()
  })
  it('refuses a subcontract without explicit material/output facts and a replay mismatch', () => {
    const value = serviceFixture()
    expect(decodeServiceOrder({ ...value, kind: 'OwnMaterialSubcontract' }, serviceId(1))).toBeNull()
    expect(decodeServiceReceipt({ order: value, isReplay: true, originalCorrelationId: 'SENTETIK' }, serviceId(1), serviceId(2), false)).toBeNull()
  })
})
