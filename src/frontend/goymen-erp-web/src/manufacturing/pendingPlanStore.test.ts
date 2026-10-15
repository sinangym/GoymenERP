import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  pendingPlanCreateKey,
  readPendingPlanCreate,
  releasePendingPlanCreate,
  reservePendingPlanCreate,
  type PendingPlanCreate,
} from './pendingPlanStore'
import { COMPANY_A, COMPANY_B, PLAN, ROLL_1, WORK_CENTER } from './testHarness'

/** Controlled server `authenticationSessionBinding` values (M01.3 r5, #66/6187473245); the epoch is no part of a stored slot. */
const BINDING = `v1.${'1'.repeat(64)}`
const OTHER_BINDING = `v1.${'2'.repeat(64)}`
const KEY = pendingPlanCreateKey(BINDING, 'actor-a', COMPANY_A)
/** Where the store keeps this actor's and company's create attempt. */
const STORED = `goymen.manufacturing.plan-create.v2:${JSON.stringify(['actor-a', COMPANY_A])}`

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

function withKey(value: PendingPlanCreate, idempotencyKey: string): PendingPlanCreate {
  return { ...value, idempotencyKey }
}

/** What the slot holds for an attempt: the attempt and the binding it was stored under. */
function record(value: PendingPlanCreate, sessionBinding: unknown = BINDING): string {
  return JSON.stringify({ ...value, sessionBinding })
}

function recalled(key: string | null = KEY): PendingPlanCreate | null {
  const slot = readPendingPlanCreate(key)
  return slot.state === 'pending' ? slot.pending : null
}

beforeEach(() => sessionStorage.clear())
afterEach(() => vi.restoreAllMocks())

describe('pending cut-to-length plan create (M01.3)', () => {
  it('recalls the frozen ordered cut lines byte-for-byte, mints no line id and cannot be mutated afterwards', () => {
    const frozen = cutSnapshot({ lines: CUT_LINES })
    expect(reservePendingPlanCreate(KEY, frozen)).toEqual({ type: 'reserved' })

    const value = recalled()

    expect(value).toEqual(frozen)
    expect(JSON.stringify(value?.request)).toBe(JSON.stringify(frozen.request))
    expect(value?.request.cutToLengthSpec?.lines.map((line) => line.lineId)).toEqual(CUT_LINES.map((line) => line.lineId))
    // Frozen: a later UI change cannot reach the recalled attempt.
    expect(Object.isFrozen(value)).toBe(true)
    expect(Object.isFrozen(value?.request)).toBe(true)
    expect(Object.isFrozen(value?.request.cutToLengthSpec?.lines)).toBe(true)
    expect(Object.isFrozen(value?.request.cutToLengthSpec?.lines[0])).toBe(true)
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
      sessionStorage.setItem(STORED, record(cutSnapshot(cutToLengthSpec)))
      // Never replayed — and never "no pending" either.
      expect(readPendingPlanCreate(KEY), JSON.stringify(cutToLengthSpec)).toEqual({ state: 'unreadable' })
    }
    // A legacy snapshot without the member stays valid and gains no definition.
    sessionStorage.clear()
    reservePendingPlanCreate(KEY, snapshot())
    expect(recalled()?.request).not.toHaveProperty('cutToLengthSpec')
  })
})

describe('pending slitting plan create', () => {
  it('recalls the frozen ordered spec, idempotency key and body byte-for-byte after a reload boundary', () => {
    const frozen = snapshot({
      lines: [{ bandWidthMm: '150.000', count: 4 }, { bandWidthMm: '240.50', count: 3 }],
      windingTargetKg: '2000.00',
    })
    reservePendingPlanCreate(KEY, frozen)

    const value = recalled()

    expect(value).toEqual(frozen)
    expect(JSON.stringify(value?.request)).toBe(JSON.stringify(frozen.request))
    expect(value?.idempotencyKey).toBe(frozen.idempotencyKey)
  })

  it('preserves legacy omitted/null snapshots and refuses malformed or out-of-range slitting content', () => {
    reservePendingPlanCreate(KEY, snapshot())
    expect(recalled()?.request.slittingSpec).toBeUndefined()
    sessionStorage.clear()
    reservePendingPlanCreate(KEY, snapshot(null))
    expect(recalled()?.request.slittingSpec).toBeNull()

    const invalid = [
      { lines: [], windingTargetKg: null },
      { lines: [{ bandWidthMm: '150.0001', count: 1 }], windingTargetKg: null },
      { lines: [{ bandWidthMm: '150', count: 0 }], windingTargetKg: null },
      { lines: [{ bandWidthMm: '150', count: 2_147_483_648 }], windingTargetKg: null },
      { lines: [{ bandWidthMm: '150', count: 1 }], windingTargetKg: '0' },
    ] as const
    for (const slittingSpec of invalid) {
      sessionStorage.setItem(STORED, record(snapshot(slittingSpec)))
      expect(readPendingPlanCreate(KEY)).toEqual({ state: 'unreadable' })
    }
  })
})

/**
 * M01.3 r5 (#66/6187473245): the create slot of an actor and company is bound to the server's session binding, not to the
 * document-local epoch. Only a proven-empty slot is absent; everything else keeps the create form closed.
 */
