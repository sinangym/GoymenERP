import { afterEach, describe, expect, it, vi } from 'vitest'
import { decodeGoodsReceiptBatch, decodeGoodsReceiptBatchPage } from './goodsReceiptBatchContract'
import { fetchGoodsReceiptBatches } from './goodsReceiptBatchApi'

const COMPANY = '22222222-2222-4222-8222-222222222222'
const ORDER = '11111111-1111-4111-8111-111111111111'
const LINE = '77777777-7777-4777-8777-777777777777'
const BATCH = '99999999-9999-4999-8999-999999999999'
const OTHER = '33333333-3333-4333-8333-333333333333'

function label(lineNo: number, over: Record<string, unknown> = {}) {
  const hex = String(lineNo).padStart(12, '0')
  return {
    lineNo,
    acceptanceId: `aaaaaaaa-aaaa-4aaa-8aaa-${hex}`,
    lotId: `bbbbbbbb-bbbb-4bbb-8bbb-${hex}`,
    labelId: `cccccccc-cccc-4ccc-8ccc-${hex}`,
    movementId: `dddddddd-dddd-4ddd-8ddd-${hex}`,
    labelCode: `SENTETIK-ETK-${lineNo}`,
    labelCodeSource: 'SupplierLabelNumber',
    supplierLabelNumber: `SENTETIK-ETK-${lineNo}`,
    measuredQuantityKg: '510',
    heatNumber: null,
    coilNumber: null,
    ...over,
  }
}

function batch(over: Record<string, unknown> = {}) {
  return {
    companyId: COMPANY, batchId: BATCH, purchaseOrderId: ORDER, purchaseOrderLineId: LINE, status: 'Posted', version: '1',
    expectedQuantityKg: '1000', receivedQuantityKg: '1020', differenceKg: '20', varianceOutcome: 'WithinTolerance',
    tolerancePercent: '2', varianceAcknowledged: true, locationId: OTHER, occurredAtUtc: '2026-11-15T06:00:00Z',
    preparedBy: 'SENTETIK-hazirlayan', preparedAtUtc: '2026-11-15T06:01:00Z', decidedBy: null, decidedAtUtc: null,
    decisionReason: null, labels: [label(1), label(2, { labelCode: 'SENTETIK-GR-7', labelCodeSource: 'Generated', supplierLabelNumber: null })],
    ...over,
  }
}

const pending = { status: 'PendingApproval', receivedQuantityKg: '1030', differenceKg: '30', varianceOutcome: 'OutsideTolerance',
  varianceAcknowledged: false }
const approved = { ...pending, status: 'Posted', version: '2', decidedBy: 'SENTETIK-onaylayan', decidedAtUtc: '2026-11-15T07:00:00Z' }

afterEach(() => { vi.unstubAllGlobals() })

