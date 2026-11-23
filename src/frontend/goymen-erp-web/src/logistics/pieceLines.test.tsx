import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { decodeInstructionDetail, isPieceLine } from './contract'
import { REALIZATION_CAPABILITY as C } from './realizationContract'
import { INSTRUCTION_DETAIL_TEXT as D, REALIZATION_TEXT as T } from './logisticsText'
import { resetPendingInstructionAttempts } from './pendingInstructionAttempts'
import { ALL_LOGISTICS, COMPANY_A, DRAFT, DRAFT_LINE, INSTRUCTION, LINE, PRODUCT, installFetch, instructionDetail,
  instructionLine, json, mutation, renderLogisticsApp } from './testHarness'

// AK-4 (KLN-D119) — a loading instruction mixing a kg line and a piece line, as LoadingInstructionLineView publishes it: the piece
// line carries loadedKg/shippedKg, its loads and shipments carry quantityKg, the kg line carries none. SENTETIK, NOT_ENV.
const PIECE_LINE = 'dddddddd-dddd-dddd-dddd-ddddddddddde'
const PIECE_UNIT = '44444444-4444-4444-4444-444444444445'
const ALLOCATION = 'eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee'
const LABEL = 'ffffffff-ffff-ffff-ffff-ffffffffffff'
const SHIPMENT = '12345678-1234-1234-1234-123456789abe'
const path = `/lojistik/yukleme-talimatlari/${INSTRUCTION}`

const pieceLine = (overrides: Record<string, unknown> = {}) => instructionLine({ lineId: PIECE_LINE, lineNo: 2, unitId: PIECE_UNIT,
  instructed: '10', loaded: '4', remainingToLoad: '6', loadedNotShipped: '4', loadedKg: '12.5', shippedKg: '0', ...overrides })
const pieceLoad = { shareId: '12345678-1234-1234-1234-123456789abd', lineId: PIECE_LINE, kind: 'Loaded', quantity: '4',
  measurement: 'Measured', sourceShipmentId: null, recordedAtUtc: '2026-09-25T10:00:00Z', recordedBy: 'SENTETIK-operator', quantityKg: '12.5' }
const mixed = (lines = [instructionLine(), pieceLine()], shares: Record<string, unknown>[] = [pieceLoad], dispatchNotes: Record<string, unknown>[] = []) =>
  instructionDetail({ status: 'Issued', version: '4' }, lines, shares, null, dispatchNotes)
const allocations = () => ({ items: [{ allocation: {
  allocationId: ALLOCATION, companyId: COMPANY_A, version: '7', status: 'Active', labelId: LABEL, productId: PRODUCT,
  demandKind: 'sales.order-line', demandId: DRAFT, demandLineId: DRAFT_LINE, demandProductVersion: '9', allocatedKg: '12.5',
  remainingKg: '12.5', isSubstitute: false, allocatedPieces: 4, remainingPieces: 4,
}, labelVersion: '11' }], searchWindowFull: false })

afterEach(() => { cleanup(); resetPendingInstructionAttempts(); vi.unstubAllGlobals(); vi.restoreAllMocks() })

