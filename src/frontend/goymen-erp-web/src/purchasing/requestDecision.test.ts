import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { decodePurchaseRequestDetail } from './contract'
import { decidePurchaseRequest } from './requestDecisionApi'
import {
  pendingPurchaseRequestDecisionKey,
  recallPendingPurchaseRequestDecision,
  releasePendingPurchaseRequestDecision,
  reservePendingPurchaseRequestDecision,
} from './pendingRequestStore'

const companyId = '11111111-1111-4111-8111-111111111111'
const requestId = '22222222-2222-4222-8222-222222222222'
const lineId = '33333333-3333-4333-8333-333333333333'
const productId = '44444444-4444-4444-8444-444444444444'
const unitId = '55555555-5555-4555-8555-555555555555'
const keyId = '66666666-6666-4666-8666-666666666666'
const at = '2026-11-02T10:00:00Z'

const line = { lineId, lineNo: 1, productId, productVersion: '7', unitId, quantity: '12.5' }
const summary = (status: string, version: string) => ({
  id: requestId, companyId, status, version, effectiveOn: '2026-11-02', createdAtUtc: at, updatedAtUtc: at, lineCount: 1,
})
const approval = { decidedBy: 'SENTETIK-manager', decidedAtUtc: at, reason: null }
const rejection = { decidedBy: 'SENTETIK-manager', decidedAtUtc: at, reason: 'SENTETIK ihtiyaç kalktı' }

function response(body: unknown, status = 200, headers: Record<string, string> = {}) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json', ...headers } })
}

beforeEach(() => sessionStorage.clear())
afterEach(() => vi.unstubAllGlobals())

