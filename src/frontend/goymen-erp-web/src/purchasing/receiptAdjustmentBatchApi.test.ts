import { afterEach, describe, expect, it, vi } from 'vitest'
import { reverseReceiptBatch } from './receiptAdjustmentBatchApi'
import { decodeReceiptReversalBatch, type ReceiptReversalBatchRequest } from './receiptAdjustmentBatchContract'

const id = (n: number) => `${n.toString().padStart(8, '0')}-1111-4111-8111-111111111111`
const request: ReceiptReversalBatchRequest = { batchId: id(20), expectedPurchaseOrderVersion: '4', reason: 'SENTETIK batch', occurredAtUtc: '2026-11-16T01:00:00Z',
  lines: [{ acceptanceId: id(3), adjustmentId: id(4), reversalLineId: id(5), inventoryMovementId: id(6), undoQuantity: '2', measuredUndoQuantityKg: '1.9', sourceBlock: 'Free' }] }
function body(replay = false) { return { companyId: id(1), orderId: id(2), batchId: id(20), orderVersion: '5', isReplay: replay, originalCorrelationId: 'SENTETIK-original', revived: false,
  supplies: [{ generalLineId: id(7), netOpenedQuantity: '0', allocatableQuantity: '6', revived: false }],
  lines: [{ purchase: { companyId: id(1), adjustmentId: id(4), orderId: id(2), acceptanceId: id(3), lineId: id(7), generalLineId: id(7),
    orderVersion: '5', netOpenedQuantity: '0', allocatableQuantity: '6', revived: false, isReplay: replay, originalCorrelationId: 'SENTETIK-original', newAcceptanceId: null, newAcceptedQuantity: '0' },
  stock: { companyId: id(1), adjustmentId: id(4), acceptanceId: id(3), inventoryMovementId: id(6), originalReceiptMovementId: id(8), labelId: id(9), labelVersion: '3',
    quantityKg: '1.9', conversions: [], isReplay: replay, originalCorrelationId: 'SENTETIK-original' }, correctionStock: null }] } }
afterEach(() => vi.unstubAllGlobals())
describe('API44 batch strict consumer, synthetic NOT_ENV', () => {
  it('accepts owner group facts without calculating a net quantity in the browser', async () => {
    const fetcher = vi.fn().mockResolvedValue(new Response(JSON.stringify(body(true)), { status: 200, headers: { 'Idempotency-Replayed': 'true' } }))
    vi.stubGlobal('fetch', fetcher)
    const result = await reverseReceiptBatch(id(1), id(2), request, 'SENTETIK-key')
    expect(result.type).toBe('applied')
    const [path, init] = fetcher.mock.calls[0] as [string, RequestInit]
    expect(path).toContain(`/orders/${id(2)}/goods-receipts/reversal-batch`)
    expect((init.headers as Record<string, string>)['Idempotency-Key']).toBe('SENTETIK-key')
    expect(JSON.parse(init.body as string)).toEqual(request)
  })
  it.each(['company', 'version', 'supply', 'missing-supply', 'duplicate-supply', 'duplicate-line', 'replay', 'correlation'])('refuses a %s mismatch', (fault) => {
    const value = body()
    if (fault === 'company') value.companyId = id(99)
    if (fault === 'version') value.lines[0].purchase.orderVersion = '6'
    if (fault === 'supply') value.supplies[0].generalLineId = id(99)
    if (fault === 'missing-supply') value.supplies = []
    if (fault === 'duplicate-supply') value.supplies.push(value.supplies[0])
    if (fault === 'duplicate-line') value.lines.push(value.lines[0])
    if (fault === 'replay') value.lines[0].stock.isReplay = true
    if (fault === 'correlation') value.originalCorrelationId = 'another-synthetic-correlation'
    expect(decodeReceiptReversalBatch(value, id(1), id(2), request, false)).toBeNull()
  })
  it.each([500, 503])('retains HTTP %s as an unresolved operation', async (status) => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{}', { status })))
    expect(await reverseReceiptBatch(id(1), id(2), request, 'SENTETIK-key')).toEqual({ type: 'unknown', reason: 'service-unavailable', status })
  })
})
