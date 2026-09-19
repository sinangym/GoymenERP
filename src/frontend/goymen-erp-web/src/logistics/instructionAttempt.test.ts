import { describe, expect, it } from 'vitest'
import {
  abandon,
  applyOutcome,
  attemptBinding,
  beginSend,
  decideSubmit,
  generateAttemptKey,
  generateClientId,
  isOutcomeStillUnknown,
  isSamePayload,
  liveSnapshot,
  type AttemptState,
} from './instructionAttempt'
import type { ShellState } from '../shell/shellState'
import { initialShellState } from '../shell/shellState'

type P = { readonly quantity: string }
type R = { readonly isReplay: boolean }
type S = AttemptState<P, R>

const request = (quantity: string) => ({ path: '/logistics/loading-instructions', binding: 'b', companyId: 'c', payload: { quantity } })
const keys = () => {
  let n = 0
  return () => `key-${(n += 1)}`
}
const sent = { key: 'key-1', binding: 'b' }

describe('one key per snapshot', () => {
  it('mints a key on the first send and reuses it for the same payload while the outcome is unknown', () => {
    const newKey = keys()
    const first = decideSubmit<P, R>({ phase: 'idle' }, request('4.5'), newKey)
    expect(first).toMatchObject({ type: 'send', reusedKey: false, snapshot: { key: 'key-1' } })
    if (first.type !== 'send') throw new Error()
    const inFlight = beginSend<P, R>(first.snapshot)
    const unknown = applyOutcome(inFlight, sent, 'b', { type: 'network-error' }, { phase: 'idle' })
    expect(unknown.phase).toBe('unknown')
    expect(isOutcomeStillUnknown(unknown)).toBe(true)
    const again = decideSubmit(unknown, request('4.5'), newKey)
    expect(again).toMatchObject({ type: 'send', reusedKey: true, snapshot: { key: 'key-1' } })
  })

  it('a changed payload is blocked while the first outcome is unknown; after abandon it is a new attempt', () => {
    const newKey = keys()
    const first = decideSubmit<P, R>({ phase: 'idle' }, request('4.5'), newKey)
    if (first.type !== 'send') throw new Error()
    const unknown = applyOutcome(beginSend<P, R>(first.snapshot), sent, 'b', { type: 'offline' }, { phase: 'idle' })
    expect(decideSubmit(unknown, request('5'), newKey)).toEqual({ type: 'blocked', reason: 'unresolved-other-payload' })
    const left = abandon(unknown)
    expect(left.phase).toBe('abandoned')
    expect(liveSnapshot(left)?.key).toBe('key-1')
    expect(decideSubmit(left, request('4.5'), newKey)).toMatchObject({ type: 'send', reusedKey: true })
    expect(decideSubmit(left, request('5'), newKey)).toMatchObject({ type: 'send', reusedKey: false, snapshot: { key: 'key-2' } })
  })

  it('a definite 4xx closes the attempt, but not when it answered a retry of an unknown outcome', () => {
    const snapshot = { ...request('4.5'), key: 'key-1' }
    const refused = applyOutcome<P, R>(beginSend(snapshot), sent, 'b', { type: 'http', status: 409, problem: null }, { phase: 'idle' })
    expect(refused).toMatchObject({ phase: 'failed', failure: 'conflict' })
    expect(isOutcomeStillUnknown(refused)).toBe(false)
    const unknown: S = { phase: 'unknown', snapshot, reason: 'service-unavailable' }
    const refusedRetry = applyOutcome<P, R>(beginSend(snapshot), sent, 'b', { type: 'http', status: 409, problem: null }, unknown)
    expect(refusedRetry).toMatchObject({ phase: 'failed', unresolvedSince: 'service-unavailable' })
    expect(isOutcomeStillUnknown(refusedRetry)).toBe(true)
  })

  it('a 5xx, an unexpected 2xx and a refused body are unknown outcomes; a success completes', () => {
    const snapshot = { ...request('4.5'), key: 'key-1' }
    expect(applyOutcome<P, R>(beginSend(snapshot), sent, 'b', { type: 'http', status: 503, problem: null }, { phase: 'idle' })).toMatchObject({ phase: 'unknown', reason: 'service-unavailable' })
    expect(applyOutcome<P, R>(beginSend(snapshot), sent, 'b', { type: 'http', status: 200, problem: null }, { phase: 'idle' })).toMatchObject({ phase: 'unknown', reason: 'malformed-response' })
    expect(applyOutcome<P, R>(beginSend(snapshot), sent, 'b', { type: 'malformed' }, { phase: 'idle' })).toMatchObject({ phase: 'unknown' })
    expect(applyOutcome<P, R>(beginSend(snapshot), sent, 'b', { type: 'succeeded', data: { isReplay: true } }, { phase: 'idle' })).toMatchObject({ phase: 'completed', result: { isReplay: true } })
  })

  it('an outcome is applied only to the attempt and binding that sent it', () => {
    const snapshot = { ...request('4.5'), key: 'key-1' }
    const inFlight = beginSend<P, R>(snapshot)
    expect(applyOutcome(inFlight, sent, 'other-binding', { type: 'succeeded', data: { isReplay: false } }, { phase: 'idle' })).toBe(inFlight)
    expect(applyOutcome(inFlight, { key: 'key-9', binding: 'b' }, 'b', { type: 'succeeded', data: { isReplay: false } }, { phase: 'idle' })).toBe(inFlight)
  })
})

describe('binding, keys and ids', () => {
  it('binds only a committed single-company context', () => {
    expect(attemptBinding(initialShellState, 'scope')).toBeNull()
    const state = {
      ...initialShellState,
      sessionEpoch: 3,
      activeContext: { ...(initialShellState.activeContext ?? {}), userId: 'u', scopeMode: 'single', companyId: 'c' },
    } as unknown as ShellState
    expect(attemptBinding(state, 'scope')).toBe(JSON.stringify(['u', '3', 'c', 'scope']))
  })

  it('mints 32-hex keys and canonical lowercase UUID client ids, and compares payloads by content', () => {
    expect(generateAttemptKey()).toMatch(/^[0-9a-f]{32}$/)
    expect(generateClientId()).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/)
    expect(isSamePayload({ a: 1, b: [1, 2] }, { b: [1, 2], a: 1 })).toBe(true)
    expect(isSamePayload({ quantity: '4.5' }, { quantity: '4.50' })).toBe(false)
  })
})
