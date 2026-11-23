import { afterEach, describe, expect, it } from 'vitest'
import { beginAllocationAttempt, loadAllocationAttempt, type AllocationAttemptScope } from './allocationAttempt'

const scope: AllocationAttemptScope = { companyId: 'SENTETIK-company', salesOrderId: 'SENTETIK-order', salesOrderLineId: 'SENTETIK-line', kind: 'create' }
afterEach(() => window.localStorage.clear())

describe('sales allocation attempt store', () => {
  it('K3 P2: reads back only a slot that holds exactly the stored fields for exactly this scope', () => {
    const begun = beginAllocationAttempt(scope, { quantityText: '1' })
    const slot = window.localStorage.key(0)!
    const stored = JSON.parse(window.localStorage.getItem(slot)!) as Record<string, unknown>
    expect(Object.keys(stored).sort()).toEqual(['body', 'correlationId', 'idempotencyKey', 'schema', 'scope'])
    expect(loadAllocationAttempt(scope)).toEqual(begun)
    const read = (value: unknown) => {
      window.localStorage.setItem(slot, JSON.stringify(value))
      return loadAllocationAttempt(scope)
    }
    expect(read({ ...stored, extra: 'SENTETIK' })).toBeNull()
    for (const field of Object.keys(stored)) {
      expect(read(Object.fromEntries(Object.entries(stored).filter(([name]) => name !== field)))).toBeNull()
    }
    expect(read({ ...stored, scope: { ...scope, extra: 'SENTETIK' } })).toBeNull()
    expect(read({ ...stored, scope: { companyId: scope.companyId, salesOrderId: scope.salesOrderId, kind: scope.kind } })).toBeNull()
    expect(read([stored])).toBeNull()
    expect(read(stored)).toEqual(begun)
  })
})
