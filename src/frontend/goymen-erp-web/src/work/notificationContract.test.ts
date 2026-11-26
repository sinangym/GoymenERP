import { describe, expect, it } from 'vitest'
import { decodeWorkNotifications } from './notificationContract'
const company = 'aaaaaaaa-0000-4000-8000-000000000001'
const item = { id: 'bbbbbbbb-0000-4000-8000-000000000001', companyId: company, kind: 'allocation-released', salesOrderId: 'cccccccc-0000-4000-8000-000000000001', salesOrderLineId: 'dddddddd-0000-4000-8000-000000000001', sourceEntityId: 'eeeeeeee-0000-4000-8000-000000000001', sourceVersion: '9223372036854775807', reasonCode: 'acceptance-link-failed', reasonText: 'SENTETIK ayrım bırakıldı', occurredAtUtc: '2026-11-17T00:00:00Z', recordedAtUtc: '2026-11-17T00:00:01Z', passivatedAtUtc: null, purchaseOrderId: null, purchaseOrderLineId: null }
describe('notification contract', () => {
  const stale = { ...item, kind: 'accounting-cost-proposal-stale', salesOrderId: null, salesOrderLineId: null,
    sourceVersion: '3', reasonCode: 'inventory_cost_preparation_stale', reasonText: 'Bayatladı' }
  it('accepts one stale proposal beside 99 existing notifications and preserves the page cursor', () => {
    const items = [...Array.from({ length: 99 }, (_, index) => ({ ...item,
      id: `bbbbbbbb-0000-4000-8000-${(index + 2).toString().padStart(12, '0')}` })), stale]
    expect(decodeWorkNotifications({ companyId: company, items, hasMore: true, nextCursor: stale.id }, company)?.items).toEqual(items)
  })
  it('requires exact stale evidence, company and a real proposal identity without loosening other variants', () => {
    for (const delta of [{ companyId: item.id }, { sourceEntityId: null }, { sourceEntityId: '00000000-0000-0000-0000-000000000000' },
      { sourceVersion: '2' }, { sourceVersion: '03' }, { reasonCode: 'inventory_cost_other' }, { reasonCode: 'inventory-cost-preparation-stale' },
      { reasonText: 'SENTETIK invented' }, { salesOrderId: item.salesOrderId }, { purchaseOrderId: item.salesOrderId },
      { salesOrderLineId: item.salesOrderLineId }, { purchaseOrderLineId: item.salesOrderLineId }, { extra: true }])
      expect(decodeWorkNotifications({ companyId: company, items: [{ ...stale, ...delta }], hasMore: false, nextCursor: null }, company)).toBeNull()
    for (const existing of [item, { ...item, kind: 'purchase-supply-revived' }]) {
      expect(decodeWorkNotifications({ companyId: company, items: [{ ...existing, reasonCode: stale.reasonCode }], hasMore: false, nextCursor: null }, company)).toBeNull()
      expect(decodeWorkNotifications({ companyId: company, items: [{ ...existing, salesOrderId: null, salesOrderLineId: null }], hasMore: false, nextCursor: null }, company)).toBeNull()
    }
  })
  it('keeps exact server evidence and passive history without changing quantities or versions', () => {
    expect(decodeWorkNotifications({ companyId: company, items: [item], hasMore: false, nextCursor: null }, company)?.items[0]).toEqual(item)
    expect(decodeWorkNotifications({ companyId: company, items: [{ ...item, passivatedAtUtc: '2027-02-15T00:00:00Z' }], hasMore: false, nextCursor: null }, company)).not.toBeNull()
  })
  it('refuses foreign items duplicates unknown variants malformed dates unsafe versions and missing fields', () => {
    for (const changed of [{ ...item, companyId: item.id }, { ...item, kind: 'invented' }, { ...item, sourceVersion: '9223372036854775808' }, { ...item, sourceVersion: '01' }, { ...item, recordedAtUtc: 'yesterday' }, { ...item, extra: 1 }, { ...item, reasonText: '' }]) expect(decodeWorkNotifications({ companyId: company, items: [changed], hasMore: false, nextCursor: null }, company)).toBeNull()
    expect(decodeWorkNotifications({ companyId: company, items: [item, item], hasMore: false, nextCursor: null }, company)).toBeNull()
    expect(decodeWorkNotifications({ companyId: company, items: [item], hasMore: true, nextCursor: item.id }, company)).toBeNull()
    const { sourceVersion: _removed, ...missing } = item
    expect(decodeWorkNotifications({ companyId: company, items: [missing], hasMore: false, nextCursor: null }, company)).toBeNull()
  })
  it('accepts a purchase recipient without inventing a sales line and refuses mixed or half targets', () => {
    const purchase = { ...item, kind: 'purchase-supply-revived', salesOrderId: null, salesOrderLineId: null, purchaseOrderId: item.salesOrderId, purchaseOrderLineId: item.salesOrderLineId }
    expect(decodeWorkNotifications({ companyId: company, items: [purchase], hasMore: false, nextCursor: null }, company)?.items[0]).toEqual(purchase)
    for (const changed of [{ ...purchase, salesOrderId: item.salesOrderId }, { ...purchase, purchaseOrderLineId: null }, { ...purchase, kind: 'allocation-released' }, { ...item, salesOrderLineId: null }])
      expect(decodeWorkNotifications({ companyId: company, items: [changed], hasMore: false, nextCursor: null }, company)).toBeNull()
  })
})
