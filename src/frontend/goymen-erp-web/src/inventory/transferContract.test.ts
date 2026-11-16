import { describe, expect, it } from 'vitest'
import { decodeTransferDetail, decodeTransferList } from './transferContract'

const companyId = '11111111-1111-4111-8111-111111111111'
const summary = { companyId, transferId: '22222222-2222-4222-8222-222222222222', version: '9223372036854775807',
  status: 'Closed', sourceLocationId: '33333333-3333-4333-8333-333333333333',
  destinationLocationId: '44444444-4444-4444-8444-444444444444', plannedAtUtc: '2026-11-16T10:00:00Z', dispatchedAtUtc: null }
const row = { transfer: summary, sourceLocationCode: 'A', sourceLocationName: 'Kaynak', destinationLocationCode: 'B', destinationLocationName: 'Hedef' }
const list = { companyId, items: [row], windowFull: false }

describe('transfer contracts', () => {
  it('preserves server strings without numeric conversion', () => {
    expect(decodeTransferList(list)).toEqual(list)
    const detail = { ...summary, sourceCustodianKind: 'Company', sourceCustodianId: companyId,
      destinationCustodianKind: 'Counterparty', destinationCustodianId: companyId,
      lines: [{ lineId: '55555555-5555-4555-8555-555555555555', labelId: '66666666-6666-4666-8666-666666666666',
        expectedLabelVersion: '9223372036854775807', plannedKg: '123456789012345.001', actualShippedKg: '123456789012345.002',
        differenceKg: '-0.001', status: 'WrittenOff' }] }
    expect(decodeTransferDetail(detail)).toEqual(detail)
  })

  it('rejects cross-company rows, duplicates, unknown states and numeric versions', () => {
    expect(decodeTransferList({ ...list, items: [{ ...row, transfer: { ...summary, companyId: summary.transferId } }] })).toBeNull()
    expect(decodeTransferList({ ...list, items: [row, row] })).toBeNull()
    expect(decodeTransferList({ ...list, items: [{ ...row, transfer: { ...summary, status: 'Completed' } }] })).toBeNull()
    expect(decodeTransferList({ ...list, items: [{ ...row, transfer: { ...summary, version: 1 } }] })).toBeNull()
    expect(decodeTransferList({ ...list, extra: true })).toBeNull()
  })
})
