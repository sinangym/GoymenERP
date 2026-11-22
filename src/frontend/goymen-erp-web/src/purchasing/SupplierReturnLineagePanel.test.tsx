import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { PurchaseOrderDetail } from './contract'
import type { GoodsReceipt } from './goodsReceiptContract'
import { SupplierReturnLineagePanel } from './SupplierReturnLineagePanel'
import { createLineageSupplierReturn, fetchSupplierReturnLineagePreview } from './supplierReturnLineageApi'
import type { SupplierReturnLineagePreview, SupplierReturnLineageRequest } from './supplierReturnLineageContract'
import { SUPPLIER_RETURN_LINEAGE_TEXT as T } from './supplierReturnLineageText'
import { resetAdjustmentAttemptsForTests } from './useReceiptAdjustmentAttempt'

vi.mock('./supplierReturnLineageApi', () => ({ fetchSupplierReturnLineagePreview: vi.fn(), createLineageSupplierReturn: vi.fn() }))
const boundary = vi.hoisted(() => ({ capture: () => ({ token: 1 }), report: vi.fn() }))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => boundary }))

const id = (n: number) => `${n.toString().padStart(8, '0')}-1111-4111-8111-111111111111`
const LABEL = id(30), SECOND = id(11), EVENT = id(40)
const detail: PurchaseOrderDetail = { order: { id: id(2), companyId: id(1), status: 'Approved', version: '4', supplierPartyId: id(8),
  companyAccountId: id(9), sourceRequestId: null, effectiveOn: '2026-11-16', createdAtUtc: '2026-11-16T00:00:00Z',
  updatedAtUtc: '2026-11-16T00:00:00Z', lineCount: 1 },
  lines: [{ lineId: id(3), lineNo: 1, kind: 'General', parentLineId: null, sourceRequestLineId: null,
    productId: id(4), productVersion: '7', unitId: id(5), ordered: '1000', technical: '0', committed: '1000', accepted: '600', remaining: '400' }], acceptances: [] }
const receipt: GoodsReceipt = { companyId: id(1), acceptanceId: id(10), purchaseOrderId: id(2), purchaseOrderLineId: id(3), purchaseOrderVersion: '4',
  productId: id(4), productVersion: '7', unitId: id(5), acceptedQuantity: '600', remainingQuantity: '400', lotId: id(12), labelId: id(13),
  labelCode: 'SENTETIK-R1-ETIKET', labelVersion: '3', movementId: id(14), locationId: id(6), measuredQuantityKg: '600', initialBlock: 'Free',
  occurredAtUtc: '2026-11-16T01:00:00Z' }

// Golden I01/I04: L4 300 kg = R1 180 kg + R2 120 kg; R1 has 240 kg invoiced and 360 kg pending left.
const plan: SupplierReturnLineagePreview = { companyId: id(1), purchaseOrderId: id(2), goodsAcceptanceId: id(10), sourceLabelId: LABEL,
  lineageMode: 'Lineage', quantityKg: '300', pieceCount: null, tracksPieces: false, freeKg: '300', blockedKg: '0', freePieces: null, blockedPieces: null,
  parts: [{ goodsAcceptanceId: id(10), purchaseOrderId: id(2), purchaseOrderLineId: id(3), quantityKg: '180', pieceCount: null,
    costSource: 'InvoiceUnitCost', receivedKg: '600', openKg: '600', pendingRemainingKg: '360',
    invoiced: [{ sourceEventId: EVENT, invoiceId: id(41), invoiceLineId: id(42), remainingKg: '240' }], selectionRequired: true },
  { goodsAcceptanceId: SECOND, purchaseOrderId: id(5), purchaseOrderLineId: id(6), quantityKg: '120', pieceCount: null,
    costSource: 'ManualCostException', receivedKg: '400', openKg: '400', pendingRemainingKg: '400', invoiced: [], selectionRequired: false }] }

