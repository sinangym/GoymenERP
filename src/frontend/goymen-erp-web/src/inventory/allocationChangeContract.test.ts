import { describe, expect, it } from 'vitest'
import {
  allocationChangeRefusalText,
  decodeAllocationChangeView,
  decodeLabelAllocations,
  describesChange,
  type AllocationChangeRequest,
} from './allocationChangeContract'

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
