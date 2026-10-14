import { beforeEach, describe, expect, it } from 'vitest'
import {
  pendingPlanCreateKey,
  recallPendingPlanCreate,
  reservePendingPlanCreate,
  type PendingPlanCreate,
} from './pendingPlanStore'
import { COMPANY_A, PLAN, ROLL_1, WORK_CENTER } from './testHarness'

const KEY = pendingPlanCreateKey(0, 'actor-a', COMPANY_A)

const CUT_LINES = [
  { lineId: 'c0000000-0000-4000-8000-0000000000bb', sequence: 40, lengthMm: '6000.000', count: 3 },
  { lineId: 'c0000000-0000-4000-8000-0000000000aa', sequence: -7, lengthMm: '2500.5', count: 2 },
]

function cutSnapshot(cutToLengthSpec: unknown): PendingPlanCreate {
  const base = snapshot()
  return { ...base, request: { ...base.request, cutToLengthSpec } as PendingPlanCreate['request'] }
}

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

describe('pending cut-to-length plan create (M01.3)', () => {
  it('recalls the frozen ordered cut lines byte-for-byte, mints no line id and cannot be mutated afterwards', () => {
    const frozen = cutSnapshot({ lines: CUT_LINES })
    reservePendingPlanCreate(KEY, frozen)

    const recalled = recallPendingPlanCreate(KEY)

    expect(recalled).toEqual(frozen)
    expect(JSON.stringify(recalled?.request)).toBe(JSON.stringify(frozen.request))
    expect(recalled?.request.cutToLengthSpec?.lines.map((line) => line.lineId)).toEqual(CUT_LINES.map((line) => line.lineId))
    // Frozen: a later UI change cannot reach the recalled attempt.
    expect(Object.isFrozen(recalled)).toBe(true)
    expect(Object.isFrozen(recalled?.request)).toBe(true)
    expect(Object.isFrozen(recalled?.request.cutToLengthSpec?.lines)).toBe(true)
    expect(Object.isFrozen(recalled?.request.cutToLengthSpec?.lines[0])).toBe(true)
  })

  it('refuses a malformed, cleared or out-of-contract cut definition instead of replaying it', () => {
    const invalid: unknown[] = [
      null,
      { lines: [] },
      { lines: CUT_LINES, extra: 1 },
      { lines: [{ ...CUT_LINES[0], extra: 1 }] },
      { lines: [{ ...CUT_LINES[0], lineId: '00000000-0000-0000-0000-000000000000' }] },
      { lines: [{ ...CUT_LINES[0], lineId: 'not-a-guid' }] },
      { lines: [CUT_LINES[0], { ...CUT_LINES[1], lineId: CUT_LINES[0].lineId.toUpperCase() }] },
      { lines: [{ ...CUT_LINES[0], sequence: 1.5 }] },
      { lines: [{ ...CUT_LINES[0], sequence: 2_147_483_648 }] },
      { lines: [{ ...CUT_LINES[0], sequence: '40' }] },
      { lines: [{ ...CUT_LINES[0], lengthMm: '6000.0000' }] },
      { lines: [{ ...CUT_LINES[0], lengthMm: '0' }] },
      { lines: [{ ...CUT_LINES[0], lengthMm: 6000 }] },
      { lines: [{ ...CUT_LINES[0], count: 0 }] },
      { lines: [{ ...CUT_LINES[0], count: 2_147_483_648 }] },
    ]
    for (const cutToLengthSpec of invalid) {
      sessionStorage.setItem(KEY!, JSON.stringify(cutSnapshot(cutToLengthSpec)))
      expect(recallPendingPlanCreate(KEY), JSON.stringify(cutToLengthSpec)).toBeNull()
    }
    // A legacy snapshot without the member stays valid and gains no definition.
    reservePendingPlanCreate(KEY, snapshot())
    expect(recallPendingPlanCreate(KEY)?.request).not.toHaveProperty('cutToLengthSpec')
  })
})

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
