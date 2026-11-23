import { describe, expect, it } from 'vitest'
import { decodePlanAllocationShortfall } from './planAllocationShortfall'
const companyId = '10000000-0000-0000-0000-000000000001'
const planId = '20000000-0000-0000-0000-000000000002'
const without = (value: object, key: string) => Object.fromEntries(Object.entries(value).filter(([name]) => name !== key))
const snapshot = { companyId, planId, labelId: '30000000-0000-0000-0000-000000000003', productId: '40000000-0000-0000-0000-000000000004',
  labelVersion: '2', requestedKg: '100', availableKg: '40', missingKg: '60', freePositionCount: 1 }
describe('plan allocation shortfall exact key set (SENTETIK)', () => {
  it('rejects an extra or a missing key', () => {
    expect(decodePlanAllocationShortfall(snapshot, companyId, planId)).toEqual(snapshot)
    expect(decodePlanAllocationShortfall({ ...snapshot, extra: 1 }, companyId, planId)).toBeNull()
    for (const key of Object.keys(snapshot)) expect(decodePlanAllocationShortfall(without(snapshot, key), companyId, planId)).toBeNull()
  })
})
