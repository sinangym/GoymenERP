import { describe, expect, it } from 'vitest'
import { decodePlanPriority, decodePlanQueue } from './planQueueApi'
const companyId = '10000000-0000-0000-0000-000000000001'
const planId = '20000000-0000-0000-0000-000000000002'
const without = (value: object, key: string) => Object.fromEntries(Object.entries(value).filter(([name]) => name !== key))
const priority = { companyId, planId, planVersion: 3, priorityScore: 0, priorityRevision: 0, recordedBy: null, recordedAtUtc: null, reason: null }
const line = { planId, dueDate: '2026-11-30', priorityScore: 0, priorityRevision: 0, plannedKg: '100', processedKg: '0', remainingKg: '100', isOverPlanned: false, isQueued: true }
const queue = { contractVersion: 1, companyId, workCenterId: '30000000-0000-0000-0000-000000000003', targetPlanId: planId, targetPlanVersion: 3,
  items: [line], queueRevision: 0, hasManualOrder: false }
describe('plan priority and queue exact key set (SENTETIK)', () => {
  it('rejects an extra or a missing key on the priority view', () => {
    expect(decodePlanPriority(priority)).toEqual(priority)
    expect(decodePlanPriority({ ...priority, extra: 1 })).toBeNull()
    for (const key of Object.keys(priority)) expect(decodePlanPriority(without(priority, key))).toBeNull()
  })
  it('rejects an extra or a missing key on the queue and on every queue line', () => {
    expect(decodePlanQueue(queue)).toEqual(queue)
    expect(decodePlanQueue({ ...queue, extra: 1 })).toBeNull()
    for (const key of Object.keys(queue)) expect(decodePlanQueue(without(queue, key))).toBeNull()
    expect(decodePlanQueue({ ...queue, items: [{ ...line, extra: 1 }] })).toBeNull()
    for (const key of Object.keys(line)) expect(decodePlanQueue({ ...queue, items: [without(line, key)] })).toBeNull()
  })
})
