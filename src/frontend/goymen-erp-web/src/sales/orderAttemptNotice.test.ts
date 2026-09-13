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
  { phase: 'failed', snapshot, failure: 'conflict', fieldErrors: {}, unresolvedSince: 'offline' },
  { phase: 'failed', snapshot, failure: 'forbidden', fieldErrors: {} },
  { phase: 'failed', snapshot, failure: 'validation', fieldErrors: {} },
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
})
