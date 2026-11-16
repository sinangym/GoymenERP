import { afterEach, describe, expect, it, vi } from 'vitest'
import { decodeSupplierReturn, decodeSupplierReturnPage } from './supplierReturnContract'
import { fetchSupplierReturns } from './supplierReturnApi'

const COMPANY = '22222222-2222-4222-8222-222222222222'
const ORDER = '11111111-1111-4111-8111-111111111111'
const OTHER = '33333333-3333-4333-8333-333333333333'

function supplierReturn(over: Record<string, unknown> = {}) {
  return {
    companyId: COMPANY, returnId: '44444444-4444-4444-8444-444444444444', returnLineId: '55555555-5555-4555-8555-555555555555',
    purchaseOrderId: ORDER, purchaseOrderLineId: '66666666-6666-4666-8666-666666666666',
    goodsAcceptanceId: '77777777-7777-4777-8777-777777777777', inventoryMovementId: '88888888-8888-4888-8888-888888888888',
    status: 'Recorded', version: '1', sourceBlock: 'Blocked', quantityKg: '120.5', costBasis: 'InvoiceUnitCost',
    unitCostTryPerKg: '41.2575', replacementExpected: false, reason: 'SENTETIK hatalı ölçü', occurredAtUtc: '2026-11-16T06:00:00Z',
    requestedBy: 'SENTETIK-iade-giren', requestedAtUtc: '2026-11-16T06:01:00Z', decidedBy: null, decidedAtUtc: null,
    decisionReason: null,
    ...over,
  }
}

const pending = { costBasis: 'ManualCostWithException', status: 'PendingCostException' }
const decided = { ...pending, version: '2', decidedBy: 'SENTETIK-istisna-onaylayan', decidedAtUtc: '2026-11-16T07:00:00Z' }

afterEach(() => { vi.unstubAllGlobals() })