describe('AK-4 piece lines in the loading instruction decoder', () => {
  it('reads an instruction mixing a kg line and a piece line, keeping the piece line kg and the share kg', () => {
    const detail = decodeInstructionDetail(mixed(), INSTRUCTION)
    expect(detail).not.toBeNull()
    const [kgLine, piece] = detail!.lines
    expect(isPieceLine(kgLine)).toBe(false)
    expect('loadedKg' in kgLine || 'shippedKg' in kgLine).toBe(false)
    expect(isPieceLine(piece)).toBe(true)
    expect(piece).toMatchObject({ loaded: '4', loadedKg: '12.5', shippedKg: '0' })
    expect(detail!.shares[0].quantityKg).toBe('12.5')
  })

  it('reads a dispatch note line carrying the shipment kg of a piece line', () => {
    const shipment = { ...pieceLoad, shareId: SHIPMENT, kind: 'Shipped', quantity: '2', measurement: null, quantityKg: '6.25' }
    const note = { dispatchNoteId: '12345678-1234-1234-1234-123456789ac0', status: 'Draft', number: null, createdBy: 'SENTETIK-preparer',
      createdAtUtc: '2026-09-25T12:00:00Z', approvedBy: null, approvedAtUtc: null, cancelledBy: null, cancelledAtUtc: null,
      cancelReason: null, eDispatchNumber: null, lines: [{ lineNo: 1, shipmentId: SHIPMENT, instructionLineId: PIECE_LINE,
        sourceOrderId: DRAFT, sourceOrderLineId: DRAFT_LINE, productId: PRODUCT, unitId: PIECE_UNIT, quantity: '2', quantityKg: '6.25' }] }
    const lines = [instructionLine(), pieceLine({ shipped: '2', loadedNotShipped: '2', shippedNotDelivered: '2', shippedKg: '6.25' })]
    expect(decodeInstructionDetail(mixed(lines, [pieceLoad, shipment], [note]), INSTRUCTION)?.dispatchNotes[0].lines[0].quantityKg).toBe('6.25')
    const wrongKg = { ...note, lines: [{ ...note.lines[0], quantityKg: '6' }] }
    expect(decodeInstructionDetail(mixed(lines, [pieceLoad, shipment], [wrongKg]), INSTRUCTION)).toBeNull()
  })

  it.each([
    ['only loadedKg on a line', [instructionLine(), (() => { const { shippedKg: _, ...line } = pieceLine(); return line })()], [pieceLoad]],
    ['a null line kg', [instructionLine(), pieceLine({ shippedKg: null })], [pieceLoad]],
    ['a negative line kg', [instructionLine(), pieceLine({ loadedKg: '-1' })], [pieceLoad]],
    ['a share kg on a kg line', [instructionLine({ loaded: '4', remainingToLoad: '0.5', loadedNotShipped: '4' })],
      [{ ...pieceLoad, lineId: LINE }]],
    ['a zero share kg', [instructionLine(), pieceLine()], [{ ...pieceLoad, quantityKg: '0' }]],
    ['a numeric share kg', [instructionLine(), pieceLine()], [{ ...pieceLoad, quantityKg: 12.5 }]],
    ['an unknown line member', [instructionLine(), pieceLine({ deliveredKg: '0' })], [pieceLoad]],
  ])('fails closed on %s', (_name, lines, shares) => {
    expect(decodeInstructionDetail(mixed(lines as Record<string, unknown>[], shares as Record<string, unknown>[]), INSTRUCTION)).toBeNull()
  })
})

