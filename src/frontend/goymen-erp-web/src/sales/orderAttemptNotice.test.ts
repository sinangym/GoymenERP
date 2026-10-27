import { describe, expect, it } from 'vitest'
import type { OrderAttemptSnapshot, OrderAttemptState } from './orderAttempt'
import { attemptNotice } from './orderAttemptNotice'
import { ORDER_DRAFT_TEXT } from './salesText'

type State = OrderAttemptState<unknown, unknown>

const snapshot: OrderAttemptSnapshot<unknown> = {
  key: 'k',
  path: '/p',
  binding: 'b',
  companyId: 'c',
  payload: {},
}

const ALL_STATES: readonly State[] = [
  { phase: 'submitting', snapshot },
  { phase: 'unknown', snapshot, reason: 'network-error' },
  { phase: 'abandoned', snapshot, reason: 'network-error' },
  { phase: 'failed', snapshot, failure: 'conflict', problem: null, unresolvedSince: 'offline' },
  { phase: 'failed', snapshot, failure: 'forbidden', problem: null },
  { phase: 'failed', snapshot, failure: 'validation', problem: null },
  { phase: 'completed', snapshot, result: {} },
]

describe('attemptNotice', () => {
  it('says nothing before an attempt exists', () => {
    expect(attemptNotice({ phase: 'idle' })).toBeNull()
  })

  it('never tells the person to reload or refresh — that would drop the key', () => {
    const texts = [
      ...ALL_STATES.map((state) => attemptNotice(state)?.message ?? ''),
      ...ALL_STATES.map((state) => attemptNotice(state, true)?.message ?? ''),
      ...ALL_STATES.map((state) => attemptNotice(state, false, true)?.message ?? ''),
    ]
    for (const message of texts) {
      expect(message).not.toMatch(/yenile|yeniden yükle|sayfayı|F5/i)
    }
  })

  it('offers retry and abandon exactly while the outcome is unknown', () => {
    expect(attemptNotice({ phase: 'unknown', snapshot, reason: 'offline' })?.actions).toEqual(['retry', 'abandon'])
    expect(attemptNotice(ALL_STATES[3])?.actions).toEqual(['retry', 'abandon'])
    for (const state of [ALL_STATES[0], ALL_STATES[2], ALL_STATES[4], ALL_STATES[5], ALL_STATES[6]]) {
      expect(attemptNotice(state)?.actions).toEqual([])
    }
  })

  it('does not say "not saved" after a refusal that only answered a retry', () => {
    const notice = attemptNotice(ALL_STATES[3])
    expect(notice?.tone).toBe('unknown')
    expect(notice?.message).toBe(ORDER_DRAFT_TEXT.attempt.refusedAfterUnknown)
    expect(notice?.message).not.toMatch(/kaydedilmedi\./)
  })

  it('says "not saved" for a definite refusal', () => {
    expect(attemptNotice(ALL_STATES[4])).toMatchObject({ tone: 'error', live: 'assertive' })
    expect(attemptNotice(ALL_STATES[4])?.message).toMatch(/kaydedilmedi/)
  })

  it('explains why a changed form was not sent while the earlier outcome is unknown', () => {
    expect(attemptNotice({ phase: 'unknown', snapshot, reason: 'offline' }, true)?.message).toBe(
      ORDER_DRAFT_TEXT.attempt.unknownChangedBlocked,
    )
  })

  it('UI-14: a definite 403 to a priced send names the create AND the price-write fence (the wire 403 carries no code)', () => {
    const forbidden = ALL_STATES[4]
    expect(forbidden).toMatchObject({ phase: 'failed', failure: 'forbidden' })
    expect(attemptNotice(forbidden, false, true)).toMatchObject({ tone: 'error', message: ORDER_DRAFT_TEXT.attempt.forbiddenPriced, actions: [] })
    expect(attemptNotice(forbidden)?.message).toBe(ORDER_DRAFT_TEXT.attempt.forbidden)
    // Only the forbidden class changes; a priced validation refusal keeps its own verdict.
    expect(attemptNotice(ALL_STATES[5], false, true)?.message).toBe(ORDER_DRAFT_TEXT.attempt.validation)
    // An unknown outcome is never turned into a refusal by the priced flag.
    expect(attemptNotice({ phase: 'unknown', snapshot, reason: 'offline' }, false, true)?.message).toBe(ORDER_DRAFT_TEXT.attempt.unknown)
  })
})
