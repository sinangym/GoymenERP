import { afterEach, describe, expect, it, vi } from 'vitest'
import { decodeSupplierReturnCostAllocation, decodeSupplierReturnCostSources } from './supplierReturnSourceContract'
import { fetchSupplierReturnCostSources } from './supplierReturnSourceApi'

const id = (n: number) => `${n.toString().padStart(8, '0')}-1111-4111-8111-111111111111`
const sources = () => ({ companyId: id(1), goodsAcceptanceId: id(2), labelId: id(3), sourceVersion: 'synthetic-v1',
  pendingQuantityKg: { numerator: '1', denominator: '3' }, pendingUndoQuantity: { numerator: '2', denominator: '3' }, pendingWholePairAvailable: true,
  invoicedSources: [{ sourceEventId: id(4), invoiceId: id(5), invoiceLineId: id(6), invoiceNumber: 'SENTETIK', invoiceLineNo: 1,
    availableQuantityKg: { numerator: '2', denominator: '3' }, availableUndoQuantity: { numerator: '1', denominator: '3' }, wholePairAvailable: true }] })
const decode = (body: unknown) => decodeSupplierReturnCostSources(body, id(1), id(2), id(3))
afterEach(() => vi.unstubAllGlobals())

describe('supplier return explicit source evidence', () => {
  it('keeps exact kg and purchase quantity evidence with invoice labels and rejects wrong owner identities', () => {
    expect(decode(sources())).toEqual(sources())
    for (const field of ['companyId', 'goodsAcceptanceId', 'labelId']) expect(decode({ ...sources(), [field]: id(9) })).toBeNull()
  })
  it('rejects malformed ratios, duplicate sources, price leakage and missing invoice labels', () => {
    expect(decode({ ...sources(), pendingQuantityKg: { numerator: '1', denominator: '0' } })).toBeNull()
    expect(decode({ ...sources(), pendingQuantityKg: { numerator: '-1', denominator: '3' } })).toBeNull()
    expect(decode({ ...sources(), pendingQuantityKg: { numerator: '1'.repeat(513), denominator: '3' } })).toBeNull()
    expect(decode({ ...sources(), invoicedSources: [sources().invoicedSources[0], sources().invoicedSources[0]] })).toBeNull()
    expect(decode({ ...sources(), unitCostTryPerKg: '10' })).toBeNull()
    expect(decode({ ...sources(), invoicedSources: [{ ...sources().invoicedSources[0], invoiceNumber: '' }] })).toBeNull()
  })
  it('accepts independent measured source pairs or explicit whole shares without calculating totals', () => {
    const value = { expectedSourceVersion: 'synthetic-v1', pending: { takeAll: false, quantityKg: '1.9', undoQuantity: '2' },
      invoicedSources: [{ sourceEventId: id(4), takeAll: true, quantityKg: null, undoQuantity: null }] }
    expect(decodeSupplierReturnCostAllocation(value)).toEqual(value)
    expect(decodeSupplierReturnCostAllocation({ ...value, pending: { takeAll: true, quantityKg: null, undoQuantity: '2' } })).not.toBeNull()
    expect(decodeSupplierReturnCostAllocation({ ...value, pending: { takeAll: true, quantityKg: null, undoQuantity: '0' } })).toBeNull()
    expect(decodeSupplierReturnCostAllocation({ ...value, pending: { takeAll: true, quantityKg: '1', undoQuantity: null } })).toBeNull()
    expect(decodeSupplierReturnCostAllocation({ ...value, pending: { takeAll: false, quantityKg: '1.900', undoQuantity: '2' } })).toBeNull()
    expect(decodeSupplierReturnCostAllocation({ ...value, pending: { takeAll: false, quantityKg: '0', undoQuantity: '1' } })).toBeNull()
    expect(decodeSupplierReturnCostAllocation({ ...value, pending: { takeAll: false, quantityKg: '1', undoQuantity: '0' } })).toBeNull()
    expect(decodeSupplierReturnCostAllocation({ ...value, pending: { takeAll: false, quantityKg: '0', undoQuantity: '0' } })).not.toBeNull()
    expect(decodeSupplierReturnCostAllocation({ ...value, invoicedSources: [value.invoicedSources[0], value.invoicedSources[0]] })).toBeNull()
  })
  it('uses the receipt route and company fence and never accepts a malformed success', async () => {
    const fetch = vi.fn().mockResolvedValueOnce(new Response(JSON.stringify(sources()), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ ...sources(), labelId: id(9) }), { status: 200 }))
    vi.stubGlobal('fetch', fetch)
    expect(await fetchSupplierReturnCostSources(id(1), id(7), id(2), id(3))).toEqual({ type: 'ok-single', data: sources() })
    expect(fetch.mock.calls[0][0]).toContain(`/orders/${id(7)}/goods-receipts/${id(2)}/supplier-return-cost-sources`)
    expect(fetch.mock.calls[0][1].headers['X-Company-Id']).toBe(id(1))
    expect(await fetchSupplierReturnCostSources(id(1), id(7), id(2), id(3))).toEqual({ type: 'malformed' })
  })
})
