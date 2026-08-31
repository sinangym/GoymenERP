import { describe, expect, it } from 'vitest'
import {
  capabilityFailClosed,
  contextKey,
  initialShellState,
  shellReducer,
  type ShellEvent,
  type ShellState,
} from './shellState'
import { fixtureContext } from './fixtures'

function run(state: ShellState, ...events: ShellEvent[]): ShellState {
  return events.reduce(shellReducer, state)
}

const ready = run(initialShellState, {
  type: 'SESSION_READY',
  context: fixtureContext('fixture-company-a'),
})

describe('shell state machine — initial', () => {
  it('starts with no active company, fail-closed', () => {
    expect(initialShellState.phase).toBe('no-active-company')
    expect(initialShellState.activeContext).toBeNull()
    expect(capabilityFailClosed(initialShellState)).toBe(true)
    expect(contextKey(initialShellState)).toBeNull()
  })

  it('SESSION_READY commits the initial context and revision', () => {
    expect(ready.phase).toBe('idle')
    expect(ready.activeContext?.companyId).toBe('fixture-company-a')
    expect(ready.committedContextRevision).toBe(1)
    expect(contextKey(ready)).not.toBeNull()
    expect(capabilityFailClosed(ready)).toBe(false)
  })
})

describe('shell state machine — two-phase switch', () => {
  it('REQUEST_SWITCH advances the fence at the START, before reauth', () => {
    const switching = run(ready, {
      type: 'REQUEST_SWITCH',
      target: { scopeMode: 'single', companyId: 'fixture-company-b' },
    })
    expect(switching.phase).toBe('switching')
    // fence advanced immediately
    expect(switching.requestFenceGeneration).toBe(ready.requestFenceGeneration + 1)
    // active context dropped to neutral; target NOT committed
    expect(switching.activeContext).toBeNull()
    expect(switching.pendingTarget?.companyId).toBe('fixture-company-b')
    // committed revision unchanged until success
    expect(switching.committedContextRevision).toBe(ready.committedContextRevision)
    // company-scoped reads blocked while switching
    expect(contextKey(switching)).toBeNull()
    expect(capabilityFailClosed(switching)).toBe(true)
  })

  it('REAUTH_OK commits target and bumps committed revision only', () => {
    const committed = run(
      ready,
      { type: 'REQUEST_SWITCH', target: { scopeMode: 'single', companyId: 'fixture-company-b' } },
      { type: 'REAUTH_OK', context: fixtureContext('fixture-company-b', 'fixture-cap-gen-2') },
    )
    expect(committed.phase).toBe('idle')
    expect(committed.activeContext?.companyId).toBe('fixture-company-b')
    expect(committed.committedContextRevision).toBe(ready.committedContextRevision + 1)
    // fence advanced exactly once (at switch start), not again on commit
    expect(committed.requestFenceGeneration).toBe(ready.requestFenceGeneration + 1)
  })

  it('REAUTH_FAIL never exposes the target; restores former context and re-fences', () => {
    const failed = run(
      ready,
      { type: 'REQUEST_SWITCH', target: { scopeMode: 'single', companyId: 'fixture-company-b' } },
      { type: 'REAUTH_FAIL', restored: fixtureContext('fixture-company-a') },
    )
    expect(failed.phase).toBe('idle')
    expect(failed.activeContext?.companyId).toBe('fixture-company-a')
    expect(failed.pendingTarget).toBeNull()
    // fence advanced twice: switch start + restore
    expect(failed.requestFenceGeneration).toBe(ready.requestFenceGeneration + 2)
    expect(failed.committedContextRevision).toBe(ready.committedContextRevision)
  })

  it('REAUTH_FAIL with no restorable context goes to explicit no-active-company', () => {
    const failed = run(
      ready,
      { type: 'REQUEST_SWITCH', target: { scopeMode: 'single', companyId: 'fixture-company-b' } },
      { type: 'REAUTH_FAIL', restored: null },
    )
    expect(failed.phase).toBe('no-active-company')
    expect(failed.activeContext).toBeNull()
    expect(capabilityFailClosed(failed)).toBe(true)
  })
})

