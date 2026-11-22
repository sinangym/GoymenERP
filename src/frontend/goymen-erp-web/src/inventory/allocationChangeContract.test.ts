import { describe, expect, it } from 'vitest'
import {
  allocationChangeRefusalText,
  decodeAllocationChangeView,
  decodeLabelAllocations,
  describesChange,
  parseAllocationPieceCount,
  type AllocationChangeRequest,
} from './allocationChangeContract'
import { ALLOCATION_PIECE_REFUSAL_TEXT } from './allocationPieceText'

const COMPANY = '11111111-1111-1111-1111-111111111111'
const LABEL = '22222222-2222-2222-2222-222222222222'
const OTHER_LABEL = '33333333-3333-3333-3333-333333333333'
const ALLOCATION = '44444444-4444-4444-4444-444444444444'
const NEW_ALLOCATION = '55555555-5555-5555-5555-555555555555'
const ORDER = '66666666-6666-6666-6666-666666666666'
const LINE = '77777777-7777-7777-7777-777777777777'
const PRODUCT = '88888888-8888-8888-8888-888888888888'
const CHANGE = '99999999-9999-9999-9999-999999999999'

function allocation(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    allocationId: ALLOCATION, companyId: COMPANY, version: '2', status: 'Active', labelId: LABEL, productId: PRODUCT,
    demandKind: 'sales.order-line', demandId: ORDER, demandLineId: LINE, demandProductVersion: '7',
    allocatedKg: '25', remainingKg: '25', reason: 'SENTETIK', reservedUntilUtc: null,
    createdAtUtc: '2026-09-28T08:00:00+00:00', updatedAtUtc: '2026-09-28T09:00:00+00:00', ...overrides,
  }
}

function change(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    changeId: CHANGE, kind: 'Resize', sourceAllocationId: ALLOCATION, sourceVersion: '1', sourceLabelId: LABEL,
    sourceRemainingKg: '60', targetAllocationId: ALLOCATION, targetLabelId: LABEL, targetRemainingKg: '25',
    reason: 'Sipariş azaldı', changedAtUtc: '2026-09-28T09:00:00+00:00', ...overrides,
  }
}

function read(items: unknown[]): Record<string, unknown> {
  return { labelId: LABEL, labelVersion: '5', items, windowFull: false }
}

function request(overrides: Partial<AllocationChangeRequest> = {}): AllocationChangeRequest {
  return {
    expectedAllocationVersion: '1', salesOrderId: ORDER, salesOrderLineId: LINE, targetLabelId: LABEL,
    expectedTargetLabelVersion: '4', quantityKg: '25', reason: 'Sipariş azaldı', newAllocationId: null, ...overrides,
  }
}

describe('allocation change contract', () => {
  it('decodes a label read with an active allocation and its resize history', () => {
    const decoded = decodeLabelAllocations(read([
      { allocation: allocation(), releasedAtUtc: null, releaseReason: null, changes: [change()] },
    ]))
    expect(decoded?.items[0].changes[0].targetRemainingKg).toBe('25')
    expect(decoded?.labelVersion).toBe('5')
  })

  it('requires release facts exactly for a released allocation', () => {
    const released = allocation({ status: 'Released', remainingKg: '0', version: '3' })
    expect(decodeLabelAllocations(read([
      { allocation: released, releasedAtUtc: '2026-09-28T10:00:00Z', releaseReason: 'Taşındı', changes: [] },
    ]))).not.toBeNull()
    expect(decodeLabelAllocations(read([
      { allocation: released, releasedAtUtc: null, releaseReason: null, changes: [] },
    ]))).toBeNull()
    expect(decodeLabelAllocations(read([
      { allocation: allocation(), releasedAtUtc: '2026-09-28T10:00:00Z', releaseReason: 'x', changes: [] },
    ]))).toBeNull()
  })

  it('refuses another label, an unrelated change, an inconsistent kind and unknown fields', () => {
    expect(decodeLabelAllocations(read([
      { allocation: allocation({ labelId: OTHER_LABEL }), releasedAtUtc: null, releaseReason: null, changes: [] },
    ]))).toBeNull()
    expect(decodeLabelAllocations(read([{ allocation: allocation(), releasedAtUtc: null, releaseReason: null,
      changes: [change({ sourceAllocationId: NEW_ALLOCATION, targetAllocationId: NEW_ALLOCATION })] }]))).toBeNull()
    expect(decodeLabelAllocations(read([{ allocation: allocation(), releasedAtUtc: null, releaseReason: null,
      changes: [change({ kind: 'Move' })] }]))).toBeNull()
    expect(decodeLabelAllocations(read([{ allocation: allocation({ extra: 1 }), releasedAtUtc: null, releaseReason: null,
      changes: [] }]))).toBeNull()
  })

  it('describes a resize only for the same allocation, label and kilograms', () => {
    const view = decodeAllocationChangeView({ kind: 'Resize', allocation: allocation(), replaced: null })!
    expect(describesChange(view, ALLOCATION, request(), COMPANY)).toBe(true)
    expect(describesChange(view, ALLOCATION, request({ quantityKg: '24' }), COMPANY)).toBe(false)
    expect(describesChange(view, NEW_ALLOCATION, request(), COMPANY)).toBe(false)
  })

  it('describes a move only when the new allocation holds the target and the source is released', () => {
    const view = decodeAllocationChangeView({
      kind: 'Move',
      allocation: allocation({ allocationId: NEW_ALLOCATION, labelId: OTHER_LABEL, version: '1' }),
      replaced: allocation({ status: 'Released', remainingKg: '0' }),
    })!
    const move = request({ targetLabelId: OTHER_LABEL, newAllocationId: NEW_ALLOCATION })
    expect(describesChange(view, ALLOCATION, move, COMPANY)).toBe(true)
    expect(describesChange(view, ALLOCATION, { ...move, newAllocationId: ALLOCATION }, COMPANY)).toBe(false)
    expect(decodeAllocationChangeView({ kind: 'Move', allocation: allocation(), replaced: null })).toBeNull()
  })

  it('names the refusal by code and never shows server text', () => {
    expect(allocationChangeRefusalText(409, 'inventory_allocation_insufficient_free_stock')).toMatch(/serbest/)
    expect(allocationChangeRefusalText(422, 'inventory_allocation_invalid_reference')).toMatch(/onaylı değil/)
    expect(allocationChangeRefusalText(403, null)).toMatch(/yetkiniz yok/)
  })
})

