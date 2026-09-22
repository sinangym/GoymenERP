import { beforeEach, describe, expect, it } from 'vitest'
import { pendingApprovalKey, recallPendingApproval, releasePendingApproval, reservePendingApproval } from './pendingApprovalStore'

beforeEach(() => sessionStorage.clear())

describe('pending purchasing approval', () => {
  it('keeps and releases the immutable actor/company/order/key/version snapshot', () => {
    const key = pendingApprovalKey(2, 'actor-a', 'company-a', 'order-a')
    const snapshot = { actorId: 'actor-a', companyId: 'company-a', orderId: 'order-a', expectedVersion: '7', idempotencyKey: 'key-a' }
    reservePendingApproval(key, snapshot)
    expect(recallPendingApproval(key)).toEqual(snapshot)
    releasePendingApproval(key, 'another-key')
    expect(recallPendingApproval(key)).toEqual(snapshot)
    releasePendingApproval(key, 'key-a')
    expect(recallPendingApproval(key)).toBeNull()
  })
})
