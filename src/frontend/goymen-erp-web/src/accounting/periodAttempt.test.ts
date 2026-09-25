/** The period attempt's lifecycle rules. Pure reducer tests. */

import { describe, expect, it } from 'vitest'
import { initialPeriodAttempt, periodAttemptReducer, type PeriodAttempt } from './periodAttempt'
import type { PeriodCommandSnapshot } from './contract'

const SNAPSHOT: PeriodCommandSnapshot = { operation: 'approve', idempotencyKey: 'key-1', periodId: 'p-1', expectedVersion: 1 }

function run(...events: Parameters<typeof periodAttemptReducer>[1][]): PeriodAttempt {
  return events.reduce(periodAttemptReducer, initialPeriodAttempt)
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

describe('periodAttemptReducer', () => {
  it('keeps an unknown snapshot and refuses a new one while it is unresolved', () => {
    const state = run({ type: 'SUBMIT', snapshot: SNAPSHOT }, unknown503, {
      type: 'SUBMIT',
      snapshot: { ...SNAPSHOT, idempotencyKey: 'key-2' },
    })
    expect(state).toMatchObject({ phase: 'unknown', snapshot: { idempotencyKey: 'key-1' } })
    expect(periodAttemptReducer(state, { type: 'RESET' })).toBe(state)
  })

  it('re-sends the SAME snapshot and keeps it unknown when the gate turns the resend away', () => {
    const state = run({ type: 'SUBMIT', snapshot: SNAPSHOT }, unknown503, { type: 'RESEND' }, forbidden)
    expect(state).toMatchObject({
      phase: 'unknown',
      snapshot: { idempotencyKey: 'key-1' },
      unknown: { kind: 'service-unavailable' },
      lastRefusal: { kind: 'forbidden' },
      resends: 1,
    })
  })

  it('settles a first-time 403 as a refusal, and a replayed answer after a resend as the recorded one', () => {
    expect(run({ type: 'SUBMIT', snapshot: SNAPSHOT }, forbidden)).toMatchObject({ phase: 'failed' })
    const replayed = run({ type: 'SUBMIT', snapshot: SNAPSHOT }, unknown503, { type: 'RESEND' }, {
      type: 'SETTLE',
      idempotencyKey: 'key-1',
      outcome: { type: 'applied', replayed: true, originalCorrelationId: 'c-1' },
    })
    expect(replayed).toMatchObject({ phase: 'applied', replayed: true })
  })

  it('ignores a late answer for another snapshot', () => {
    const state = run({ type: 'SUBMIT', snapshot: SNAPSHOT }, { ...unknown503, idempotencyKey: 'key-other' })
    expect(state.phase).toBe('submitting')
  })
})
