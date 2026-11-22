/** KLN-D119 (AK-2/AK-2b) — the optional piece members of the Inventory count, scrap, transfer receipt and transformation
 * contracts, as the OpenAPI publishes them (absent for a kg-only label, an int for a piece-tracking one). SENTETIK, NOT_ENV. */
import { describe, expect, it } from 'vitest'
import { decodeStockCountView, describesRecordedCount, isStockCountRequest, parsePieceCount, type StockCountRequest } from './stockCountContract'
import { decodeStockScrapView, isStockScrapRequest } from './stockScrapContract'
import { decodeReceiptFacts } from './transferReceiptApi'
import { isLabelTransformationRequest } from './labelTransformationContract'
import type { TransferDetail } from './transferContract'

const COMPANY = '11111111-1111-1111-1111-111111111111'
const LABEL = '22222222-2222-2222-2222-222222222222'
const PARTY = '55555555-5555-5555-5555-555555555555'
const ID = '77777777-7777-7777-7777-777777777777'
const OUT_A = '33333333-3333-3333-3333-333333333333'
const OUT_B = '44444444-4444-4444-4444-444444444444'

const count = (overrides: Record<string, unknown> = {}) => ({
  countId: ID, companyId: COMPANY, labelId: LABEL, labelVersion: '3', ownerKind: 'Company', ownerId: PARTY,
  custodianKind: 'Company', custodianId: PARTY, locationId: PARTY, productId: PARTY, block: 'Free',
  bookKg: '100', countedKg: '97.5', differenceKg: '-2.5', countedAtUtc: '2026-09-27T08:00:00+00:00',
  reason: 'Sayım', recordedAtUtc: '2026-09-27T08:00:01+00:00', status: 'PendingApproval', decision: null, ...overrides,
})
const scrap = (overrides: Record<string, unknown> = {}) => ({
  scrapId: ID, companyId: COMPANY, labelId: LABEL, labelVersion: '3', ownerKind: 'Company', ownerId: PARTY,
  custodianKind: 'Company', custodianId: PARTY, locationId: PARTY, productId: PARTY, block: 'Free',
  bookKg: '100', scrapKg: '2.5', remainingKg: '97.5', scrappedAtUtc: '2026-09-28T08:00:00+00:00',
  reason: 'Kenar hasarı', recordedAtUtc: '2026-09-28T08:00:01+00:00', status: 'PendingApproval', decision: null, ...overrides,
})

describe('KLN-D119 stock count pieces', () => {
  it('reads a piece-tracking count with both figures and a kg-only count with neither', () => {
    expect(decodeStockCountView(count({ bookPieceCount: 10, countedPieceCount: 9 }))).toMatchObject({ bookPieceCount: 10, countedPieceCount: 9 })
    const kgOnly = decodeStockCountView(count())
    expect(kgOnly).not.toBeNull()
    expect('bookPieceCount' in kgOnly! || 'countedPieceCount' in kgOnly!).toBe(false)
    expect(decodeStockCountView(count({ countedKg: '0', differenceKg: '-100', bookPieceCount: 10, countedPieceCount: 0 }))?.countedPieceCount).toBe(0)
  })

  it.each([
    ['only the book pieces', { bookPieceCount: 10 }],
    ['a null figure', { bookPieceCount: 10, countedPieceCount: null }],
    ['a text figure', { bookPieceCount: 10, countedPieceCount: '9' }],
    ['a fraction', { bookPieceCount: 10, countedPieceCount: 8.5 }],
    ['zero book pieces with book kg', { bookPieceCount: 0, countedPieceCount: 9 }],
    ['zero counted pieces with counted kg', { bookPieceCount: 10, countedPieceCount: 0 }],
  ])('fails closed on %s', (_name, pieces) => {
    expect(decodeStockCountView(count(pieces))).toBeNull()
  })

  it('carries the counted pieces in the request and matches them in the answer', () => {
    const request: StockCountRequest = { countId: ID, labelId: LABEL, expectedLabelVersion: '3', countedKg: '97.5',
      countedAtUtc: '2026-09-27T08:00:00Z', reason: 'Sayım', countedPieceCount: 9 }
    expect(isStockCountRequest(request)).toBe(true)
    expect(isStockCountRequest({ ...request, countedPieceCount: '9' })).toBe(false)
    const view = decodeStockCountView(count({ bookPieceCount: 10, countedPieceCount: 9 }))!
    expect(describesRecordedCount(view, request, COMPANY)).toBe(true)
    expect(describesRecordedCount(view, { ...request, countedPieceCount: 8 }, COMPANY)).toBe(false)
  })

  it('reads a typed piece count only as whole digits', () => {
    expect(parsePieceCount(' 12 ')).toBe(12)
    expect(parsePieceCount('0')).toBe(0)
    for (const raw of ['', '1,5', '1.5', '-1', '1e3', '99999999999']) expect(parsePieceCount(raw)).toBeNull()
  })
})

