import { afterEach, describe, expect, it, vi } from 'vitest'
import { savePlan, type SavePlanRequest } from './planCommandApi'
import {
  COMPANY_A,
  PLAN,
  ROLL_1,
  ROLL_2,
  WORK_CENTER,
  json,
  plan,
  problem,
} from './testHarness'

const REQUEST: SavePlanRequest = {
  planId: PLAN,
  workCenterId: WORK_CENTER,
  inputs: [
    { parentRollId: ROLL_1, plannedKg: '1000.125', expectedLabelVersion: '7' },
    { parentRollId: ROLL_2, plannedKg: '500.5', expectedLabelVersion: '12' },
  ],
  target: { basis: 'GoodOutputMass', kg: '900' },
}

const CONTEXT = { companyId: COMPANY_A, idempotencyKey: 'plan-key-1' }

function answer(response: Response | (() => never)) {
  const fetchMock = vi.fn(async () => (typeof response === 'function' ? response() : response))
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function created(headers: Record<string, string> = {}) {
  return json(plan({ target: REQUEST.target, slittingSpec: REQUEST.slittingSpec ?? null }), 201, {
    Location: `/api/v1/manufacturing/plans/${PLAN}`,
    ...headers,
  })
}

afterEach(() => vi.unstubAllGlobals())

describe('savePlan', () => {
  it('posts the frozen label versions, kilograms and target with the caller-owned key', async () => {
    const fetchMock = answer(created())

    const outcome = await savePlan(REQUEST, CONTEXT)

    expect(outcome).toMatchObject({ type: 'applied', replayed: false, originalCorrelationId: null })
    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit]
    expect(url).toBe('/api/v1/manufacturing/plans')
    expect(init).toMatchObject({ method: 'POST' })
    expect(init.headers).toMatchObject({
      'X-Company-Id': COMPANY_A,
      'Idempotency-Key': 'plan-key-1',
      'Content-Type': 'application/json',
    })
    expect(JSON.parse(init.body as string)).toEqual(REQUEST)
  })

  it('accepts the stored 201 only when replay and original-correlation headers agree', async () => {
    answer(created({ 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'corr-1' }))
    expect(await savePlan(REQUEST, CONTEXT)).toMatchObject({
      type: 'applied',
      replayed: true,
      originalCorrelationId: 'corr-1',
    })

    answer(created({ 'Idempotency-Replayed': 'true' }))
    expect(await savePlan(REQUEST, CONTEXT)).toEqual({ type: 'unknown', reason: 'undecodable' })
  })

  it('accepts the server canonical decimal text for the exact values that were sent', async () => {
    const request = {
      ...REQUEST,
      inputs: [
        { ...REQUEST.inputs[0], plannedKg: '1000.1250' },
        { ...REQUEST.inputs[1], plannedKg: '500.500' },
      ],
      target: { basis: 'GoodOutputMass' as const, kg: '900.00' },
    }
    answer(created())

    expect(await savePlan(request, CONTEXT)).toMatchObject({ type: 'applied', replayed: false })
  })

  it('binds a committed slitting response to the exact ordered request and accepts canonical decimal text', async () => {
    const request: SavePlanRequest = {
      ...REQUEST,
      slittingSpec: {
        lines: [{ bandWidthMm: '150.000', count: 4 }, { bandWidthMm: '240.50', count: 3 }],
        windingTargetKg: '2000.00',
      },
    }
    answer(json(plan({
      target: REQUEST.target,
      slittingSpec: {
        lines: [{ bandWidthMm: '150', count: 4 }, { bandWidthMm: '240.5', count: 3 }],
        windingTargetKg: '2000',
      },
    }), 201, { Location: `/api/v1/manufacturing/plans/${PLAN}` }))

    expect(await savePlan(request, CONTEXT)).toMatchObject({ type: 'applied' })

    answer(json(plan({
      target: REQUEST.target,
      slittingSpec: {
        lines: [{ bandWidthMm: '240.5', count: 3 }, { bandWidthMm: '150', count: 4 }],
        windingTargetKg: '2000',
      },
    }), 201, { Location: `/api/v1/manufacturing/plans/${PLAN}` }))
    expect(await savePlan(request, CONTEXT)).toEqual({ type: 'unknown', reason: 'undecodable' })
  })

  it('M01.3: sends the authored cut lines and trusts a 201 only for the exact ordered definition', async () => {
    const lines = [
      { lineId: 'c0000000-0000-4000-8000-0000000000bb', sequence: 40, lengthMm: '6000.000', count: 3 },
      { lineId: 'c0000000-0000-4000-8000-0000000000aa', sequence: -7, lengthMm: '2500.5', count: 2 },
    ]
    const request: SavePlanRequest = { ...REQUEST, cutToLengthSpec: { lines } }
    const stored = lines.map((line) => ({ ...line, lengthMm: line.lengthMm === '6000.000' ? '6000' : line.lengthMm }))
    const committed = (answered: unknown) => json(
      plan({ process: 'CutToLength', target: REQUEST.target, cutToLengthSpec: answered }),
      201,
      { Location: `/api/v1/manufacturing/plans/${PLAN}` },
    )

    const fetchMock = answer(committed({ lines: stored }))
    expect(await savePlan(request, CONTEXT)).toMatchObject({ type: 'applied' })
    const [, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit]
    expect(JSON.parse(init.body as string).cutToLengthSpec).toEqual({ lines })

    // The server's GUID text may differ in case only.
    answer(committed({ lines: stored.map((line) => ({ ...line, lineId: line.lineId.toUpperCase() })) }))
    expect(await savePlan(request, CONTEXT)).toMatchObject({ type: 'applied' })

    for (const answered of [
      null,
      { lines: [...stored].reverse() },
      { lines: [stored[0]] },
      { lines: stored.map((line, index) => ({ ...line, sequence: index + 1 })) },
      { lines: [stored[0], { ...stored[1], lineId: 'c0000000-0000-4000-8000-0000000000cc' }] },
      { lines: [stored[0], { ...stored[1], lengthMm: '2500.6' }] },
      { lines: [stored[0], { ...stored[1], count: 1 }] },
    ]) {
      answer(committed(answered))
      expect(await savePlan(request, CONTEXT), JSON.stringify(answered)).toEqual({ type: 'unknown', reason: 'undecodable' })
    }

    // A request without cut lines is never confirmed by an answer that carries some.
    answer(committed({ lines: stored }))
    expect(await savePlan(REQUEST, CONTEXT)).toEqual({ type: 'unknown', reason: 'undecodable' })
  })

  it.each([
    ['another company', plan({ companyId: 'bbbbbbbb-0000-0000-0000-000000000002', target: REQUEST.target })],
    ['another plan', plan({ planId: 'd0000000-0000-4000-8000-000000000099', target: REQUEST.target })],
    ['another work center', plan({ workCenterId: 'd0000000-0000-4000-8000-000000000099', target: REQUEST.target })],
    ['another target', plan({ target: { basis: 'InputMass', kg: '900' } })],
  ])('keeps a 201 that describes %s unknown', async (_name, body) => {
    answer(json(body, 201, { Location: `/api/v1/manufacturing/plans/${PLAN}` }))
    expect(await savePlan(REQUEST, CONTEXT)).toEqual({ type: 'unknown', reason: 'undecodable' })
  })

  it('marks only the endpoint’s stored terminal failures as verified replays', async () => {
    answer(problem(404, {}, { 'Idempotency-Replayed': 'true' }))
    expect(await savePlan(REQUEST, CONTEXT)).toMatchObject({ type: 'failed', status: 404, replayed: true })

    answer(problem(409, { code: 'inventory_label_version_conflict' }, { 'Idempotency-Replayed': 'true' }))
    expect(await savePlan(REQUEST, CONTEXT)).toMatchObject({
      type: 'failed',
      status: 409,
      code: 'inventory_label_version_conflict',
      replayed: true,
    })

    answer(problem(409, { code: 'idempotency_conflict' }, { 'Idempotency-Replayed': 'true' }))
    expect(await savePlan(REQUEST, CONTEXT)).toMatchObject({ type: 'failed', replayed: false })
  })

  it.each([
    [problem(400, { code: 'manufacturing_request_invalid', field: 'inputs[0].expectedLabelVersion' }), 400],
    [problem(403, { code: 'forbidden' }), 403],
    [problem(422, { code: 'inventory_parent_roll_context_required' }), 422],
  ])('returns a contract refusal without guessing (%#)', async (response, status) => {
    answer(response)
    expect(await savePlan(REQUEST, CONTEXT)).toMatchObject({ type: 'failed', status, replayed: false })
  })

  it('keeps transport, 5xx, malformed and unexpected success answers unknown', async () => {
    answer(() => {
      throw new TypeError('failed to fetch')
    })
    expect(await savePlan(REQUEST, CONTEXT)).toMatchObject({ type: 'unknown' })

    answer(problem(503, { code: 'service_unavailable' }))
    expect(await savePlan(REQUEST, CONTEXT)).toEqual({ type: 'unknown', reason: 'service-unavailable' })

    answer(problem(409))
    expect(await savePlan(REQUEST, CONTEXT)).toEqual({ type: 'unknown', reason: 'undecodable' })

    answer(json({}, 200))
    expect(await savePlan(REQUEST, CONTEXT)).toEqual({ type: 'unknown', reason: 'unexpected-status' })
  })
})

