import { describe, expect, it } from 'vitest'
import { decodeFutureAllocationList, decodeFutureSupplyOptions } from './futureAllocationContract'

const id = '11111111-1111-4111-8111-111111111111'
const other = '22222222-2222-4222-8222-222222222222'
const allocation = { allocationId: id, companyId: id, version: '9223372036854775807', status: 'Active', salesOrderId: id, salesOrderLineId: id,
  purchaseOrderId: id, purchaseOrderLineId: id, productId: id, productVersion: '1', unitId: id, allocatedQuantity: '100.001', remainingQuantity: '90.001',
  releasedQuantity: '0', consumedQuantity: '10', manualSequence: null, reason: 'Sentetik gerekçe', releaseReasonCode: null,
  releaseOrigin: null, releaseReason: null, createdAtUtc: '2026-11-16T09:00:00Z', updatedAtUtc: '2026-11-16T09:00:00Z', releasedAtUtc: null }
const list = { companyId: id, salesOrderId: id, salesOrderLineId: id, items: [allocation], windowFull: false, unitCodes: { [id]: 'kg' },
  evaluatedOn: '2026-11-17', schedules: { [id]: { status: 'Scheduled', deliveryDate: '2026-11-16', isOverdue: true } } }
const options = { companyId: id, salesOrderId: id, salesOrderLineId: id, productId: id, productVersion: '1', demandUnitId: id,
  demandUnitCode: 'kg', openDemandQuantity: '100', items: [{ purchaseOrderId: id, purchaseOrderLineId: id, lineNo: 1,
    supplierCode: 'SYN-1', supplierName: 'Sentetik tedarikçi', unitId: id, unitCode: 'kg', allocatableQuantity: '50.001', reservedQuantity: '10', availableQuantity: '40.001' }], nextCursor: null }

describe('future allocation read contracts', () => {
  it('retains exact server quantities, versions and unit names', () => {
    expect(decodeFutureAllocationList(list)).toEqual(list)
    expect(decodeFutureSupplyOptions(options)).toEqual(options)
  })
  it('rejects price fields, duplicated sources and missing unit identities', () => {
    expect(decodeFutureSupplyOptions({ ...options, items: [{ ...options.items[0], price: '1' }] })).toBeNull()
    expect(decodeFutureSupplyOptions({ ...options, items: [options.items[0], options.items[0]] })).toBeNull()
    expect(decodeFutureAllocationList({ ...list, unitCodes: {} })).toBeNull()
    expect(decodeFutureAllocationList({ ...list, items: [{ ...allocation, companyId: other }] })).toBeNull()
    expect(decodeFutureAllocationList({ ...list, items: [{ ...allocation, version: 1 }] })).toBeNull()
  })
  it('keeps unknown, undated and scheduled owner dates distinct and rejects an incoherent status', () => {
    expect(decodeFutureAllocationList({ ...list, schedules: { [id]: { status: 'Unavailable', deliveryDate: null, isOverdue: null } } })).not.toBeNull()
    expect(decodeFutureAllocationList({ ...list, schedules: { [id]: { status: 'Undated', deliveryDate: null, isOverdue: false } } })).not.toBeNull()
    expect(decodeFutureAllocationList({ ...list, schedules: {} })).toBeNull()
    expect(decodeFutureAllocationList({ ...list, evaluatedOn: '2026-02-30' })).toBeNull()
    expect(decodeFutureAllocationList({ ...list, schedules: { [id]: { status: 'Scheduled', deliveryDate: '2026-02-30', isOverdue: true } } })).toBeNull()
    expect(decodeFutureAllocationList({ ...list, schedules: { [id]: { status: 'Unavailable', deliveryDate: null, isOverdue: false } } })).toBeNull()
    expect(decodeFutureAllocationList({ ...list, schedules: { [id]: { status: 'Inactive', deliveryDate: null, isOverdue: false } } })).toBeNull()
  })
})
