import { describe, expect, it } from 'vitest'
import type { PurchaseOrderDetail } from './contract'
import { decodeGoodsReceiptOptions, decodeGoodsReceiptPage, decodeGoodsReceiptResponse, type GoodsReceiptRequest } from './goodsReceiptContract'

// Synthetic NOT_ENV data only.
const COMPANY = '11111111-1111-1111-1111-111111111111'
const ORDER = '22222222-2222-2222-2222-222222222222'
const LINE = '33333333-3333-3333-3333-333333333333'
const PRODUCT = '44444444-4444-4444-4444-444444444444'
const UNIT = '55555555-5555-5555-5555-555555555555'
const LOCATION = '66666666-6666-6666-6666-666666666666'
const detail: PurchaseOrderDetail = { order: { id: ORDER, companyId: COMPANY, status: 'Approved', version: '3', supplierPartyId: PRODUCT,
  companyAccountId: UNIT, sourceRequestId: null, effectiveOn: '2026-09-25', createdAtUtc: '2026-09-25T09:00:00Z',
  updatedAtUtc: '2026-09-25T09:00:00Z', lineCount: 1 }, lines: [{ lineId: LINE, lineNo: 1, kind: 'General', parentLineId: null,
  sourceRequestLineId: null, productId: PRODUCT, productVersion: '7', unitId: UNIT, ordered: '10', technical: '0', committed: '10',
  accepted: '0', remaining: '10' }], acceptances: [] }
const request: GoodsReceiptRequest = { expectedPurchaseOrderVersion: '3', expectedProductVersion: '7', acceptanceId: LOCATION,
  acceptedQuantity: '2', measuredQuantityKg: '1.9', locationId: LOCATION, lotId: LINE, labelId: UNIT, labelCode: 'SYNTHETIC',
  heatNumber: null, coilNumber: null, movementId: PRODUCT, occurredAtUtc: '2026-09-25T10:00:00Z' }
const receipt = () => ({ companyId: COMPANY, acceptanceId: LOCATION, purchaseOrderId: ORDER, purchaseOrderLineId: LINE,
  purchaseOrderVersion: '4', productId: PRODUCT, productVersion: '7', unitId: UNIT, acceptedQuantity: '2', remainingQuantity: '8',
  lotId: LINE, labelId: UNIT, labelCode: 'SYNTHETIC', labelVersion: '1', movementId: PRODUCT, locationId: LOCATION,
  measuredQuantityKg: '1.9', initialBlock: 'Free', occurredAtUtc: '2026-09-25T10:00:00Z' })
const options = () => ({
  source: { companyId: COMPANY, purchaseOrderId: ORDER, purchaseOrderLineId: LINE, purchaseOrderVersion: '3', productId: PRODUCT,
    productVersion: '7', unitId: UNIT, effectiveOn: '2026-09-25' },
  locations: [{ locationId: LOCATION, locationCode: 'D-01', locationName: 'SYNTHETIC Mal Kabul', siteName: 'SYNTHETIC Fabrika' }],
  searchWindowFull: false,
  futureSupply: { companyId: COMPANY, purchaseOrderId: ORDER, purchaseOrderGeneralLineId: LINE, unitId: UNIT, activeAllocatedQuantity: '2.125' },
})
const without = <T extends object>(value: T, key: keyof T): Partial<T> => {
  const copy: Partial<T> = { ...value }
  delete copy[key]
  return copy
}

describe('goods receipt decoders accept exactly the contract key set (synthetic NOT_ENV)', () => {
  it('PurchaseGoodsReceiptPageV1: valid page decodes; an extra or a missing key fails closed', () => {
    const page = { items: [receipt()], searchWindowFull: false }
    expect(decodeGoodsReceiptPage(page, detail)?.items).toHaveLength(1)
    expect(decodeGoodsReceiptPage({ ...page, nextCursor: null }, detail)).toBeNull()
    expect(decodeGoodsReceiptPage(without(page, 'searchWindowFull'), detail)).toBeNull()
    expect(decodeGoodsReceiptPage(without(page, 'items'), detail)).toBeNull()
    expect(decodeGoodsReceiptPage({ ...page, items: [{ ...receipt(), unitPrice: '1' }] }, detail)).toBeNull()
    expect(decodeGoodsReceiptPage({ ...page, items: [without(receipt(), 'remainingQuantity')] }, detail)).toBeNull()
  })
  it('PurchaseGoodsReceiptOptionsV1: valid options decode; an extra or a missing key fails closed at every level', () => {
    const valid = options()
    expect(decodeGoodsReceiptOptions(valid, detail, detail.lines[0])).toEqual(valid)
    expect(decodeGoodsReceiptOptions({ ...valid, price: '1' }, detail, detail.lines[0])).toBeNull()
    expect(decodeGoodsReceiptOptions(without(valid, 'searchWindowFull'), detail, detail.lines[0])).toBeNull()
    expect(decodeGoodsReceiptOptions(without(valid, 'locations'), detail, detail.lines[0])).toBeNull()
    expect(decodeGoodsReceiptOptions({ ...valid, source: { ...valid.source, supplierPartyId: PRODUCT } }, detail, detail.lines[0])).toBeNull()
    expect(decodeGoodsReceiptOptions({ ...valid, source: without(valid.source, 'effectiveOn') }, detail, detail.lines[0])).toBeNull()
    expect(decodeGoodsReceiptOptions({ ...valid, locations: [{ ...valid.locations[0], siteId: LOCATION }] }, detail, detail.lines[0])).toBeNull()
    expect(decodeGoodsReceiptOptions({ ...valid, locations: [without(valid.locations[0], 'siteName')] }, detail, detail.lines[0])).toBeNull()
    expect(decodeGoodsReceiptOptions({ ...valid, futureSupply: { ...valid.futureSupply, price: '1' } }, detail, detail.lines[0])).toBeNull()
    expect(decodeGoodsReceiptOptions({ ...valid, futureSupply: without(valid.futureSupply, 'unitId') }, detail, detail.lines[0])).toBeNull()
  })
  it('PurchaseGoodsReceiptResponseV1: valid answer decodes with its optional members; an extra or a missing key fails closed', () => {
    const answer = { receipt: receipt(), isReplay: false, originalCorrelationId: 'SYNTHETIC-correlation' }
    const release = { futureAllocationId: ORDER, releasedQuantity: '7.5', failureCode: 'demand-not-approved' }
    expect(decodeGoodsReceiptResponse(answer, detail, LINE, request, false)?.receipt.acceptanceId).toBe(LOCATION)
    expect(decodeGoodsReceiptResponse({ ...answer, futureSupplyReleases: [release] }, detail, LINE, request, false)?.futureSupplyReleases).toEqual([release])
    expect(decodeGoodsReceiptResponse({ ...answer, unitCost: '1' }, detail, LINE, request, false)).toBeNull()
    expect(decodeGoodsReceiptResponse(without(answer, 'originalCorrelationId'), detail, LINE, request, false)).toBeNull()
    expect(decodeGoodsReceiptResponse(without(answer, 'isReplay'), detail, LINE, request, false)).toBeNull()
    expect(decodeGoodsReceiptResponse(without(answer, 'receipt'), detail, LINE, request, false)).toBeNull()
    expect(decodeGoodsReceiptResponse({ ...answer, receipt: { ...receipt(), unitCost: '1' } }, detail, LINE, request, false)).toBeNull()
    expect(decodeGoodsReceiptResponse({ ...answer, receipt: without(receipt(), 'labelVersion') }, detail, LINE, request, false)).toBeNull()
  })
})