describe('KLN-D119 stock scrap pieces', () => {
  it('reads a partial and a whole piece scrap', () => {
    expect(decodeStockScrapView(scrap({ bookPieceCount: 10, scrapPieceCount: 1 }))).toMatchObject({ bookPieceCount: 10, scrapPieceCount: 1 })
    expect(decodeStockScrapView(scrap({ scrapKg: '100', remainingKg: '0', bookPieceCount: 10, scrapPieceCount: 10 }))?.scrapPieceCount).toBe(10)
    expect(decodeStockScrapView(scrap())).not.toBeNull()
  })

  it.each([
    ['only the scrap pieces', { scrapPieceCount: 1 }],
    ['a partial scrap taking every piece', { bookPieceCount: 10, scrapPieceCount: 10 }],
    ['a whole scrap leaving pieces', { scrapKg: '100', remainingKg: '0', bookPieceCount: 10, scrapPieceCount: 9 }],
    ['zero book pieces', { bookPieceCount: 0, scrapPieceCount: 0 }],
    ['a text figure', { bookPieceCount: 10, scrapPieceCount: '1' }],
  ])('fails closed on %s', (_name, pieces) => {
    expect(decodeStockScrapView(scrap(pieces))).toBeNull()
  })

  it('accepts the scrapped pieces in a remembered request', () => {
    const request = { scrapId: ID, labelId: LABEL, expectedLabelVersion: '3', scrapKg: '2.5', scrappedAtUtc: '2026-09-28T08:00:00Z', reason: 'Hasar' }
    expect(isStockScrapRequest({ ...request, scrapPieceCount: 1 })).toBe(true)
    expect(isStockScrapRequest({ ...request, scrapPieceCount: null })).toBe(false)
  })
})

