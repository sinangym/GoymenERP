import { afterEach, describe, expect, it, vi } from 'vitest'
import { fetchAdditionalCosts, fetchAdditionalCostSource, recordAdditionalCost } from './additionalCostApi'
import type { RecordAdditionalCost } from './additionalCostContract'
import { expenseFact, expenseId as id, expenseSource } from './additionalCostFixtures.testSupport'
afterEach(() => vi.unstubAllGlobals())
const request: RecordAdditionalCost = { costId: id(4), expectedVersion: '0', orderId: id(2), sourceKind: 1, expectedOrderVersion: '3', acceptanceId: null,
  supplierPartyId: id(5), companyAccountId: id(6), businessDate: '2026-11-17', expenseTypeId: null, expectedExpenseTypeVersion: null,
  description: 'SENTETIK nakliye', netAmount: '100.01', vatRateCode: 'SENTETIK-20', expectedVatListVersion: '1', reason: 'SENTETIK belge' }
describe('expense transport boundary', () => {
  it('reads only the selected server-scoped source and refuses another company payload', async () => {
    const fetcher = vi.fn().mockResolvedValueOnce(new Response(JSON.stringify(expenseSource()), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify([expenseFact()]), { status: 200 }))
    vi.stubGlobal('fetch', fetcher)
    expect((await fetchAdditionalCostSource(id(1), 'GoodsOrder', id(2), new AbortController().signal)).type).toBe('ok-single')
    expect(fetcher.mock.calls[0][0]).toContain('/sources/1/' + id(2))
    expect(fetcher.mock.calls[0][1].cache).toBe('no-store')
    expect(await fetchAdditionalCosts(id(99), 'GoodsOrder', id(2), new AbortController().signal)).toEqual({ type: 'malformed' })
  })
  it('keeps server-unavailable and invalid replay responses unresolved rather than assuming a rejected write', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(new Response('{}', { status: 503 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ cost: expenseFact(), isReplay: true, originalCorrelationId: 'SENTETIK-old' }),
        { status: 200, headers: { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'SENTETIK-other' } })))
    expect(await recordAdditionalCost(id(1), 'GoodsOrder', request, 'SENTETIK-key')).toEqual({ type: 'unknown', reason: 'service-unavailable', status: 503 })
    expect(await recordAdditionalCost(id(1), 'GoodsOrder', request, 'SENTETIK-key')).toEqual({ type: 'unknown', reason: 'malformed' })
  })
  it('preserves the exact body and idempotency key and reads the original replay receipt', async () => {
    const fetcher = vi.fn().mockResolvedValue(new Response(JSON.stringify({ cost: expenseFact(), isReplay: true, originalCorrelationId: 'SENTETIK-old' }),
      { status: 200, headers: { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'SENTETIK-old' } }))
    vi.stubGlobal('fetch', fetcher)
    expect((await recordAdditionalCost(id(1), 'GoodsOrder', request, 'SENTETIK-key')).type).toBe('applied')
    expect(fetcher.mock.calls[0][1].body).toBe(JSON.stringify(request))
    expect(fetcher.mock.calls[0][1].headers['Idempotency-Key']).toBe('SENTETIK-key')
  })
})
