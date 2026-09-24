import { describe, expect, it } from 'vitest'
import { allocationAttemptIdentity, classifyAllocationReply, type AllocationCommandRequest } from './allocationCommand'

const COMPANY = '11111111-1111-4111-8111-111111111111'
const ALLOCATION = '33333333-3333-4333-8333-333333333333'
const SOURCE = '44444444-4444-4444-8444-444444444444'
const OPEN_ITEM = '55555555-5555-4555-8555-555555555555'

const request: AllocationCommandRequest = {
  operation: 'allocation-create',
  body: {
    allocationId: ALLOCATION, sourceId: SOURCE, expectedSourceVersion: '1',
    openItemId: OPEN_ITEM, expectedOpenItemVersion: '1', amount: '250.10', reason: 'Fatura kapama',
  },
}

function resource(overrides: Record<string, unknown> = {}) {
  return {
    id: ALLOCATION, companyId: COMPANY, sourceId: SOURCE, openItemId: OPEN_ITEM,
    currencyId: '66666666-6666-4666-8666-666666666666', amount: '250.10', status: 'Active', version: '1',
    reason: 'Fatura kapama', createdBy: 'actor-a', createdAtUtc: '2026-09-24T08:00:00Z',
    releasedBy: null, releasedAtUtc: null, releaseReason: null, ...overrides,
  }
}

function success(value = resource()) {
  return { kind: 'body' as const, body: {
    outcome: 'Succeeded', resource: value, isReplay: false, originalCorrelationId: '', issues: [],
  } }
}

describe('Finance allocation command boundary', () => {
  it('settles create only for its own active allocation and exact source/open item', () => {
    expect(classifyAllocationReply(success(), { companyId: COMPANY }, request).type).toBe('succeeded')
    expect(classifyAllocationReply(success(resource({ sourceId: crypto.randomUUID() })), { companyId: COMPANY }, request))
      .toEqual({ type: 'unknown', reason: 'malformed' })
    expect(classifyAllocationReply(success(resource({ openItemId: crypto.randomUUID() })), { companyId: COMPANY }, request))
      .toEqual({ type: 'unknown', reason: 'malformed' })
  })

  it('settles release only after the same allocation is released', () => {
    const release: AllocationCommandRequest = {
      operation: 'allocation-release',
      body: { allocationId: ALLOCATION, expectedAllocationVersion: '1', expectedOpenItemVersion: '2', reason: 'Yanlış dağıtım' },
    }
    const released = resource({
      status: 'Released', version: '2', releasedBy: 'actor-b', releasedAtUtc: '2026-09-24T09:00:00Z',
      releaseReason: 'Yanlış dağıtım',
    })
    expect(classifyAllocationReply(success(released), { companyId: COMPANY }, release).type).toBe('succeeded')
    expect(classifyAllocationReply(success(), { companyId: COMPANY }, release))
      .toEqual({ type: 'unknown', reason: 'malformed' })
  })

  it('keeps absent, unavailable, conflicting, foreign and malformed answers unresolved', () => {
    expect(classifyAllocationReply({ kind: 'no-reply' }, { companyId: COMPANY }, request))
      .toEqual({ type: 'unknown', reason: 'no-reply' })
    for (const outcome of ['Unavailable', 'IdempotencyConflict'] as const) {
      const reply = { kind: 'body' as const, body: {
        outcome, resource: null, isReplay: false, originalCorrelationId: '', issues: [],
      } }
      expect(classifyAllocationReply(reply, { companyId: COMPANY }, request).type).toBe('unknown')
    }
    expect(classifyAllocationReply(success(resource({ companyId: '22222222-2222-4222-8222-222222222222' })),
      { companyId: COMPANY }, request)).toEqual({ type: 'unknown', reason: 'malformed' })
  })

  it('binds attempt identity to actor, company, session and normalized request structure', () => {
    const binding = { sessionEpoch: 7, actorId: 'actor-a', companyId: COMPANY }
    expect(allocationAttemptIdentity(binding, request)).toBe(allocationAttemptIdentity(binding, {
      body: { ...request.body }, operation: 'allocation-create',
    }))
    expect(allocationAttemptIdentity(binding, request)).not.toBe(allocationAttemptIdentity(
      { ...binding, actorId: 'actor-b' }, request))
  })
})
