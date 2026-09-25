import { describe, expect, it } from 'vitest'
import {
  decodeReversalProposal,
  decodeReversalProposalCommandResult,
  decodeReversalProposalPage,
} from './contract'

const pending = {
  id: '11111111-1111-4111-8111-111111111111',
  originalSourceId: '22222222-2222-4222-8222-222222222222',
  occurredOn: '2026-09-22',
  reason: 'Hatalı tahsilat',
  status: 'Pending',
  proposedBy: 'maker',
  proposedAtUtc: '2026-09-21T12:00:00+00:00',
  decidedBy: null,
  decidedAtUtc: null,
  decisionReason: null,
  reversalSourceId: null,
  version: '1',
} as const

describe('FIN-T06 reversal proposal contract', () => {
  it('decodes the exact pending shape and its page', () => {
    expect(decodeReversalProposal(pending)).toEqual({ ok: true, value: pending })
    expect(decodeReversalProposalPage({ items: [pending] })).toEqual({ ok: true, value: { items: [pending] } })
  })

  it('refuses an impossible status shape and unknown members', () => {
    expect(decodeReversalProposal({ ...pending, status: 'Approved' })).toEqual({ ok: false })
    expect(decodeReversalProposal({ ...pending, hidden: true })).toEqual({ ok: false })
    expect(decodeReversalProposal({ ...pending, occurredOn: '09/22/2026' })).toEqual({ ok: false })
  })

  it('decodes a successful proposal command only with a proposal resource', () => {
    expect(decodeReversalProposalCommandResult({
      outcome: 'Succeeded', resource: pending, isReplay: false,
      originalCorrelationId: 'corr-1', issues: [],
    })).toEqual({
      ok: true,
      value: {
        outcome: 'Succeeded', resource: pending, isReplay: false,
        originalCorrelationId: 'corr-1', issues: [],
      },
    })
    expect(decodeReversalProposalCommandResult({
      outcome: 'Succeeded', resource: null, isReplay: false,
      originalCorrelationId: 'corr-1', issues: [],
    })).toEqual({ ok: false })
  })

  it('requires the maker-checker fields on approved and rejected resources', () => {
    const approved = {
      ...pending,
      status: 'Approved',
      decidedBy: 'checker',
      decidedAtUtc: '2026-09-21T13:00:00+00:00',
      reversalSourceId: '33333333-3333-4333-8333-333333333333',
      version: '2',
    } as const
    const rejected = {
      ...pending,
      status: 'Rejected',
      decidedBy: 'checker',
      decidedAtUtc: '2026-09-21T13:00:00+00:00',
      decisionReason: 'Belge doğrulanamadı',
      version: '2',
    } as const
    expect(decodeReversalProposal(approved).ok).toBe(true)
    expect(decodeReversalProposal(rejected).ok).toBe(true)
  })
})
