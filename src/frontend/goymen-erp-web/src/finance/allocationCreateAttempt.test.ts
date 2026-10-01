import { afterEach, describe, expect, it } from 'vitest'
import type { FinanceAllocationWireSnapshot } from './allocationHttp'
import {
  allocationCreateAttemptSlot,
  clearAllocationCreateAttemptStore,
  finishAllocationCreateAttempt,
  markAllocationCreateAttemptUnknown,
  recallAllocationCreateAttempt,
  recoverAllocationCreateAttempt,
  reserveAllocationCreateAttempt,
  resendAllocationCreateAttempt,
} from './allocationCreateAttempt'

const binding = {
  sessionEpoch: 7,
  actorId: 'finance-user',
  companyId: '11111111-1111-4111-8111-111111111111',
}

function snapshot(key = 'create-key'): FinanceAllocationWireSnapshot {
  return {
    key,
    binding,
    request: {
      operation: 'allocation-create',
      body: {
        allocationId: '22222222-2222-4222-8222-222222222222',
        sourceId: '33333333-3333-4333-8333-333333333333',
        expectedSourceVersion: '2',
        openItemId: '44444444-4444-4444-8444-444444444444',
        expectedOpenItemVersion: '3',
        amount: '432.10',
        reason: 'Fatura kapama',
      },
    },
  }
}

afterEach(clearAllocationCreateAttemptStore)

describe('allocationCreateAttempt', () => {
  it('reserves synchronously and refuses a replacement identity while unresolved', () => {
    const slot = allocationCreateAttemptSlot(binding)
    const first = reserveAllocationCreateAttempt(slot, snapshot())
    expect(first?.phase).toBe('submitting')
    expect(reserveAllocationCreateAttempt(slot, snapshot('replacement-key'))).toBeNull()
    expect(recallAllocationCreateAttempt(slot)?.snapshot).toEqual(first?.snapshot)
  })

  it('recovers an orphaned submission and resends the exact same id key and body', () => {
    const slot = allocationCreateAttemptSlot(binding)
    const original = reserveAllocationCreateAttempt(slot, snapshot())!
    const recovered = recoverAllocationCreateAttempt(slot)
    expect(recovered).toEqual({ phase: 'unknown', snapshot: original.snapshot })
    expect(resendAllocationCreateAttempt(slot, original.snapshot.key)).toEqual({
      phase: 'submitting',
      snapshot: original.snapshot,
    })
  })

  it('keeps attempts isolated by company actor and session namespace', () => {
    const firstSlot = allocationCreateAttemptSlot(binding)
    const nextSlot = allocationCreateAttemptSlot({ ...binding, sessionEpoch: 8 })
    reserveAllocationCreateAttempt(firstSlot, snapshot())
    expect(recallAllocationCreateAttempt(nextSlot)).toBeNull()
    expect(recallAllocationCreateAttempt(firstSlot)?.snapshot.key).toBe('create-key')
  })

  it('clears only after a terminal result with the exact key', () => {
    const slot = allocationCreateAttemptSlot(binding)
    reserveAllocationCreateAttempt(slot, snapshot())
    markAllocationCreateAttemptUnknown(slot, 'create-key')
    finishAllocationCreateAttempt(slot, 'other-key')
    expect(recallAllocationCreateAttempt(slot)?.phase).toBe('unknown')
    finishAllocationCreateAttempt(slot, 'create-key')
    expect(recallAllocationCreateAttempt(slot)).toBeNull()
  })
})