function recorded(request: SupplierReturnLineageRequest, status: 'Recorded' | 'PendingCostException' = 'PendingCostException') {
  return { type: 'applied' as const, response: { isReplay: false, originalCorrelationId: 'SENTETIK-original', return: {
    companyId: id(1), returnId: request.returnId, returnLineId: request.returnLineId, purchaseOrderId: id(2), purchaseOrderLineId: id(3),
    goodsAcceptanceId: request.goodsAcceptanceId, inventoryMovementId: request.inventoryMovementId, status, version: '1',
    sourceBlock: request.sourceBlock, quantityKg: request.quantityKg,
    costBasis: status === 'Recorded' ? 'InvoiceUnitCost' as const : 'ManualCostWithException' as const, unitCostTryPerKg: '19.9936',
    priceDisclosure: 'Visible' as const, replacementExpected: false, reason: request.reason, occurredAtUtc: request.occurredAtUtc,
    requestedBy: 'SENTETIK-giren', requestedAtUtc: '2026-11-22T06:01:00Z', decidedBy: null, decidedAtUtc: null, decisionReason: null,
    sourceLabelId: request.sourceLabelId, lineageMode: 'Lineage' as const } } }
}

function mount(onSettled = vi.fn()) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 0, gcTime: 0 } } })
  render(<QueryClientProvider client={client}><SupplierReturnLineagePanel detail={detail} receipt={receipt} binding="read-generation-1"
    attemptBinding="SENTETIK-actor-session-company" onSettled={onSettled} /></QueryClientProvider>)
  return onSettled
}
async function openAndAsk(kg = '300', pieces = '') {
  await userEvent.click(screen.getByRole('button', { name: T.show }))
  await userEvent.type(screen.getByLabelText(T.sourceLabel), LABEL)
  await userEvent.type(screen.getByLabelText(T.quantityKg), kg)
  if (pieces !== '') await userEvent.type(screen.getByLabelText(T.pieceCount), pieces)
  await userEvent.type(screen.getByLabelText(T.reason), 'SENTETIK kusurlu parti')
  await userEvent.click(screen.getByRole('button', { name: T.preview }))
}

beforeEach(() => { vi.mocked(fetchSupplierReturnLineagePreview).mockResolvedValue({ type: 'ok-single', data: plan }) })
afterEach(() => { cleanup(); resetAdjustmentAttemptsForTests(); vi.resetAllMocks() })

