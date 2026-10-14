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

  it.each([
    ['ordered cut lines', { lines: CUT_LINES }],
    ['the legacy request without cut lines', undefined],
  ])('restores %s after the reload and re-sends the same key and byte-equal body', async (_case, cut) => {
    const before = await reloaded()
    const key = before.pendingPlanLifecycleKey(4, ACTOR, COMPANY_A, PLAN)
    const pending = revise({}, cut ?? 'omit')
    before.reservePendingPlanLifecycle(key, pending)
    const lost = vi.fn(async () => { throw new TypeError('connection lost') })
    vi.stubGlobal('fetch', lost)
    const first = await before.revisePlan(COMPANY_A, PLAN, pending.snapshot as Extract<Pending['snapshot'], { kind: 'revise' }>)
    expect(first.type).toBe('unknown')

    const after = await reloaded()
    const recalled = after.recallPendingPlanLifecycle(after.pendingPlanLifecycleKey(4, ACTOR, COMPANY_A, PLAN))
    expect(recalled).toEqual(pending)
    expect(JSON.stringify(recalled)).toBe(JSON.stringify(pending))
    // Deep-frozen: nothing typed after the reload can reach the frozen attempt.
    expect(Object.isFrozen(recalled)).toBe(true)
    const request = (recalled!.snapshot as Extract<Pending['snapshot'], { kind: 'revise' }>).request
    expect(Object.isFrozen(request.inputs[0])).toBe(true)
    if (cut !== undefined) expect(Object.isFrozen(request.cutToLengthSpec?.lines[1])).toBe(true)
    else expect(request).not.toHaveProperty('cutToLengthSpec')

    const replay = vi.fn(async () => { throw new TypeError('still offline') })
    vi.stubGlobal('fetch', replay)
    await after.revisePlan(COMPANY_A, PLAN, recalled!.snapshot as Extract<Pending['snapshot'], { kind: 'revise' }>)
    expect(sentBodies(replay)).toEqual(sentBodies(lost))
    expect(sentBodies(replay)[0].key).toBe(REVISE_KEY)
  })

  it('never restores the attempt in another session, actor, company or plan context', async () => {
    const before = await reloaded()
    before.reservePendingPlanLifecycle(before.pendingPlanLifecycleKey(4, ACTOR, COMPANY_A, PLAN), revise())
    const after = await reloaded()
    for (const key of [
      after.pendingPlanLifecycleKey(5, ACTOR, COMPANY_A, PLAN),
      after.pendingPlanLifecycleKey(4, '88888888-8888-8888-8888-888888888888', COMPANY_A, PLAN),
      after.pendingPlanLifecycleKey(4, ACTOR, 'bbbbbbbb-0000-0000-0000-000000000002', PLAN),
      after.pendingPlanLifecycleKey(4, ACTOR, COMPANY_A, 'd0000000-0000-4000-8000-000000000099'),
    ]) {
      expect(after.recallPendingPlanLifecycle(key), key ?? 'null').toBeNull()
    }
    expect(after.recallPendingPlanLifecycle(after.pendingPlanLifecycleKey(4, ACTOR, COMPANY_A, PLAN))).not.toBeNull()
  })

  it('refuses a stored record whose content is foreign, malformed or out of contract, and keeps it as evidence', async () => {
    const api = await reloaded()
    const key = api.pendingPlanLifecycleKey(4, ACTOR, COMPANY_A, PLAN)!
    const stored = (value: unknown) => {
      // The exact slot the store itself writes, found by its unique context suffix.
      api.reservePendingPlanLifecycle(key, revise())
      const slot = Object.keys(sessionStorage).find((name) => name.endsWith(key))!
      sessionStorage.setItem(slot, typeof value === 'string' ? value : JSON.stringify(value))
      return slot
    }
    const base = revise()
    const request = (base.snapshot as Extract<Pending['snapshot'], { kind: 'revise' }>).request
    const bad: unknown[] = [
      '{not json',
      { ...base, actorId: '88888888-8888-8888-8888-888888888888' },
      { ...base, companyId: 'bbbbbbbb-0000-0000-0000-000000000002' },
      { ...base, planId: 'd0000000-0000-4000-8000-000000000099' },
      { ...base, extra: 1 },
      { ...base, snapshot: { ...base.snapshot, kind: 'approve' } },
      { ...base, snapshot: { ...base.snapshot, idempotencyKey: '' } },
      revise({ expectedVersion: 0 }),
      revise({ expectedVersion: '3' }),
      revise({ workCenterId: 'not-a-guid' }),
      revise({ inputs: [] }),
      revise({ inputs: [{ ...request.inputs[0], plannedKg: '0' }] }),
      revise({ inputs: [{ ...request.inputs[0], extra: 1 }] }),
      revise({ inputs: [request.inputs[0], request.inputs[0]] }),
      revise({ target: { basis: 'Other', kg: '1' } }),
      revise({ reason: '' }),
      revise({ reason: ' padded ' }),
      revise({ extra: 1 }),
      revise({}, null),
      revise({}, { lines: [] }),
      revise({}, { lines: [{ ...CUT_LINES[0], sequence: 1.5 }] }),
      revise({}, { lines: [{ ...CUT_LINES[0], lengthMm: '6000.0000' }] }),
      revise({}, { lines: [CUT_LINES[0], { ...CUT_LINES[1], lineId: CUT_LINES[0].lineId }] }),
    ]
    for (const value of bad) {
      const slot = stored(value)
      const fresh = await reloaded()
      expect(fresh.recallPendingPlanLifecycle(key), JSON.stringify(value)).toBeNull()
      // Not silently discarded: the unreadable record stays where it was.
      expect(sessionStorage.getItem(slot)).toBe(typeof value === 'string' ? value : JSON.stringify(value))
    }
  })

  it('releases only the exact settled attempt; an older answer cannot remove a newer pending attempt', async () => {
    const api = await reloaded()
    const key = api.pendingPlanLifecycleKey(4, ACTOR, COMPANY_A, PLAN)
    const older = revise()
    const newer = { ...older, snapshot: { ...older.snapshot, idempotencyKey: '30000000-0000-4000-8000-000000000002' } }
    api.reservePendingPlanLifecycle(key, older)
    api.reservePendingPlanLifecycle(key, newer)
    api.releasePendingPlanLifecycle(key, REVISE_KEY)
    expect((await reloaded()).recallPendingPlanLifecycle(key)?.snapshot.idempotencyKey)
      .toBe('30000000-0000-4000-8000-000000000002')

    const current = await reloaded()
    expect(current.recallPendingPlanLifecycle(key)).not.toBeNull()
    current.releasePendingPlanLifecycle(key, '30000000-0000-4000-8000-000000000002')
    expect(current.recallPendingPlanLifecycle(key)).toBeNull()
    expect((await reloaded()).recallPendingPlanLifecycle(key)).toBeNull()
  })

  it('keeps the other lifecycle commands in-memory only, as before', async () => {
    const api = await reloaded()
    const key = api.pendingPlanLifecycleKey(4, ACTOR, COMPANY_A, PLAN)
    api.reservePendingPlanLifecycle(key, revise())
    // A newer command in the same slot replaces the stored revise, exactly as it replaces it in memory.
    api.reservePendingPlanLifecycle(key, {
      actorId: ACTOR, companyId: COMPANY_A, planId: PLAN,
      snapshot: { kind: 'approve', idempotencyKey: '30000000-0000-4000-8000-000000000003', expectedVersion: 3 },
    })
    expect(api.recallPendingPlanLifecycle(key)?.snapshot.kind).toBe('approve')
    expect((await reloaded()).recallPendingPlanLifecycle(key)).toBeNull()
  })

  it('a storage failure keeps the in-memory attempt for this module life', async () => {
    const api = await reloaded()
    const key = api.pendingPlanLifecycleKey(4, ACTOR, COMPANY_A, PLAN)
    const setItem = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('quota') })
    try {
      api.reservePendingPlanLifecycle(key, revise())
      expect(api.recallPendingPlanLifecycle(key)?.snapshot.idempotencyKey).toBe(REVISE_KEY)
    } finally {
      setItem.mockRestore()
    }
  })
})
