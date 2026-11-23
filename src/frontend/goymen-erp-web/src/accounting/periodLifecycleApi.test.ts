import { afterEach, describe, expect, it, vi } from 'vitest'
import { decodePeriodLifecycle, decodePeriodReopening, submitPeriodReopening, type PeriodLifecycle, type PeriodReopeningAttempt } from './periodLifecycleApi'
import { keyProbes } from './exactKeysTestSupport'
const id = (n: number) => `ae000000-0000-4000-8000-${String(n).padStart(12, '0')}`
const company = id(1); const period = id(2)
const view = (): PeriodLifecycle => ({ companyId: company, periodId: period, periodVersion: 3,
  closures: [{ id: id(3), companyId: company, periodId: period, closedPeriodVersion: 3, targetPeriodId: id(4),
    carryVoucherId: id(5), sourceHash: 'a'.repeat(64), lineCount: 1, closedBy: id(6), closedAtUtc: '2026-11-17T09:00:00+00:00',
    reopeningId: null, reopenedBy: null, reopenedAtUtc: null }],
  reopenings: [{ id: id(7), companyId: company, periodId: period, closureId: id(3), closedPeriodVersion: 3,
    proposedBy: id(8), proposedAtUtc: '2026-11-17T10:00:00+00:00', reason: 'Düzeltme', approvedBy: null, approvedAtUtc: null, version: 1 }] })
afterEach(() => vi.unstubAllGlobals())
describe('period lifecycle source contract', () => {
  it('accepts a bound closure and pending reopening without creating a second balance', () => {
    expect(decodePeriodLifecycle(view(), company, period)).toEqual(view())
  })
  it.each([
    (v: any) => { v.companyId = id(9) },
    (v: any) => { v.closures[0].companyId = id(9) },
    (v: any) => { v.closures.push(v.closures[0]) },
    (v: any) => { v.closures[0].sourceHash = 'missing' },
    (v: any) => { v.closures[0].lineCount = -1 },
    (v: any) => { v.reopenings[0].closureId = id(9) },
    (v: any) => { v.reopenings[0].version = 2; v.reopenings[0].approvedBy = id(8); v.reopenings[0].approvedAtUtc = '2026-11-17T11:00:00Z' },
    (v: any) => { v.closures[0].reopeningId = id(7) },
  ])('rejects inconsistent or cross company history', mutate => {
    const value = structuredClone(view()); mutate(value)
    expect(decodePeriodLifecycle(value, company, period)).toBeNull()
  })
  it('freezes a mismatched successful response as unknown and resends the same request', async () => {
    const attempt: PeriodReopeningAttempt = { operation: 'propose', periodId: period, key: 'same-key',
      request: { expectedVersion: 3, proposalId: id(7), reason: 'Düzeltme' } }
    const fetch = vi.fn().mockResolvedValueOnce(new Response(JSON.stringify({ ...view().reopenings[0], id: id(9) }), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify(view().reopenings[0]), { status: 200 }))
    vi.stubGlobal('fetch', fetch)
    expect(await submitPeriodReopening(company, attempt)).toEqual({ type: 'unknown' })
    expect((await submitPeriodReopening(company, attempt)).type).toBe('applied')
    expect(fetch.mock.calls[0]).toEqual(fetch.mock.calls[1])
  })
})
describe('period lifecycle exact key sets', () => {
  it('rejects an unknown or missing key on the lifecycle, a closure and a reopening', () => {
    expect(decodePeriodLifecycle(view(), company, period)).not.toBeNull()
    for (const [label, body] of keyProbes(view(), [[[], 'periodVersion'], [['closures', 0], 'lineCount'], [['reopenings', 0], 'reason']]))
      expect(decodePeriodLifecycle(body, company, period), label).toBeNull()
  })
  it('rejects an unknown or missing key on a reopening command response', () => {
    const reopening = view().reopenings[0]
    expect(decodePeriodReopening(reopening, company, period)).toEqual(reopening)
    for (const [label, body] of keyProbes(reopening, [[[], 'approvedBy']])) expect(decodePeriodReopening(body, company, period), label).toBeNull()
  })
})
