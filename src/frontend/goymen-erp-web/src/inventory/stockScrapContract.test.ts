import { describe, expect, it } from 'vitest'
import {
  decodeLabelStockScraps,
  decodeStockScrapView,
  describesRecordedScrap,
  describesScrapDecision,
  isScrapKilogramText,
  isStockScrapRequest,
  remainingKilograms,
  stockScrapRefusalText,
} from './stockScrapContract'

const COMPANY = '11111111-1111-1111-1111-111111111111'
const LABEL = '22222222-2222-2222-2222-222222222222'
const SCRAP = '77777777-7777-7777-7777-777777777777'
const MOVEMENT = '88888888-8888-8888-8888-888888888888'
const PARTY = '55555555-5555-5555-5555-555555555555'

function wire(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    scrapId: SCRAP, companyId: COMPANY, labelId: LABEL, labelVersion: '3', ownerKind: 'Company', ownerId: PARTY,
    custodianKind: 'Company', custodianId: PARTY, locationId: PARTY, productId: PARTY, block: 'Free',
    bookKg: '100', scrapKg: '2.5', remainingKg: '97.5', scrappedAtUtc: '2026-09-28T08:00:00+00:00',
    reason: 'Kenar hasarı', recordedAtUtc: '2026-09-28T08:00:01.1234567+00:00', status: 'PendingApproval', decision: null,
    ...overrides,
  }
}

const approved = { decision: 'Approved', decidedAtUtc: '2026-09-28T09:00:00Z', reason: 'Uygun', scrapMovementId: MOVEMENT }

describe('stock scrap contract', () => {
  it('computes book − scrap as canonical kilograms and refuses a scrap above the book', () => {
    expect(remainingKilograms('100', '2.5')).toBe('97.5')
    expect(remainingKilograms('1250.5', '1250.5')).toBe('0')
    expect(remainingKilograms('1', '1.001')).toBeNull()
  })

  it('accepts only a positive scrap with at most three decimals', () => {
    expect(isScrapKilogramText('0.001')).toBe(true)
    expect(isScrapKilogramText('97.125')).toBe(true)
    expect(isScrapKilogramText('0')).toBe(false)
    expect(isScrapKilogramText('97.1255')).toBe(false)
    expect(isScrapKilogramText('-1')).toBe(false)
    expect(isScrapKilogramText('1.50')).toBe(false)
  })

  it('decodes only a self-consistent scrap', () => {
    expect(decodeStockScrapView(wire())?.status).toBe('PendingApproval')
    expect(decodeStockScrapView(wire({ remainingKg: '97' }))).toBeNull()
    expect(decodeStockScrapView(wire({ scrapKg: '0', remainingKg: '100' }))).toBeNull()
    expect(decodeStockScrapView(wire({ status: 'Approved' }))).toBeNull()
    expect(decodeStockScrapView(wire({ status: 'Approved', decision: approved }))?.decision?.scrapMovementId).toBe(MOVEMENT)
    expect(decodeStockScrapView(wire({ status: 'Approved', decision: { ...approved, scrapMovementId: null } }))).toBeNull()
    expect(decodeStockScrapView(wire({ status: 'Rejected', decision: { ...approved, decision: 'Rejected' } }))).toBeNull()
    expect(decodeStockScrapView(wire({ status: 'Rejected', decision: { ...approved, decision: 'Rejected', scrapMovementId: null } }))?.status)
      .toBe('Rejected')
    expect(decodeStockScrapView(wire({ extra: true }))).toBeNull()
  })

  it('refuses a label read that mixes another label', () => {
    expect(decodeLabelStockScraps({ labelId: LABEL, windowFull: false, items: [wire()] })?.items).toHaveLength(1)
    expect(decodeLabelStockScraps({ labelId: SCRAP, windowFull: false, items: [wire()] })).toBeNull()
  })

  it('shows an answer only when it describes this request', () => {
    const request = {
      scrapId: SCRAP, labelId: LABEL, expectedLabelVersion: '3', scrapKg: '2.5',
      scrappedAtUtc: '2026-09-28T08:00:00.000Z', reason: 'Kenar hasarı',
    }
    expect(isStockScrapRequest(request)).toBe(true)
    expect(isStockScrapRequest({ ...request, scrapKg: '0' })).toBe(false)
    const view = decodeStockScrapView(wire())!
    expect(describesRecordedScrap(view, request, COMPANY)).toBe(true)
    expect(describesRecordedScrap(view, { ...request, scrapKg: '2' }, COMPANY)).toBe(false)
    expect(describesRecordedScrap(view, request, PARTY)).toBe(false)
    const decided = decodeStockScrapView(wire({ status: 'Approved', decision: approved }))!
    expect(describesScrapDecision(decided, SCRAP, { decision: 'Approve', reason: 'Uygun' }, COMPANY)).toBe(true)
    expect(describesScrapDecision(decided, SCRAP, { decision: 'Reject', reason: 'Uygun' }, COMPANY)).toBe(false)
  })

  it('explains maker-checker, stale, over-book and allocation refusals in the user language', () => {
    expect(stockScrapRefusalText(409, 'inventory_stock_scrap_self_approval')).toMatch(/başka bir yetkili/)
    expect(stockScrapRefusalText(409, 'inventory_stock_scrap_stale')).toMatch(/yeniden girin/)
    expect(stockScrapRefusalText(409, 'inventory_stock_scrap_exceeds_book')).toMatch(/defter miktarından fazla/)
    expect(stockScrapRefusalText(409, 'inventory_stock_scrap_allocation_conflict')).toMatch(/tahsis/)
    expect(stockScrapRefusalText(409, 'inventory_stock_scrap_not_own_stock')).toMatch(/müşteri malı/)
    expect(stockScrapRefusalText(403, null)).toMatch(/yetkiniz yok/)
  })
})