describe('KLN-D119 allocation pieces in the change contract', () => {
  const item = (overrides: Record<string, unknown>) =>
    read([{ allocation: allocation(overrides), releasedAtUtc: null, releaseReason: null, changes: [] }])

  it('reads the piece figures when present and leaves them undefined when absent', () => {
    const decoded = decodeLabelAllocations(item({ allocatedPieces: 12, remainingPieces: 8 }))
    expect([decoded?.items[0].allocation.allocatedPieces, decoded?.items[0].allocation.remainingPieces]).toEqual([12, 8])
    const kgOnly = decodeLabelAllocations(item({}))
    expect(kgOnly).not.toBeNull()
    expect(kgOnly?.items[0].allocation.allocatedPieces).toBeUndefined()
    expect(kgOnly?.items[0].allocation.remainingPieces).toBeUndefined()
  })

  it('accepts the published substitute flag only as a boolean', () => {
    expect(decodeLabelAllocations(item({ isSubstitute: false }))).not.toBeNull()
    expect(decodeLabelAllocations(item({ isSubstitute: 'false' }))).toBeNull()
  })

  it.each([
    ['a null count', { allocatedPieces: null, remainingPieces: 8 }],
    ['a text count', { allocatedPieces: 12, remainingPieces: '8' }],
    ['a fractional count', { allocatedPieces: 12.5, remainingPieces: 8 }],
    ['a negative count', { allocatedPieces: 12, remainingPieces: -1 }],
    ['only the allocated figure', { allocatedPieces: 12 }],
    ['only the remaining figure', { remainingPieces: 8 }],
    ['more remaining than allocated', { allocatedPieces: 8, remainingPieces: 12 }],
    ['an unknown member', { remainingPiece: 8 }],
  ])('fails closed on %s', (_name, overrides) => {
    expect(decodeLabelAllocations(item(overrides))).toBeNull()
  })

  it('shows a change answer only when it holds exactly the pieces the request named', () => {
    const view = (pieces: number) => decodeAllocationChangeView({
      kind: 'Resize', replaced: null, allocation: allocation({ allocatedPieces: 12, remainingPieces: pieces }),
    })!
    expect(describesChange(view(5), ALLOCATION, request({ pieceCount: 5 }), COMPANY)).toBe(true)
    expect(describesChange(view(6), ALLOCATION, request({ pieceCount: 5 }), COMPANY)).toBe(false)
    expect(describesChange(view(6), ALLOCATION, request(), COMPANY)).toBe(true)
  })

  it('takes only a whole number of at least one as a typed piece count', () => {
    expect(parseAllocationPieceCount('7')).toBe(7)
    for (const bad of ['', '0', '-1', '1.5', '1,5', '07', '99999999999']) expect(parseAllocationPieceCount(bad)).toBeNull()
  })

  it.each([
    'inventory_allocation_piece_count_required', 'inventory_allocation_piece_count_not_tracked',
    'inventory_allocation_piece_count_invalid', 'inventory_allocation_insufficient_free_pieces',
  ])('tells %s in Turkish without the code', (code) => {
    const text = allocationChangeRefusalText(code === 'inventory_allocation_insufficient_free_pieces' ? 409 : 422, code)
    expect(text).toBe(ALLOCATION_PIECE_REFUSAL_TEXT[code])
    expect(text).toMatch(/aded|adet/i)
    expect(text).not.toContain(code)
  })
})
