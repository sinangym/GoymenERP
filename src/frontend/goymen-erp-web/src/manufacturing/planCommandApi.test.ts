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
  return json(plan({ target: REQUEST.target }), 201, {
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
