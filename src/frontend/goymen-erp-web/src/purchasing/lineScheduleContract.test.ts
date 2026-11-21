import { afterEach, describe, expect, it, vi } from 'vitest'
import { decodePurchaseOrderLineScheduleHistory } from './lineScheduleContract'
import { fetchPurchaseOrderLineScheduleHistory } from './lineScheduleApi'

const COMPANY = '22222222-2222-4222-8222-222222222222'
const ORDER = '11111111-1111-4111-8111-111111111111'
const LINE = '77777777-7777-4777-8777-777777777777'
const SLICE_A = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'
const SLICE_B = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb'

function revision(orderVersion: string, over: Record<string, unknown> = {}) {
  return {
    orderVersion, recordedBy: 'SENTETIK-recorder', recordedAtUtc: '2026-11-03T09:10:00Z',
    slices: [{ sliceId: SLICE_A, sliceNo: 1, quantity: '4', deliveryDate: '2026-11-20' },
      { sliceId: SLICE_B, sliceNo: 2, quantity: '6.5', deliveryDate: '2026-12-04' }],
    ...over,
  }
}

function body(over: Record<string, unknown> = {}) {
  return { orderId: ORDER, lineId: LINE, orderVersion: '5', revisions: [revision('3'), revision('5')], ...over }
}

afterEach(() => { vi.unstubAllGlobals() })

describe('order line schedule history decoder (F-027; ALS-D94)', () => {
  it('accepts exactly the asked order and line with rising revisions up to the order version, and an empty history', () => {
    expect(decodePurchaseOrderLineScheduleHistory(body(), ORDER, LINE)).toEqual(body())
    expect(decodePurchaseOrderLineScheduleHistory(body({ revisions: [] }), ORDER, LINE)?.revisions).toEqual([])
    expect(decodePurchaseOrderLineScheduleHistory(body({ orderId: ORDER.toUpperCase() }), ORDER, LINE)).not.toBeNull()
  })

  it.each<[string, unknown]>([
    ['not an object', []],
    ['an extra field', body({ companyId: COMPANY })],
    ['a missing field', (({ revisions: _, ...rest }) => rest)(body())],
    ['another order', body({ orderId: '33333333-3333-4333-8333-333333333333' })],
    ['another line', body({ lineId: '33333333-3333-4333-8333-333333333333' })],
    ['a nil line id', body({ lineId: '00000000-0000-0000-0000-000000000000' })],
    ['a non-canonical order version', body({ orderVersion: '05' })],
    ['revisions that are not a list', body({ revisions: {} })],
    ['a revision past the order version', body({ revisions: [revision('3'), revision('6')] })],
    ['revisions out of order', body({ revisions: [revision('5'), revision('3')] })],
    ['a repeated revision version', body({ revisions: [revision('3'), revision('3')] })],
    ['a revision with an extra field', body({ revisions: [revision('3', { approvedBy: 'x' })] })],
    ['a blank recorder', body({ revisions: [revision('3', { recordedBy: ' ' })] })],
    ['a padded recorder', body({ revisions: [revision('3', { recordedBy: ' SENTETIK ' })] })],
    ['a bad instant', body({ revisions: [revision('3', { recordedAtUtc: '2026-11-03 09:10' })] })],
    ['a revision without slices', body({ revisions: [revision('3', { slices: [] })] })],
    ['a slice out of position', body({ revisions: [revision('3', { slices: [{ sliceId: SLICE_A, sliceNo: 2, quantity: '4', deliveryDate: '2026-11-20' }] })] })],
    ['a zero slice quantity', body({ revisions: [revision('3', { slices: [{ sliceId: SLICE_A, sliceNo: 1, quantity: '0', deliveryDate: '2026-11-20' }] })] })],
    ['a repeated slice id', body({ revisions: [revision('3', { slices: [
      { sliceId: SLICE_A, sliceNo: 1, quantity: '4', deliveryDate: '2026-11-20' },
      { sliceId: SLICE_A, sliceNo: 2, quantity: '6', deliveryDate: '2026-11-21' }] })] })],
    ['an impossible date', body({ revisions: [revision('3', { slices: [{ sliceId: SLICE_A, sliceNo: 1, quantity: '4', deliveryDate: '2026-02-30' }] })] })],
  ])('refuses %s as a whole, never a partial history', (_, value) => {
    expect(decodePurchaseOrderLineScheduleHistory(value, ORDER, LINE)).toBeNull()
  })

  it('reads the line\'s history path with the company header and maps the answers to view outcomes', async () => {
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)
    fetchMock.mockResolvedValueOnce(new Response(JSON.stringify(body()), { status: 200 }))
    const ok = await fetchPurchaseOrderLineScheduleHistory(COMPANY, ORDER, LINE)
    expect(ok).toEqual({ type: 'ok-single', data: body() })
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url.endsWith(`/purchasing/orders/${ORDER}/lines/${LINE}/schedule-history`)).toBe(true)
    expect(init.method ?? 'GET').toBe('GET')
    expect(Object.values(init.headers as Record<string, string>)).toContain(COMPANY)

    fetchMock.mockResolvedValueOnce(new Response(JSON.stringify(body({ lineId: SLICE_A })), { status: 200 }))
    expect(await fetchPurchaseOrderLineScheduleHistory(COMPANY, ORDER, LINE)).toEqual({ type: 'malformed' })
    fetchMock.mockResolvedValueOnce(new Response(JSON.stringify({ code: 'service_unavailable' }), { status: 503 }))
    expect(await fetchPurchaseOrderLineScheduleHistory(COMPANY, ORDER, LINE))
      .toEqual({ type: 'http', status: 503, body: { code: 'service_unavailable' } })
    fetchMock.mockRejectedValueOnce(new TypeError('SENTETIK no answer'))
    expect((await fetchPurchaseOrderLineScheduleHistory(COMPANY, ORDER, LINE)).type).toMatch(/^(network-error|offline)$/)
  })
})
