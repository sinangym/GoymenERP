import { describe, expect, it } from 'vitest'
import { decodePurchaseOrderDetail, decodePurchaseOrderList } from './contract'

const COMPANY_GDC = '10000000-0000-0000-0000-000000000001'
const summary = {
  id: '11111111-1111-4111-8111-111111111111', companyId: COMPANY_GDC,
  status: 'Draft', version: '1', supplierPartyId: '33333333-3333-4333-8333-333333333333',
  companyAccountId: '44444444-4444-4444-8444-444444444444', sourceRequestId: null,
  effectiveOn: '2026-09-22', createdAtUtc: '2026-09-22T10:00:00Z', updatedAtUtc: '2026-09-22T10:00:00Z', lineCount: 1,
}
const line = {
  lineId: '55555555-5555-4555-8555-555555555555', lineNo: 1, kind: 'General', parentLineId: null,
  sourceRequestLineId: null, productId: '66666666-6666-4666-8666-666666666666', productVersion: '7',
  unitId: '77777777-7777-4777-8777-777777777777', ordered: '10', technical: '0', committed: '10', accepted: '0', remaining: '10',
}

describe('purchasing read contract', () => {
  it('decodes exact list and detail shapes', () => {
    expect(decodePurchaseOrderList({ items: [summary], nextCursor: null })?.items[0].companyId).toBe(COMPANY_GDC)
    const detail = decodePurchaseOrderDetail({ order: summary, lines: [line] })
    expect(detail?.order.companyId).toBe(COMPANY_GDC)
    expect(detail?.lines[0].remaining).toBe('10')
  })

  it('fails closed on loose fields, bad quantities and a line-count mismatch', () => {
    expect(decodePurchaseOrderList({ items: [{ ...summary, extra: true }], nextCursor: null })).toBeNull()
    expect(decodePurchaseOrderList({ items: [{ ...summary, companyId: 'not-a-guid' }], nextCursor: null })).toBeNull()
    expect(decodePurchaseOrderList({ items: [{ ...summary, companyId: '00000000-0000-0000-0000-000000000000' }], nextCursor: null })).toBeNull()
    expect(decodePurchaseOrderDetail({ order: summary, lines: [{ ...line, remaining: '-1' }] })).toBeNull()
    expect(decodePurchaseOrderDetail({ order: { ...summary, lineCount: 2 }, lines: [line] })).toBeNull()
  })

  it('fails closed on non-canonical versions, invalid instants, dates and line kinds', () => {
    expect(decodePurchaseOrderList({ items: [{ ...summary, version: '02' }], nextCursor: null })).toBeNull()
    expect(decodePurchaseOrderList({ items: [{ ...summary, updatedAtUtc: 'not-an-instant' }], nextCursor: null })).toBeNull()
    expect(decodePurchaseOrderList({ items: [{ ...summary, effectiveOn: '2026-02-30' }], nextCursor: null })).toBeNull()
    expect(decodePurchaseOrderDetail({ order: summary, lines: [{ ...line, kind: 'Unknown' }] })).toBeNull()
    expect(decodePurchaseOrderDetail({ order: summary, lines: [{ ...line, productVersion: '9223372036854775808' }] })).toBeNull()
  })
})
