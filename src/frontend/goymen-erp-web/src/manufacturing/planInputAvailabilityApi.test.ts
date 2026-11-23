import { describe, expect, it } from 'vitest'
import { decodePlanInputAvailability } from './planInputAvailabilityApi'
const companyId = '10000000-0000-0000-0000-000000000001'
const planId = '20000000-0000-0000-0000-000000000002'
const without = (value: object, key: string) => Object.fromEntries(Object.entries(value).filter(([name]) => name !== key))
const input = { labelId: '30000000-0000-0000-0000-000000000003', productId: '40000000-0000-0000-0000-000000000004', labelVersion: '2',
  requestedKg: '100', physicalKg: '120', blockedKg: '0', allocatedKg: '10', freeKg: '110', missingKg: '0', freePositionCount: 1 }
const cancelled = { orderId: '50000000-0000-0000-0000-000000000005', planVersionAfterCancel: 2 }
const view = { contractVersion: 1, companyId, planId, planVersion: 3, inputs: [input], cancelledOrders: [cancelled], readAtUtc: '2026-11-17T09:00:00Z' }
describe('plan input availability exact key set (SENTETIK)', () => {
  it('rejects an extra or a missing key on the view, every input line and every cancelled order', () => {
    expect(decodePlanInputAvailability(view)).toEqual(view)
    expect(decodePlanInputAvailability({ ...view, extra: 1 })).toBeNull()
    for (const key of Object.keys(view)) expect(decodePlanInputAvailability(without(view, key))).toBeNull()
    expect(decodePlanInputAvailability({ ...view, inputs: [{ ...input, extra: 1 }] })).toBeNull()
    for (const key of Object.keys(input)) expect(decodePlanInputAvailability({ ...view, inputs: [without(input, key)] })).toBeNull()
    expect(decodePlanInputAvailability({ ...view, cancelledOrders: [{ ...cancelled, extra: 1 }] })).toBeNull()
    for (const key of Object.keys(cancelled)) expect(decodePlanInputAvailability({ ...view, cancelledOrders: [without(cancelled, key)] })).toBeNull()
  })
})