describe('bulk goods receipt decoder (F-027; KLN-D41, KLN-D43, KLN-D45)', () => {
  it('accepts a posted inside-tolerance batch, a pending and an approved outside-tolerance one, a rejected one and a short receipt without tolerance', () => {
    expect(decodeGoodsReceiptBatch(batch(), COMPANY, ORDER)).toEqual(batch())
    expect(decodeGoodsReceiptBatch(batch(pending), COMPANY, ORDER)).not.toBeNull()
    expect(decodeGoodsReceiptBatch(batch(approved), COMPANY, ORDER)).not.toBeNull()
    expect(decodeGoodsReceiptBatch(batch({ ...approved, status: 'Rejected', decisionReason: 'SENTETIK gerekçe' }), COMPANY, ORDER))
      .not.toBeNull()
    expect(decodeGoodsReceiptBatch(batch({ ...pending, receivedQuantityKg: '990', differenceKg: '-10', varianceOutcome: 'ToleranceNotSet',
      tolerancePercent: null }), COMPANY, ORDER)).not.toBeNull()
    expect(decodeGoodsReceiptBatch(batch({ receivedQuantityKg: '1000', differenceKg: '0', varianceOutcome: 'Exact', tolerancePercent: null,
      varianceAcknowledged: false }), COMPANY, ORDER)).not.toBeNull()
    expect(decodeGoodsReceiptBatchPage({ items: [batch()], searchWindowFull: false }, COMPANY, ORDER)?.items).toHaveLength(1)
    expect(decodeGoodsReceiptBatchPage({ items: [], searchWindowFull: true }, COMPANY, ORDER)?.items).toEqual([])
  })

  it.each<[string, unknown]>([
    ['not an object', []],
    ['an extra field', batch({ approvedBy: 'x' })],
    ['another company', batch({ companyId: OTHER })],
    ['another order', batch({ purchaseOrderId: OTHER })],
    ['an unknown status', batch({ status: 'Draft' })],
    ['an unknown variance outcome', batch({ varianceOutcome: 'Small' })],
    ['a non-canonical kg', batch({ receivedQuantityKg: '1020.0' })],
    ['an exact outcome with a difference', batch({ varianceOutcome: 'Exact' })],
    ['a not-set outcome with a tolerance', batch({ ...pending, varianceOutcome: 'ToleranceNotSet' })],
    ['an inside-tolerance batch without the acknowledgement', batch({ varianceAcknowledged: false })],
    ['a posted outside-tolerance batch without an approver', batch({ ...pending, status: 'Posted' })],
    ['a pending batch with a decision', batch({ ...approved, status: 'PendingApproval' })],
    ['a rejection without a reason', batch({ ...approved, status: 'Rejected' })],
    ['the preparer deciding', batch({ ...approved, decidedBy: 'SENTETIK-hazirlayan' })],
    ['a batch without labels', batch({ labels: [] })],
    ['labels out of position', batch({ labels: [label(2), label(1)] })],
    ['a zero label kg', batch({ labels: [label(1, { measuredQuantityKg: '0' })] })],
    ['an unknown code source', batch({ labels: [label(1, { labelCodeSource: 'Scanned' })] })],
    ['a supplier-sourced code that is not the supplier number', batch({ labels: [label(1, { supplierLabelNumber: 'SENTETIK-X' })] })],
  ])('refuses %s as a whole, never a partial batch', (_, value) => {
    expect(decodeGoodsReceiptBatch(value, COMPANY, ORDER)).toBeNull()
  })

  it('refuses a page with a repeated batch or a malformed item', () => {
    expect(decodeGoodsReceiptBatchPage({ items: [batch(), batch()], searchWindowFull: false }, COMPANY, ORDER)).toBeNull()
    expect(decodeGoodsReceiptBatchPage({ items: [batch({ status: 'x' })], searchWindowFull: false }, COMPANY, ORDER)).toBeNull()
    expect(decodeGoodsReceiptBatchPage({ items: [batch()] }, COMPANY, ORDER)).toBeNull()
  })

  it('reads the order\'s batch path with the company header and maps the answers to view outcomes', async () => {
    const page = { items: [batch()], searchWindowFull: false }
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)
    fetchMock.mockResolvedValueOnce(new Response(JSON.stringify(page), { status: 200 }))
    expect(await fetchGoodsReceiptBatches(COMPANY, ORDER)).toEqual({ type: 'ok-single', data: page })
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url.endsWith(`/purchasing/orders/${ORDER}/goods-receipt-batches`)).toBe(true)
    expect(init.method ?? 'GET').toBe('GET')
    expect(Object.values(init.headers as Record<string, string>)).toContain(COMPANY)

    fetchMock.mockResolvedValueOnce(new Response(JSON.stringify({ items: [batch({ companyId: OTHER })], searchWindowFull: false }), { status: 200 }))
    expect(await fetchGoodsReceiptBatches(COMPANY, ORDER)).toEqual({ type: 'malformed' })
    fetchMock.mockResolvedValueOnce(new Response(JSON.stringify({ code: 'service_unavailable' }), { status: 503 }))
    expect(await fetchGoodsReceiptBatches(COMPANY, ORDER)).toEqual({ type: 'http', status: 503, body: { code: 'service_unavailable' } })
    fetchMock.mockRejectedValueOnce(new TypeError('SENTETIK no answer'))
    expect((await fetchGoodsReceiptBatches(COMPANY, ORDER)).type).toMatch(/^(network-error|offline)$/)
  })
})
