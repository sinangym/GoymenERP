import { afterEach, describe, expect, it, vi } from 'vitest'
import { fetchFutureAllocations, fetchFutureSupplyOptions, sendFutureAllocation, type FutureAllocationPending } from './futureAllocationApi'

const id = '11111111-1111-4111-8111-111111111111'
const other = '22222222-2222-4222-8222-222222222222'
const pending: FutureAllocationPending = { kind: 'release', key: 'SENTETIK-key', allocationId: id,
  payload: { expectedAllocationVersion: '1', quantityText: null, reasonCode: 'manual-release', reasonText: 'Sentetik gerekçe' } }
afterEach(() => { vi.restoreAllMocks(); vi.unstubAllGlobals() })
describe('future allocation HTTP', () => {
  it('keeps an unknown result transport unchanged and accepts a refused replay only with coherent headers', async () => {
    const fetch = vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(new Response('{}', { status: 503 }))
      .mockResolvedValueOnce(new Response('{"code":"inventory_allocation_version_conflict"}', { status: 409,
        headers: { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'SENTETIK-first' } }))
    expect(await sendFutureAllocation(id, id, id, pending)).toEqual({ type: 'unknown', reason: 'service-unavailable' })
    expect(await sendFutureAllocation(id, id, id, pending)).toEqual({ type: 'failed', status: 409, code: 'inventory_allocation_version_conflict' })
    expect(fetch.mock.calls[0]).toEqual(fetch.mock.calls[1])
  })
  it('rejects another company read and opaque cursor misuse without interpreting an empty success', async () => {
    const mock = vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(new Response(JSON.stringify({ companyId: other, salesOrderId: id,
      salesOrderLineId: id, items: [], windowFull: false, unitCodes: {} }))).mockResolvedValueOnce(new Response('{}', { status: 400 }))
    expect(await fetchFutureAllocations(id, id, id)).toEqual({ type: 'malformed' })
    expect((await fetchFutureSupplyOptions(id, id, id, 'opaque:+/=')).type).toBe('http')
    expect(String(mock.mock.calls[1][0])).toContain('cursor=opaque%3A%2B%2F%3D')
    expect(mock.mock.calls[0][1]).toMatchObject({ cache: 'no-store', headers: { 'X-Company-Id': id } })
  })
  it.each([401, 403, 404, 409, 422])('preserves terminal HTTP %s separately from service unavailability', async status => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response('{}', { status }))
    expect(await sendFutureAllocation(id, id, id, pending)).toEqual({ type: 'failed', status, code: null })
  })
})