describe('AK-4 piece line commands send the physical kg', () => {
  it('shows the piece line kg and sends quantityKg with a piece-line load, never with a kg-line load', async () => {
    const loads: { lineId: string; body: Record<string, unknown> }[] = []
    let current = mixed()
    let version = 4
    installFetch({ onDetail: () => json(current), onLoad: (lineId, call) => {
      const body = JSON.parse(call.body!) as Record<string, unknown>
      loads.push({ lineId, body })
      version += 1
      const share = { shareId: body.loadId, lineId, kind: 'Loaded', quantity: body.quantity, measurement: body.measurement,
        sourceShipmentId: null, recordedAtUtc: '2026-09-25T10:05:00Z', recordedBy: 'SENTETIK-operator',
        ...(body.quantityKg === undefined ? {} : { quantityKg: body.quantityKg }) }
      const lines = lineId === LINE
        ? [instructionLine({ loaded: '2.5', remainingToLoad: '2', loadedNotShipped: '2.5' }), pieceLine()]
        : [instructionLine({ loaded: '2.5', remainingToLoad: '2', loadedNotShipped: '2.5' }),
          pieceLine({ loaded: '6', remainingToLoad: '4', loadedNotShipped: '6', loadedKg: '18.75' })]
      current = instructionDetail({ status: 'Issued', version: String(version) }, lines,
        [...(current.shares as Record<string, unknown>[]), share])
      return json(mutation(current))
    } })
    renderLogisticsApp(path)
    expect((await screen.findByTestId('logistics-line-2-loaded')).textContent).toBe('4 (12.5 kg)')
    expect(screen.getByTestId('logistics-line-1-loaded').textContent).toBe('0')
    // The kg line: no kg field, no quantityKg.
    expect(screen.queryByLabelText(D.load.quantityKg)).toBeNull()
    await userEvent.type(screen.getByLabelText(D.load.quantity), '2,5')
    await userEvent.click(screen.getByTestId('logistics-load-button'))
    await waitFor(() => expect(loads).toHaveLength(1))
    expect(loads[0].lineId).toBe(LINE)
    expect(loads[0].body).not.toHaveProperty('quantityKg')
    // The piece line: the kg is required before anything is sent, then travels as exact text.
    await waitFor(() => expect(screen.getByTestId('logistics-load-notice').textContent).toBe(D.command.completed))
    await userEvent.selectOptions(screen.getByLabelText(D.load.line), PIECE_LINE)
    const pieces = screen.getByLabelText(D.load.quantityPieces)
    await userEvent.clear(pieces)
    await userEvent.type(pieces, '2')
    await userEvent.click(screen.getByTestId('logistics-load-button'))
    const kg = screen.getByLabelText(D.load.quantityKg)
    expect(kg.getAttribute('aria-required')).toBe('true')
    expect(kg.getAttribute('aria-invalid')).toBe('true')
    expect(loads).toHaveLength(1)
    await userEvent.type(kg, '6,25')
    await userEvent.click(screen.getByTestId('logistics-load-button'))
    await waitFor(() => expect(loads).toHaveLength(2))
    expect(loads[1]).toEqual({ lineId: PIECE_LINE, body: expect.objectContaining({ quantity: '2', quantityKg: '6.25', measurement: 'Measured' }) })
    await waitFor(() => expect(screen.getByTestId('logistics-line-2-loaded').textContent).toBe('6 (18.75 kg)'))
  })

  it('sends quantityKg with a piece-line shipment and refuses to send without it', async () => {
    const posts: Record<string, unknown>[] = []
    installFetch({ capabilities: [...ALL_LOGISTICS, C.shipment, C.allocation], onDetail: () => json(mixed()),
      onAllocations: () => json(allocations()), onShipment: (_, call) => {
        posts.push(JSON.parse(call.body!) as Record<string, unknown>)
        return json(mutation(mixed()))
      } })
    renderLogisticsApp(path)
    const form = await screen.findByTestId('shipment-2')
    await within(form).findByRole('option', { name: /Etiket ref/ })
    await userEvent.selectOptions(within(form).getByLabelText(T.allocation), ALLOCATION)
    await userEvent.type(within(form).getByLabelText(T.quantityPieces), '2')
    await userEvent.click(within(form).getByTestId('shipment-2-submit'))
    expect(within(form).getByRole('alert').textContent).toBe(T.invalidPieces)
    expect(posts).toHaveLength(0)
    await userEvent.type(within(form).getByLabelText(T.quantityKg), '6,25')
    await userEvent.click(within(form).getByTestId('shipment-2-submit'))
    await waitFor(() => expect(posts).toHaveLength(1))
    expect(posts[0]).toMatchObject({ allocationId: ALLOCATION, quantity: '2', quantityKg: '6.25' })
  })
})

describe('AK-4 delivery quantity label follows the line the server published', () => {
  it('asks a piece line for pieces and a kg line for kilograms', async () => {
    installFetch({ capabilities: [...ALL_LOGISTICS, C.delivery], onDetail: () => json(mixed()) })
    renderLogisticsApp(path)
    const piece = await screen.findByTestId('delivery-2')
    expect(within(piece).getByLabelText(T.quantityPieces)).toBeTruthy()
    expect(within(piece).queryByLabelText(T.quantity)).toBeNull()
    const kg = screen.getByTestId('delivery-1')
    expect(within(kg).getByLabelText(T.quantity)).toBeTruthy()
    expect(within(kg).queryByLabelText(T.quantityPieces)).toBeNull()
  })
})
