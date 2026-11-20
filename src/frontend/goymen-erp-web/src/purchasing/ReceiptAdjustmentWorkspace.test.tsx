import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { PurchaseOrderDetail } from './contract'
import type { GoodsReceipt } from './goodsReceiptContract'
import { ReceiptAdjustmentWorkspace } from './ReceiptAdjustmentWorkspace'
import { RECEIPT_ADJUSTMENT_TEXT as T } from './receiptAdjustmentText'
import { createSupplierReturn, decideSupplierReturn, reverseReceipt } from './receiptAdjustmentApi'
import { decodeReceiptReversal, type ReceiptReversalRequest, type ReceiptReversalResponse } from './receiptAdjustmentContract'
import { fetchGoodsReceiptOptions, fetchGoodsReceipts } from './goodsReceiptApi'
import { fetchSupplierReturns } from './supplierReturnApi'
import { fetchSupplierReturnCostSources } from './supplierReturnSourceApi'
import { resetAdjustmentAttemptsForTests } from './useReceiptAdjustmentAttempt'

vi.mock('./goodsReceiptApi', () => ({ fetchGoodsReceipts: vi.fn(), fetchGoodsReceiptOptions: vi.fn() }))
vi.mock('./supplierReturnApi', () => ({ fetchSupplierReturns: vi.fn() }))
vi.mock('./supplierReturnSourceApi', () => ({ fetchSupplierReturnCostSources: vi.fn() }))
vi.mock('./receiptAdjustmentApi', () => ({ reverseReceipt: vi.fn(), createSupplierReturn: vi.fn(), decideSupplierReturn: vi.fn() }))
const boundary = vi.hoisted(() => ({ capture: () => ({ token: 1 }), report: vi.fn() }))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => boundary }))
const id = (n: number) => `${n.toString().padStart(8, '0')}-1111-4111-8111-111111111111`
const detail: PurchaseOrderDetail = { order: { id: id(2), companyId: id(1), status: 'Approved', version: '4', supplierPartyId: id(8),
  companyAccountId: id(9), sourceRequestId: null, effectiveOn: '2026-11-16', createdAtUtc: '2026-11-16T00:00:00Z',
  updatedAtUtc: '2026-11-16T00:00:00Z', lineCount: 1 },
  lines: [{ lineId: id(3), lineNo: 1, kind: 'General', parentLineId: null, sourceRequestLineId: null,
    productId: id(4), productVersion: '7', unitId: id(5), ordered: '10', technical: '0', committed: '10', accepted: '4', remaining: '6' }], acceptances: [] }
const receipt: GoodsReceipt = { companyId: id(1), acceptanceId: id(10), purchaseOrderId: id(2), purchaseOrderLineId: id(3), purchaseOrderVersion: '4',
  productId: id(4), productVersion: '7', unitId: id(5), acceptedQuantity: '4', remainingQuantity: '6', lotId: id(11), labelId: id(12),
  labelCode: 'SENTETIK-ACCEPTED-LABEL', labelVersion: '2', movementId: id(13), locationId: id(6), measuredQuantityKg: '3.9', initialBlock: 'Free', occurredAtUtc: '2026-11-16T01:00:00Z' }
const pendingReturn = { companyId: id(1), returnId: id(20), returnLineId: id(21), purchaseOrderId: id(2), purchaseOrderLineId: id(3),
  goodsAcceptanceId: id(10), inventoryMovementId: id(22), status: 'PendingCostException' as const, version: '1', sourceBlock: 'Free' as const,
  quantityKg: '1.9', costBasis: 'ManualCostWithException' as const, unitCostTryPerKg: '12.3456', priceDisclosure: 'Visible' as const, replacementExpected: true,
  reason: 'SENTETIK return', occurredAtUtc: '2026-11-16T02:00:00Z', requestedBy: 'SENTETIK maker', requestedAtUtc: '2026-11-16T02:00:01Z',
  decidedBy: null, decidedAtUtc: null, decisionReason: null, expectedPurchaseOrderVersion: '4', undoQuantity: '2' }
const answer: ReceiptReversalResponse = { purchase: { orderVersion: '5', netOpenedQuantity: '2', allocatableQuantity: '8', revived: false,
  isReplay: false, originalCorrelationId: 'SENTETIK-original' }, stock: { quantityKg: '1.9', conversions: [], isReplay: false }, correctionStock: null }