describe('KLN-D119 transfer receipt pieces', () => {
  const LINE = '66666666-6666-4666-8666-666666666666'
  const MOVEMENT = '99999999-9999-4999-8999-999999999999'
  const transfer = { companyId: COMPANY, transferId: ID, version: '3', status: 'Accepted',
    lines: [{ lineId: LINE, labelId: LABEL, expectedLabelVersion: '7', plannedKg: '10', actualShippedKg: '10', differenceKg: '0', status: 'Accepted' }] } as unknown as TransferDetail
  const row = (overrides: Record<string, unknown> = {}) => ({ lineId: LINE, dispatchedKg: '10', receivedKg: '8', shortageKg: '2', overageKg: '0',
    shortageMovementId: MOVEMENT, overageReason: null, receivedAtUtc: '2026-11-17T12:00:00Z', receivedBy: 'SENTETIK-actor', overageStatus: 'None',
    overageDecisionReason: null, overageDecidedAtUtc: null, overageDecidedBy: null, overageMovementId: null, ...overrides })
  const facts = (receipt: Record<string, unknown>, renewals: Record<string, unknown>[] = []) => ({ companyId: COMPANY, transferId: ID, version: '3',
    status: 'Accepted', receipts: [receipt], losses: [], overageRenewals: renewals })
  const pieces = { dispatchedPieceCount: 5, receivedPieceCount: 4, shortagePieceCount: 1, overagePieceCount: 0 }

  it('reads a piece-tracking receipt with all four figures and a kg-only receipt with none', () => {
    expect(decodeReceiptFacts(facts(row(pieces)), transfer)?.receipts[0]).toMatchObject(pieces)
    expect(decodeReceiptFacts(facts(row()), transfer)).not.toBeNull()
  })

  it('reads a renewal naming its excess pieces', () => {
    const over = row({ receivedKg: '12', shortageKg: '0', overageKg: '2', shortageMovementId: null, overageReason: 'SENTETIK fazla',
      overageStatus: 'Rejected', overageDecisionReason: 'SENTETIK ret', overageDecidedAtUtc: '2026-11-17T12:30:00Z', overageDecidedBy: 'SENTETIK-other',
      dispatchedPieceCount: 5, receivedPieceCount: 6, shortagePieceCount: 0, overagePieceCount: 1 })
    const renewal = { lineId: LINE, requestNumber: 2, overageKg: '2', reason: 'SENTETIK yeni', requestedAtUtc: '2026-11-17T13:00:00Z',
      requestedBy: 'SENTETIK-actor', status: 'Pending', decisionReason: null, decidedAtUtc: null, decidedBy: null, movementId: null, overagePieceCount: 1 }
    expect(decodeReceiptFacts(facts(over, [renewal]), transfer)?.overageRenewals[0].overagePieceCount).toBe(1)
    expect(decodeReceiptFacts(facts(over, [{ ...renewal, overagePieceCount: '1' }]), transfer)).toBeNull()
  })

  it.each([
    ['three of the four figures', { dispatchedPieceCount: 5, receivedPieceCount: 4, shortagePieceCount: 1 }],
    ['pieces that do not add up', { ...pieces, shortagePieceCount: 2 }],
    ['a two-sided difference', { ...pieces, receivedPieceCount: 5, overagePieceCount: 1 }],
    ['zero received pieces', { ...pieces, receivedPieceCount: 0, shortagePieceCount: 5 }],
    ['a text figure', { ...pieces, receivedPieceCount: '4' }],
  ])('fails closed on %s', (_name, figures) => {
    expect(decodeReceiptFacts(facts(row(figures)), transfer)).toBeNull()
  })
})

describe('KLN-D119 label transformation output pieces', () => {
  const split = (outputs: Record<string, unknown>[]) => ({ transformationId: ID, kind: 'Split', sources: [{ labelId: LABEL, expectedLabelVersion: '3' }],
    outputs, occurredAtUtc: '2026-09-27T08:00:00Z', reason: 'Bölme' })

  it('accepts outputs that all name their pieces or none does', () => {
    expect(isLabelTransformationRequest(split([{ labelId: OUT_A, code: 'A', quantityKg: '60', pieceCount: 6 },
      { labelId: OUT_B, code: 'B', quantityKg: '40', pieceCount: 4 }]))).toBe(true)
    expect(isLabelTransformationRequest(split([{ labelId: OUT_A, code: 'A', quantityKg: '60' }, { labelId: OUT_B, code: 'B', quantityKg: '40' }]))).toBe(true)
  })

  it.each([
    ['one output without pieces', [{ labelId: OUT_A, code: 'A', quantityKg: '60', pieceCount: 6 }, { labelId: OUT_B, code: 'B', quantityKg: '40' }]],
    ['a zero-piece output', [{ labelId: OUT_A, code: 'A', quantityKg: '60', pieceCount: 0 }, { labelId: OUT_B, code: 'B', quantityKg: '40', pieceCount: 4 }]],
    ['a null piece count', [{ labelId: OUT_A, code: 'A', quantityKg: '60', pieceCount: null }, { labelId: OUT_B, code: 'B', quantityKg: '40', pieceCount: 4 }]],
  ])('refuses %s', (_name, outputs) => {
    expect(isLabelTransformationRequest(split(outputs))).toBe(false)
  })
})
