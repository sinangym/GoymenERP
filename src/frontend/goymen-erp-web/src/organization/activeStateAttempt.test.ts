import { describe, expect, it } from 'vitest'
import {
  attemptFingerprint,
  attemptReducer,
  initialAttemptState,
  isBusy,
  unresolvedSnapshot,
  type AttemptSnapshot,
  type AttemptState,
} from './activeStateAttempt'

const BINDING = 'ctx-a'

function snapshot(key = 'key-1', overrides: Partial<AttemptSnapshot['payload']> = {}): AttemptSnapshot {
  return {
    key,
    binding: BINDING,
    payload: { isActive: false, expectedVersion: 3, reason: 'Bakım', ...overrides },
  }
}

function submitting(key = 'key-1'): AttemptState {
  return attemptReducer(initialAttemptState, { type: 'SUBMIT', snapshot: snapshot(key) })
}

describe('key lifecycle', () => {
  it('holds no key until a submit', () => {
    expect(initialAttemptState).toEqual({ phase: 'idle' })
    expect(unresolvedSnapshot(initialAttemptState)).toBeNull()
  })

  it('freezes one snapshot on submit', () => {
    const state = submitting()
    expect(state).toMatchObject({ phase: 'submitting', resends: 0 })
    expect(unresolvedSnapshot(state)?.key).toBe('key-1')
    expect(isBusy(state)).toBe(true)
  })

  it('ignores a second submit while one is in flight — a double click mints no key', () => {
    const first = submitting('key-1')
    const second = attemptReducer(first, { type: 'SUBMIT', snapshot: snapshot('key-2') })
    expect(second).toBe(first)
    expect(unresolvedSnapshot(second)?.key).toBe('key-1')
  })
})

describe('settling', () => {
  it('completes on an applied outcome and carries the replay flag', () => {
    const state = attemptReducer(submitting(), {
      type: 'SETTLE',
      key: 'key-1',
      outcome: { type: 'applied', replayed: true, originalCorrelationId: 'corr-1' },
    })
    expect(state).toMatchObject({ phase: 'applied', replayed: true, originalCorrelationId: 'corr-1' })
  })

  it('closes the attempt on a definite refusal', () => {
    const state = attemptReducer(submitting(), {
      type: 'SETTLE',
      key: 'key-1',
      outcome: { type: 'failed', failure: { kind: 'forbidden' } },
    })
    expect(state).toMatchObject({ phase: 'failed', failure: { kind: 'forbidden' } })
    // A closed attempt holds no unresolved key.
    expect(unresolvedSnapshot(state)).toBeNull()
  })

  it('keeps the snapshot AND the key on an unknown outcome', () => {
    const state = attemptReducer(submitting(), {
      type: 'SETTLE',
      key: 'key-1',
      outcome: { type: 'unknown', unknown: { kind: 'network-error' } },
    })
    expect(state).toMatchObject({ phase: 'unknown', reason: { kind: 'network-error' }, resends: 0 })
    expect(unresolvedSnapshot(state)?.key).toBe('key-1')
  })

  it('drops a settle for a superseded key — a late response never becomes state', () => {
    const state = submitting('key-1')
    const settled = attemptReducer(state, {
      type: 'SETTLE',
      key: 'key-stale',
      outcome: { type: 'applied', replayed: false, originalCorrelationId: null },
    })
    expect(settled).toBe(state)
  })

  it('ignores a settle when nothing is in flight', () => {
    const settled = attemptReducer(initialAttemptState, {
      type: 'SETTLE',
      key: 'key-1',
      outcome: { type: 'applied', replayed: false, originalCorrelationId: null },
    })
    expect(settled).toEqual(initialAttemptState)
  })
})

describe('resend', () => {
  function unknown(): AttemptState {
    return attemptReducer(submitting(), {
      type: 'SETTLE',
      key: 'key-1',
      outcome: { type: 'unknown', unknown: { kind: 'service-unavailable', status: 503 } },
    })
  }

  it('re-sends under the SAME key so the server replays instead of writing twice', () => {
    const state = attemptReducer(unknown(), { type: 'RESEND' })
    expect(state).toMatchObject({ phase: 'submitting', resends: 1 })
    expect(unresolvedSnapshot(state)?.key).toBe('key-1')
  })

  it('keeps the FIRST uncertainty when a re-send is itself unknown', () => {
    // The original uncertainty still stands: that first request may have applied.
    const resent = attemptReducer(unknown(), { type: 'RESEND' })
    const settled = attemptReducer(resent, {
      type: 'SETTLE',
      key: 'key-1',
      outcome: { type: 'unknown', unknown: { kind: 'offline' } },
    })
    expect(settled).toMatchObject({
      phase: 'unknown',
      reason: { kind: 'service-unavailable', status: 503 },
    })
  })

  it('lets a re-send resolve to a definite answer', () => {
    const resent = attemptReducer(unknown(), { type: 'RESEND' })
    const settled = attemptReducer(resent, {
      type: 'SETTLE',
      key: 'key-1',
      outcome: { type: 'applied', replayed: true, originalCorrelationId: 'corr-1' },
    })
    expect(settled).toMatchObject({ phase: 'applied', replayed: true })
  })

  it('refuses to re-send anything that is not unresolved', () => {
    const failed = attemptReducer(submitting(), {
      type: 'SETTLE',
      key: 'key-1',
      outcome: { type: 'failed', failure: { kind: 'not-found' } },
    })
    expect(attemptReducer(failed, { type: 'RESEND' })).toBe(failed)
    expect(attemptReducer(initialAttemptState, { type: 'RESEND' })).toBe(initialAttemptState)
  })
})

describe('fingerprint', () => {
  it('treats the same content in the same context as one attempt', () => {
    expect(attemptFingerprint(BINDING, snapshot().payload)).toBe(
      attemptFingerprint(BINDING, snapshot('other-key').payload),
    )
  })

  it('ignores surrounding whitespace in the reason, matching the wire value', () => {
    expect(attemptFingerprint(BINDING, { isActive: false, expectedVersion: 3, reason: '  Bakım  ' })).toBe(
      attemptFingerprint(BINDING, { isActive: false, expectedVersion: 3, reason: 'Bakım' }),
    )
  })

  it.each([
    ['a different target state', { isActive: true }],
    ['a different expected version', { expectedVersion: 4 }],
    ['a different justification', { reason: 'Başka gerekçe' }],
  ])('makes %s a NEW attempt', (_label, overrides) => {
    expect(attemptFingerprint(BINDING, snapshot('k', overrides).payload)).not.toBe(
      attemptFingerprint(BINDING, snapshot().payload),
    )
  })

  it('makes a different company context a new attempt', () => {
    // Readiness in one context is not readiness in another.
    expect(attemptFingerprint('ctx-b', snapshot().payload)).not.toBe(
      attemptFingerprint(BINDING, snapshot().payload),
    )
  })
})

describe('reset', () => {
  it('clears the attempt', () => {
    expect(attemptReducer(submitting(), { type: 'RESET' })).toEqual(initialAttemptState)
  })
})
