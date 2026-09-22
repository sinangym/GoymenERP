import { afterEach, describe, expect, it, vi } from 'vitest'
import { fetchPurchaseOrder, fetchPurchaseOrders } from './api'
import { approvePurchaseOrder } from './approvalApi'

const company = '22222222-2222-4222-8222-222222222222'
const order = '11111111-1111-4111-8111-111111111111'
const summary = { id: order, companyId: company, status: 'Draft', version: '1', supplierPartyId: '33333333-3333-4333-8333-333333333333', companyAccountId: '44444444-4444-4444-8444-444444444444', sourceRequestId: null, effectiveOn: '2026-09-22', createdAtUtc: '2026-09-22T10:00:00Z', updatedAtUtc: '2026-09-22T10:00:00Z', lineCount: 0 }

afterEach(() => vi.unstubAllGlobals())

describe('purchasing response binding', () => {
  it('refuses list and detail data from another company or order', async () => {
    const fetch = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify({ items: [{ ...summary, companyId: '99999999-9999-4999-8999-999999999999' }], nextCursor: null }), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ order: { ...summary, id: '88888888-8888-4888-8888-888888888888' }, lines: [] }), { status: 200 }))
    vi.stubGlobal('fetch', fetch)
    expect((await fetchPurchaseOrders(company)).type).toBe('malformed')
    expect((await fetchPurchaseOrder(company, order)).type).toBe('malformed')
  })

  it('treats a replay body without matching replay headers as unknown', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({
      detail: { order: { ...summary, status: 'Approved', version: '2' }, lines: [] },
      isReplay: true, originalCorrelationId: 'corr-1',
    }), { status: 200 })))
    expect(await approvePurchaseOrder(company, order, '1', 'key-1')).toEqual({ type: 'unknown', reason: 'undecodable' })
  })
})
