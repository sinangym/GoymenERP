import { beforeEach, describe, expect, it } from 'vitest'
import {
  pendingPlanCreateKey,
  recallPendingPlanCreate,
  reservePendingPlanCreate,
  type PendingPlanCreate,
} from './pendingPlanStore'
import { COMPANY_A, PLAN, ROLL_1, WORK_CENTER } from './testHarness'

const KEY = pendingPlanCreateKey(0, 'actor-a', COMPANY_A)

function snapshot(slittingSpec: PendingPlanCreate['request']['slittingSpec'] = undefined): PendingPlanCreate {
  return {
    actorId: 'actor-a',
    companyId: COMPANY_A,
    idempotencyKey: '10000000-0000-4000-8000-000000000001',
    request: {
      planId: PLAN,
      workCenterId: WORK_CENTER,
      inputs: [{ parentRollId: ROLL_1, plannedKg: '900.50', expectedLabelVersion: '7' }],
      target: null,
      ...(slittingSpec === undefined ? {} : { slittingSpec }),
    },
  }
}

beforeEach(() => sessionStorage.clear())

describe('pending slitting plan create', () => {
  it('recalls the frozen ordered spec, idempotency key and body byte-for-byte after a reload boundary', () => {
    const frozen = snapshot({
      lines: [{ bandWidthMm: '150.000', count: 4 }, { bandWidthMm: '240.50', count: 3 }],
      windingTargetKg: '2000.00',
    })
    reservePendingPlanCreate(KEY, frozen)

    const recalled = recallPendingPlanCreate(KEY)

    expect(recalled).toEqual(frozen)
    expect(JSON.stringify(recalled?.request)).toBe(JSON.stringify(frozen.request))
    expect(recalled?.idempotencyKey).toBe(frozen.idempotencyKey)
  })

  it('preserves legacy omitted/null snapshots and refuses malformed or out-of-range slitting content', () => {
    reservePendingPlanCreate(KEY, snapshot())
    expect(recallPendingPlanCreate(KEY)?.request.slittingSpec).toBeUndefined()
    reservePendingPlanCreate(KEY, snapshot(null))
    expect(recallPendingPlanCreate(KEY)?.request.slittingSpec).toBeNull()

    const invalid = [
      { lines: [], windingTargetKg: null },
      { lines: [{ bandWidthMm: '150.0001', count: 1 }], windingTargetKg: null },
      { lines: [{ bandWidthMm: '150', count: 0 }], windingTargetKg: null },
      { lines: [{ bandWidthMm: '150', count: 2_147_483_648 }], windingTargetKg: null },
      { lines: [{ bandWidthMm: '150', count: 1 }], windingTargetKg: '0' },
    ] as const
    for (const slittingSpec of invalid) {
      sessionStorage.setItem(KEY!, JSON.stringify(snapshot(slittingSpec)))
      expect(recallPendingPlanCreate(KEY)).toBeNull()
    }
  })
})
