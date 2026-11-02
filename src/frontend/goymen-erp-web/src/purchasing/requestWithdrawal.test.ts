import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { decodePurchaseRequestDetail } from './contract'
import { decidePurchaseRequest } from './requestDecisionApi'
import {
  pendingPurchaseRequestWithdrawalKey,
  recallPendingPurchaseRequestWithdrawal,
  releasePendingPurchaseRequestWithdrawal,
  reservePendingPurchaseRequestWithdrawal,
} from './pendingRequestStore'

const companyId = '11111111-1111-4111-8111-111111111111'
const requestId = '22222222-2222-4222-8222-222222222222'
const lineId = '33333333-3333-4333-8333-333333333333'
const productId = '44444444-4444-4444-8444-444444444444'
const unitId = '55555555-5555-4555-8555-555555555555'
const keyId = '66666666-6666-4666-8666-666666666666'
const actorId = '77777777-7777-4777-8777-777777777777'
const at = '2026-11-02T10:00:00Z'

const line = { lineId, lineNo: 1, productId, productVersion: '7', unitId, quantity: '12.5' }
const summary = (status: string, version: string) => ({
  id: requestId, companyId, status, version, effectiveOn: '2026-11-02', createdAtUtc: at, updatedAtUtc: at, lineCount: 1,
})
const withdrawal = { decidedBy: 'SENTETIK-submitter', decidedAtUtc: at, reason: 'SENTETIK yanlış ürün' }
const body = (status: string, version: string, extra: Record<string, unknown> = {}) => ({
  request: summary(status, version), lines: [line], approval: null, rejection: null, withdrawal: null,
  preparedBy: 'SENTETIK-preparer', submittedBy: 'SENTETIK-submitter', ...extra,
})

function response(payload: unknown, status = 200, headers: Record<string, string> = {}) {
  return new Response(JSON.stringify(payload), { status, headers: { 'Content-Type': 'application/json', ...headers } })
}

beforeEach(() => sessionStorage.clear())
afterEach(() => vi.unstubAllGlobals())

