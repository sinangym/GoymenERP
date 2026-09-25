import { afterEach, describe, expect, it } from 'vitest'
import {
  pendingPurchaseRequestCreateKey,
  pendingPurchaseRequestSubmissionKey,
  recallPendingPurchaseRequestCreate,
  recallPendingPurchaseRequestSubmission,
  releasePendingPurchaseRequestCreate,
  releasePendingPurchaseRequestSubmission,
  reservePendingPurchaseRequestCreate,
  reservePendingPurchaseRequestSubmission,
} from './pendingRequestStore'

const actorId = 'actor-a'
const companyId = '11111111-1111-4111-8111-111111111111'
const requestId = '22222222-2222-4222-8222-222222222222'
const lineId = '33333333-3333-4333-8333-333333333333'
const productId = '44444444-4444-4444-8444-444444444444'
const unitId = '55555555-5555-4555-8555-555555555555'
const keyId = '66666666-6666-4666-8666-666666666666'

afterEach(() => sessionStorage.clear())

describe('purchase request pending stores', () => {
  it('keeps one immutable multi-line create payload until the same key is released', () => {
    const key = pendingPurchaseRequestCreateKey(3, actorId, companyId)
    const snapshot = {
      actorId, companyId, idempotencyKey: keyId,
      payload: { effectiveOn: '2026-09-23', lines: [{ lineId, productId, expectedProductVersion: '7', unitId, quantity: '12.5' }] },
    }
    reservePendingPurchaseRequestCreate(key, snapshot)
    expect(recallPendingPurchaseRequestCreate(key)).toEqual(snapshot)
    releasePendingPurchaseRequestCreate(key, '77777777-7777-4777-8777-777777777777')
    expect(recallPendingPurchaseRequestCreate(key)).toEqual(snapshot)
    releasePendingPurchaseRequestCreate(key, keyId)
    expect(recallPendingPurchaseRequestCreate(key)).toBeNull()
  })

  it('scopes submission to session, actor, company and request', () => {
    const key = pendingPurchaseRequestSubmissionKey(4, actorId, companyId, requestId)
    const snapshot = { actorId, companyId, requestId, expectedVersion: '2', idempotencyKey: keyId }
    reservePendingPurchaseRequestSubmission(key, snapshot)
    expect(recallPendingPurchaseRequestSubmission(key)).toEqual(snapshot)
    releasePendingPurchaseRequestSubmission(key, keyId)
    expect(recallPendingPurchaseRequestSubmission(key)).toBeNull()
  })
})
