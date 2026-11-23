import { afterEach, describe, expect, it, vi } from 'vitest'
import { decodeOrder } from './contract'
import { APPROVER, COMPANY_A, ORDER, order } from './testHarness'
import { decodeReopenAttempt, reopenOrder } from './orderReopenApi'
const attempt = { idempotencyKey: '40000000-0000-0000-0000-000000000004', expectedVersion: 4, reason: 'Synthetic correction' }
const history = { closedOrderVersion: 4, closedByUserId: APPROVER, closedAtUtc: '2026-11-16T20:00:00Z',
  reopenedOrderVersion: 5, reopenedByUserId: APPROVER, reopenedAtUtc: '2026-11-16T20:30:00Z', reason: attempt.reason }
afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks() })
describe('reopening contract', () => {
  it('retains valid history and rejects duplicate, future or incomplete transition chains', () => {
    expect(decodeOrder(order({ version: 5, reopenings: [history] }))?.reopenings).toEqual([history])
    expect(decodeOrder(order({ version: 5, reopenings: [history, history] }))).toBeNull()
    expect(decodeOrder(order({ version: 4, reopenings: [history] }))).toBeNull()
    expect(decodeOrder(order({ version: 5, reopenings: [{ ...history, closedByUserId: null }] }))).toBeNull()
    expect(decodeReopenAttempt({ ...attempt, expectedVersion: -1 })).toBeNull()
    expect(decodeReopenAttempt({ ...attempt, extra: true })).toBeNull()
  })
  it('requires the exact reopened order body and replay provenance and keeps 503 ambiguous', async () => {
    const fetchMock = vi.fn(); vi.stubGlobal('fetch', fetchMock)
    const body = order({ version: 5, reopenings: [history] })
    fetchMock.mockResolvedValueOnce(new Response(JSON.stringify(body), { status: 503 }))
    expect(await reopenOrder(COMPANY_A, ORDER, attempt)).toEqual({ type: 'unknown' })
    fetchMock.mockResolvedValueOnce(new Response(JSON.stringify(body), { status: 200, headers: { 'Idempotency-Replayed': 'true' } }))
    expect(await reopenOrder(COMPANY_A, ORDER, attempt)).toEqual({ type: 'unknown' })
    fetchMock.mockResolvedValueOnce(new Response(JSON.stringify(body), { status: 200,
      headers: { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'synthetic-correlation' } }))
    expect(await reopenOrder(COMPANY_A, ORDER, attempt)).toEqual({ type: 'applied' })
    fetchMock.mockResolvedValueOnce(new Response(JSON.stringify({ code: 'manufacturing_order_reopen_period_not_open' }), { status: 422 }))
    expect(await reopenOrder(COMPANY_A, ORDER, attempt)).toEqual({ type: 'failed', status: 422, code: 'manufacturing_order_reopen_period_not_open' })
  })
})
const without = (value: object, key: string) => Object.fromEntries(Object.entries(value).filter(([name]) => name !== key))
describe('reopening history exact key set (SENTETIK)', () => {
  it('rejects an extra or a missing key on a reopening row', () => {
    expect(decodeOrder(order({ version: 5, reopenings: [history] }))?.reopenings).toEqual([history])
    expect(decodeOrder(order({ version: 5, reopenings: [{ ...history, extra: 1 }] }))).toBeNull()
    for (const key of Object.keys(history)) expect(decodeOrder(order({ version: 5, reopenings: [without(history, key)] }))).toBeNull()
  })
})