/**
 * M01.3 K2 P1: an unresolved revise survives a REAL page reload. A reload recreates the JavaScript module (its
 * in-memory slot map is gone) while the tab's sessionStorage stays: `vi.resetModules()` + a fresh dynamic import is
 * that boundary here. The real F5 is the joined MFG browser run's evidence, not this test's.
 */
describe('pending revise across a module re-creation (reload boundary)', () => {
  const ACTOR = '99999999-9999-9999-9999-999999999999'
  const REVISE_KEY = '30000000-0000-4000-8000-000000000001'
  /** The shell's session binding (M01.3 K2 #728/6180459736); the epoch is no part of a stored slot any more. */
  const BINDING = '50000000-0000-4000-8000-000000000001'
  const OTHER_BINDING = '50000000-0000-4000-8000-000000000002'
  const REVISE_PREFIX = 'goymen.manufacturing.plan-revise.v2:'
  const LEGACY_REVISE_PREFIX = 'goymen.manufacturing.plan-revise.v1:'
  const CUT_LINES = [
    { lineId: 'c0000000-0000-4000-8000-0000000000bb', sequence: 40, lengthMm: '6000.000', count: 3 },
    { lineId: 'c0000000-0000-4000-8000-0000000000aa', sequence: -7, lengthMm: '2500.50', count: 2 },
  ]
  type Api = typeof import('./planCommandApi')
  type Pending = import('./planCommandApi').PendingPlanLifecycle

  function revise(overrides: Record<string, unknown> = {}, cut: unknown = { lines: CUT_LINES }): Pending {
    return {
      actorId: ACTOR,
      companyId: COMPANY_A,
      planId: PLAN,
      snapshot: {
        kind: 'revise',
        idempotencyKey: REVISE_KEY,
        request: {
          expectedVersion: 3,
          workCenterId: WORK_CENTER,
          inputs: [
            {
              parentRollId: ROLL_1,
              plannedKg: '900.50',
              expectedLabelVersion: '7',
              commercialContext: {
                partyId: 'A1000000-0000-4000-8000-000000000001',
                companyPartyAccountId: 'a1000000-0000-4000-8000-000000000002',
                partyVersion: ' party:v01 ',
                accountVersion: 'account:v02/α',
              },
            },
            { parentRollId: ROLL_2, plannedKg: '500.5', expectedLabelVersion: '12', commercialContext: null },
          ],
          target: { basis: 'GoodOutputMass', kg: '850.0' },
          reason: 'Kesim satırları güncellendi',
          ...(cut === 'omit' ? {} : { cutToLengthSpec: cut }),
          ...overrides,
        } as Extract<Pending['snapshot'], { kind: 'revise' }>['request'],
      },
    }
  }

  async function reloaded(): Promise<Api> {
    vi.resetModules()
    return import('./planCommandApi')
  }

  function sentBodies(fetchMock: ReturnType<typeof vi.fn>) {
    return fetchMock.mock.calls.map((call) => {
      const init = (call as unknown as [string, RequestInit])[1]
      return { body: init.body as string, key: (init.headers as Record<string, string>)['Idempotency-Key'] }
    })
  }

  afterEach(() => sessionStorage.clear())

  function slotName(_key: string | null): string {
    // The exact slot the store itself writes (each case stores one).
    const names = Object.keys(sessionStorage).filter((candidate) => candidate.startsWith(REVISE_PREFIX))
    if (names.length !== 1) throw new Error('no single stored slot')
    return names[0]
  }

  /** What the slot holds for an attempt: the attempt and the binding it was stored under. */
  function record(pending: Pending, sessionBinding: unknown = BINDING): Record<string, unknown> {
    return { ...pending, sessionBinding }
  }

  function approve(idempotencyKey = '30000000-0000-4000-8000-000000000003'): Pending {
    return {
      actorId: ACTOR, companyId: COMPANY_A, planId: PLAN,
      snapshot: { kind: 'approve', idempotencyKey, expectedVersion: 3 },
    }
  }

  function withKey(pending: Pending, idempotencyKey: string): Pending {
    return { ...pending, snapshot: { ...pending.snapshot, idempotencyKey } } as Pending
  }

  it.each([
    ['ordered cut lines', { lines: CUT_LINES }],
    ['the legacy request without cut lines', undefined],
  ])('restores %s after the reload and re-sends the same key and byte-equal body', async (_case, cut) => {
    const before = await reloaded()
    const key = before.pendingPlanLifecycleKey(BINDING, ACTOR, COMPANY_A, PLAN)
    const pending = revise({}, cut ?? 'omit')
    expect(before.reservePendingPlanLifecycle(key, pending)).toEqual({ type: 'reserved' })
    const lost = vi.fn(async () => { throw new TypeError('connection lost') })
    vi.stubGlobal('fetch', lost)
    const first = await before.revisePlan(COMPANY_A, PLAN, pending.snapshot as Extract<Pending['snapshot'], { kind: 'revise' }>)
    expect(first.type).toBe('unknown')

    const after = await reloaded()
    const slot = after.readPendingPlanLifecycle(after.pendingPlanLifecycleKey(BINDING, ACTOR, COMPANY_A, PLAN))
    expect(slot.state).toBe('pending')
    const recalled = slot.state === 'pending' ? slot.pending : null
    expect(recalled).toEqual(pending)
    expect(JSON.stringify(recalled)).toBe(JSON.stringify(pending))
    // Deep-frozen: nothing typed after the reload can reach the frozen attempt.
    expect(Object.isFrozen(recalled)).toBe(true)
    const request = (recalled!.snapshot as Extract<Pending['snapshot'], { kind: 'revise' }>).request
    expect(Object.isFrozen(request.inputs[0])).toBe(true)
    if (cut !== undefined) expect(Object.isFrozen(request.cutToLengthSpec?.lines[1])).toBe(true)
    else expect(request).not.toHaveProperty('cutToLengthSpec')
    // The exact retry re-reserves the very same attempt.
    expect(after.reservePendingPlanLifecycle(key, recalled!)).toEqual({ type: 'reserved' })

    const replay = vi.fn(async () => { throw new TypeError('still offline') })
    vi.stubGlobal('fetch', replay)
    await after.revisePlan(COMPANY_A, PLAN, recalled!.snapshot as Extract<Pending['snapshot'], { kind: 'revise' }>)
    expect(sentBodies(replay)).toEqual(sentBodies(lost))
    expect(sentBodies(replay)[0].key).toBe(REVISE_KEY)
  })

  it('a pending revise of an earlier session is never absent in a later one, from memory or after the reload', async () => {
    const api = await reloaded()
    const old = api.pendingPlanLifecycleKey(BINDING, ACTOR, COMPANY_A, PLAN)
    expect(api.reservePendingPlanLifecycle(old, revise())).toEqual({ type: 'reserved' })
    const raw = sessionStorage.getItem(slotName(old))
    // A real session boundary in the same document: the attempt is still held in memory under the old binding.
    const next = api.pendingPlanLifecycleKey(OTHER_BINDING, ACTOR, COMPANY_A, PLAN)
    for (const fresh of [api, await reloaded()]) {
      expect(fresh.readPendingPlanLifecycle(next)).toEqual({ state: 'unreadable' })
      // Neither its replay nor a replacement key gets in, and the record is neither deleted nor rebound.
      expect(fresh.reservePendingPlanLifecycle(next, revise())).toEqual({ type: 'unreadable' })
      expect(fresh.reservePendingPlanLifecycle(next, withKey(revise(), '30000000-0000-4000-8000-0000000000fe')))
        .toEqual({ type: 'unreadable' })
      expect(fresh.reservePendingPlanLifecycle(next, approve())).toEqual({ type: 'unreadable' })
      fresh.releasePendingPlanLifecycle(next, REVISE_KEY)
      expect(sessionStorage.getItem(slotName(old))).toBe(raw)
    }
  })

  it('without a session binding nothing is absent and nothing may be sent', async () => {
    const api = await reloaded()
    const unbound = api.pendingPlanLifecycleKey(null, ACTOR, COMPANY_A, PLAN)
    expect(unbound).not.toBeNull()
    expect(api.readPendingPlanLifecycle(unbound)).toEqual({ state: 'unreadable' })
    expect(api.reservePendingPlanLifecycle(unbound, revise())).toEqual({ type: 'unreadable' })
    expect(api.reservePendingPlanLifecycle(unbound, approve())).toEqual({ type: 'unreadable' })
    expect(Object.keys(sessionStorage).some((name) => name.startsWith(REVISE_PREFIX))).toBe(false)
  })

  it('a legacy epoch-keyed record of the same context is not absence; one of another plan does not block', async () => {
    const legacy = LEGACY_REVISE_PREFIX + JSON.stringify([3, ACTOR, COMPANY_A, PLAN])
    const raw = JSON.stringify(revise())
    sessionStorage.setItem(legacy, raw)
    const api = await reloaded()
    const key = api.pendingPlanLifecycleKey(BINDING, ACTOR, COMPANY_A, PLAN)
    expect(api.readPendingPlanLifecycle(key)).toEqual({ state: 'unreadable' })
    expect(api.reservePendingPlanLifecycle(key, withKey(revise(), '30000000-0000-4000-8000-0000000000fd')))
      .toEqual({ type: 'unreadable' })
    expect(sessionStorage.getItem(legacy)).toBe(raw)
    const other = api.pendingPlanLifecycleKey(BINDING, ACTOR, COMPANY_A, 'd0000000-0000-4000-8000-000000000099')
    expect(api.readPendingPlanLifecycle(other)).toEqual({ state: 'absent' })

    const keys = vi.spyOn(Storage.prototype, 'key').mockImplementation(() => { throw new Error('denied') })
    try {
      expect((await reloaded()).readPendingPlanLifecycle(other)).toEqual({ state: 'unreadable' })
    } finally {
      keys.mockRestore()
    }
  })

  it('reads another actor, company or plan context as proven absent, independent of this slot', async () => {
    const before = await reloaded()
    before.reservePendingPlanLifecycle(before.pendingPlanLifecycleKey(BINDING, ACTOR, COMPANY_A, PLAN), revise())
    const after = await reloaded()
    for (const key of [
      after.pendingPlanLifecycleKey(BINDING, '88888888-8888-8888-8888-888888888888', COMPANY_A, PLAN),
      after.pendingPlanLifecycleKey(BINDING, ACTOR, 'bbbbbbbb-0000-0000-0000-000000000002', PLAN),
      after.pendingPlanLifecycleKey(BINDING, ACTOR, COMPANY_A, 'd0000000-0000-4000-8000-000000000099'),
    ]) {
      expect(after.readPendingPlanLifecycle(key), key ?? 'null').toEqual({ state: 'absent' })
      // An unrelated slot stays usable.
      expect(after.reservePendingPlanLifecycle(key, approve()), key ?? 'null').toEqual({ type: 'reserved' })
    }
    expect(after.readPendingPlanLifecycle(after.pendingPlanLifecycleKey(BINDING, ACTOR, COMPANY_A, PLAN)).state).toBe('pending')
  })

  it('reads a foreign, malformed or out-of-contract record as unreadable, keeps it and lets nothing in', async () => {
    const api = await reloaded()
    const key = api.pendingPlanLifecycleKey(BINDING, ACTOR, COMPANY_A, PLAN)!
    const stored = (value: unknown) => {
      api.clearPendingPlanLifecycles()
      api.reservePendingPlanLifecycle(key, revise())
      const slot = slotName(key)
      sessionStorage.setItem(slot, typeof value === 'string' ? value : JSON.stringify(value))
      return slot
    }
    const base = record(revise())
    const request = (revise().snapshot as Extract<Pending['snapshot'], { kind: 'revise' }>).request
    const { sessionBinding: _binding, ...unbound } = base
    const bad: unknown[] = [
      '{not json',
      { ...base, actorId: '88888888-8888-8888-8888-888888888888' },
      { ...base, companyId: 'bbbbbbbb-0000-0000-0000-000000000002' },
      { ...base, planId: 'd0000000-0000-4000-8000-000000000099' },
      { ...base, extra: 1 },
      // M01.3 K2 #728/6180459736: another session's, a missing and a malformed binding.
      record(revise(), OTHER_BINDING),
      unbound,
      record(revise(), null),
      record(revise(), 4),
      { ...base, snapshot: { ...revise().snapshot, kind: 'approve' } },
      { ...base, snapshot: { ...revise().snapshot, idempotencyKey: '' } },
      record(revise({ expectedVersion: 0 })),
      record(revise({ expectedVersion: '3' })),
      record(revise({ workCenterId: 'not-a-guid' })),
      record(revise({ inputs: [] })),
      record(revise({ inputs: [{ ...request.inputs[0], plannedKg: '0' }] })),
      record(revise({ inputs: [{ ...request.inputs[0], extra: 1 }] })),
      record(revise({ inputs: [request.inputs[0], request.inputs[0]] })),
      record(revise({ target: { basis: 'Other', kg: '1' } })),
      record(revise({ reason: '' })),
      record(revise({ reason: ' padded ' })),
      record(revise({ extra: 1 })),
      record(revise({}, null)),
      record(revise({}, { lines: [] })),
      record(revise({}, { lines: [{ ...CUT_LINES[0], sequence: 1.5 }] })),
      record(revise({}, { lines: [{ ...CUT_LINES[0], lengthMm: '6000.0000' }] })),
      record(revise({}, { lines: [CUT_LINES[0], { ...CUT_LINES[1], lineId: CUT_LINES[0].lineId }] })),
    ]
    for (const value of bad) {
      const slot = stored(value)
      const raw = sessionStorage.getItem(slot)
      const fresh = await reloaded()
      const label = JSON.stringify(value)
      // Never collapsed into "no pending".
      expect(fresh.readPendingPlanLifecycle(key), label).toEqual({ state: 'unreadable' })
      // No new command of any kind, and no new key, takes the slot; nothing is replaced or deleted.
      expect(fresh.reservePendingPlanLifecycle(key, withKey(revise(), '30000000-0000-4000-8000-0000000000ff')), label)
        .toEqual({ type: 'unreadable' })
      expect(fresh.reservePendingPlanLifecycle(key, approve()), label).toEqual({ type: 'unreadable' })
      fresh.releasePendingPlanLifecycle(key, REVISE_KEY)
      expect(sessionStorage.getItem(slot), label).toBe(raw)
      expect(fresh.readPendingPlanLifecycle(key), label).toEqual({ state: 'unreadable' })
    }
  })

  it('reads an unavailable store as unreadable, never as absent, and sends nothing into it', async () => {
    const api = await reloaded()
    const key = api.pendingPlanLifecycleKey(BINDING, ACTOR, COMPANY_A, PLAN)
    const getItem = vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('denied') })
    try {
      expect(api.readPendingPlanLifecycle(key)).toEqual({ state: 'unreadable' })
      expect(api.reservePendingPlanLifecycle(key, revise())).toEqual({ type: 'unreadable' })
      expect(api.reservePendingPlanLifecycle(key, approve())).toEqual({ type: 'unreadable' })
    } finally {
      getItem.mockRestore()
    }
    expect(api.readPendingPlanLifecycle(key)).toEqual({ state: 'absent' })
  })

  it('a revise that cannot be stored durably is neither reserved nor held, so it is never sent', async () => {
    const api = await reloaded()
    const key = api.pendingPlanLifecycleKey(BINDING, ACTOR, COMPANY_A, PLAN)
    const setItem = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('quota') })
    try {
      expect(api.reservePendingPlanLifecycle(key, revise())).toEqual({ type: 'not-durable' })
      expect(api.readPendingPlanLifecycle(key)).toEqual({ state: 'absent' })
    } finally {
      setItem.mockRestore()
    }
    expect((await reloaded()).readPendingPlanLifecycle(key)).toEqual({ state: 'absent' })

    // A write the store cannot read back is not durable either.
    const fresh = await reloaded()
    const lying = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => undefined)
    try {
      expect(fresh.reservePendingPlanLifecycle(key, revise())).toEqual({ type: 'not-durable' })
      expect(fresh.readPendingPlanLifecycle(key)).toEqual({ state: 'absent' })
    } finally {
      lying.mockRestore()
    }
  })

  it('an unresolved revise is never displaced by another command of the same slot', async () => {
    const api = await reloaded()
    const key = api.pendingPlanLifecycleKey(BINDING, ACTOR, COMPANY_A, PLAN)
    const held = revise()
    api.reservePendingPlanLifecycle(key, held)
    const raw = sessionStorage.getItem(slotName(key!))
    for (const other of [
      approve(),
      { ...approve(), snapshot: { kind: 'create-order', idempotencyKey: '30000000-0000-4000-8000-000000000004', orderId: '30000000-0000-4000-8000-000000000005' } } as Pending,
      withKey(held, '30000000-0000-4000-8000-000000000002'),
      revise({ reason: 'Aynı anahtar, başka gövde' }),
    ]) {
      expect(api.reservePendingPlanLifecycle(key, other)).toEqual({ type: 'occupied', pending: held })
    }
    expect(sessionStorage.getItem(slotName(key!))).toBe(raw)
    const after = await reloaded()
    const recalled = after.readPendingPlanLifecycle(key)
    expect(recalled.state === 'pending' && recalled.pending.snapshot.idempotencyKey).toBe(REVISE_KEY)
    // The same refusal after the reload, from the stored side alone.
    expect(after.reservePendingPlanLifecycle(key, approve()).type).toBe('occupied')
  })

  it('releases only the exact settled attempt; a stale answer cannot remove a newer pending attempt', async () => {
    const api = await reloaded()
    const key = api.pendingPlanLifecycleKey(BINDING, ACTOR, COMPANY_A, PLAN)
    const older = revise()
    const newer = withKey(older, '30000000-0000-4000-8000-000000000002')
    api.reservePendingPlanLifecycle(key, older)
    api.releasePendingPlanLifecycle(key, REVISE_KEY)
    expect(api.readPendingPlanLifecycle(key)).toEqual({ state: 'absent' })
    expect(api.reservePendingPlanLifecycle(key, newer)).toEqual({ type: 'reserved' })
    // A late answer of the older attempt arrives: the newer one stays, in memory and across the reload.
    api.releasePendingPlanLifecycle(key, REVISE_KEY)
    expect(api.readPendingPlanLifecycle(key)).toEqual({ state: 'pending', pending: newer })
    const current = await reloaded()
    expect(current.readPendingPlanLifecycle(key)).toEqual({ state: 'pending', pending: newer })
    current.releasePendingPlanLifecycle(key, '30000000-0000-4000-8000-000000000002')
    expect(current.readPendingPlanLifecycle(key)).toEqual({ state: 'absent' })
    expect((await reloaded()).readPendingPlanLifecycle(key)).toEqual({ state: 'absent' })
  })

  it('a release whose stored removal fails keeps the same attempt held, never a different one', async () => {
    const api = await reloaded()
    const key = api.pendingPlanLifecycleKey(BINDING, ACTOR, COMPANY_A, PLAN)
    api.reservePendingPlanLifecycle(key, revise())
    const removeItem = vi.spyOn(Storage.prototype, 'removeItem').mockImplementation(() => { throw new Error('denied') })
    try {
      api.releasePendingPlanLifecycle(key, REVISE_KEY)
    } finally {
      removeItem.mockRestore()
    }
    expect(api.readPendingPlanLifecycle(key)).toEqual({ state: 'pending', pending: revise() })
    expect((await reloaded()).readPendingPlanLifecycle(key)).toEqual({ state: 'pending', pending: revise() })
  })

  it('keeps the other lifecycle commands in-memory only, as before', async () => {
    const api = await reloaded()
    const key = api.pendingPlanLifecycleKey(BINDING, ACTOR, COMPANY_A, PLAN)
    expect(api.reservePendingPlanLifecycle(key, approve())).toEqual({ type: 'reserved' })
    expect(Object.keys(sessionStorage).some((name) => name.startsWith(REVISE_PREFIX))).toBe(false)
    expect(api.readPendingPlanLifecycle(key)).toEqual({ state: 'pending', pending: approve() })
    // A newer approval may still replace an in-memory approval, exactly as before.
    expect(api.reservePendingPlanLifecycle(key, approve('30000000-0000-4000-8000-000000000006'))).toEqual({ type: 'reserved' })
    api.releasePendingPlanLifecycle(key, '30000000-0000-4000-8000-000000000006')
    expect(api.readPendingPlanLifecycle(key)).toEqual({ state: 'absent' })
    api.reservePendingPlanLifecycle(key, approve())
    expect((await reloaded()).readPendingPlanLifecycle(key)).toEqual({ state: 'absent' })
  })
})
