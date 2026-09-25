/** The fact attempt's lifecycle rules. Pure reducer tests. */

import { describe, expect, it } from 'vitest'
import { factAttemptReducer, initialFactAttempt, type FactAttempt } from './factAttempt'
import type { FactSnapshot } from './contract'

const SNAPSHOT: FactSnapshot = {
  kind: 'input-issues',
  factId: 'f1',
  movementId: 'm1',
  expectedParentLabelVersion: null,
  idempotencyKey: 'key-1',
  expectedVersion: 1,
  parentRollId: 'r1',
  kg: '10',
  occurredAtUtc: '2026-09-18T09:00:00.000Z',
}

function run(...events: Parameters<typeof factAttemptReducer>[1][]): FactAttempt {
  return events.reduce(factAttemptReducer, initialFactAttempt)
}

const unknown503 = {
  type: 'SETTLE' as const,
  idempotencyKey: 'key-1',
  outcome: { type: 'unknown' as const, unknown: { kind: 'service-unavailable' as const, status: 503 } },
}
const forbidden = {
  type: 'SETTLE' as const,
  idempotencyKey: 'key-1',
  outcome: { type: 'failed' as const, failure: { kind: 'forbidden' as const }, replayed: false },
}

describe('factAttemptReducer', () => {
  it('keeps an unknown snapshot and refuses a new one while it is unresolved', () => {
    const state = run({ type: 'SUBMIT', snapshot: SNAPSHOT }, unknown503, {
      type: 'SUBMIT',
      snapshot: { ...SNAPSHOT, idempotencyKey: 'key-2', factId: 'f2' },
    })
    expect(state).toMatchObject({ phase: 'unknown', snapshot: { idempotencyKey: 'key-1', factId: 'f1' } })
    expect(factAttemptReducer(state, { type: 'RESET' })).toBe(state)
  })

  it('re-sends the same snapshot unchanged', () => {
    const state = run({ type: 'SUBMIT', snapshot: SNAPSHOT }, unknown503, { type: 'RESEND' })
    expect(state).toMatchObject({ phase: 'submitting', snapshot: SNAPSHOT, resends: 1 })
  })

  it('does not let a 403 on a re-send settle an earlier request (authority is checked before the record)', () => {
    const state = run({ type: 'SUBMIT', snapshot: SNAPSHOT }, unknown503, { type: 'RESEND' }, forbidden)
    expect(state).toMatchObject({
      phase: 'unknown',
      unknown: { kind: 'service-unavailable' },
      lastRefusal: { kind: 'forbidden' },
    })
  })

  it('lets a 403 on the first send settle it: nothing was recorded before', () => {
    expect(run({ type: 'SUBMIT', snapshot: SNAPSHOT }, forbidden)).toMatchObject({ phase: 'failed' })
  })

  it('settles a re-sent snapshot by its recorded answer', () => {
    const state = run({ type: 'SUBMIT', snapshot: SNAPSHOT }, unknown503, { type: 'RESEND' }, {
      type: 'SETTLE',
      idempotencyKey: 'key-1',
      outcome: { type: 'applied', replayed: true, originalCorrelationId: 'c' },
    })
    expect(state).toMatchObject({ phase: 'applied', replayed: true })
  })

  it('ignores an answer for another snapshot', () => {
    const state = run({ type: 'SUBMIT', snapshot: SNAPSHOT }, { ...unknown503, idempotencyKey: 'other' })
    expect(state.phase).toBe('submitting')
  })
})
