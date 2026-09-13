import { describe, expect, it } from 'vitest'
import type { ShellState } from '../shell/shellState'
import {
  abandon,
  applyOutcome,
  beginSend,
  decideSubmit,
  generateOrderAttemptKey,
  isOutcomeStillUnknown,
  isSamePayload,
  orderAttemptBinding,
  type OrderAttemptState,
  type OrderCommandOutcome,
} from './orderAttempt'

type Payload = { readonly kind: string; readonly lines: readonly { quantity: string }[] }
type State = OrderAttemptState<Payload, { id: string }>

const PATH = '/sales/orders'
const COMPANY_A = 'company-a'
const COMPANY_B = 'company-b'
const PAYLOAD: Payload = { kind: 'Order', lines: [{ quantity: '1000' }] }
const CHANGED: Payload = { kind: 'Order', lines: [{ quantity: '1100' }] }

function shell(companyId: string | null, over: { scopeMode?: 'single' | 'group'; sessionEpoch?: number; userId?: string } = {}) {
  return {
    activeContext: {
      userId: over.userId ?? 'user-1',
      companyId,
      scopeMode: over.scopeMode ?? 'single',
    },
    sessionEpoch: over.sessionEpoch ?? 1,
  } as unknown as ShellState
}

const BINDING_A = orderAttemptBinding(shell(COMPANY_A), 'sales.order.create') as string
const BINDING_B = orderAttemptBinding(shell(COMPANY_B), 'sales.order.create') as string

function keys() {
  let n = 0
  const minted: string[] = []
  return {
    minted,
    next: () => {
      const key = `key-${++n}`
      minted.push(key)
      return key
    },
  }
}

function request(payload: Payload = PAYLOAD, binding = BINDING_A, companyId = COMPANY_A) {
  return { path: PATH, binding, companyId, payload }
}

/** Submit, then settle with `outcome`. Returns the new state. */
function send(state: State, outcome: OrderCommandOutcome<{ id: string }>, mint: () => string, payload = PAYLOAD): State {
  const decision = decideSubmit(state, request(payload), mint)
  if (decision.type !== 'send') throw new Error(`blocked: ${decision.reason}`)
  const inFlight = beginSend<Payload, { id: string }>(decision.snapshot)
  return applyOutcome(inFlight, decision.snapshot, BINDING_A, outcome, state)
}

const NO_FIELDS = {}

describe('orderAttemptBinding', () => {
  it('binds actor, session and company — and refuses a group view as a write target', () => {
    expect(BINDING_A).not.toBe(BINDING_B)
    expect(orderAttemptBinding(shell(COMPANY_A, { sessionEpoch: 2 }), 's')).not.toBe(
      orderAttemptBinding(shell(COMPANY_A), 's'),
    )
    expect(orderAttemptBinding(shell(COMPANY_A, { userId: 'user-2' }), 's')).not.toBe(
      orderAttemptBinding(shell(COMPANY_A), 's'),
    )
    expect(orderAttemptBinding(shell(null, { scopeMode: 'group' }), 's')).toBeNull()
    expect(orderAttemptBinding({ activeContext: null, sessionEpoch: 1 } as unknown as ShellState, 's')).toBeNull()
  })
})

