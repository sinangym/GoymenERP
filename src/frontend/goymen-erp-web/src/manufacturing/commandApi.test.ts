/**
 * The fact command's classification of every answer the contract describes, and of
 * the ones it does not. Unit tests over a stubbed fetch: not real-API evidence.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { closeOrder, recordFact } from './commandApi'
import type { CloseSnapshot, FactSnapshot } from './contract'
import { COMPANY_A, ORDER, ROLL_1, ROLL_2, json, problem, stockShortfall } from './testHarness'

const SNAPSHOT: FactSnapshot = {
  kind: 'input-issues',
  factId: 'f0000000-0000-4000-8000-000000000001',
  movementId: 'f0000000-0000-4000-8000-000000000002',
  expectedParentLabelVersion: null,
  idempotencyKey: 'key-1',
  expectedVersion: 4,
  parentRollId: ROLL_1,
  kg: '900.5',
  occurredAtUtc: '2026-09-18T09:00:00.000Z',
}

function answer(response: Response | (() => never)) {
  const fetchMock = vi.fn(async () => (typeof response === 'function' ? response() : response))
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('recordFact', () => {
  it('posts exactly the frozen snapshot with its own key and the company header', async () => {
    const fetchMock = answer(json({}, 200))
    await recordFact(COMPANY_A, ORDER, SNAPSHOT)
    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit]
    expect(url).toBe(`/api/v1/manufacturing/orders/${ORDER}/input-issues`)
    expect(init.method).toBe('POST')
    expect(init.headers).toMatchObject({ 'Idempotency-Key': 'key-1', 'X-Company-Id': COMPANY_A })
    expect(JSON.parse(init.body as string)).toEqual({
      issueId: SNAPSHOT.factId,
      expectedVersion: 4,
      parentRollId: ROLL_1,
      movementId: SNAPSHOT.movementId,
      kg: '900.5',
      occurredAtUtc: SNAPSHOT.occurredAtUtc,
    })
  })

  it('posts the strict output snapshot to the output route without changing any identity', async () => {
    const output: FactSnapshot = {
      ...SNAPSHOT,
      kind: 'outputs',
      expectedParentLabelVersion: '8',
      outputClass: 'GoodProduct',
      productId: 'f0000000-0000-4000-8000-000000000010',
      expectedProductVersion: '12',
      lotId: 'f0000000-0000-4000-8000-000000000011',
      labelId: 'f0000000-0000-4000-8000-000000000012',
      labelCode: 'OUT-12',
      heatNumber: null,
      coilNumber: 'COIL-12',
      requiredDocuments: ['CERT-12'],
      ownerKind: 'Company',
      ownerId: COMPANY_A,
      custodianKind: 'Company',
      custodianId: COMPANY_A,
      locationId: 'f0000000-0000-4000-8000-000000000013',
      initialBlock: 'Free',
    }
    const fetchMock = answer(json({}, 200))

    await recordFact(COMPANY_A, ORDER, output)

    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit]
    expect(url).toBe(`/api/v1/manufacturing/orders/${ORDER}/outputs`)
    expect(init.headers).toMatchObject({ 'Idempotency-Key': 'key-1', 'X-Company-Id': COMPANY_A })
    expect(JSON.parse(init.body as string)).toMatchObject({
      outputId: output.factId,
      movementId: output.movementId,
      expectedParentLabelVersion: '8',
      expectedProductVersion: '12',
      ownerKind: 'Company',
      ownerId: COMPANY_A,
      custodianKind: 'Company',
      custodianId: COMPANY_A,
      initialBlock: 'Free',
    })
  })

  it('posts process loss to its explicit non-stock route with only the RecordFact body', async () => {
    const loss: FactSnapshot = {
      ...SNAPSHOT,
      kind: 'process-losses',
      movementId: null,
      expectedParentLabelVersion: null,
    }
    const fetchMock = answer(json({}, 200))

    await recordFact(COMPANY_A, ORDER, loss)

    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit]
    expect(url).toBe(`/api/v1/manufacturing/orders/${ORDER}/process-losses`)
    expect(JSON.parse(init.body as string)).toEqual({
      factId: loss.factId,
      expectedVersion: 4,
      parentRollId: ROLL_1,
      kg: '900.5',
      occurredAtUtc: loss.occurredAtUtc,
    })
  })

  it('reads a 200 as applied and a replay as the recorded answer', async () => {
    answer(json({}, 200))
    expect(await recordFact(COMPANY_A, ORDER, SNAPSHOT)).toEqual({
      type: 'applied',
      replayed: false,
      originalCorrelationId: null,
    })
    answer(json({}, 200, { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'corr-1' }))
    expect(await recordFact(COMPANY_A, ORDER, SNAPSHOT)).toEqual({
      type: 'applied',
      replayed: true,
      originalCorrelationId: 'corr-1',
    })
  })

  it.each([
    [problem(409, { code: 'idempotency_conflict' }), { kind: 'idempotency-conflict' }],
    [problem(409, { code: 'manufacturing_concurrent_change' }), { kind: 'concurrent-change' }],
    [
      problem(409, { code: 'inventory_manufacturing_parent_label_version_conflict' }),
      { kind: 'label-version-conflict' },
    ],
    [
      problem(409, { code: 'inventory_manufacturing_input_return_exceeds_issued' }),
      { kind: 'rule', code: 'inventory_manufacturing_input_return_exceeds_issued' },
    ],
    [
      problem(409, { code: 'manufacturing_fact_id_conflict' }),
      { kind: 'id-conflict', code: 'manufacturing_fact_id_conflict' },
    ],
    [
      problem(409, { code: 'manufacturing_version_conflict', expectedVersion: '4', currentVersion: '6' }),
      { kind: 'version-conflict', expectedVersion: 4, currentVersion: 6, conflictIsCurrent: true },
    ],
    [problem(422, { code: 'manufacturing_return_exceeds_issued' }), { kind: 'rule', code: 'manufacturing_return_exceeds_issued' }],
    [problem(400, { code: 'manufacturing_request_invalid', field: 'kg' }), { kind: 'validation', field: 'kg' }],
    [problem(403, { code: 'forbidden' }), { kind: 'forbidden' }],
    [problem(404), { kind: 'not-found' }],
  ])('classifies a described refusal by its code (%#)', async (response, failure) => {
    answer(response)
    expect(await recordFact(COMPANY_A, ORDER, SNAPSHOT)).toEqual({ type: 'failed', failure, replayed: false })
  })

  it('marks a replayed version conflict as history, not the current version', async () => {
    answer(
      problem(
        409,
        { code: 'manufacturing_version_conflict', expectedVersion: '4', currentVersion: '5' },
        { 'Idempotency-Replayed': 'true' },
      ),
    )
    const outcome = await recordFact(COMPANY_A, ORDER, SNAPSHOT)
    expect(outcome).toMatchObject({ type: 'failed', replayed: true, failure: { conflictIsCurrent: false } })
  })

  it.each([
    [problem(503, { code: 'service_unavailable' }), { kind: 'service-unavailable', status: 503 }],
    [json({}, 201), { kind: 'unexpected-success', status: 201 }],
    [problem(409), { kind: 'undecodable', status: 409 }],
    [problem(409, { code: 'something_else' }), { kind: 'undecodable', status: 409 }],
    [problem(422), { kind: 'undecodable', status: 422 }],
    [problem(418, { code: 'teapot' }), { kind: 'undecodable', status: 418 }],
  ])('leaves an answer it cannot read UNKNOWN, never refused (%#)', async (response, unknown) => {
    answer(response)
    expect(await recordFact(COMPANY_A, ORDER, SNAPSHOT)).toEqual({ type: 'unknown', unknown })
  })

  it('treats no response to a write as unknown, not as not-applied', async () => {
    answer(() => {
      throw new TypeError('failed to fetch')
    })
    expect(await recordFact(COMPANY_A, ORDER, SNAPSHOT)).toMatchObject({ type: 'unknown' })
  })
})

describe('recordFact — SAT-D318 physical-stock refusal detail', () => {
  const CODE = 'inventory_manufacturing_physical_stock_insufficient'
  const PLAIN = { type: 'failed', replayed: false, failure: { kind: 'rule', code: CODE } }

  function detail(overrides: Record<string, unknown> = {}) {
    return stockShortfall(SNAPSHOT.factId, '900.5', '480.25', overrides)
  }

  function withoutMember(name: string): Record<string, unknown> {
    const value: Record<string, unknown> = { ...detail() }
    delete value[name]
    return value
  }

  it('keeps the detail that names exactly the sent issue, its order, label and quantity', async () => {
    answer(problem(409, { code: CODE, stockShortfall: detail() }))

    expect(await recordFact(COMPANY_A, ORDER, SNAPSHOT)).toEqual({
      type: 'failed',
      replayed: false,
      failure: { kind: 'rule', code: CODE, stockShortfall: detail() },
    })
  })

  it('compares the quantity by value, as the server writes the sent kilograms back without trailing zeros', async () => {
    answer(problem(409, { code: CODE, stockShortfall: detail() }))
    expect(await recordFact(COMPANY_A, ORDER, { ...SNAPSHOT, kg: '900.500' })).toEqual({
      ...PLAIN,
      failure: { kind: 'rule', code: CODE, stockShortfall: detail() },
    })

    answer(problem(409, { code: CODE, stockShortfall: detail({ requestedKg: '900' }) }))
    expect(await recordFact(COMPANY_A, ORDER, { ...SNAPSHOT, kg: '900.000' })).toMatchObject({
      failure: { stockShortfall: { requestedKg: '900' } },
    })
  })

  it.each([
    ['nothing usable', '0'],
    ['a whole number', '900'],
    ['just below the request', '900.499'],
    ['the finest server figure below it', '900.4999999'],
  ])('keeps the usable kilograms the server computed when they are %s', async (_case, availableKg) => {
    answer(problem(409, { code: CODE, stockShortfall: detail({ availableKg }) }))

    expect(await recordFact(COMPANY_A, ORDER, SNAPSHOT)).toMatchObject({
      failure: { kind: 'rule', code: CODE, stockShortfall: { requestedKg: '900.5', availableKg } },
    })
  })

  it('keeps a detail with no place when the label holds no stock anywhere', async () => {
    answer(problem(409, { code: CODE, stockShortfall: detail({ locationId: null }) }))

    expect(await recordFact(COMPANY_A, ORDER, SNAPSHOT)).toMatchObject({
      failure: { kind: 'rule', code: CODE, stockShortfall: { locationId: null, labelId: ROLL_1 } },
    })
  })

  it('reads the identities in either letter case as the same issue', async () => {
    const upper = detail({ sourceId: ORDER.toUpperCase(), sourceLineId: SNAPSHOT.factId.toUpperCase() })
    answer(problem(409, { code: CODE, stockShortfall: upper }))

    expect(await recordFact(COMPANY_A, ORDER, SNAPSHOT)).toMatchObject({ failure: { stockShortfall: upper } })
  })

  it('keeps the same definite refusal without the detail when an older server sends none', async () => {
    answer(problem(409, { code: CODE }))
    expect(await recordFact(COMPANY_A, ORDER, SNAPSHOT)).toEqual(PLAIN)
  })

  it.each([
    ['an undeclared free physical figure', detail({ freePhysicalKg: '1000' })],
    ['a reserved figure', detail({ reservedKg: '1000' })],
    ['no usable member', withoutMember('availableKg')],
    ['a null usable figure', detail({ availableKg: null })],
    ['a numeric usable figure', detail({ availableKg: 480.25 })],
    ['a negative usable figure', detail({ availableKg: '-1' })],
    ['a padded usable figure', detail({ availableKg: '480.250' })],
    ['a grouped usable figure', detail({ availableKg: '1,000' })],
    ['usable kg equal to the request', detail({ availableKg: '900.5' })],
    ['usable kg just above the request', detail({ availableKg: '900.5000001' })],
    ['usable kg above the request in the whole part', detail({ availableKg: '1000' })],
    ['no place member', withoutMember('locationId')],
    ['no quantity member', withoutMember('requestedKg')],
    ['a null detail', null],
    ['an array', [detail()]],
    ['a text', 'stok yok'],
    ['another movement kind', detail({ movementKind: 'Return' })],
    ['another source kind', detail({ sourceKind: 'manufacturing.input-return' })],
    ['another order', detail({ sourceId: 'd0000000-0000-4000-8000-000000000099' })],
    ['another issue', detail({ sourceLineId: 'f0000000-0000-4000-8000-000000000099' })],
    ['no issue line', detail({ sourceLineId: null })],
    ['another label', detail({ labelId: ROLL_2 })],
    ['another quantity', detail({ requestedKg: '900.6' })],
    ['a padded quantity', detail({ requestedKg: '900.50' })],
    ['a numeric quantity', detail({ requestedKg: 900.5 })],
    ['a zero quantity', detail({ requestedKg: '0' })],
    ['a nil product', detail({ productId: '00000000-0000-0000-0000-000000000000' })],
    ['a missing product', detail({ productId: null })],
    ['a malformed place', detail({ locationId: 'depo-1' })],
    ['a nil place', detail({ locationId: '00000000-0000-0000-0000-000000000000' })],
  ])('drops a detail with %s and keeps the definite refusal, never UNKNOWN', async (_case, shortfall) => {
    answer(problem(409, { code: CODE, stockShortfall: shortfall }))
    expect(await recordFact(COMPANY_A, ORDER, SNAPSHOT)).toEqual(PLAIN)
  })

  it('does not take a replayed detail as this attempt’s', async () => {
    answer(problem(409, { code: CODE, stockShortfall: detail() }, { 'Idempotency-Replayed': 'true' }))
    expect(await recordFact(COMPANY_A, ORDER, SNAPSHOT)).toEqual({ ...PLAIN, replayed: true })
  })

  it('reads the detail only on an input issue’s physical-stock refusal', async () => {
    answer(problem(409, { code: CODE, stockShortfall: detail() }))
    const ret: FactSnapshot = { ...SNAPSHOT, kind: 'input-returns', expectedParentLabelVersion: '7' }
    expect(await recordFact(COMPANY_A, ORDER, ret)).toEqual(PLAIN)

    const other = 'inventory_manufacturing_parent_label_not_releasable'
    answer(problem(409, { code: other, stockShortfall: detail() }))
    expect(await recordFact(COMPANY_A, ORDER, SNAPSHOT)).toEqual({
      type: 'failed',
      replayed: false,
      failure: { kind: 'rule', code: other },
    })

    const close: CloseSnapshot = { idempotencyKey: 'close-1', expectedVersion: 4 }
    answer(problem(409, { code: CODE, stockShortfall: detail() }))
    expect(await closeOrder(COMPANY_A, ORDER, close)).toEqual(PLAIN)
  })
})