function mount(over: Partial<React.ComponentProps<typeof ReceiptAdjustmentWorkspace>> = {}) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(<QueryClientProvider client={client}><ReceiptAdjustmentWorkspace detail={detail} binding="read-generation-1"
    attemptBinding="SENTETIK-actor-session-company" canAdjust canReturn canDecide={false} onSettled={vi.fn()} {...over} /></QueryClientProvider>)
}
async function openAndFill() {
  await userEvent.click(screen.getByRole('button', { name: T.show }))
  await screen.findByRole('heading', { name: receipt.labelCode })
  await userEvent.type(screen.getByLabelText(T.purchaseQuantity), '2')
  await userEvent.type(screen.getByLabelText(T.measuredKg), '1,9')
  await userEvent.type(screen.getByLabelText(T.reason), 'SENTETIK correction')
  if ((screen.getByLabelText(T.mode) as HTMLSelectElement).value === 'return') {
    await userEvent.click(await screen.findByLabelText(T.selectSource(T.pendingSource)))
    await userEvent.type(screen.getByLabelText(T.sourceKg(T.pendingSource)), '1,9')
    await userEvent.type(screen.getByLabelText(T.sourceQuantity(T.pendingSource)), '2')
  }
}
beforeEach(() => {
  vi.mocked(fetchGoodsReceipts).mockResolvedValue({ type: 'ok-single', data: { items: [receipt], searchWindowFull: false } })
  vi.mocked(fetchGoodsReceiptOptions).mockResolvedValue({ type: 'http', status: 503, body: null })
  vi.mocked(fetchSupplierReturns).mockResolvedValue({ type: 'ok-single', data: { items: [pendingReturn], searchWindowFull: false } })
  vi.mocked(fetchSupplierReturnCostSources).mockResolvedValue({ type: 'ok-single', data: {
    companyId: id(1), goodsAcceptanceId: id(10), labelId: id(12), sourceVersion: 'synthetic-source-v1',
    pendingQuantityKg: { numerator: '39', denominator: '10' }, pendingUndoQuantity: { numerator: '4', denominator: '1' }, pendingWholePairAvailable: true, invoicedSources: [],
  } })
})
afterEach(() => { cleanup(); resetAdjustmentAttemptsForTests(); vi.resetAllMocks() })
describe('Receipt undo, correction and supplier return workflow, synthetic NOT_ENV', () => {
  it('shows the physical owner kg refusal in the supplier return workflow', async () => {
    const detail = 'Sistemde 1 kg var, 1.9 kg istendi.'
    vi.mocked(createSupplierReturn).mockResolvedValue({ type: 'failed', status: 409, code: 'SENTETIK-physical', detail })
    mount({ canAdjust: false }); await openAndFill()
    await userEvent.click(screen.getByRole('button', { name: T.save }))
    expect(await screen.findByText(detail)).toBeTruthy()
    expect(createSupplierReturn).toHaveBeenCalledOnce()
  })
  it('shows the server physical stock refusal alongside the conflict state', async () => {
    const detail = 'Sistemde 1 kg var, 1.9 kg istendi.'
    vi.mocked(reverseReceipt).mockResolvedValue({ type: 'failed', status: 409, code: 'SENTETIK-physical', detail })
    mount(); await openAndFill(); await userEvent.click(screen.getByRole('button', { name: T.save }))
    expect(await screen.findByText(detail)).toBeTruthy()
    expect(reverseReceipt).toHaveBeenCalledOnce()
  })
  it('keeps an unresolved operation frozen and retries identical payload and key after a read capability refresh', async () => {
    vi.mocked(reverseReceipt).mockResolvedValueOnce({ type: 'unknown', reason: 'service-unavailable', status: 503 })
      .mockResolvedValueOnce({ type: 'applied', response: answer })
    const first = mount(); await openAndFill(); await userEvent.click(screen.getByRole('button', { name: T.save }))
    await screen.findByText(T.unknown)
    expect(screen.getByLabelText(T.purchaseQuantity).matches(':disabled')).toBe(true)
    first.unmount()
    mount({ binding: 'read-generation-2' }); await userEvent.click(screen.getByRole('button', { name: T.show }))
    await userEvent.click(await screen.findByRole('button', { name: T.retry }))
    await screen.findByText(T.saved)
    const [one, two] = vi.mocked(reverseReceipt).mock.calls
    expect(two).toEqual(one)
    expect(one[3].undoQuantity).toBe('2'); expect(one[3].measuredUndoQuantityKg).toBe('1.9')
  })
  it('sends a correction receipt with new identities and server-owned original location without recomputing quantities', async () => {
    vi.mocked(reverseReceipt).mockResolvedValue({ type: 'unknown', reason: 'network-error' })
    mount(); await openAndFill(); await userEvent.selectOptions(screen.getByLabelText(T.mode), 'correction')
    await userEvent.type(screen.getByLabelText(T.newQuantity), '2.5'); await userEvent.type(screen.getByLabelText(T.newKg), '2.4')
    await userEvent.type(screen.getByLabelText(T.newLabel), 'SENTETIK-NEW-LABEL'); await userEvent.click(screen.getByRole('button', { name: T.save }))
    await waitFor(() => expect(reverseReceipt).toHaveBeenCalledOnce())
    const request = vi.mocked(reverseReceipt).mock.calls[0][3]
    expect(request.replacementReceipt).toMatchObject({ acceptedQuantity: '2.5', measuredQuantityKg: '2.4', expectedProductVersion: '7', locationId: receipt.locationId })
    expect(request.replacementReceipt?.acceptanceId).not.toBe(receipt.acceptanceId)
    expect(request.replacementReceipt?.movementId).not.toBe(request.inventoryMovementId)
  })
  it('records explicit replacement expectation and waits for the cost exception without claiming stock has left', async () => {
    vi.mocked(createSupplierReturn).mockResolvedValue({ type: 'applied', response: { return: pendingReturn, isReplay: false, originalCorrelationId: 'SENTETIK' } })
    mount({ canAdjust: false }); await openAndFill(); await userEvent.type(screen.getByLabelText(T.manualCost), '12,3456')
    await userEvent.click(screen.getByLabelText(T.replacement)); await userEvent.click(screen.getByRole('button', { name: T.save }))
    await screen.findByText(T.pending)
    expect(vi.mocked(createSupplierReturn).mock.calls[0][2]).toMatchObject({ undoQuantity: '2', quantityKg: '1.9', replacementExpected: true, expectedPurchaseOrderVersion: '4', manualUnitCostTryPerKg: '12.3456' })
  })
  it('carries independently entered source quantities and keeps the exact selection on an unknown retry', async () => {
    vi.mocked(createSupplierReturn).mockResolvedValue({ type: 'unknown', reason: 'network-error' })
    mount({ canAdjust: false }); await openAndFill()
    await userEvent.click(screen.getByRole('button', { name: T.save }))
    await screen.findByText(T.unknown)
    const first = vi.mocked(createSupplierReturn).mock.calls[0]
    expect(first[2].costAllocation).toEqual({ expectedSourceVersion: 'synthetic-source-v1',
      pending: { takeAll: false, quantityKg: '1.9', undoQuantity: '2' }, invoicedSources: [] })
    expect(screen.getByLabelText(T.sourceKg(T.pendingSource)).matches(':disabled')).toBe(true)
    await userEvent.click(screen.getByRole('button', { name: T.retry }))
    await waitFor(() => expect(createSupplierReturn).toHaveBeenCalledTimes(2))
    expect(vi.mocked(createSupplierReturn).mock.calls[1]).toEqual(first)
  })
  it('sends whole exact source selections without rounding or converting their fractional quantities', async () => {
    vi.mocked(fetchSupplierReturnCostSources).mockResolvedValue({ type: 'ok-single', data: {
      companyId: id(1), goodsAcceptanceId: id(10), labelId: id(12), sourceVersion: 'synthetic-source-v2',
      pendingQuantityKg: { numerator: '1', denominator: '3' }, pendingUndoQuantity: { numerator: '2', denominator: '3' }, pendingWholePairAvailable: true,
      invoicedSources: [{ sourceEventId: id(70), invoiceId: id(71), invoiceLineId: id(72), invoiceNumber: 'SENTETIK-FATURA', invoiceLineNo: 1,
        availableQuantityKg: { numerator: '2', denominator: '3' }, availableUndoQuantity: { numerator: '1', denominator: '3' }, wholePairAvailable: true }],
    } })
    vi.mocked(createSupplierReturn).mockResolvedValue({ type: 'unknown', reason: 'network-error' })
    mount({ canAdjust: false }); await openAndFill()
    await userEvent.clear(screen.getByLabelText(T.purchaseQuantity)); await userEvent.type(screen.getByLabelText(T.purchaseQuantity), '1')
    await userEvent.clear(screen.getByLabelText(T.measuredKg)); await userEvent.type(screen.getByLabelText(T.measuredKg), '1')
    await userEvent.click(screen.getByLabelText(T.takeAllSource(T.pendingSource)))
    const invoice = T.invoiceSource('SENTETIK-FATURA', 1)
    await userEvent.click(screen.getByLabelText(T.selectSource(invoice)))
    await userEvent.click(screen.getByLabelText(T.takeAllSource(invoice)))
    await userEvent.click(screen.getByRole('button', { name: T.save }))
    await waitFor(() => expect(createSupplierReturn).toHaveBeenCalledOnce())
    expect(vi.mocked(createSupplierReturn).mock.calls[0][2].costAllocation).toEqual({ expectedSourceVersion: 'synthetic-source-v2',
      pending: { takeAll: true, quantityKg: null, undoQuantity: null },
      invoicedSources: [{ sourceEventId: id(70), takeAll: true, quantityKg: null, undoQuantity: null }] })
  })
  it('does not post when source allocation evidence is unavailable', async () => {
    vi.mocked(fetchSupplierReturnCostSources).mockResolvedValue({ type: 'http', status: 503, body: null })
    mount({ canAdjust: false }); await userEvent.click(screen.getByRole('button', { name: T.show }))
    await screen.findByRole('heading', { name: receipt.labelCode })
    await userEvent.type(screen.getByLabelText(T.purchaseQuantity), '2')
    await userEvent.type(screen.getByLabelText(T.measuredKg), '1,9')
    await userEvent.type(screen.getByLabelText(T.reason), 'SENTETIK')
    await userEvent.click(screen.getByRole('button', { name: T.save }))
    expect(createSupplierReturn).not.toHaveBeenCalled()
  })
  it('requires independent purchase quantity for whole remaining kg after a shipment', async () => {
    vi.mocked(fetchSupplierReturnCostSources).mockResolvedValue({ type: 'ok-single', data: {
      companyId: id(1), goodsAcceptanceId: id(10), labelId: id(12), sourceVersion: 'synthetic-shipped',
      pendingQuantityKg: { numerator: '19', denominator: '10' }, pendingUndoQuantity: { numerator: '4', denominator: '1' },
      pendingWholePairAvailable: false, invoicedSources: [],
    } })
    vi.mocked(createSupplierReturn).mockResolvedValue({ type: 'unknown', reason: 'network-error' })
    mount({ canAdjust: false }); await openAndFill()
    await userEvent.click(screen.getByLabelText(T.takeAllSource(T.pendingSource)))
    expect(screen.queryByLabelText(T.sourceKg(T.pendingSource))).toBeNull()
    const quantity = screen.getByLabelText(T.sourceQuantity(T.pendingSource))
    await userEvent.clear(quantity)
    await userEvent.click(screen.getByRole('button', { name: T.save }))
    expect(createSupplierReturn).not.toHaveBeenCalled()
    await userEvent.type(quantity, '2')
    await userEvent.click(screen.getByRole('button', { name: T.save }))
    await waitFor(() => expect(createSupplierReturn).toHaveBeenCalledOnce())
    expect(vi.mocked(createSupplierReturn).mock.calls[0][2].costAllocation?.pending)
      .toEqual({ takeAll: true, quantityKg: null, undoQuantity: '2' })
  })
  it('submits a visible expected order version on approval and requires a reason on rejection', async () => {
    vi.mocked(decideSupplierReturn).mockResolvedValue({ type: 'unknown', reason: 'network-error' })
    mount({ canAdjust: false, canReturn: false, canDecide: true })
    await userEvent.click(screen.getByRole('button', { name: T.show }))
    await userEvent.click(await screen.findByRole('button', { name: T.reject }))
    expect(decideSupplierReturn).not.toHaveBeenCalled()
    await userEvent.clear(screen.getByLabelText(T.expectedOrderVersion)); await userEvent.type(screen.getByLabelText(T.expectedOrderVersion), '7')
    await userEvent.click(screen.getByRole('button', { name: T.approve }))
    await waitFor(() => expect(decideSupplierReturn).toHaveBeenCalledOnce())
    expect(vi.mocked(decideSupplierReturn).mock.calls[0][4]).toEqual({ expectedVersion: '1', expectedPurchaseOrderVersion: '7', reason: null })
  })

  it('shows the frozen source selection and withheld price before deciding a return', async () => {
    vi.mocked(fetchSupplierReturns).mockResolvedValue({ type: 'ok-single', data: { searchWindowFull: false, items: [{ ...pendingReturn,
      priceDisclosure: 'Withheld', unitCostTryPerKg: null,
      costAllocation: { expectedSourceVersion: 'synthetic-v1', pending: { takeAll: false, quantityKg: '0.9', undoQuantity: '1' },
        invoicedSources: [{ sourceEventId: id(70), takeAll: false, quantityKg: '1', undoQuantity: '1' }] },
      costAllocationSources: [{ sourceEventId: id(70), invoiceId: id(71), invoiceLineId: id(72), invoiceNumber: 'SENTETIK-FATURA', invoiceLineNo: 2 }],
    }] } })
    mount({ canAdjust: false, canReturn: false, canDecide: true })
    await userEvent.click(screen.getByRole('button', { name: T.show }))
    const summary = await screen.findByRole('region', { name: T.selectedSources })
    expect(summary.textContent).toContain(T.invoiceSource('SENTETIK-FATURA', 2))
    expect(summary.textContent).toContain(`${T.pendingSource}: ${T.measuredKg}: 0.9; ${T.purchaseQuantity}: 1`)
    expect(screen.getByText(`${T.manualCost}: ${T.sourcePriceWithheld}`)).toBeTruthy()
    expect(decideSupplierReturn).not.toHaveBeenCalled()
  })
  it('explains how to replace a legacy pending request without guessing its missing source quantities', async () => {
    vi.mocked(decideSupplierReturn).mockResolvedValue({ type: 'failed', status: 409, code: 'purchasing_supplier_return_cost_allocation_required', isReplay: false })
    mount({ canAdjust: false, canReturn: false, canDecide: true })
    await userEvent.click(screen.getByRole('button', { name: T.show }))
    await userEvent.click(await screen.findByRole('button', { name: T.approve }))
    expect(await screen.findByText(T.sourceAllocationRequired)).toBeTruthy()
    expect((screen.getByRole('button', { name: T.reject }) as HTMLButtonElement).disabled).toBe(false)
    expect(createSupplierReturn).not.toHaveBeenCalled()
  })
  it('requires explicit re-selection after a refreshed source version changes', async () => {
    mount({ canAdjust: false, canReturn: true })
    await openAndFill()
    const first = await vi.mocked(fetchSupplierReturnCostSources).mock.results[0].value
    if (first.type !== 'ok-single') throw new Error('synthetic source fixture missing')
    vi.mocked(fetchSupplierReturnCostSources).mockResolvedValue({ ...first, data: { ...first.data, sourceVersion: 'synthetic-refreshed-v2' } })
    await userEvent.click(screen.getByRole('button', { name: T.refreshSources }))
    await waitFor(() => expect((screen.getByLabelText(T.selectSource(T.pendingSource)) as HTMLInputElement).checked).toBe(false))
    await userEvent.click(screen.getByRole('button', { name: T.save }))
    expect(createSupplierReturn).not.toHaveBeenCalled()
    expect(await screen.findByText(T.invalid)).toBeTruthy()
  })
  it.each([401, 403, 404, 409, 503])('shows the %s read state and never posts with unavailable source data', async (status) => {
    vi.mocked(fetchGoodsReceipts).mockResolvedValue({ type: 'http', status, body: null })
    mount(); await userEvent.click(screen.getByRole('button', { name: T.show }))
    await waitFor(() => expect(fetchGoodsReceipts).toHaveBeenCalled())
    expect(screen.queryByRole('button', { name: T.save })).toBeNull()
    expect(reverseReceipt).not.toHaveBeenCalled()
    if (status === 401 || status === 403) expect(boundary.report).toHaveBeenCalledWith(status, { token: 1 })
  })
})
describe('Strict undo response identity and replay decoder', () => {
  const request: ReceiptReversalRequest = { expectedPurchaseOrderVersion: '4', adjustmentId: id(30), reversalLineId: id(31), inventoryMovementId: id(32),
    undoQuantity: '2', measuredUndoQuantityKg: '1.9', sourceBlock: 'Free', reason: 'SENTETIK', occurredAtUtc: '2026-11-16T03:00:00Z' }
  const wire = () => ({ purchase: { ...answer.purchase, companyId: id(1), adjustmentId: id(30), orderId: id(2), acceptanceId: id(10), lineId: id(3),
    generalLineId: id(3), newAcceptanceId: null, newAcceptedQuantity: '0' }, stock: { ...answer.stock, companyId: id(1), adjustmentId: id(30), acceptanceId: id(10),
    inventoryMovementId: id(32), originalReceiptMovementId: id(13), labelId: id(12), labelVersion: '3', originalCorrelationId: answer.purchase.originalCorrelationId }, correctionStock: null })
  it('accepts exact owner facts and rejects a wrong company, movement or replay header', () => {
    expect(decodeReceiptReversal(wire(), id(1), id(2), id(10), request, false)).not.toBeNull()
    expect(decodeReceiptReversal(wire(), id(9), id(2), id(10), request, false)).toBeNull()
    const malformed = wire(); malformed.stock.inventoryMovementId = id(99)
    expect(decodeReceiptReversal(malformed, id(1), id(2), id(10), request, false)).toBeNull()
    expect(decodeReceiptReversal(wire(), id(1), id(2), id(10), request, true)).toBeNull()
  })
})
