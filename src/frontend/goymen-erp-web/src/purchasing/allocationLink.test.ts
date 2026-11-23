import { afterEach, describe, expect, it, vi } from 'vitest'
import { decodePurchaseFutureAllocationReleaseResponse, decodePurchaseOrderFutureAllocationLinks } from './allocationLinkContract'
import { fetchPurchaseOrderFutureAllocations, releasePurchaseOrderFutureAllocation } from './allocationLinkApi'

const companyId = '11111111-1111-4111-8111-111111111111'
const orderId = '22222222-2222-4222-8222-222222222222'
const lineId = '33333333-3333-4333-8333-333333333333'
const allocationId = '44444444-4444-4444-8444-444444444444'
const salesOrderId = '55555555-5555-4555-8555-555555555555'
const salesOrderLineId = '66666666-6666-4666-8666-666666666666'
const unitId = '77777777-7777-4777-8777-777777777777'
const key = '88888888-8888-4888-8888-888888888888'
const at = '2026-11-12T09:00:00Z'

const link = (extra: Record<string, unknown> = {}) => ({
  allocationId, version: '3', lineId, salesOrderId, salesOrderLineId, unitId,
  allocatedQuantity: '5', remainingQuantity: '2.5', createdAtUtc: at, ...extra,
})
const links = (extra: Record<string, unknown> = {}) => ({ orderId, orderVersion: '4', links: [link()], ...extra })
const release = (extra: Record<string, unknown> = {}) => ({
  orderId, orderVersion: '4', allocationId, isReplay: false, originalCorrelationId: 'corr-1', ...extra,
})

function respond(status: number, body: unknown, headers: Record<string, string> = {}) {
  vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify(body), { status, headers })))
}

afterEach(() => { vi.unstubAllGlobals() })

describe('F17-4 future allocation link contract (ALS-D83/ALS-D85)', () => {
  it('decodes the exact links view and keeps the owner quantity text as sent', () => {
    expect(decodePurchaseOrderFutureAllocationLinks(links())?.links[0]).toEqual(link())
    expect(decodePurchaseOrderFutureAllocationLinks(links({ links: [] }))?.links).toEqual([])
  })

  it.each([
    ['an extra key', links({ extra: 1 })],
    ['a missing key', { orderId, orderVersion: '4' }],
    ['a zero version', links({ orderVersion: '0' })],
    ['an extra link key', links({ links: [link({ extra: 1 })] })],
    ['a numeric quantity', links({ links: [link({ remainingQuantity: 2.5 })] })],
    ['a negative quantity', links({ links: [link({ allocatedQuantity: '-1' })] })],
    ['a nil allocation id', links({ links: [link({ allocationId: '00000000-0000-0000-0000-000000000000' })] })],
    ['a duplicate allocation', links({ links: [link(), link()] })],
    ['a bad instant', links({ links: [link({ createdAtUtc: 'yesterday' })] })],
  ])('refuses the whole links body with %s', (_, body) => {
    expect(decodePurchaseOrderFutureAllocationLinks(body)).toBeNull()
  })

  it('decodes the release response strictly', () => {
    expect(decodePurchaseFutureAllocationReleaseResponse(release())).toEqual(release())
    expect(decodePurchaseFutureAllocationReleaseResponse(release({ isReplay: 'false' }))).toBeNull()
    expect(decodePurchaseFutureAllocationReleaseResponse(release({ originalCorrelationId: '' }))).toBeNull()
    expect(decodePurchaseFutureAllocationReleaseResponse({ ...release(), order: {} })).toBeNull()
  })
})

describe('F17-4 future allocation link API', () => {
  it('reads the links of the order asked for and refuses another order', async () => {
    respond(200, links())
    expect((await fetchPurchaseOrderFutureAllocations(companyId, orderId)).type).toBe('ok-single')
    respond(200, links({ orderId: lineId }))
    expect((await fetchPurchaseOrderFutureAllocations(companyId, orderId)).type).toBe('malformed')
    respond(403, { code: 'forbidden' })
    expect(await fetchPurchaseOrderFutureAllocations(companyId, orderId)).toEqual({ type: 'http', status: 403, body: { code: 'forbidden' } })
  })

  it('posts the allocation version and reason with the key and accepts an unchanged order version', async () => {
    respond(200, release())
    const outcome = await releasePurchaseOrderFutureAllocation(companyId, orderId, '4', { allocationId, expectedAllocationVersion: '3' }, key, 'SENTETIK bağ')
    expect(outcome).toEqual({ type: 'applied', response: release(), replayed: false })
    const [url, init] = vi.mocked(fetch).mock.calls[0] as [string, RequestInit]
    expect(url).toContain(`/${orderId}/future-allocations/${allocationId}/release`)
    expect(JSON.parse(init.body as string)).toEqual({ expectedAllocationVersion: '3', reasonText: 'SENTETIK bağ' })
    expect((init.headers as Record<string, string>)['Idempotency-Key']).toBe(key)
  })

  it('treats a changed order version, a foreign allocation or mismatched replay headers as unknown', async () => {
    const send = () => releasePurchaseOrderFutureAllocation(companyId, orderId, '4', { allocationId, expectedAllocationVersion: '3' }, key, 'x')
    respond(200, release({ orderVersion: '5' }))
    expect(await send()).toEqual({ type: 'unknown', reason: 'undecodable' })
    respond(200, release({ allocationId: lineId }))
    expect(await send()).toEqual({ type: 'unknown', reason: 'undecodable' })
    respond(200, release({ isReplay: true }))
    expect(await send()).toEqual({ type: 'unknown', reason: 'undecodable' })
    respond(200, release({ isReplay: true }), { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'corr-1' })
    expect((await send()).type).toBe('applied')
  })

  it('maps definite refusals to failed with their code and a 5xx to unknown', async () => {
    const send = () => releasePurchaseOrderFutureAllocation(companyId, orderId, '4', { allocationId, expectedAllocationVersion: '3' }, key, 'x')
    respond(409, { code: 'purchasing_future_allocation_version_conflict' })
    expect(await send()).toEqual({ type: 'failed', status: 409, code: 'purchasing_future_allocation_version_conflict' })
    respond(422, { code: 'purchasing_future_allocation_release_reason_required' })
    expect(await send()).toEqual({ type: 'failed', status: 422, code: 'purchasing_future_allocation_release_reason_required' })
    respond(503, { code: 'service_unavailable' })
    expect(await send()).toEqual({ type: 'unknown', reason: 'service-unavailable' })
  })
})
