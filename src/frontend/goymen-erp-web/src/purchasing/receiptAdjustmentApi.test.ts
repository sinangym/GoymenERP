import { afterEach, describe, expect, it, vi } from 'vitest'
import { createSupplierReturn, decideSupplierReturn, reverseReceipt } from './receiptAdjustmentApi'
import type { ReceiptReversalRequest } from './receiptAdjustmentContract'

const id = (n: number) => `${n.toString().padStart(8, '0')}-1111-4111-8111-111111111111`
const request: ReceiptReversalRequest = { expectedPurchaseOrderVersion: '4', adjustmentId: id(4), reversalLineId: id(5),
  inventoryMovementId: id(6), undoQuantity: '2', measuredUndoQuantityKg: '1.9', sourceBlock: 'Free', reason: 'SENTETIK', occurredAtUtc: '2026-11-16T01:00:00Z' }
function body(replay = false) { return {
  purchase: { companyId: id(1), adjustmentId: id(4), orderId: id(2), acceptanceId: id(3), lineId: id(7), generalLineId: id(7),
    orderVersion: '5', netOpenedQuantity: '2', allocatableQuantity: '8', revived: false, isReplay: replay, originalCorrelationId: 'SENTETIK-original',
    newAcceptanceId: null, newAcceptedQuantity: '0' },
  stock: { companyId: id(1), adjustmentId: id(4), acceptanceId: id(3), inventoryMovementId: id(6), originalReceiptMovementId: id(8),
    labelId: id(9), labelVersion: '3', quantityKg: '1.9', conversions: [], isReplay: replay, originalCorrelationId: 'SENTETIK-original' }, correctionStock: null,
} }
const response = (value: unknown, status: number, replay = false) => new Response(JSON.stringify(value), {
  status, headers: { 'Content-Type': 'application/json', ...(replay ? { 'Idempotency-Replayed': 'true' } : {}) },
})
afterEach(() => vi.unstubAllGlobals())
describe('Receipt adjustment HTTP consumer, synthetic NOT_ENV', () => {
  it('preserves the same bounded stock detail on supplier return creation and exception approval', async () => {
    const detail = 'Sistemde 1 kg var, 1.9 kg istendi.'
    vi.stubGlobal('fetch', vi.fn().mockImplementation(async () => response({ status: 409, code: 'SENTETIK-physical', detail }, 409)))
    const result = await createSupplierReturn(id(1), id(2), { returnId: id(10), returnLineId: id(11),
      goodsAcceptanceId: id(3), inventoryMovementId: id(6), sourceBlock: 'Free', quantityKg: '1.9',
      manualUnitCostTryPerKg: null, replacementExpected: false, reason: 'SENTETIK return',
      occurredAtUtc: '2026-11-16T01:00:00Z', expectedPurchaseOrderVersion: '4', undoQuantity: '2' }, 'SENTETIK-create-key')
    expect(result).toMatchObject({ type: 'failed', status: 409, detail })
    expect(await decideSupplierReturn(id(1), id(2), id(10), true,
      { expectedVersion: '1', reason: null }, 'SENTETIK-approval-key')).toMatchObject({ type: 'failed', status: 409, detail })
  })
  it.each([false, true])('preserves the same physical kg detail on first response and terminal replay=%s', async (replay) => {
    const detail = 'Sistemde -0.001 kg var, 1.9 kg istendi.'
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response({ status: 409, code: 'purchasing_supplier_return_stock_conflict', detail }, 409, replay)))
    expect(await reverseReceipt(id(1), id(2), id(3), request, 'SENTETIK-key'))
      .toEqual({ type: 'failed', status: 409, code: 'purchasing_supplier_return_stock_conflict', detail, isReplay: replay })
  })
  it.each(['Sistemde -0 kg var, 2 kg istendi.', 'Sistemde 1 kg var, 0 kg istendi.',
    'Sistemde 1.0001 kg var, 2 kg istendi.', 'Sistemde 01 kg var, 2 kg istendi.',
    'Sistemde 1.00 kg var, 2 kg istendi.', 'Sistemde 1 kg var, 2 kg istendi.\n',
    'Sistemde 1\u0000 kg var, 2 kg istendi.', '<script>unsafe</script>', null])
  ('omits noncanonical physical refusal detail %s', async (detail) => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response({ status: 409, code: 'SENTETIK', detail }, 409)))
    expect(await reverseReceipt(id(1), id(2), id(3), request, 'SENTETIK-key')).not.toHaveProperty('detail')
  })
  it.each([400, 401, 403, 404, 422])('does not display kg from a nonphysical HTTP %s', async (status) => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response({ status, detail: 'Sistemde 1 kg var, 2 kg istendi.' }, status)))
    expect(await reverseReceipt(id(1), id(2), id(3), request, 'SENTETIK-key')).not.toHaveProperty('detail')
  })
  it('does not take detail from an HTTP/body status mismatch', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response({ status: 503, detail: 'Sistemde 1 kg var, 2 kg istendi.' }, 409)))
    expect(await reverseReceipt(id(1), id(2), id(3), request, 'SENTETIK-key')).not.toHaveProperty('detail')
  })
  it('sends exact distinct purchase quantity and measured kg with company and idempotency fences and validates replay', async () => {
    const fetcher = vi.fn().mockResolvedValue(response(body(true), 200, true)); vi.stubGlobal('fetch', fetcher)
    const result = await reverseReceipt(id(1), id(2), id(3), request, 'SENTETIK-key')
    expect(result.type).toBe('applied')
    if (result.type === 'applied') expect(result.response.purchase.isReplay).toBe(true)
    const [path, init] = fetcher.mock.calls[0] as [string, RequestInit]
    expect(path).toContain(`/orders/${id(2)}/goods-receipts/${id(3)}/reversal`)
    expect((init.headers as Record<string, string>)['Idempotency-Key']).toBe('SENTETIK-key')
    expect(JSON.parse(init.body as string)).toEqual(request)
  })
  it.each([500, 503])('keeps an HTTP %s unresolved', async (status) => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response({ code: 'service_unavailable' }, status)))
    expect(await reverseReceipt(id(1), id(2), id(3), request, 'SENTETIK-key'))
      .toEqual({ type: 'unknown', reason: 'service-unavailable', status })
  })
  it.each(['company', 'movement', 'replay', 'status', 'body'])('keeps a %s response mismatch unresolved', async (fault) => {
    const value = body()
    if (fault === 'company') value.purchase.companyId = id(99)
    if (fault === 'movement') value.stock.inventoryMovementId = id(99)
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response(fault === 'body' ? {} : value, fault === 'status' ? 201 : 200, fault === 'replay')))
    expect((await reverseReceipt(id(1), id(2), id(3), request, 'SENTETIK-key')).type).toBe('unknown')
  })
  it('preserves a recorded terminal replay marker rather than discarding its status', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response({ code: 'SENTETIK-terminal' }, 409, true)))
    expect(await reverseReceipt(id(1), id(2), id(3), request, 'SENTETIK-key'))
      .toEqual({ type: 'failed', status: 409, code: 'SENTETIK-terminal', isReplay: true })
  })
})