describe('Supplier return from another label (AK-8b; SAT-D433, SAT-D440–SAT-D445, K3-B2), synthetic NOT_ENV', () => {
  it('is collapsed, then shows the empty state and refuses an invalid form without reading anything', async () => {
    mount()
    expect(screen.queryByLabelText(T.sourceLabel)).toBeNull()
    await userEvent.click(screen.getByRole('button', { name: T.show }))
    expect((screen.getByText(T.noPreview)).getAttribute('data-state')).toBe('empty-no-data')
    await userEvent.type(screen.getByLabelText(T.sourceLabel), 'etiket-değil')
    await userEvent.click(screen.getByRole('button', { name: T.preview }))
    expect((screen.getByRole('alert')).textContent).toContain(T.invalid)
    expect(fetchSupplierReturnLineagePreview).not.toHaveBeenCalled()
  })

  it('shows loading, then the per-receipt distribution with kg, cost source, open quantity and the open parts', async () => {
    let resolve!: (value: { type: 'ok-single'; data: SupplierReturnLineagePreview }) => void
    vi.mocked(fetchSupplierReturnLineagePreview).mockReturnValue(new Promise((done) => { resolve = done }))
    mount(); await openAndAsk()
    expect(document.querySelector('[data-state="initial-loading"]')).not.toBeNull()
    resolve({ type: 'ok-single', data: plan })
    const table = within(await screen.findByRole('table', { name: T.tableCaption }))
    expect(vi.mocked(fetchSupplierReturnLineagePreview).mock.calls[0][0]).toMatchObject({ acceptanceId: id(10), sourceLabelId: LABEL,
      quantityKg: '300', pieceCount: null })
    const first = within(screen.getByTestId('supplier-return-lineage-part-1')), second = within(screen.getByTestId('supplier-return-lineage-part-2'))
    expect((first.getByRole('rowheader')).textContent).toContain(T.ownReceipt)
    expect(first.getAllByRole('cell').map((cell) => cell.textContent)).toEqual(['180', T.noPieces, T.costSourceText.InvoiceUnitCost, '600', '600', '360', '240'])
    expect((second.getByRole('rowheader')).textContent).toContain(T.otherReceipt(2))
    expect(second.getAllByRole('cell').map((cell) => cell.textContent)).toEqual(['120', T.noPieces, T.costSourceText.ManualCostException, '400', '400', '400', T.noInvoice])
    expect(table.getAllByRole('columnheader')).toHaveLength(8)
    expect((screen.getByTestId('supplier-return-lineage-plan')).getAttribute('data-mode')).toBe('Lineage')
    expect(screen.getByText(T.labelStock('300', '0'))).toBeTruthy()
  })

  it('posts ONE return with a part per receipt, the explicit invoiced/pending choice and the manual cost of the unreadable receipt', async () => {
    vi.mocked(createLineageSupplierReturn).mockImplementation(async (_company, _order, request) => recorded(request))
    const settled = mount(); await openAndAsk()
    await screen.findByRole('table', { name: T.tableCaption })
    await userEvent.type(screen.getByLabelText(T.pendingKg(T.ownReceipt)), '80')
    await userEvent.type(screen.getByLabelText(T.invoicedKg(T.ownReceipt, 1)), '100')
    await userEvent.type(screen.getByLabelText(T.manualCost(T.otherReceipt(2))), '21,44')
    await userEvent.click(screen.getByRole('button', { name: T.save }))

    expect((await screen.findByTestId('supplier-return-lineage-result')).textContent).toContain(T.pending)
    expect(createLineageSupplierReturn).toHaveBeenCalledTimes(1)
    const [company, order, request, key] = vi.mocked(createLineageSupplierReturn).mock.calls[0]
    expect([company, order]).toEqual([id(1), id(2)])
    expect(key).toMatch(/^[0-9a-f-]{36}$/)
    expect(request).toMatchObject({ goodsAcceptanceId: id(10), sourceLabelId: LABEL, quantityKg: '300', sourceBlock: 'Free',
      manualUnitCostTryPerKg: null, replacementExpected: false, reason: 'SENTETIK kusurlu parti' })
    expect(request).not.toHaveProperty('pieceCount')
    expect(request.lineageParts).toHaveLength(2)
    expect(request.lineageParts![0]).toEqual({ goodsAcceptanceId: id(10), returnLineId: request.returnLineId,
      inventoryMovementId: request.inventoryMovementId, pendingKg: '80', invoiced: [{ sourceEventId: EVENT, quantityKg: '100' }] })
    expect(request.lineageParts![1]).toMatchObject({ goodsAcceptanceId: SECOND, manualUnitCostTryPerKg: '21.44' })
    expect(request.lineageParts![1]).not.toHaveProperty('pendingKg')
    expect(new Set([request.returnId, request.returnLineId, request.inventoryMovementId, request.lineageParts![1].returnLineId,
      request.lineageParts![1].inventoryMovementId]).size).toBe(5)
    expect(settled).toHaveBeenCalledTimes(1)
  })

  it('does not post a choice or a manual cost that is not a number', async () => {
    mount(); await openAndAsk()
    await screen.findByRole('table', { name: T.tableCaption })
    await userEvent.type(screen.getByLabelText(T.pendingKg(T.ownReceipt)), 'seksen')
    await userEvent.click(screen.getByRole('button', { name: T.save }))
    expect(screen.getByText(T.invalidSelection)).toBeTruthy()
    expect(createLineageSupplierReturn).not.toHaveBeenCalled()
  })

  it('shows the refusal the command would give and keeps the plan from being posted', async () => {
    vi.mocked(fetchSupplierReturnLineagePreview).mockResolvedValue({ type: 'ok-single',
      data: { ...plan, refusal: 'purchasing_supplier_return_lineage_receipt_limit_exceeded' } })
    mount(); await openAndAsk()
    const refusal = await screen.findByText((text) => text.includes(T.code.purchasing_supplier_return_lineage_receipt_limit_exceeded))
    expect((refusal).getAttribute('data-code')).toBe('purchasing_supplier_return_lineage_receipt_limit_exceeded')
    expect((screen.getByRole('button', { name: T.save }) as HTMLButtonElement).disabled).toBe(true)
  })

  it('a quantity return shows the moving average as its cost source and an unrepresentable net price as a refusal', async () => {
    vi.mocked(fetchSupplierReturnLineagePreview).mockResolvedValue({ type: 'ok-single', data: { ...plan, lineageMode: 'Quantity', quantityKg: '80',
      refusal: 'purchasing_supplier_return_quantity_price_undecided',
      parts: [{ ...plan.parts[0], quantityKg: '80', costSource: 'MovingAverage', invoiced: [], selectionRequired: false }] } })
    mount(); await openAndAsk('80')
    expect((await screen.findByTestId('supplier-return-lineage-plan')).getAttribute('data-mode')).toBe('Quantity')
    expect(screen.getByText(T.mode.Quantity)).toBeTruthy()
    expect((within(screen.getByTestId('supplier-return-lineage-part-1')).getAllByRole('cell')[2]).textContent).toContain(T.costSourceText.MovingAverage)
    expect(screen.getByText((text) => text.includes(T.code.purchasing_supplier_return_quantity_price_undecided))).toBeTruthy()
    expect((screen.getByRole('button', { name: T.save }) as HTMLButtonElement).disabled).toBe(true)
  })

  it('a quantity return whose average cannot be read carries its manual cost on the document and waits for approval', async () => {
    vi.mocked(fetchSupplierReturnLineagePreview).mockResolvedValue({ type: 'ok-single', data: { ...plan, lineageMode: 'Quantity', quantityKg: '80',
      parts: [{ ...plan.parts[0], quantityKg: '80', costSource: 'ManualCostException', invoiced: [], selectionRequired: false }] } })
    vi.mocked(createLineageSupplierReturn).mockImplementation(async (_company, _order, request) => recorded(request))
    mount(); await openAndAsk('80')
    await userEvent.type(await screen.findByLabelText(T.manualCost(T.ownReceipt)), '20.5')
    await userEvent.click(screen.getByRole('button', { name: T.save }))
    await screen.findByTestId('supplier-return-lineage-result')
    const request = vi.mocked(createLineageSupplierReturn).mock.calls[0][2]
    expect(request.manualUnitCostTryPerKg).toBe('20.5')
    expect(request.lineageParts).toEqual([{ goodsAcceptanceId: id(10), returnLineId: request.returnLineId, inventoryMovementId: request.inventoryMovementId }])
    expect(request).not.toHaveProperty('supplierPieceQuantity')
  })

  it('SAT-D455: a quantity return can carry the document pieces of a piece order line and never sends them when left empty', async () => {
    vi.mocked(fetchSupplierReturnLineagePreview).mockResolvedValue({ type: 'ok-single', data: { ...plan, lineageMode: 'Quantity', quantityKg: '80',
      parts: [{ ...plan.parts[0], quantityKg: '80', costSource: 'MovingAverage', invoiced: [], selectionRequired: false }] } })
    vi.mocked(createLineageSupplierReturn).mockResolvedValue({ type: 'failed', status: 422, code: 'purchasing_supplier_return_quantity_piece_quantity_mismatch' })
    mount(); await openAndAsk('80')
    await userEvent.type(await screen.findByLabelText(T.supplierPieceQuantity), '4')
    await userEvent.click(screen.getByRole('button', { name: T.save }))
    expect(await screen.findByText(T.code.purchasing_supplier_return_quantity_piece_quantity_mismatch)).toBeTruthy()
    expect(vi.mocked(createLineageSupplierReturn).mock.calls[0][2].supplierPieceQuantity).toBe(4)
  })

  it('sends the pieces of a piece-tracking label and shows each receipt\'s pieces (golden I10)', async () => {
    vi.mocked(fetchSupplierReturnLineagePreview).mockResolvedValue({ type: 'ok-single', data: { ...plan, quantityKg: '100', pieceCount: 7, tracksPieces: true,
      freePieces: 20, blockedPieces: 0, parts: [{ ...plan.parts[0], quantityKg: '60', pieceCount: 5, invoiced: [], selectionRequired: false },
        { ...plan.parts[1], quantityKg: '40', pieceCount: 2, costSource: 'InvoiceUnitCost' }] } })
    vi.mocked(createLineageSupplierReturn).mockImplementation(async (_company, _order, request) => ({ ...recorded(request, 'Recorded'),
      response: { ...recorded(request, 'Recorded').response, return: { ...recorded(request, 'Recorded').response.return, pieceCount: 7 } } }))
    mount(); await openAndAsk('100', '7')
    await screen.findByRole('table', { name: T.tableCaption })
    expect(vi.mocked(fetchSupplierReturnLineagePreview).mock.calls[0][0]).toMatchObject({ pieceCount: 7 })
    expect((within(screen.getByTestId('supplier-return-lineage-part-1')).getAllByRole('cell')[1]).textContent).toContain('5')
    expect(screen.getByText(T.labelPieces(20, 0))).toBeTruthy()
    await userEvent.click(screen.getByRole('button', { name: T.save }))
    expect((await screen.findByTestId('supplier-return-lineage-result')).textContent).toContain(T.saved)
    expect(vi.mocked(createLineageSupplierReturn).mock.calls[0][2].pieceCount).toBe(7)
  })

  it('asks for a new preview when the label, kg or pieces change after the preview', async () => {
    mount(); await openAndAsk()
    await screen.findByRole('table', { name: T.tableCaption })
    await userEvent.type(screen.getByLabelText(T.quantityKg), '1')
    expect(screen.getByText(T.previewStale)).toBeTruthy()
    expect((screen.getByRole('button', { name: T.save }) as HTMLButtonElement).disabled).toBe(true)
  })

  it.each([
    [503, 'service-unavailable'], [404, 'not-found'], [403, 'forbidden'],
  ])('shows the %i preview answer as its own state', async (status, state) => {
    vi.mocked(fetchSupplierReturnLineagePreview).mockResolvedValue({ type: 'http', status, body: { code: 'SENTETIK' } })
    mount(); await openAndAsk()
    await waitFor(() => expect(document.querySelector(`[data-state="${state}"]`)).not.toBeNull())
    expect(screen.queryByRole('button', { name: T.save })).toBeNull()
    if (status === 403) expect(boundary.report).toHaveBeenCalledWith(403, { token: 1 })
  })

  it.each([
    [409, 'purchasing_supplier_return_lineage_receipt_mismatch'], [422, 'purchasing_supplier_return_piece_count_required'],
  ])('explains a %i preview refusal with its code', async (status, code) => {
    vi.mocked(fetchSupplierReturnLineagePreview).mockResolvedValue({ type: 'http', status, body: { code } })
    mount(); await openAndAsk()
    const alert = await screen.findByText(T.code[code])
    expect((alert).getAttribute('data-code')).toBe(code)
    expect((alert).getAttribute('data-state')).toBe('conflict')
  })

  it.each([['malformed', 'malformed-response'], ['offline', 'offline'], ['network-error', 'network-error']] as const)(
    'shows a %s preview as its own state', async (type, state) => {
      vi.mocked(fetchSupplierReturnLineagePreview).mockResolvedValue({ type })
      mount(); await openAndAsk()
      await waitFor(() => expect(document.querySelector(`[data-state="${state}"]`)).not.toBeNull())
    })

  it('explains a 409 command refusal with its code and reports the settle so the reads refresh', async () => {
    vi.mocked(createLineageSupplierReturn).mockResolvedValue({ type: 'failed', status: 409, code: 'purchasing_supplier_return_lineage_selection_required' })
    vi.mocked(fetchSupplierReturnLineagePreview).mockResolvedValue({ type: 'ok-single',
      data: { ...plan, parts: [{ ...plan.parts[0], selectionRequired: false }, { ...plan.parts[1], costSource: 'InvoiceUnitCost' }] } })
    const settled = mount(); await openAndAsk()
    await screen.findByRole('table', { name: T.tableCaption })
    await userEvent.click(screen.getByRole('button', { name: T.save }))
    expect((await screen.findByText(T.code.purchasing_supplier_return_lineage_selection_required)).getAttribute('data-code')).toBe('purchasing_supplier_return_lineage_selection_required')
    expect(document.querySelector('[data-state="conflict"]')).not.toBeNull()
    expect(settled).toHaveBeenCalledTimes(1)
  })

  it('keeps the same key when the outcome is unknown (a 503) and retries the same content', async () => {
    vi.mocked(fetchSupplierReturnLineagePreview).mockResolvedValue({ type: 'ok-single',
      data: { ...plan, parts: [{ ...plan.parts[0], selectionRequired: false }, { ...plan.parts[1], costSource: 'InvoiceUnitCost' }] } })
    vi.mocked(createLineageSupplierReturn).mockResolvedValueOnce({ type: 'unknown', reason: 'service-unavailable', status: 503 })
      .mockImplementation(async (_company, _order, request) => recorded(request, 'Recorded'))
    mount(); await openAndAsk()
    await screen.findByRole('table', { name: T.tableCaption })
    await userEvent.click(screen.getByRole('button', { name: T.save }))
    expect(await screen.findByText(T.unknown)).toBeTruthy()
    expect(document.querySelector('[data-state="service-unavailable"]')).not.toBeNull()
    expect((screen.getByRole('button', { name: T.save }) as HTMLButtonElement).disabled).toBe(true)
    await userEvent.click(screen.getByRole('button', { name: T.retry }))
    expect((await screen.findByTestId('supplier-return-lineage-result')).textContent).toContain(T.saved)
    const calls = vi.mocked(createLineageSupplierReturn).mock.calls
    expect(calls).toHaveLength(2)
    expect(calls[1][3]).toBe(calls[0][3])
    expect(calls[1][2]).toEqual(calls[0][2])
  })
})