describe('shell state machine — dirty gate', () => {
  const dirty = run(ready, { type: 'DIRTY_SET', scope: 'cari-form' })

  it('a dirty scope forces confirmation and does not change the active context', () => {
    const confirming = run(dirty, {
      type: 'REQUEST_SWITCH',
      target: { scopeMode: 'single', companyId: 'fixture-company-b' },
    })
    expect(confirming.phase).toBe('confirming-switch')
    expect(confirming.activeContext?.companyId).toBe('fixture-company-a')
    expect(confirming.requestFenceGeneration).toBe(dirty.requestFenceGeneration)
  })

  it('CONFIRM_SWITCH_STAY cancels and keeps the context', () => {
    const stayed = run(
      dirty,
      { type: 'REQUEST_SWITCH', target: { scopeMode: 'single', companyId: 'fixture-company-b' } },
      { type: 'CONFIRM_SWITCH_STAY' },
    )
    expect(stayed.phase).toBe('idle')
    expect(stayed.pendingTarget).toBeNull()
    expect(stayed.activeContext?.companyId).toBe('fixture-company-a')
  })

  it('CONFIRM_SWITCH_DISCARD clears dirty scopes and proceeds to switching', () => {
    const proceeding = run(
      dirty,
      { type: 'REQUEST_SWITCH', target: { scopeMode: 'single', companyId: 'fixture-company-b' } },
      { type: 'CONFIRM_SWITCH_DISCARD' },
    )
    expect(proceeding.phase).toBe('switching')
    expect(proceeding.dirtyScopes).toHaveLength(0)
    expect(proceeding.requestFenceGeneration).toBe(dirty.requestFenceGeneration + 1)
  })
})

describe('shell state machine — session and capability', () => {
  it('SESSION_RESET drops every context-bound field and re-fences', () => {
    const afterReset = run(ready, { type: 'DIRTY_SET', scope: 'x' }, { type: 'SESSION_RESET' })
    expect(afterReset.activeContext).toBeNull()
    expect(afterReset.dirtyScopes).toHaveLength(0)
    expect(afterReset.phase).toBe('no-active-company')
    expect(afterReset.requestFenceGeneration).toBe(ready.requestFenceGeneration + 1)
  })

  it('CAPABILITY_REVOKED fails capability closed and re-fences without losing company identity', () => {
    const revoked = run(ready, { type: 'CAPABILITY_REVOKED' })
    expect(revoked.activeContext?.companyId).toBe('fixture-company-a')
    expect(revoked.activeContext?.capabilityGeneration).toBeNull()
    expect(capabilityFailClosed(revoked)).toBe(true)
    expect(revoked.requestFenceGeneration).toBe(ready.requestFenceGeneration + 1)
  })
})

describe('contextKey (INV-NS)', () => {
  it('differs across companies and across committed revisions', () => {
    const a = run(ready)
    const b = run(
      ready,
      { type: 'REQUEST_SWITCH', target: { scopeMode: 'single', companyId: 'fixture-company-b' } },
      { type: 'REAUTH_OK', context: fixtureContext('fixture-company-b') },
    )
    expect(contextKey(a)).not.toEqual(contextKey(b))
  })

  it('does not embed the request fence generation', () => {
    // A failed switch bumps the fence twice but leaves company + committed revision;
    // the cache namespace must be unchanged.
    const before = contextKey(ready)
    const afterFailedSwitch = run(
      ready,
      { type: 'REQUEST_SWITCH', target: { scopeMode: 'single', companyId: 'fixture-company-b' } },
      { type: 'REAUTH_FAIL', restored: fixtureContext('fixture-company-a') },
    )
    expect(contextKey(afterFailedSwitch)).toEqual(before)
  })
})
