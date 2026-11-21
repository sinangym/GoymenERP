import { describe, expect, it, vi, afterEach } from 'vitest'
import { decodeNonKgAttempt, decodeNonKgHistory, nonKgQuantity, recordNonKgInput } from './nonKgInputApi'
const company = '10000000-0000-0000-0000-000000000001'
const order = '10000000-0000-0000-0000-000000000002'
const fact = '10000000-0000-0000-0000-000000000003'
const replacement = '10000000-0000-0000-0000-000000000004'
const entry = { factId: fact, revision: 1, kind: 1, item: 'Gerçek boya', unit: 1, quantity: '2.25', occurredAtUtc: '2026-11-17T09:00:00Z', replacesFactId: null, reason: 'Gerçek ölçüm' }
const history = { companyId: company, orderId: order, orderVersion: 3, revision: 1, facts: [entry] }
const attempt = { companyId: company, orderId: order, key: replacement, body: { factId: fact, expectedVersion: 3, expectedRevision: 0, kind: 'Paint' as const, item: 'Gerçek boya', unit: 'Litre' as const, quantity: '2.25', occurredAtUtc: '2026-11-17T09:00:00Z', replacesFactId: null, reason: 'Gerçek ölçüm' } }
afterEach(() => vi.unstubAllGlobals())
describe('actual non kg consumption contract', () => {
  it('keeps exact text quantities, scoped history and an immutable correction chain', () => {
    expect(nonKgQuantity('0.0000000000000000000000000001')).toBe(true)
    expect(nonKgQuantity('1.000')).toBe(false)
    expect(decodeNonKgHistory(history, company, order)?.facts[0].quantity).toBe('2.25')
    expect(decodeNonKgHistory(history, replacement, order)).toBeNull()
    expect(decodeNonKgHistory({ ...history, revision: 2, facts: [entry, { ...entry, factId: replacement, revision: 2, quantity: '0', replacesFactId: fact }] }, company, order)?.facts[0].quantity).toBe('2.25')
    for (const invalid of [ { ...entry, kind: 8 }, { ...entry, unit: 2 }, { ...entry, quantity: '0' }, { ...entry, revision: 2 }, { ...entry, replacesFactId: replacement } ])
      expect(decodeNonKgHistory({ ...history, facts: [invalid] }, company, order)).toBeNull()
  })
  it('does not admit another company, a paint metre or a new zero consumption body', () => {
    expect(decodeNonKgAttempt(attempt, company, order)).toEqual(attempt)
    expect(decodeNonKgAttempt(attempt, replacement, order)).toBeNull()
    expect(decodeNonKgAttempt({ ...attempt, body: { ...attempt.body, unit: 'Metre' } }, company, order)).toBeNull()
    expect(decodeNonKgAttempt({ ...attempt, body: { ...attempt.body, quantity: '0' } }, company, order)).toBeNull()
  })
  it('resends the identical body and key after an unknown outcome and preserves malformed success as unknown', async () => {
    const fetch = vi.fn().mockRejectedValueOnce(new TypeError('synthetic lost reply')).mockResolvedValueOnce(new Response('{"bad":true}', { status: 200 }))
    vi.stubGlobal('fetch', fetch)
    expect(await recordNonKgInput(attempt)).toBe('unknown'); expect(await recordNonKgInput(attempt)).toBe('unknown')
    expect(fetch.mock.calls[0][1]).toEqual(fetch.mock.calls[1][1])
    expect(fetch.mock.calls[0][1].headers['Idempotency-Key']).toBe(attempt.key)
  })
})
