import { describe, expect, it } from 'vitest'
import { decodePaymentAllocation, decodePaymentAllocationCandidates } from './PaymentAllocationContract'
import { recoverPaymentAllocation, reservePaymentAllocation, retryPaymentAllocation, settlePaymentAllocation } from './PaymentAllocationAttempt'
import type { PaymentAllocationRequest } from './PaymentAllocationHttp'
const id = (n: number) => `${n.toString().padStart(8, '0')}-1111-4111-8111-111111111111`
const company = id(1), payment = id(2), party = id(3), account = id(4), successor = id(5), successorAccount = id(6)
const original = { partyId: party, companyAccountId: account, availableAmount: '0.00', assignmentVersion: '0', transferId: null }
const transferred = { partyId: successor, companyAccountId: successorAccount, availableAmount: '12.0001', assignmentVersion: '2', transferId: id(7) }
const candidate = () => ({ companyId: company, paymentId: payment, paymentVersion: '3', currencyId: id(8), originalPartyId: party, originalAccountId: account,
  positions: [original, transferred], openItems: [{ openItemId: id(9), version: '4', sourceKind: 'official', sourceId: id(10), dueOn: null,
    originalPartyId: party, originalAccountId: account, positions: [transferred] }], pageIndex: 0, hasMore: false, allocations: [], hasMoreAllocations: false })
const allocation = () => ({ id: id(11), companyId: company, paymentId: payment, openItemId: id(9), currencyId: id(8), amount: '10.00', status: 'Active', version: '1',
  reason: 'SENTETIK', createdBy: 'SENTETIK', createdAtUtc: '2026-11-17T08:00:00Z', releasedBy: null, releasedAtUtc: null, releaseReason: null,
  allocationPartyId: successor, allocationAccountId: successorAccount })
describe('Payment allocation bounded beneficiary contract', () => {
  it('accepts a transferred item position without inventing an original-account remainder', () => {
    const answer = decodePaymentAllocationCandidates(candidate(), company, payment, 0)
    expect(answer.ok && answer.value.positions[0].availableAmount).toBe('0.00')
    expect(answer.ok && answer.value.openItems[0].positions).toEqual([transferred])
  })
  it('rejects stale request identities, duplicate positions and invalid evidence', () => {
    expect(decodePaymentAllocationCandidates(candidate(), id(20), payment, 0).ok).toBe(false)
    expect(decodePaymentAllocationCandidates(candidate(), company, id(20), 0).ok).toBe(false)
    expect(decodePaymentAllocationCandidates(candidate(), company, payment, 1).ok).toBe(false)
    for (const positions of [[transferred], [original, transferred, transferred], [original, { ...transferred, transferId: null }],
      [original, { ...transferred, availableAmount: 12.0001 }]]) {
      expect(decodePaymentAllocationCandidates({ ...candidate(), positions }, company, payment, 0).ok).toBe(false)
    }
  })
  it('requires the actual payment wire identity and an all-or-none beneficiary pair', () => {
    expect(decodePaymentAllocation(allocation(), company).ok).toBe(true)
    expect(decodePaymentAllocation({ ...allocation(), sourceId: payment }, company).ok).toBe(false)
    const { allocationPartyId: _omit, ...partial } = allocation()
    expect(decodePaymentAllocation(partial, company).ok).toBe(false)
    expect(decodePaymentAllocationCandidates({ ...candidate(), allocations: [{ ...allocation(), paymentId: id(20) }] }, company, payment, 0).ok).toBe(false)
  })
  it('blocks a replacement command and recovers the identical request after an uncertain response', () => {
    const slot = 'SENTETIK-payment-contract'
    const body: PaymentAllocationRequest = { operation: 'create', body: { allocationId: id(11), paymentId: payment, expectedPaymentVersion: '3',
      openItemId: id(9), expectedOpenItemVersion: '4', amount: '10.00', reason: 'SENTETIK', allocationPartyId: successor, allocationAccountId: successorAccount } }
    const first = reservePaymentAllocation(slot, body)!
    expect(reservePaymentAllocation(slot, body)).toBeNull()
    expect(Object.isFrozen(first.request.body)).toBe(true)
    expect(recoverPaymentAllocation(slot)?.phase).toBe('unknown')
    const retry = retryPaymentAllocation(slot)!
    expect(retry.key).toBe(first.key); expect(retry.request).toBe(first.request)
    settlePaymentAllocation(slot, first.key)
  })
})
