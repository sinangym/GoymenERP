import { describe, expect, it } from 'vitest'
import {
  decodeLabelStockCounts,
  decodeStockCountView,
  describesDecision,
  describesRecordedCount,
  differenceKilograms,
  isCountedKilogramText,
  isStockCountRequest,
  stockCountRefusalText,
} from './stockCountContract'

const COMPANY = '11111111-1111-1111-1111-111111111111'
const LABEL = '22222222-2222-2222-2222-222222222222'
const COUNT = '77777777-7777-7777-7777-777777777777'
const MOVEMENT = '88888888-8888-8888-8888-888888888888'
const PARTY = '55555555-5555-5555-5555-555555555555'

function wire(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    countId: COUNT, companyId: COMPANY, labelId: LABEL, labelVersion: '3', ownerKind: 'Company', ownerId: PARTY,
    custodianKind: 'Company', custodianId: PARTY, locationId: PARTY, productId: PARTY, block: 'Free',
    bookKg: '100', countedKg: '97.5', differenceKg: '-2.5', countedAtUtc: '2026-09-27T08:00:00+00:00',
    reason: 'Sayım', recordedAtUtc: '2026-09-27T08:00:01.1234567+00:00', status: 'PendingApproval', decision: null,
    ...overrides,
  }
}

describe('stock count contract', () => {
  it('computes counted − book as canonical signed kilograms', () => {
    expect(differenceKilograms('100', '97.5')).toBe('-2.5')
    expect(differenceKilograms('1250.5', '1250.5')).toBe('0')
    expect(differenceKilograms('0.001', '1')).toBe('0.999')
  })

  it('accepts zero as a counted amount but never a negative or a fourth decimal', () => {
    expect(isCountedKilogramText('0')).toBe(true)
    expect(isCountedKilogramText('97.125')).toBe(true)
    expect(isCountedKilogramText('97.1255')).toBe(false)
    expect(isCountedKilogramText('-1')).toBe(false)
    expect(isCountedKilogramText('1.50')).toBe(false)
  })

  it('decodes only a self-consistent count', () => {
    expect(decodeStockCountView(wire())?.status).toBe('PendingApproval')
    expect(decodeStockCountView(wire({ differenceKg: '-2' }))).toBeNull()
    expect(decodeStockCountView(wire({ status: 'NoDifference' }))).toBeNull()
    expect(decodeStockCountView(wire({ countedKg: '100', differenceKg: '0', status: 'NoDifference' }))?.status).toBe('NoDifference')
    expect(decodeStockCountView(wire({ status: 'Approved' }))).toBeNull()
    expect(decodeStockCountView(wire({ status: 'Approved', decision: {
      decision: 'Approved', decidedAtUtc: '2026-09-27T09:00:00Z', reason: 'Uygun', adjustmentMovementId: MOVEMENT } }))?.status)
      .toBe('Approved')
    expect(decodeStockCountView(wire({ status: 'Approved', decision: {
      decision: 'Approved', decidedAtUtc: '2026-09-27T09:00:00Z', reason: 'Uygun', adjustmentMovementId: null } }))).toBeNull()
    expect(decodeStockCountView(wire({ status: 'Rejected', decision: {
      decision: 'Rejected', decidedAtUtc: '2026-09-27T09:00:00Z', reason: 'Yeniden say', adjustmentMovementId: MOVEMENT } }))).toBeNull()
    expect(decodeStockCountView(wire({ extra: true }))).toBeNull()
  })

  it('refuses a label read that mixes another label', () => {
    expect(decodeLabelStockCounts({ labelId: LABEL, windowFull: false, items: [wire()] })?.items).toHaveLength(1)
    expect(decodeLabelStockCounts({ labelId: COUNT, windowFull: false, items: [wire()] })).toBeNull()
  })

  it('shows an answer only when it describes this request', () => {
    const request = {
      countId: COUNT, labelId: LABEL, expectedLabelVersion: '3', countedKg: '97.5',
      countedAtUtc: '2026-09-27T08:00:00.000Z', reason: 'Sayım',
    }
    expect(isStockCountRequest(request)).toBe(true)
    const view = decodeStockCountView(wire())!
    expect(describesRecordedCount(view, request, COMPANY)).toBe(true)
    expect(describesRecordedCount(view, { ...request, countedKg: '97' }, COMPANY)).toBe(false)
    expect(describesRecordedCount(view, request, PARTY)).toBe(false)
    const approved = decodeStockCountView(wire({ status: 'Approved', decision: {
      decision: 'Approved', decidedAtUtc: '2026-09-27T09:00:00Z', reason: 'Uygun', adjustmentMovementId: MOVEMENT } }))!
    expect(describesDecision(approved, COUNT, { decision: 'Approve', reason: 'Uygun' }, COMPANY)).toBe(true)
    expect(describesDecision(approved, COUNT, { decision: 'Reject', reason: 'Uygun' }, COMPANY)).toBe(false)
  })

  it('explains maker-checker, stale and allocation refusals in the user language', () => {
    expect(stockCountRefusalText(409, 'inventory_stock_count_self_approval')).toMatch(/başka bir yetkili/)
    expect(stockCountRefusalText(409, 'inventory_stock_count_stale')).toMatch(/yeniden sayın/)
    expect(stockCountRefusalText(409, 'inventory_stock_count_allocation_conflict')).toMatch(/tahsis/)
    expect(stockCountRefusalText(403, null)).toMatch(/yetkiniz yok/)
  })
})