describe('supplier return decoder (B-19; SAT-D306, SAT-D393, ALS-D91)', () => {
  it('accepts an invoice-cost return, a pending, an approved and a rejected manual-cost return', () => {
    expect(decodeSupplierReturn(supplierReturn(), COMPANY, ORDER)).toEqual(supplierReturn())
    expect(decodeSupplierReturn(supplierReturn(pending), COMPANY, ORDER)).not.toBeNull()
    expect(decodeSupplierReturn(supplierReturn({ ...decided, status: 'Recorded' }), COMPANY, ORDER)).not.toBeNull()
    expect(decodeSupplierReturn(supplierReturn({ ...decided, status: 'Rejected', decisionReason: 'SENTETIK belge yok' }), COMPANY, ORDER))
      .not.toBeNull()
    expect(decodeSupplierReturnPage({ items: [supplierReturn()], searchWindowFull: false }, COMPANY, ORDER)?.items).toHaveLength(1)
    expect(decodeSupplierReturnPage({ items: [], searchWindowFull: true }, COMPANY, ORDER)?.items).toEqual([])
  })

  it('refuses another company or order, extra or missing fields, unknown names and non-canonical numbers', () => {
    expect(decodeSupplierReturn(supplierReturn({ companyId: OTHER }), COMPANY, ORDER)).toBeNull()
    expect(decodeSupplierReturn(supplierReturn({ purchaseOrderId: OTHER }), COMPANY, ORDER)).toBeNull()
    expect(decodeSupplierReturn({ ...supplierReturn(), extra: 1 }, COMPANY, ORDER)).toBeNull()
    const missing = Object.fromEntries(Object.entries(supplierReturn()).filter(([name]) => name !== 'reason'))
    expect(decodeSupplierReturn(missing, COMPANY, ORDER)).toBeNull()
    expect(decodeSupplierReturn(supplierReturn({ status: 'Posted' }), COMPANY, ORDER)).toBeNull()
    expect(decodeSupplierReturn(supplierReturn({ sourceBlock: 'free' }), COMPANY, ORDER)).toBeNull()
    expect(decodeSupplierReturn(supplierReturn({ costBasis: 'Average' }), COMPANY, ORDER)).toBeNull()
    for (const quantityKg of ['0', '1.50', '01', '-1', '1e3', 12]) {
      expect(decodeSupplierReturn(supplierReturn({ quantityKg }), COMPANY, ORDER)).toBeNull()
    }
    expect(decodeSupplierReturn(supplierReturn({ unitCostTryPerKg: '0' }), COMPANY, ORDER)).toBeNull()
    expect(decodeSupplierReturn(supplierReturn({ replacementExpected: true }), COMPANY, ORDER)).toBeNull()
    expect(decodeSupplierReturn(supplierReturn({ reason: ' ' }), COMPANY, ORDER)).toBeNull()
  })

  it('refuses incoherent status, cost basis and decision facts', () => {
    expect(decodeSupplierReturn(supplierReturn({ status: 'PendingCostException' }), COMPANY, ORDER)).toBeNull()
    expect(decodeSupplierReturn(supplierReturn({ decidedBy: 'SENTETIK-x', decidedAtUtc: '2026-11-16T07:00:00Z' }), COMPANY, ORDER))
      .toBeNull()
    expect(decodeSupplierReturn(supplierReturn({ ...pending, decidedBy: 'SENTETIK-x', decidedAtUtc: '2026-11-16T07:00:00Z' }),
      COMPANY, ORDER)).toBeNull()
    expect(decodeSupplierReturn(supplierReturn({ ...decided, status: 'Recorded', decidedAtUtc: null }), COMPANY, ORDER)).toBeNull()
    expect(decodeSupplierReturn(supplierReturn({ ...decided, status: 'Recorded', decisionReason: 'SENTETIK' }), COMPANY, ORDER))
      .toBeNull()
    expect(decodeSupplierReturn(supplierReturn({ ...decided, status: 'Rejected' }), COMPANY, ORDER)).toBeNull()
    expect(decodeSupplierReturn(supplierReturn({ ...pending, status: 'Recorded' }), COMPANY, ORDER)).toBeNull()
  })

  it('refuses a page with a duplicate return, a bad item or an extra key', () => {
    expect(decodeSupplierReturnPage({ items: [supplierReturn(), supplierReturn()], searchWindowFull: false }, COMPANY, ORDER)).toBeNull()
    expect(decodeSupplierReturnPage({ items: [supplierReturn({ companyId: OTHER })], searchWindowFull: false }, COMPANY, ORDER))
      .toBeNull()
    expect(decodeSupplierReturnPage({ items: [], searchWindowFull: false, next: null }, COMPANY, ORDER)).toBeNull()
    expect(decodeSupplierReturnPage({ items: [], searchWindowFull: 'no' }, COMPANY, ORDER)).toBeNull()
  })
})

describe('supplier return list client (B-19)', () => {
  it('reads the order route with the company header and maps every outcome', async () => {
    const page = { items: [supplierReturn()], searchWindowFull: false }
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)
    fetchMock.mockResolvedValueOnce(new Response(JSON.stringify(page), { status: 200 }))
    expect(await fetchSupplierReturns(COMPANY, ORDER)).toEqual({ type: 'ok-single', data: page })
    const [url] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toContain(`/${ORDER}/supplier-returns`)
    fetchMock.mockResolvedValueOnce(new Response(JSON.stringify({ items: [supplierReturn({ companyId: OTHER })], searchWindowFull: false }),
      { status: 200 }))
    expect(await fetchSupplierReturns(COMPANY, ORDER)).toEqual({ type: 'malformed' })
    fetchMock.mockResolvedValueOnce(new Response(JSON.stringify({ code: 'purchasing_supplier_return_unavailable' }), { status: 503 }))
    expect(await fetchSupplierReturns(COMPANY, ORDER))
      .toEqual({ type: 'http', status: 503, body: { code: 'purchasing_supplier_return_unavailable' } })
    fetchMock.mockRejectedValueOnce(new TypeError('SENTETIK no answer'))
    expect((await fetchSupplierReturns(COMPANY, ORDER)).type).toMatch(/^(network-error|offline)$/)
  })
})
