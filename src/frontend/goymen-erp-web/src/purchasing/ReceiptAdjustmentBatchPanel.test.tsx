import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { PurchaseOrderDetail } from './contract'
import type { GoodsReceipt } from './goodsReceiptContract'
import { ReceiptAdjustmentBatchPanel } from './ReceiptAdjustmentBatchPanel'
import { reverseReceiptBatch } from './receiptAdjustmentBatchApi'
import { RECEIPT_BATCH_ADJUSTMENT_TEXT as T } from './receiptAdjustmentBatchText'
import { resetAdjustmentAttemptsForTests } from './useReceiptAdjustmentAttempt'

vi.mock('./receiptAdjustmentBatchApi', () => ({ reverseReceiptBatch: vi.fn() }))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => ({ capture: () => ({}), report: vi.fn() }) }))
const id = (n: number) => `${n.toString().padStart(8, '0')}-1111-4111-8111-111111111111`
const detail: PurchaseOrderDetail = { order: { id: id(2), companyId: id(1), status: 'Approved', version: '4', supplierPartyId: id(8), companyAccountId: id(9),
  sourceRequestId: null, effectiveOn: '2026-11-16', createdAtUtc: '2026-11-16T00:00:00Z', updatedAtUtc: '2026-11-16T00:00:00Z', lineCount: 1 }, lines: [], acceptances: [] }
const receipt: GoodsReceipt = { companyId: id(1), acceptanceId: id(10), purchaseOrderId: id(2), purchaseOrderLineId: id(3), purchaseOrderVersion: '4', productId: id(4), productVersion: '7',
  unitId: id(5), acceptedQuantity: '4', remainingQuantity: '6', lotId: id(11), labelId: id(12), labelCode: 'SENTETIK-ACCEPTED', labelVersion: '2', movementId: id(13),
  locationId: id(6), measuredQuantityKg: '3.9', initialBlock: 'Free', occurredAtUtc: '2026-11-16T01:00:00Z' }
const mount = (binding = 'read-1', attemptBinding = 'synthetic-actor-company') => render(<ReceiptAdjustmentBatchPanel detail={detail} receipts={[receipt]}
  binding={binding} attemptBinding={attemptBinding} onSettled={vi.fn()} />)
afterEach(() => { cleanup(); resetAdjustmentAttemptsForTests(); vi.resetAllMocks() })
describe('Batch correction journey, synthetic NOT_ENV', () => {
  it('freezes exact selected content and key across refresh/remount after an unknown response', async () => {
    vi.mocked(reverseReceiptBatch).mockResolvedValue({ type: 'unknown', reason: 'service-unavailable', status: 503 })
    const first = mount(); await userEvent.click(screen.getByLabelText(T.choose))
    await userEvent.type(screen.getByLabelText(T.quantity), '2'); await userEvent.type(screen.getByLabelText(T.kg), '1,9')
    await userEvent.type(screen.getByLabelText(T.reason), 'SENTETIK same correction'); await userEvent.click(screen.getByRole('button', { name: T.save }))
    await screen.findByText(T.unknown); expect((screen.getByLabelText(T.quantity) as HTMLInputElement).disabled).toBe(true)
    first.unmount(); mount('read-2'); await userEvent.click(await screen.findByRole('button', { name: T.retry }))
    await waitFor(() => expect(reverseReceiptBatch).toHaveBeenCalledTimes(2))
    expect(vi.mocked(reverseReceiptBatch).mock.calls[1]).toEqual(vi.mocked(reverseReceiptBatch).mock.calls[0])
    expect(vi.mocked(reverseReceiptBatch).mock.calls[0][2].lines[0]).toMatchObject({ undoQuantity: '2', measuredUndoQuantityKg: '1.9' })
  })
  it('does not send without a chosen receipt and uses a separate company/actor scope for unresolved attempts', async () => {
    mount(); await userEvent.click(screen.getByRole('button', { name: T.save })); expect(reverseReceiptBatch).not.toHaveBeenCalled()
    expect(screen.getByRole('alert')).toHaveTextContent(T.invalid)
  })
})
