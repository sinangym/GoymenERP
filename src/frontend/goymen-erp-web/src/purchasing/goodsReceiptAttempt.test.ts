// BLG-01: the durable identity a pending goods receipt is bound to. The query namespace (contextKeyString) carries the
// capability generation and the committed context revision; the attempt identity must NOT, or a capability refresh
// for the same person in the same session loses the attempt and invites a second idempotency key.
import { describe, expect, it } from 'vitest'
import { receiptAttemptBinding, receiptAttemptScope } from './goodsReceiptAttempt'
import { contextKey, contextKeyString, type ShellState } from '../shell/shellState'

const ACTOR = '33333333-3333-4333-8333-333333333333'
const OTHER_ACTOR = '44444444-4444-4444-8444-444444444444'
const COMPANY = '22222222-2222-4222-8222-222222222222'
const OTHER_COMPANY = '99999999-9999-4999-8999-999999999999'
const ORDER = '11111111-1111-4111-8111-111111111111'
const LINE = '77777777-7777-4777-8777-777777777777'

type Shell = Pick<ShellState, 'activeContext' | 'sessionEpoch'>

function shell(over: { userId?: string; companyId?: string | null; scopeMode?: 'single' | 'group';
  capabilityGeneration?: string | null; sessionEpoch?: number } = {}): Shell {
  return {
    sessionEpoch: over.sessionEpoch ?? 4,
    activeContext: {
      userId: over.userId ?? ACTOR,
      scopeMode: over.scopeMode ?? 'single',
      companyId: over.companyId === undefined ? COMPANY : over.companyId,
      capabilityGeneration: over.capabilityGeneration === undefined ? 'gen-1' : over.capabilityGeneration,
      capabilities: ['purchasing.goods-receipts.record'],
    } as ShellState['activeContext'],
  }
}

describe('receiptAttemptBinding (BLG-01)', () => {
  it('stays the same when only the capability generation changes', () => {
    expect(receiptAttemptBinding(shell({ capabilityGeneration: 'gen-2' }))).toBe(receiptAttemptBinding(shell()))
    expect(receiptAttemptBinding(shell({ capabilityGeneration: null }))).toBe(receiptAttemptBinding(shell()))
  })

  it('is not the query namespace, which does change with the capability generation', () => {
    // A bound context (phase idle) before and after a capability refresh of the same person in the same session.
    const before = { ...shell(), phase: 'idle', committedContextRevision: 1 } as unknown as ShellState
    const after = { ...shell({ capabilityGeneration: 'gen-2' }), phase: 'idle', committedContextRevision: 2 } as unknown as ShellState
    const [first, second] = [contextKey(before), contextKey(after)]
    expect(first).not.toBeNull()
    expect(second).not.toBeNull()
    expect(contextKeyString(first!)).not.toBe(contextKeyString(second!))
    expect(receiptAttemptBinding(before)).toBe(receiptAttemptBinding(after))
  })

  it('changes with the actor, the session epoch or the company', () => {
    const base = receiptAttemptBinding(shell())
    expect(receiptAttemptBinding(shell({ userId: OTHER_ACTOR }))).not.toBe(base)
    expect(receiptAttemptBinding(shell({ sessionEpoch: 5 }))).not.toBe(base)
    expect(receiptAttemptBinding(shell({ companyId: OTHER_COMPANY }))).not.toBe(base)
  })

  it('binds nothing without a single committed company', () => {
    expect(receiptAttemptBinding({ sessionEpoch: 1, activeContext: null })).toBeNull()
    expect(receiptAttemptBinding(shell({ scopeMode: 'group', companyId: null }))).toBeNull()
  })

  it('gives each order line its own collision-free slot', () => {
    const binding = receiptAttemptBinding(shell()) as string
    expect(receiptAttemptScope(binding, ORDER, LINE)).not.toBe(receiptAttemptScope(binding, LINE, ORDER))
    expect(receiptAttemptScope(binding, ORDER, LINE)).toBe(receiptAttemptScope(binding, ORDER, LINE))
  })
})