describe('one key per snapshot', () => {
  it('mints nothing for a blocked double click while the first send is in flight', () => {
    const k = keys()
    const decision = decideSubmit<Payload, { id: string }>({ phase: 'idle' }, request(), k.next)
    expect(decision.type).toBe('send')
    if (decision.type !== 'send') return
    const inFlight = beginSend<Payload, { id: string }>(decision.snapshot)
    expect(decideSubmit(inFlight, request(), k.next)).toEqual({ type: 'blocked', reason: 'in-flight' })
    expect(k.minted).toEqual(['key-1'])
  })

  it('keeps key, path, company and normalized payload through an unknown outcome and its retry', () => {
    const k = keys()
    const unknown = send({ phase: 'idle' }, { type: 'network-error' }, k.next)
    expect(unknown.phase).toBe('unknown')
    // Same business snapshot typed with another key order is still the same attempt.
    const reordered = { lines: [{ quantity: '1000' }], kind: 'Order' } as Payload
    const retry = decideSubmit(unknown, request(reordered), k.next)
    expect(retry).toMatchObject({ type: 'send', reusedKey: true })
    if (retry.type !== 'send') return
    expect(retry.snapshot).toEqual({ key: 'key-1', path: PATH, binding: BINDING_A, companyId: COMPANY_A, payload: PAYLOAD })
    expect(k.minted).toEqual(['key-1'])
  })

  it.each([
    ['no response', { type: 'network-error' } as const],
    ['offline', { type: 'offline' } as const],
    ['our own abort', { type: 'aborted' } as const],
    ['a 5xx', { type: 'http', status: 503, fieldErrors: NO_FIELDS } as const],
    ['a 2xx that is not the pinned success', { type: 'http', status: 204, fieldErrors: NO_FIELDS } as const],
    ['a success body the decoder refused', { type: 'malformed' } as const],
  ])('treats %s as unknown, never as a refusal', (_, outcome) => {
    const state = send({ phase: 'idle' }, outcome, keys().next)
    expect(state.phase).toBe('unknown')
    expect(isOutcomeStillUnknown(state)).toBe(true)
  })

  it('does not open a second attempt for a changed payload while the first is unknown', () => {
    const k = keys()
    const unknown = send({ phase: 'idle' }, { type: 'network-error' }, k.next)
    expect(decideSubmit(unknown, request(CHANGED), k.next)).toEqual({
      type: 'blocked',
      reason: 'unresolved-other-payload',
    })
    expect(k.minted).toEqual(['key-1'])
  })

  it('after an explicit abandon: the same payload still reuses its key, a changed one starts anew', () => {
    const k = keys()
    const abandoned = abandon(send({ phase: 'idle' }, { type: 'network-error' }, k.next))
    expect(abandoned.phase).toBe('abandoned')
    expect(decideSubmit(abandoned, request(), k.next)).toMatchObject({ type: 'send', reusedKey: true })
    expect(k.minted).toEqual(['key-1'])
    expect(decideSubmit(abandoned, request(CHANGED), k.next)).toMatchObject({ type: 'send', reusedKey: false })
    expect(k.minted).toEqual(['key-1', 'key-2'])
  })

  it('never carries an unresolved attempt into another company', () => {
    const k = keys()
    const unknown = send({ phase: 'idle' }, { type: 'network-error' }, k.next)
    expect(decideSubmit(unknown, request(PAYLOAD, BINDING_B, COMPANY_B), k.next)).toEqual({
      type: 'blocked',
      reason: 'context-changed',
    })
    expect(k.minted).toEqual(['key-1'])
  })
})

