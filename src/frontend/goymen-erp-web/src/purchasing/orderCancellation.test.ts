import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { decodePurchaseOrderCancelImpact, decodePurchaseOrderCommandResponse, decodePurchaseOrderDetail } from './contract'
import { cancelPurchaseOrderRemainder, fetchPurchaseOrderCancelImpact } from './orderCancellationApi'
import {
  pendingOrderCancellationKey,
  recallPendingOrderCancellation,
  releasePendingOrderCancellation,
  reservePendingOrderCancellation,
} from './pendingCancellationStore'

const companyId = '11111111-1111-4111-8111-111111111111'
const orderId = '22222222-2222-4222-8222-222222222222'
const lineId = '33333333-3333-4333-8333-333333333333'
const productId = '44444444-4444-4444-8444-444444444444'
const unitId = '55555555-5555-4555-8555-555555555555'
const keyId = '66666666-6666-4666-8666-666666666666'
const actorId = '77777777-7777-4777-8777-777777777777'
const partyId = '88888888-8888-4888-8888-888888888888'
const at = '2026-11-03T09:00:00Z'

const line = (extra: Record<string, unknown> = { cancelled: '0' }) => ({
  lineId, lineNo: 1, kind: 'General', parentLineId: null, sourceRequestLineId: null, productId, productVersion: '7', unitId,
  ordered: '10', technical: '0', committed: '10', accepted: '4', remaining: '6', ...extra,
})
const summary = (status: string, version: string) => ({
  id: orderId, companyId, status, version, supplierPartyId: partyId, companyAccountId: '99999999-9999-4999-8999-999999999999',
  sourceRequestId: null, effectiveOn: '2026-11-03', createdAtUtc: at, updatedAtUtc: at, lineCount: 1,
})
const readSummary = (status: string, version: string) => ({
  ...summary(status, version), supplierDisplay: { partyId, code: 'CAR-000001', legalName: 'SENTETIK Tedarik', tradeName: null },
})
const cancellation = { cancelledBy: 'SENTETIK-manager', cancelledAtUtc: at, reason: 'SENTETIK kalan gelmeyecek' }
const cancelledLine = line({ remaining: '0', cancelled: '6' })
const detail = (status: string, version: string, extra: Record<string, unknown> = {}) => ({
  order: summary(status, version), lines: [status === 'Cancelled' ? cancelledLine : line()], acceptances: [],
  cancellation: status === 'Cancelled' ? cancellation : null, ...extra,
})
const impact = (extra: Record<string, unknown> = {}) => ({
  orderId, status: 'Approved', version: '4', sourceRequestId: null, cancellable: true, blocker: null, hasAcceptance: true,
  lines: [{ lineId, lineNo: 1, kind: 'General', productId, unitId, committed: '10', accepted: '4', toCancel: '6' }], ...extra,
})

function response(payload: unknown, status = 200, headers: Record<string, string> = {}) {
  return new Response(JSON.stringify(payload), { status, headers: { 'Content-Type': 'application/json', ...headers } })
}

beforeEach(() => sessionStorage.clear())
afterEach(() => vi.unstubAllGlobals())

