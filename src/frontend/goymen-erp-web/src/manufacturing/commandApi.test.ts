/**
 * The fact command's classification of every answer the contract describes, and of
 * the ones it does not. Unit tests over a stubbed fetch: not real-API evidence.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { recordFact } from './commandApi'
import type { FactSnapshot } from './contract'
import { COMPANY_A, ORDER, ROLL_1, json, problem } from './testHarness'

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
