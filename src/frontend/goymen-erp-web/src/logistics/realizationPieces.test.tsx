import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, within } from '@testing-library/react'
import { decodeInstructionDetail } from './contract'
import { decodeShipmentAllocations, REALIZATION_CAPABILITY as C } from './realizationContract'
import { REALIZATION_TEXT as T } from './logisticsText'
import { resetPendingInstructionAttempts } from './pendingInstructionAttempts'
import { ALL_LOGISTICS, COMPANY_A, DRAFT, DRAFT_LINE, INSTRUCTION, LINE, PRODUCT,
  installFetch, instructionDetail, instructionLine, json, renderLogisticsApp } from './testHarness'

// KLN-D119 (AK-4b): the allocation offered for a piece line's shipment shows the pieces it still holds. SENTETIK.
const ALLOCATION = 'eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee'
const LABEL = 'ffffffff-ffff-ffff-ffff-ffffffffffff'
const path = `/lojistik/yukleme-talimatlari/${INSTRUCTION}`
const loadedShare = { shareId: '12345678-1234-1234-1234-123456789abd', lineId: LINE, kind: 'Loaded', quantity: '4',
  measurement: 'Measured', sourceShipmentId: null, recordedAtUtc: '2026-09-25T10:00:00Z', recordedBy: 'SENTETIK-operator' }
const loaded = () => instructionDetail({ status: 'Issued', version: '4' },
  [instructionLine({ loaded: '4', remainingToLoad: '0.5', loadedNotShipped: '4' })], [loadedShare])
const allocations = (overrides: Record<string, unknown> = {}) => ({ items: [{ allocation: {
  allocationId: ALLOCATION, companyId: COMPANY_A, version: '7', status: 'Active', labelId: LABEL, productId: PRODUCT,
  demandKind: 'sales.order-line', demandId: DRAFT, demandLineId: DRAFT_LINE, demandProductVersion: '9', allocatedKg: '4',
  remainingKg: '4', isSubstitute: false, ...overrides,
}, labelVersion: '11' }], searchWindowFull: false })

const instructionLineOf = () => decodeInstructionDetail(loaded(), INSTRUCTION)!.lines[0]

afterEach(() => { cleanup(); resetPendingInstructionAttempts(); vi.unstubAllGlobals(); vi.restoreAllMocks() })

describe('KLN-D119 remaining pieces of a shipment allocation', () => {
  it('keeps the remaining pieces when present and leaves them undefined when absent', () => {
    const line = instructionLineOf()
    expect(decodeShipmentAllocations(allocations({ allocatedPieces: 12, remainingPieces: 8 }), COMPANY_A, line)?.items[0]
      .remainingPieces).toBe(8)
    const kgOnly = decodeShipmentAllocations(allocations(), COMPANY_A, line)
    expect(kgOnly).not.toBeNull()
    expect(kgOnly?.items[0].remainingPieces).toBeUndefined()
    expect('remainingPieces' in kgOnly!.items[0]).toBe(false)
  })

  it.each([
    ['a null count', { allocatedPieces: 12, remainingPieces: null }],
    ['a text count', { allocatedPieces: 12, remainingPieces: '8' }],
    ['a fractional count', { allocatedPieces: 12, remainingPieces: 7.5 }],
    ['a negative count', { allocatedPieces: 12, remainingPieces: -1 }],
    ['only the remaining figure', { remainingPieces: 8 }],
    ['only the allocated figure', { allocatedPieces: 12 }],
    ['more remaining than allocated', { allocatedPieces: 8, remainingPieces: 12 }],
  ])('fails closed on %s', (_name, overrides) => {
    expect(decodeShipmentAllocations(allocations(overrides), COMPANY_A, instructionLineOf())).toBeNull()
  })

  it('shows the remaining pieces beside the remaining kg in the shipment allocation choice', async () => {
    installFetch({ capabilities: [...ALL_LOGISTICS, C.shipment, C.allocation], onDetail: () => json(loaded()),
      onAllocations: () => json(allocations({ allocatedPieces: 12, remainingPieces: 8 })) })
    renderLogisticsApp(path)
    const form = await screen.findByTestId('shipment-1')
    const option = await within(form).findByRole('option', { name: /Etiket ref/ })
    expect(option.textContent).toContain(`(${T.remaining}: 4 kg · 8 adet)`)
  })

  it('shows only kilograms for an allocation without pieces', async () => {
    installFetch({ capabilities: [...ALL_LOGISTICS, C.shipment, C.allocation], onDetail: () => json(loaded()),
      onAllocations: () => json(allocations()) })
    renderLogisticsApp(path)
    const form = await screen.findByTestId('shipment-1')
    const option = await within(form).findByRole('option', { name: /Etiket ref/ })
    expect(option.textContent).toContain(`(${T.remaining}: 4 kg)`)
    expect(option.textContent).not.toContain('adet')
  })

  it('shows the malformed state, not a choice, when the allocation read carries a mistyped piece figure', async () => {
    installFetch({ capabilities: [...ALL_LOGISTICS, C.shipment, C.allocation], onDetail: () => json(loaded()),
      onAllocations: () => json(allocations({ allocatedPieces: 12, remainingPieces: '8' })) })
    renderLogisticsApp(path)
    const form = await screen.findByTestId('shipment-1')
    expect(await within(form).findByText(/Beklenmeyen yanıt/)).toBeTruthy()
    expect(within(form).queryByRole('option', { name: /Etiket ref/ })).toBeNull()
  })
})