describe('A1-1 request decision wire contract', () => {
  it('decodes the decision facts and keeps them consistent with the status', () => {
    expect(decodePurchaseRequestDetail({ request: summary('Approved', '4'), lines: [line], approval, rejection: null })?.approval?.decidedBy)
      .toBe('SENTETIK-manager')
    expect(decodePurchaseRequestDetail({ request: summary('Rejected', '5'), lines: [line], approval, rejection })?.rejection?.reason)
      .toBe('SENTETIK ihtiyaç kalktı')
    expect(decodePurchaseRequestDetail({ request: summary('Submitted', '3'), lines: [line], approval: null, rejection: null })?.approval)
      .toBeNull()
    // A legacy body (before A1-1) still reads, but never for a decided status.
    expect(decodePurchaseRequestDetail({ request: summary('Submitted', '3'), lines: [line] })?.request.status).toBe('Submitted')
    expect(decodePurchaseRequestDetail({ request: summary('Approved', '4'), lines: [line] })).toBeNull()
  })

  it.each([
    ['approved without approval', { request: summary('Approved', '4'), lines: [line], approval: null, rejection: null }],
    ['rejected without rejection', { request: summary('Rejected', '4'), lines: [line], approval: null, rejection: null }],
    ['submitted with a decision', { request: summary('Submitted', '3'), lines: [line], approval, rejection: null }],
    ['approval with a reason', { request: summary('Approved', '4'), lines: [line], approval: { ...approval, reason: 'x' }, rejection: null }],
    ['rejection without a reason', { request: summary('Rejected', '4'), lines: [line], approval: null, rejection: { ...rejection, reason: null } }],
    ['blank rejection reason', { request: summary('Rejected', '4'), lines: [line], approval: null, rejection: { ...rejection, reason: ' ' } }],
    ['unknown decision field', { request: summary('Approved', '4'), lines: [line], approval: { ...approval, extra: 1 }, rejection: null }],
    ['unknown status', { request: summary('Closed', '4'), lines: [line], approval: null, rejection: null }],
  ])('refuses %s', (_name, body) => {
    expect(decodePurchaseRequestDetail(body)).toBeNull()
  })

  it('posts the approval to its route with the key and version only', async () => {
    const fetch = vi.fn().mockResolvedValue(response({
      detail: { request: summary('Approved', '4'), lines: [line], approval, rejection: null }, isReplay: false, originalCorrelationId: 'corr-1',
    }))
    vi.stubGlobal('fetch', fetch)
    const outcome = await decidePurchaseRequest(companyId, requestId, 'approval', '3', keyId)
    expect(outcome).toMatchObject({ type: 'applied', replayed: false })
    expect(String(fetch.mock.calls[0][0]).endsWith(`/purchasing/requests/${requestId}/approval`)).toBe(true)
    expect(JSON.parse(fetch.mock.calls[0][1].body as string)).toEqual({ expectedVersion: '3' })
    expect(fetch.mock.calls[0][1].headers['Idempotency-Key']).toBe(keyId)
    expect(fetch.mock.calls[0][1].headers['X-Company-Id']).toBe(companyId)
  })

  it('posts the rejection with its reason and requires the replay headers to agree', async () => {
    const fetch = vi.fn().mockResolvedValue(response({
      detail: { request: summary('Rejected', '5'), lines: [line], approval: null, rejection }, isReplay: true, originalCorrelationId: 'corr-1',
    }, 200, { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'corr-1' }))
    vi.stubGlobal('fetch', fetch)
    const outcome = await decidePurchaseRequest(companyId, requestId, 'rejection', '4', keyId, 'SENTETIK ihtiyaç kalktı')
    expect(outcome).toMatchObject({ type: 'applied', replayed: true })
    expect(String(fetch.mock.calls[0][0]).endsWith(`/purchasing/requests/${requestId}/rejection`)).toBe(true)
    expect(JSON.parse(fetch.mock.calls[0][1].body as string)).toEqual({ expectedVersion: '4', reason: 'SENTETIK ihtiyaç kalktı' })
  })

  it.each([
    ['a 200 whose status is not the decision', response({
      detail: { request: summary('Submitted', '3'), lines: [line], approval: null, rejection: null }, isReplay: false, originalCorrelationId: 'corr-1',
    })],
    ['a replay without its headers', response({
      detail: { request: summary('Approved', '4'), lines: [line], approval, rejection: null }, isReplay: true, originalCorrelationId: 'corr-1',
    })],
    ['a 503', response({ code: 'service_unavailable' }, 503)],
  ])('treats %s as an unknown result', async (_name, answer) => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(answer))
    expect((await decidePurchaseRequest(companyId, requestId, 'approval', '3', keyId)).type).toBe('unknown')
  })

  it('reports a refusal with its code and a lost connection as unknown', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response({ code: 'purchasing_request_has_approved_order' }, 409, { 'Idempotency-Replayed': 'true' })))
    expect(await decidePurchaseRequest(companyId, requestId, 'rejection', '4', keyId, 'x'))
      .toEqual({ type: 'failed', status: 409, code: 'purchasing_request_has_approved_order', replayed: true })
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('offline')))
    expect((await decidePurchaseRequest(companyId, requestId, 'approval', '3', keyId)).type).toBe('unknown')
  })

  it('keeps an unresolved decision snapshot per actor, company and request, and only valid ones', () => {
    const key = pendingPurchaseRequestDecisionKey(1, 'SENTETIK-actor', companyId, requestId)
    expect(pendingPurchaseRequestDecisionKey(1, null, companyId, requestId)).toBeNull()
    const snapshot = { actorId: 'SENTETIK-actor', companyId, requestId, kind: 'rejection' as const, expectedVersion: '4', idempotencyKey: keyId, reason: 'neden' }
    reservePendingPurchaseRequestDecision(key, snapshot)
    expect(recallPendingPurchaseRequestDecision(key)).toEqual(snapshot)
    releasePendingPurchaseRequestDecision(key, '77777777-7777-4777-8777-777777777777')
    expect(recallPendingPurchaseRequestDecision(key)).toEqual(snapshot)
    releasePendingPurchaseRequestDecision(key, keyId)
    expect(recallPendingPurchaseRequestDecision(key)).toBeNull()
    sessionStorage.setItem(key!, JSON.stringify({ ...snapshot, reason: ' ' }))
    expect(recallPendingPurchaseRequestDecision(key)).toBeNull()
    sessionStorage.setItem(key!, JSON.stringify({ ...snapshot, kind: 'approval' }))
    expect(recallPendingPurchaseRequestDecision(key)).toBeNull()
  })
})