describe('A1-1b request withdrawal wire contract (ALS-D46, ALS-D47)', () => {
  it('decodes the withdrawal and the recorded makers, and keeps them consistent with the status', () => {
    const withdrawn = decodePurchaseRequestDetail(body('Withdrawn', '4', { withdrawal }))
    expect(withdrawn?.withdrawal?.reason).toBe('SENTETIK yanlış ürün')
    expect([withdrawn?.preparedBy, withdrawn?.submittedBy]).toEqual(['SENTETIK-preparer', 'SENTETIK-submitter'])
    // ALS-D47: the preparer withdraws too, also a request submitted before A1-1 (no recorded submitter).
    expect(decodePurchaseRequestDetail(body('Withdrawn', '4', { withdrawal: { ...withdrawal, decidedBy: 'SENTETIK-preparer' } }))
      ?.withdrawal?.decidedBy).toBe('SENTETIK-preparer')
    expect(decodePurchaseRequestDetail(body('Withdrawn', '4', {
      withdrawal: { ...withdrawal, decidedBy: 'SENTETIK-preparer' }, submittedBy: null,
    }))?.submittedBy).toBeNull()
    expect(decodePurchaseRequestDetail(body('Submitted', '3'))?.submittedBy).toBe('SENTETIK-submitter')
    // A submission recorded before A1-1 carries no submitter; nobody may withdraw it, but it still reads.
    expect(decodePurchaseRequestDetail(body('Submitted', '3', { submittedBy: null }))?.submittedBy).toBeNull()
    expect(decodePurchaseRequestDetail(body('Draft', '2', { submittedBy: null }))?.withdrawal).toBeNull()
    // The A1-1 body (without the two fields) still reads, but never as Withdrawn.
    expect(decodePurchaseRequestDetail({ request: summary('Submitted', '3'), lines: [line], approval: null, rejection: null })?.submittedBy)
      .toBeNull()
    expect(decodePurchaseRequestDetail({ request: summary('Withdrawn', '4'), lines: [line], approval: null, rejection: null })).toBeNull()
    expect(decodePurchaseRequestDetail({ request: summary('Withdrawn', '4'), lines: [line] })).toBeNull()
  })

  it.each([
    ['withdrawn without a withdrawal', body('Withdrawn', '4')],
    ['withdrawn without a reason', body('Withdrawn', '4', { withdrawal: { ...withdrawal, reason: null } })],
    ['withdrawn with a blank reason', body('Withdrawn', '4', { withdrawal: { ...withdrawal, reason: ' ' } })],
    ['withdrawn by someone other than a maker', body('Withdrawn', '4', { withdrawal: { ...withdrawal, decidedBy: 'SENTETIK-other' } })],
    ['withdrawn by an unrecorded submitter', body('Withdrawn', '4', { withdrawal, submittedBy: null })],
    ['a blank preparer', body('Submitted', '3', { preparedBy: ' ' })],
    ['withdrawn and approved', body('Withdrawn', '4', { withdrawal, approval: { decidedBy: 'SENTETIK-manager', decidedAtUtc: at, reason: null } })],
    ['submitted with a withdrawal', body('Submitted', '3', { withdrawal })],
    ['a draft with a submitter', body('Draft', '2')],
    ['an approved request without a submitter', body('Approved', '4', { approval: { decidedBy: 'SENTETIK-manager', decidedAtUtc: at, reason: null }, submittedBy: null })],
    ['a blank submitter', body('Submitted', '3', { submittedBy: ' ' })],
    ['an unknown field', { ...body('Submitted', '3'), extra: 1 }],
  ])('refuses %s', (_name, payload) => {
    expect(decodePurchaseRequestDetail(payload)).toBeNull()
  })

  it('posts the withdrawal to its route with the key, the version and the reason as written', async () => {
    const fetch = vi.fn().mockResolvedValue(response({
      detail: body('Withdrawn', '4', { withdrawal }), isReplay: false, originalCorrelationId: 'corr-1',
    }))
    vi.stubGlobal('fetch', fetch)
    const outcome = await decidePurchaseRequest(companyId, requestId, 'withdrawal', '3', keyId, ' SENTETIK yanlış ürün ')
    expect(outcome.type).toBe('applied')
    const [url, init] = fetch.mock.calls[0] as [string, RequestInit]
    expect(url.endsWith(`/purchasing/requests/${requestId}/withdrawal`)).toBe(true)
    expect((init.headers as Record<string, string>)['Idempotency-Key']).toBe(keyId)
    expect(JSON.parse(init.body as string)).toEqual({ expectedVersion: '3', reason: ' SENTETIK yanlış ürün ' })
  })

  it('treats a 200 that does not carry Withdrawn as unknown, and a refusal as failed with its code', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response({ detail: body('Submitted', '3'), isReplay: false, originalCorrelationId: 'corr-1' })))
    expect(await decidePurchaseRequest(companyId, requestId, 'withdrawal', '3', keyId, 'x')).toEqual({ type: 'unknown', reason: 'undecodable' })
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response({ code: 'purchasing_request_not_preparer' }, 409, { 'Idempotency-Replayed': 'true' })))
    expect(await decidePurchaseRequest(companyId, requestId, 'withdrawal', '3', keyId, 'x'))
      .toEqual({ type: 'failed', status: 409, code: 'purchasing_request_not_preparer', replayed: true })
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('network')))
    expect((await decidePurchaseRequest(companyId, requestId, 'withdrawal', '3', keyId, 'x')).type).toBe('unknown')
  })

  it('keeps one unresolved withdrawal per session, actor, company and request, and releases only its own key', () => {
    const key = pendingPurchaseRequestWithdrawalKey(1, actorId, companyId, requestId)
    expect(key).toBe(`goymen.purchasing.request-withdrawal.v1:1:${actorId}:${companyId}:${requestId}`)
    expect(pendingPurchaseRequestWithdrawalKey(1, null, companyId, requestId)).toBeNull()
    expect(pendingPurchaseRequestWithdrawalKey(1, actorId, companyId, 'not-a-guid')).toBeNull()
    const snapshot = { actorId, companyId, requestId, expectedVersion: '3', idempotencyKey: keyId, reason: 'SENTETIK' }
    reservePendingPurchaseRequestWithdrawal(key, snapshot)
    expect(recallPendingPurchaseRequestWithdrawal(key)).toEqual(snapshot)
    releasePendingPurchaseRequestWithdrawal(key, '99999999-9999-4999-8999-999999999999')
    expect(recallPendingPurchaseRequestWithdrawal(key)).toEqual(snapshot)
    releasePendingPurchaseRequestWithdrawal(key, keyId)
    expect(recallPendingPurchaseRequestWithdrawal(key)).toBeNull()
    for (const reason of ['', '   ', 'x'.repeat(4001)]) {
      sessionStorage.setItem(key ?? '', JSON.stringify({ ...snapshot, reason }))
      expect(recallPendingPurchaseRequestWithdrawal(key)).toBeNull()
    }
  })
})
