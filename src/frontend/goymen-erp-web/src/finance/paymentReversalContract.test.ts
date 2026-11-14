import { describe, expect, it } from 'vitest'
import {
  decodePaymentReversalCommandResult,
  decodePaymentReversalProposal,
  decodePaymentReversalProposalFor,
  decodePaymentReversalProposalPage,
} from './paymentReversalContract'

const proposalId = '11111111-1111-4111-8111-111111111111'
const paymentId = '22222222-2222-4222-8222-222222222222'
const eventId = '33333333-3333-4333-8333-333333333333'

function pending(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    id: proposalId, paymentId, occurredOn: '2026-09-24', reason: 'SENTETIK hatalı ödeme', status: 'Pending',
    proposedBy: 'maker', proposedAtUtc: '2026-09-24T09:00:00+00:00', decidedBy: null, decidedAtUtc: null,
    decisionReason: null, reversalEventId: null, version: '1', ...overrides,
  }
}

const decided = { decidedBy: 'checker', decidedAtUtc: '2026-09-24T10:00:00+00:00' }

describe('API-23 payment reversal decoders', () => {
  it('accepts the three status shapes and refuses a decision field that disagrees with the status', () => {
    expect(decodePaymentReversalProposal(pending()).ok).toBe(true)
    expect(decodePaymentReversalProposal(pending({ ...decided, status: 'Approved', reversalEventId: eventId, version: '2' })).ok).toBe(true)
    expect(decodePaymentReversalProposal(pending({ ...decided, status: 'Rejected', decisionReason: 'Belge yok', version: '2' })).ok).toBe(true)

    expect(decodePaymentReversalProposal(pending({ decidedBy: 'checker' })).ok).toBe(false)
    expect(decodePaymentReversalProposal(pending({ ...decided, status: 'Approved' })).ok).toBe(false)
    expect(decodePaymentReversalProposal(pending({ ...decided, status: 'Approved', reversalEventId: eventId, decisionReason: 'x' })).ok).toBe(false)
    expect(decodePaymentReversalProposal(pending({ ...decided, status: 'Rejected', reversalEventId: eventId, decisionReason: 'x' })).ok).toBe(false)
    expect(decodePaymentReversalProposal(pending({ status: 'Unknown' })).ok).toBe(false)
  })

  it('refuses a missing, extra or mistyped key', () => {
    const missing = pending()
    delete missing.reversalEventId
    expect(decodePaymentReversalProposal(missing).ok).toBe(false)
    expect(decodePaymentReversalProposal({ ...pending(), originalSourceId: paymentId }).ok).toBe(false)
    expect(decodePaymentReversalProposal(pending({ paymentId: 'not-a-uuid' })).ok).toBe(false)
    expect(decodePaymentReversalProposal(pending({ occurredOn: '2026-02-30' })).ok).toBe(false)
    expect(decodePaymentReversalProposal(pending({ reason: ' boşluklu ' })).ok).toBe(false)
    expect(decodePaymentReversalProposal(pending({ version: 1 })).ok).toBe(false)
  })

  it('binds a detail read to its proposal and the pending page to pending, unique items', () => {
    expect(decodePaymentReversalProposalFor(proposalId)(pending()).ok).toBe(true)
    expect(decodePaymentReversalProposalFor(eventId)(pending()).ok).toBe(false)
    expect(decodePaymentReversalProposalPage({ items: [pending()] }).ok).toBe(true)
    expect(decodePaymentReversalProposalPage({ items: [pending(), pending()] }).ok).toBe(false)
    expect(decodePaymentReversalProposalPage({
      items: [pending({ ...decided, status: 'Rejected', decisionReason: 'x' })],
    }).ok).toBe(false)
    expect(decodePaymentReversalProposalPage({ items: [], nextCursor: null }).ok).toBe(false)
  })

  it('reads a command result only with a resource on success', () => {
    const result = (outcome: string, resource: unknown) =>
      ({ outcome, resource, isReplay: false, originalCorrelationId: 'SENTETIK-1', issues: [] })
    expect(decodePaymentReversalCommandResult(result('Succeeded', pending())).ok).toBe(true)
    expect(decodePaymentReversalCommandResult(result('ValidationFailed', null)).ok).toBe(true)
    expect(decodePaymentReversalCommandResult(result('Succeeded', null)).ok).toBe(false)
    expect(decodePaymentReversalCommandResult(result('Denied', pending())).ok).toBe(false)
  })
})