describe('definite answers', () => {
  it('a 400 closes the attempt and keeps the server field errors; the next submit is a new key', () => {
    const k = keys()
    const failed = send(
      { phase: 'idle' },
      { type: 'http', status: 400, fieldErrors: { businessDate: 'x' } },
      k.next,
    )
    expect(failed).toMatchObject({ phase: 'failed', failure: 'validation', fieldErrors: { businessDate: 'x' } })
    expect(isOutcomeStillUnknown(failed)).toBe(false)
    expect(decideSubmit(failed, request(), k.next)).toMatchObject({ type: 'send', reusedKey: false })
    expect(k.minted).toEqual(['key-1', 'key-2'])
  })

  it('a 409 answering a retry of an unknown outcome does not claim the first send wrote nothing', () => {
    const k = keys()
    const unknown = send({ phase: 'idle' }, { type: 'network-error' }, k.next)
    const refused = send(unknown, { type: 'http', status: 409, fieldErrors: NO_FIELDS }, k.next)
    expect(refused).toMatchObject({ phase: 'failed', failure: 'conflict', unresolvedSince: 'network-error' })
    expect(isOutcomeStillUnknown(refused)).toBe(true)
    // The snapshot stays live: resubmitting the same payload is still the same key.
    expect(decideSubmit(refused, request(), k.next)).toMatchObject({ type: 'send', reusedKey: true })
    expect(k.minted).toEqual(['key-1'])
  })

  it('a 403 on a retry is a denial of replay access, not evidence of rollback', () => {
    const k = keys()
    const unknown = send({ phase: 'idle' }, { type: 'http', status: 502, fieldErrors: NO_FIELDS }, k.next)
    const refused = send(unknown, { type: 'http', status: 403, fieldErrors: NO_FIELDS }, k.next)
    expect(refused).toMatchObject({ phase: 'failed', failure: 'forbidden', unresolvedSince: 'service-unavailable' })
  })

  it('success completes once and freezes further submits', () => {
    const k = keys()
    const unknown = send({ phase: 'idle' }, { type: 'network-error' }, k.next)
    const done = send(unknown, { type: 'succeeded', data: { id: 'order-1' } }, k.next)
    expect(done).toMatchObject({ phase: 'completed', result: { id: 'order-1' } })
    expect(decideSubmit(done, request(), k.next)).toEqual({ type: 'blocked', reason: 'completed' })
    expect(k.minted).toEqual(['key-1'])
  })
})

describe('late answers', () => {
  it('writes nothing when the context changed while the send was in flight', () => {
    const decision = decideSubmit<Payload, { id: string }>({ phase: 'idle' }, request(), () => 'key-1')
    if (decision.type !== 'send') throw new Error('expected send')
    const inFlight = beginSend<Payload, { id: string }>(decision.snapshot)
    // The person is now in company B; A's success arrives.
    const after = applyOutcome(inFlight, decision.snapshot, BINDING_B, { type: 'succeeded', data: { id: 'order-1' } }, { phase: 'idle' })
    expect(after).toBe(inFlight)
  })

  it('ignores an outcome for an attempt that is no longer the one in flight', () => {
    const k = keys()
    const first = decideSubmit<Payload, { id: string }>({ phase: 'idle' }, request(), k.next)
    if (first.type !== 'send') throw new Error('expected send')
    const abandoned = abandon<Payload, { id: string }>({ phase: 'unknown', snapshot: first.snapshot, reason: 'network-error' })
    const second = decideSubmit(abandoned, request(CHANGED), k.next)
    if (second.type !== 'send') throw new Error('expected send')
    const inFlight = beginSend<Payload, { id: string }>(second.snapshot)
    // The first send's answer turns up late.
    expect(applyOutcome(inFlight, first.snapshot, BINDING_A, { type: 'succeeded', data: { id: 'late' } }, abandoned)).toBe(inFlight)
  })

  it('abandon cannot drop an attempt that is still in flight', () => {
    const decision = decideSubmit<Payload, { id: string }>({ phase: 'idle' }, request(), () => 'key-1')
    if (decision.type !== 'send') throw new Error('expected send')
    const inFlight = beginSend<Payload, { id: string }>(decision.snapshot)
    expect(abandon(inFlight)).toBe(inFlight)
  })
})

describe('helpers', () => {
  it('generates 32 hex characters from the given random source', () => {
    const key = generateOrderAttemptKey({
      getRandomValues: <T extends ArrayBufferView | null>(array: T) => {
        ;(array as unknown as Uint8Array).fill(171)
        return array
      },
    })
    expect(key).toBe('ab'.repeat(16))
  })

  it('compares payloads by value, ignoring key order and undefined members', () => {
    expect(isSamePayload({ a: 1, b: [1, { c: 2 }] }, { b: [1, { c: 2 }], a: 1, d: undefined })).toBe(true)
    expect(isSamePayload({ lines: [{ quantity: '1000' }] }, { lines: [{ quantity: '1000.0' }] })).toBe(false)
  })
})
