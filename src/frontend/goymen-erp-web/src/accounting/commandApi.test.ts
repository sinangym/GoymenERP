/**
 * The period commands' classification of every answer the contract describes, and of the ones it does
 * not. Unit tests over a stubbed fetch: not real-API evidence.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { sendPeriodCommand } from './commandApi'
import type { PeriodCommandSnapshot } from './contract'
import { COMPANY_A, PERIOD_SEPTEMBER, json, problem } from './testHarness'

const PROPOSE: PeriodCommandSnapshot = {
  operation: 'propose',
  idempotencyKey: 'key-1',
  periodId: PERIOD_SEPTEMBER,
  startsOn: '2026-09-01',
  endsOn: '2026-09-30',
}
const APPROVE: PeriodCommandSnapshot = { operation: 'approve', idempotencyKey: 'key-2', periodId: PERIOD_SEPTEMBER, expectedVersion: 1 }

function answer(response: Response | (() => never)) {
  const fetchMock = vi.fn(async () => (typeof response === 'function' ? response() : response))
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('sendPeriodCommand', () => {
  it('posts the frozen snapshot with its own key and the company header, to the right route', async () => {
    const fetchMock = answer(json({}, 201))
    await sendPeriodCommand(COMPANY_A, PROPOSE)
    let [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit]
    expect(url).toBe('/api/v1/accounting/periods')
    expect(init.headers).toMatchObject({ 'Idempotency-Key': 'key-1', 'X-Company-Id': COMPANY_A })
    expect(JSON.parse(init.body as string)).toEqual({ periodId: PERIOD_SEPTEMBER, startsOn: '2026-09-01', endsOn: '2026-09-30' })

    const approve = answer(json({}, 200))
    await sendPeriodCommand(COMPANY_A, APPROVE)
    ;[url, init] = approve.mock.calls[0] as unknown as [string, RequestInit]
    expect(url).toBe(`/api/v1/accounting/periods/${PERIOD_SEPTEMBER}/approval`)
    expect(JSON.parse(init.body as string)).toEqual({ expectedVersion: 1 })

    const close = answer(json({}, 200))
    await sendPeriodCommand(COMPANY_A, { ...APPROVE, operation: 'close' })
    expect((close.mock.calls[0] as unknown as [string])[0]).toBe(`/api/v1/accounting/periods/${PERIOD_SEPTEMBER}/close`)
  })

  it('reads the operation’s own success status as applied, and a replay as the recorded answer', async () => {
    answer(json({}, 201))
    expect(await sendPeriodCommand(COMPANY_A, PROPOSE)).toEqual({ type: 'applied', replayed: false, originalCorrelationId: null })
    answer(json({}, 200, { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'corr-1' }))
    expect(await sendPeriodCommand(COMPANY_A, APPROVE)).toEqual({ type: 'applied', replayed: true, originalCorrelationId: 'corr-1' })
    // A 200 for a proposal is not its contract: unknown, never applied.
    answer(json({}, 200))
    expect(await sendPeriodCommand(COMPANY_A, PROPOSE)).toEqual({ type: 'unknown', unknown: { kind: 'unexpected-success', status: 200 } })
  })

  it.each([
    [problem(403, { code: 'forbidden' }), { kind: 'forbidden' }],
    [problem(404), { kind: 'not-found' }],
    [problem(400, { code: 'accounting_request_invalid', field: 'expectedVersion' }), { kind: 'validation', field: 'expectedVersion' }],
    [problem(409, { code: 'idempotency_conflict' }), { kind: 'idempotency-conflict' }],
    [problem(409, { code: 'accounting_concurrent_change' }), { kind: 'concurrent-change' }],
    [problem(409, { code: 'accounting_period_overlap' }), { kind: 'period-conflict', code: 'accounting_period_overlap' }],
    [
      problem(409, { code: 'accounting_version_conflict', expectedVersion: '7', currentVersion: '1' }),
      { kind: 'version-conflict', expectedVersion: 7, currentVersion: 1, conflictIsCurrent: true },
    ],
    [problem(422, { code: 'accounting_period_not_proposed' }), { kind: 'rule', code: 'accounting_period_not_proposed' }],
  ])('classifies a described refusal by its code (%#)', async (response, failure) => {
    answer(response)
    expect(await sendPeriodCommand(COMPANY_A, APPROVE)).toEqual({ type: 'failed', failure, replayed: false })
  })

  it('keeps a replayed refusal marked as the first attempt’s answer', async () => {
    answer(problem(409, { code: 'accounting_version_conflict', expectedVersion: '7', currentVersion: '1' }, { 'Idempotency-Replayed': 'true' }))
    expect(await sendPeriodCommand(COMPANY_A, APPROVE)).toMatchObject({
      type: 'failed',
      replayed: true,
      failure: { kind: 'version-conflict', conflictIsCurrent: false },
    })
  })

  it('never reads a 5xx, an undescribed 409 or no answer as a refusal: UNKNOWN', async () => {
    answer(problem(503, { code: 'service_unavailable' }))
    expect(await sendPeriodCommand(COMPANY_A, APPROVE)).toEqual({ type: 'unknown', unknown: { kind: 'service-unavailable', status: 503 } })
    answer(problem(409, { code: 'something_new' }))
    expect(await sendPeriodCommand(COMPANY_A, APPROVE)).toEqual({ type: 'unknown', unknown: { kind: 'undecodable', status: 409 } })
    answer(() => {
      throw new TypeError('network')
    })
    expect((await sendPeriodCommand(COMPANY_A, APPROVE)).type).toBe('unknown')
  })
})