describe('A1-3 order remainder cancellation wire contract (plan API-10; No.161 §2)', () => {
  it('decodes the cancelled order, its cancellation and each line\'s cancelled share, consistent with the status', () => {
    const read = decodePurchaseOrderDetail({ ...detail('Cancelled', '5'), order: readSummary('Cancelled', '5') })
    expect(read?.cancellation?.reason).toBe('SENTETIK kalan gelmeyecek')
    expect(read?.lines[0].cancelled).toBe('6')
    const approved = decodePurchaseOrderDetail({ ...detail('Approved', '4'), order: readSummary('Approved', '4') })
    expect([approved?.cancellation, approved?.lines[0].cancelled]).toEqual([null, '0'])
    // The bodies from before A1-3 still read (no cancellation, no cancelled share), but never as Cancelled.
    expect(decodePurchaseOrderDetail({ order: readSummary('Approved', '4'), lines: [line({})], acceptances: [] })?.cancellation).toBeUndefined()
    expect(decodePurchaseOrderDetail({ order: readSummary('Cancelled', '5'), lines: [line({})], acceptances: [] })).toBeNull()
    expect(decodePurchaseOrderDetail({ order: readSummary('Cancelled', '5'), lines: [line({})] })).toBeNull()
  })

  it.each([
    ['cancelled without a cancellation', detail('Cancelled', '5', { cancellation: null })],
    ['a cancellation on an approved order', detail('Approved', '4', { cancellation })],
    ['a blank reason', detail('Cancelled', '5', { cancellation: { ...cancellation, reason: '  ' } })],
    ['a blank canceller', detail('Cancelled', '5', { cancellation: { ...cancellation, cancelledBy: '' } })],
    ['a malformed instant', detail('Cancelled', '5', { cancellation: { ...cancellation, cancelledAtUtc: 'dün' } })],
    ['an extra cancellation field', detail('Cancelled', '5', { cancellation: { ...cancellation, approvedBy: 'x' } })],
    ['a current body whose line has no cancelled share', detail('Approved', '4', { lines: [line({})] })],
    ['an old body whose line has a cancelled share', { order: summary('Approved', '4'), lines: [line()], acceptances: [] }],
    ['a cancelled share that is not a decimal', detail('Approved', '4', { lines: [line({ cancelled: '-1' })] })],
  ])('refuses %s', (_name, payload) => {
    expect(decodePurchaseOrderCommandResponse({ detail: payload, isReplay: false, originalCorrelationId: 'corr' })).toBeNull()
  })

  it('decodes the impact preview and refuses one whose blocker disagrees with cancellable or whose lines are not in order', () => {
    expect(decodePurchaseOrderCancelImpact(impact())?.lines[0].toCancel).toBe('6')
    expect(decodePurchaseOrderCancelImpact(impact({ cancellable: false, blocker: 'purchasing_order_nothing_to_cancel' }))?.blocker)
      .toBe('purchasing_order_nothing_to_cancel')
    expect(decodePurchaseOrderCancelImpact(impact({ cancellable: false, blocker: null }))).toBeNull()
    expect(decodePurchaseOrderCancelImpact(impact({ cancellable: false, blocker: 'something_else' }))).toBeNull()
    expect(decodePurchaseOrderCancelImpact(impact({ blocker: 'purchasing_order_nothing_to_cancel' }))).toBeNull()
    expect(decodePurchaseOrderCancelImpact(impact({ lines: [{ ...impact().lines[0], lineNo: 2 }] }))).toBeNull()
    expect(decodePurchaseOrderCancelImpact(impact({ lines: [{ ...impact().lines[0], toCancel: '6,5' }] }))).toBeNull()
    expect(decodePurchaseOrderCancelImpact({ ...impact(), extra: true })).toBeNull()
  })

  it('posts the expected version and the reason under the Idempotency-Key and accepts only this order, now Cancelled', async () => {
    const fetchMock = vi.fn(async () => response({ detail: detail('Cancelled', '5'), isReplay: false, originalCorrelationId: 'corr-1' }))
    vi.stubGlobal('fetch', fetchMock)
    const outcome = await cancelPurchaseOrderRemainder(companyId, orderId, '4', keyId, 'SENTETIK kalan gelmeyecek')
    expect(outcome.type).toBe('applied')
    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit]
    expect(url).toMatch(new RegExp(`/purchasing/orders/${orderId}/cancellation$`))
    expect(init.method).toBe('POST')
    expect((init.headers as Record<string, string>)['Idempotency-Key']).toBe(keyId)
    expect((init.headers as Record<string, string>)['X-Company-Id']).toBe(companyId)
    expect(JSON.parse(init.body as string)).toEqual({ expectedVersion: '4', reason: 'SENTETIK kalan gelmeyecek' })

    vi.stubGlobal('fetch', vi.fn(async () => response({ detail: detail('Approved', '4'), isReplay: false, originalCorrelationId: 'corr-1' })))
    expect(await cancelPurchaseOrderRemainder(companyId, orderId, '4', keyId, 'x')).toEqual({ type: 'unknown', reason: 'undecodable' })
    vi.stubGlobal('fetch', vi.fn(async () => response({ detail: detail('Cancelled', '5'), isReplay: true, originalCorrelationId: 'corr-1' })))
    expect(await cancelPurchaseOrderRemainder(companyId, orderId, '4', keyId, 'x')).toEqual({ type: 'unknown', reason: 'undecodable' })
    vi.stubGlobal('fetch', vi.fn(async () => response({ detail: detail('Cancelled', '5'), isReplay: true, originalCorrelationId: 'corr-1' }, 200,
      { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'corr-1' })))
    expect(await cancelPurchaseOrderRemainder(companyId, orderId, '4', keyId, 'x')).toMatchObject({ type: 'applied', replayed: true })
  })

  it('classifies refusals as failed with their code and a lost answer, a 5xx or another status as unknown', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => response({ code: 'purchasing_order_nothing_to_cancel' }, 409, { 'Idempotency-Replayed': 'true' })))
    expect(await cancelPurchaseOrderRemainder(companyId, orderId, '4', keyId, 'x'))
      .toEqual({ type: 'failed', status: 409, code: 'purchasing_order_nothing_to_cancel', replayed: true })
    vi.stubGlobal('fetch', vi.fn(async () => response({}, 403)))
    expect(await cancelPurchaseOrderRemainder(companyId, orderId, '4', keyId, 'x')).toEqual({ type: 'failed', status: 403, code: null, replayed: false })
    vi.stubGlobal('fetch', vi.fn(async () => response({}, 503)))
    expect(await cancelPurchaseOrderRemainder(companyId, orderId, '4', keyId, 'x')).toEqual({ type: 'unknown', reason: 'service-unavailable' })
    vi.stubGlobal('fetch', vi.fn(async () => { throw new TypeError('network') }))
    expect((await cancelPurchaseOrderRemainder(companyId, orderId, '4', keyId, 'x')).type).toBe('unknown')
  })

  it('reads the preview of the asked order only', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => response(impact())))
    expect(await fetchPurchaseOrderCancelImpact(companyId, orderId)).toMatchObject({ type: 'ok-single' })
    vi.stubGlobal('fetch', vi.fn(async () => response(impact({ orderId: keyId }))))
    expect(await fetchPurchaseOrderCancelImpact(companyId, orderId)).toEqual({ type: 'malformed' })
    vi.stubGlobal('fetch', vi.fn(async () => response({}, 404)))
    expect(await fetchPurchaseOrderCancelImpact(companyId, orderId)).toEqual({ type: 'http', status: 404, body: {} })
  })

  it('keeps one unresolved attempt per session, actor, company and order, and releases only its own key', () => {
    const key = pendingOrderCancellationKey(1, actorId, companyId, orderId)
    expect(pendingOrderCancellationKey(1, null, companyId, orderId)).toBeNull()
    expect(pendingOrderCancellationKey(1, actorId, companyId, 'not-an-id')).toBeNull()
    const snapshot = { actorId, companyId, orderId, expectedVersion: '4', idempotencyKey: keyId, reason: 'SENTETIK' }
    reservePendingOrderCancellation(key, snapshot)
    expect(recallPendingOrderCancellation(key)).toEqual(snapshot)
    releasePendingOrderCancellation(key, '99999999-9999-4999-8999-999999999999')
    expect(recallPendingOrderCancellation(key)).toEqual(snapshot)
    releasePendingOrderCancellation(key, keyId)
    expect(recallPendingOrderCancellation(key)).toBeNull()
    sessionStorage.setItem(key!, JSON.stringify({ ...snapshot, reason: '   ' }))
    expect(recallPendingOrderCancellation(key)).toBeNull()
    sessionStorage.setItem(key!, JSON.stringify({ ...snapshot, expectedVersion: '0' }))
    expect(recallPendingOrderCancellation(key)).toBeNull()
  })
})