describe('pending plan create slot', () => {
  it('a pending create of an earlier session is not absent in a later one: no replay, no replacement key', () => {
    reservePendingPlanCreate(KEY, snapshot())
    const raw = sessionStorage.getItem(STORED)
    const next = pendingPlanCreateKey(OTHER_BINDING, 'actor-a', COMPANY_A)
    expect(readPendingPlanCreate(next)).toEqual({ state: 'unreadable' })
    expect(reservePendingPlanCreate(next, snapshot())).toEqual({ type: 'unreadable' })
    expect(reservePendingPlanCreate(next, withKey(snapshot(), '10000000-0000-4000-8000-0000000000ff')))
      .toEqual({ type: 'unreadable' })
    releasePendingPlanCreate(next, snapshot().idempotencyKey)
    expect(sessionStorage.getItem(STORED)).toBe(raw)
  })

  it.each([
    ['not JSON', () => '{not json'],
    ['another actor', () => record({ ...snapshot(), actorId: 'actor-b' })],
    ['another company', () => record({ ...snapshot(), companyId: COMPANY_B })],
    ['no binding', () => JSON.stringify(snapshot())],
    ['a null binding', () => record(snapshot(), null)],
    ['an epoch for a binding', () => record(snapshot(), 0)],
    ['an extra field', () => JSON.stringify({ ...snapshot(), sessionBinding: BINDING, extra: 1 })],
  ])('a stored record with %s is unreadable, kept, and lets nothing in', (_case, value) => {
    const raw = value()
    sessionStorage.setItem(STORED, raw)
    expect(readPendingPlanCreate(KEY)).toEqual({ state: 'unreadable' })
    expect(reservePendingPlanCreate(KEY, withKey(snapshot(), '10000000-0000-4000-8000-0000000000fe')))
      .toEqual({ type: 'unreadable' })
    releasePendingPlanCreate(KEY, snapshot().idempotencyKey)
    expect(sessionStorage.getItem(STORED)).toBe(raw)
  })

  it('a legacy epoch-keyed create of the same actor and company is not absence; another company is unaffected', () => {
    const legacy = `goymen.manufacturing.plan-create.v1:2:actor-a:${COMPANY_A}`
    sessionStorage.setItem(legacy, JSON.stringify(snapshot()))
    expect(readPendingPlanCreate(KEY)).toEqual({ state: 'unreadable' })
    expect(reservePendingPlanCreate(KEY, snapshot())).toEqual({ type: 'unreadable' })
    expect(sessionStorage.getItem(legacy)).toBe(JSON.stringify(snapshot()))
    expect(readPendingPlanCreate(pendingPlanCreateKey(BINDING, 'actor-a', COMPANY_B))).toEqual({ state: 'absent' })
  })

  it('without a session binding the slot is unreadable, and nothing is stored or sent', () => {
    const unbound = pendingPlanCreateKey(null, 'actor-a', COMPANY_A)
    expect(unbound).not.toBeNull()
    expect(readPendingPlanCreate(unbound)).toEqual({ state: 'unreadable' })
    expect(reservePendingPlanCreate(unbound, snapshot())).toEqual({ type: 'unreadable' })
    expect(sessionStorage.length).toBe(0)
    expect(pendingPlanCreateKey(BINDING, null, COMPANY_A)).toBeNull()
    expect(pendingPlanCreateKey(BINDING, 'actor-a', null)).toBeNull()
  })

  it('an unavailable store is unreadable, never absent', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('denied') })
    expect(readPendingPlanCreate(KEY)).toEqual({ state: 'unreadable' })
    expect(reservePendingPlanCreate(KEY, snapshot())).toEqual({ type: 'unreadable' })
  })

  it('a create that cannot be stored and read back is not reserved, so it is never sent', () => {
    const setItem = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('quota') })
    expect(reservePendingPlanCreate(KEY, snapshot())).toEqual({ type: 'not-durable' })
    setItem.mockImplementation(() => undefined)
    expect(reservePendingPlanCreate(KEY, snapshot())).toEqual({ type: 'not-durable' })
    setItem.mockRestore()
    expect(readPendingPlanCreate(KEY)).toEqual({ state: 'absent' })
  })

  it('an unresolved create is never displaced; only its exact retry is reserved', () => {
    const held = snapshot()
    reservePendingPlanCreate(KEY, held)
    const raw = sessionStorage.getItem(STORED)
    for (const other of [
      withKey(held, '10000000-0000-4000-8000-000000000002'),
      { ...held, request: { ...held.request, target: { basis: 'InputMass' as const, kg: '1' } } },
    ]) {
      expect(reservePendingPlanCreate(KEY, other)).toEqual({ type: 'occupied', pending: held })
    }
    expect(sessionStorage.getItem(STORED)).toBe(raw)
    expect(reservePendingPlanCreate(KEY, held)).toEqual({ type: 'reserved' })
    expect(sessionStorage.getItem(STORED)).toBe(raw)
  })

  it('releases only the exact settled attempt; a stale answer cannot remove a newer one', () => {
    const older = snapshot()
    const newer = withKey(older, '10000000-0000-4000-8000-000000000002')
    reservePendingPlanCreate(KEY, older)
    releasePendingPlanCreate(KEY, older.idempotencyKey)
    expect(readPendingPlanCreate(KEY)).toEqual({ state: 'absent' })
    reservePendingPlanCreate(KEY, newer)
    releasePendingPlanCreate(KEY, older.idempotencyKey)
    expect(recalled()).toEqual(newer)
  })

  it('a release whose removal fails keeps the same attempt pending', () => {
    reservePendingPlanCreate(KEY, snapshot())
    vi.spyOn(Storage.prototype, 'removeItem').mockImplementation(() => { throw new Error('denied') })
    releasePendingPlanCreate(KEY, snapshot().idempotencyKey)
    expect(recalled()).toEqual(snapshot())
  })
})
