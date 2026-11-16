import { afterEach, describe, expect, it, vi } from 'vitest'
import { reverseReceipt } from './receiptAdjustmentApi'
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
