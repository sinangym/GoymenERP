import { describe, expect, it } from 'vitest'
import { createFenceView, isResponseStale } from './contextFence'
import {
  initialShellState,
  shellReducer,
  type ShellEvent,
  type ShellState,
} from './shellState'
import { fixtureContext } from './fixtures'

function run(state: ShellState, ...events: ShellEvent[]): ShellState {
  return events.reduce(shellReducer, state)
}

describe('isResponseStale (pure)', () => {
  it('is stale once the generation has moved', () => {
    expect(isResponseStale(3, 3)).toBe(false)
    expect(isResponseStale(3, 4)).toBe(true)
  })
})

describe('FenceView bound to the single authoritative generation (reducer)', () => {
  it('a response captured before a reducer-driven switch is stale after it', () => {
    let state = run(initialShellState, {
      type: 'SESSION_READY',
      context: fixtureContext('company-a'),
    })
    const view = createFenceView(() => state.requestFenceGeneration)

    const captured = view.capture() // request issued against company A
    state = run(state, { type: 'REQUEST_SWITCH', target: { scopeMode: 'single', companyId: 'company-b' } })

    // Even if AbortController is missed, the A response is fenced by the reducer's bump.
    expect(view.isStale(captured)).toBe(true)
  })

  it('rapid A→B→A with out-of-order responses: only the last attempt commits', () => {
    let state = run(initialShellState, { type: 'SESSION_READY', context: fixtureContext('company-a') })
    const view = createFenceView(() => state.requestFenceGeneration)

    // → B
    state = run(state, { type: 'REQUEST_SWITCH', target: { scopeMode: 'single', companyId: 'company-b' } })
    const bAttempt = state.requestFenceGeneration
    const bRequest = view.capture()

    // → A again, before B's reauth resolves
    state = run(state, { type: 'REAUTH_OK', forGeneration: bAttempt, context: fixtureContext('company-b') })
    state = run(state, { type: 'REQUEST_SWITCH', target: { scopeMode: 'single', companyId: 'company-a' } })
    const aAttempt = state.requestFenceGeneration
    const aRequest = view.capture()
    state = run(state, { type: 'REAUTH_OK', forGeneration: aAttempt, context: fixtureContext('company-a') })

    // The slow B-attempt response arrives last — it is stale and must not paint.
    expect(view.isStale(bRequest)).toBe(true)
    expect(view.isStale(aRequest)).toBe(false)
    expect(state.activeContext?.companyId).toBe('company-a')

    // A stale B REAUTH_OK arriving now is a no-op (wrong generation).
    const afterStaleReauth = run(state, {
      type: 'REAUTH_OK',
      forGeneration: bAttempt,
      context: fixtureContext('company-b'),
    })
    expect(afterStaleReauth).toBe(state)
  })
})
