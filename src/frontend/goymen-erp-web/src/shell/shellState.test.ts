import { describe, expect, it } from 'vitest'
import {
  capabilityFailClosed,
  contextKey,
  contextKeyString,
  initialShellState,
  needsBootstrap,
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

/** Drive a full successful switch and return the committed state. */
function switchTo(state: ShellState, companyId: string, cap: string | null = 'gen-x'): ShellState {
  const switching = run(state, {
    type: 'REQUEST_SWITCH',
    target: { scopeMode: 'single', companyId },
  })
  return run(switching, {
    type: 'REAUTH_OK',
    forGeneration: switching.requestFenceGeneration,
    context: { userId: 'fixture-user', scopeMode: 'single', companyId, capabilityGeneration: cap },
  })
}

describe('initial + login', () => {
  it('starts with no active company, fail-closed', () => {
    expect(initialShellState.phase).toBe('no-active-company')
    expect(capabilityFailClosed(initialShellState)).toBe(true)
    expect(contextKey(initialShellState)).toBeNull()
  })

  it('SESSION_READY commits only from no-active-company', () => {
    expect(ready.phase).toBe('idle')
    expect(ready.committedContextRevision).toBe(1)
    // A second SESSION_READY from `idle` is ignored (no unconditional commit).
    const injected = run(ready, {
      type: 'SESSION_READY',
      context: fixtureContext('evil-company', 'evil-gen'),
    })
    expect(injected).toBe(ready)
  })
})

describe('two-phase switch — attempt binding (Codex #133 P1-1)', () => {
  it('fence advances at switch start; target not committed until REAUTH_OK', () => {
    const switching = run(ready, {
      type: 'REQUEST_SWITCH',
      target: { scopeMode: 'single', companyId: 'fixture-company-b' },
    })
    expect(switching.phase).toBe('switching')
    expect(switching.requestFenceGeneration).toBe(ready.requestFenceGeneration + 1)
    expect(switching.activeContext).toBeNull()
    expect(switching.verifiedFormerContext?.companyId).toBe('fixture-company-a')
    expect(switching.committedContextRevision).toBe(ready.committedContextRevision)
    expect(contextKey(switching)).toBeNull()
  })

  it('REAUTH_OK for the WRONG generation is ignored', () => {
    const switching = run(ready, {
      type: 'REQUEST_SWITCH',
      target: { scopeMode: 'single', companyId: 'fixture-company-b' },
    })
    const stale = run(switching, {
      type: 'REAUTH_OK',
      forGeneration: switching.requestFenceGeneration - 1,
      context: fixtureContext('fixture-company-b'),
    })
    expect(stale).toBe(switching)
  })

  it('REAUTH_OK whose context does not match the pending target fails closed', () => {
    const switching = run(ready, {
      type: 'REQUEST_SWITCH',
      target: { scopeMode: 'single', companyId: 'fixture-company-b' },
    })
    // server/fixture returns a different company than requested — injection
    const injected = run(switching, {
      type: 'REAUTH_OK',
      forGeneration: switching.requestFenceGeneration,
      context: { userId: 'user-z', scopeMode: 'single', companyId: 'company-c', capabilityGeneration: 'g' },
    })
    expect(injected.phase).toBe('no-active-company')
    expect(injected.activeContext).toBeNull()
    expect(capabilityFailClosed(injected)).toBe(true)
  })

  it('REAUTH_OK with a different actor than the former context fails closed', () => {
    const switching = run(ready, {
      type: 'REQUEST_SWITCH',
      target: { scopeMode: 'single', companyId: 'fixture-company-b' },
    })
    const injected = run(switching, {
      type: 'REAUTH_OK',
      forGeneration: switching.requestFenceGeneration,
      context: { userId: 'user-z', scopeMode: 'single', companyId: 'fixture-company-b', capabilityGeneration: 'g' },
    })
    expect(injected.phase).toBe('no-active-company')
  })

  it('REAUTH_OK matching target + actor commits and bumps committed revision only', () => {
    const committed = switchTo(ready, 'fixture-company-b', 'gen-2')
    expect(committed.phase).toBe('idle')
    expect(committed.activeContext?.companyId).toBe('fixture-company-b')
    expect(committed.committedContextRevision).toBe(ready.committedContextRevision + 1)
    expect(committed.requestFenceGeneration).toBe(ready.requestFenceGeneration + 1)
  })
})

describe('two-phase switch — failure (Codex #133 P1-1)', () => {
  it('REAUTH_FAIL restores the former SNAPSHOT unchanged, never a caller-supplied context', () => {
    const switching = run(ready, {
      type: 'REQUEST_SWITCH',
      target: { scopeMode: 'single', companyId: 'fixture-company-b' },
    })
    const failed = run(switching, {
      type: 'REAUTH_FAIL',
      forGeneration: switching.requestFenceGeneration,
    })
    expect(failed.phase).toBe('idle')
    expect(failed.activeContext?.companyId).toBe('fixture-company-a') // from snapshot
    expect(failed.activeContext?.userId).toBe('fixture-user')
    expect(failed.activeContext?.capabilityGeneration).toBe('fixture-cap-gen-1') // unchanged
    expect(failed.requestFenceGeneration).toBe(ready.requestFenceGeneration + 2)
    expect(failed.committedContextRevision).toBe(ready.committedContextRevision)
  })

  it('REAUTH_FAIL for the wrong generation is ignored', () => {
    const switching = run(ready, {
      type: 'REQUEST_SWITCH',
      target: { scopeMode: 'single', companyId: 'fixture-company-b' },
    })
    const stale = run(switching, { type: 'REAUTH_FAIL', forGeneration: 999 })
    expect(stale).toBe(switching)
  })

  it('REAUTH_FAIL from the initial no-active-company path stays no-active-company', () => {
    const switching = run(initialShellState, {
      type: 'REQUEST_SWITCH',
      target: { scopeMode: 'single', companyId: 'x' },
    })
    const failed = run(switching, {
      type: 'REAUTH_FAIL',
      forGeneration: switching.requestFenceGeneration,
    })
    expect(failed.phase).toBe('no-active-company')
    expect(failed.activeContext).toBeNull()
  })
})

describe('dirty gate', () => {
  const dirty = run(ready, { type: 'DIRTY_SET', scope: 'cari-form' })

  it('a dirty scope forces confirmation without changing the active context or fence', () => {
    const confirming = run(dirty, {
      type: 'REQUEST_SWITCH',
      target: { scopeMode: 'single', companyId: 'fixture-company-b' },
    })
    expect(confirming.phase).toBe('confirming-switch')
    expect(confirming.activeContext?.companyId).toBe('fixture-company-a')
    expect(confirming.requestFenceGeneration).toBe(dirty.requestFenceGeneration)
  })

  it('STAY cancels; DISCARD clears scopes and proceeds to switching', () => {
    const stayed = run(
      dirty,
      { type: 'REQUEST_SWITCH', target: { scopeMode: 'single', companyId: 'fixture-company-b' } },
      { type: 'CONFIRM_SWITCH_STAY' },
    )
    expect(stayed.phase).toBe('idle')
    expect(stayed.activeContext?.companyId).toBe('fixture-company-a')

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

describe('session + capability', () => {
  it('SESSION_RESET drops every context-bound field and re-fences', () => {
    const afterReset = run(ready, { type: 'DIRTY_SET', scope: 'x' }, { type: 'SESSION_RESET' })
    expect(afterReset.activeContext).toBeNull()
    expect(afterReset.dirtyScopes).toHaveLength(0)
    expect(afterReset.phase).toBe('no-active-company')
    expect(afterReset.requestFenceGeneration).toBe(ready.requestFenceGeneration + 1)
  })

  it('Codex #139 P1-1: after SESSION_RESET the new session CAN bootstrap again', () => {
    expect(needsBootstrap(ready)).toBe(false)
    const afterReset = run(ready, { type: 'SESSION_RESET' })
    // the cache-namespace revision stays monotonic (never reused) ...
    expect(afterReset.committedContextRevision).toBe(ready.committedContextRevision)
    // ... but the session epoch advanced, so bootstrap is required again.
    expect(afterReset.sessionEpoch).toBe(ready.sessionEpoch + 1)
    expect(afterReset.bootstrappedSessionEpoch).toBeNull()
    expect(needsBootstrap(afterReset)).toBe(true)
  })

  it('Codex #139 P1-1: actor B actually reaches a committed context after the reset', () => {
    const actorB = run(
      ready,
      { type: 'SESSION_RESET' },
      {
        type: 'SESSION_READY',
        context: {
          userId: 'actor-b',
          scopeMode: 'single',
          companyId: 'fixture-company-b',
          capabilityGeneration: 'gen-b',
        },
      },
    )
    expect(actorB.phase).toBe('idle')
    expect(actorB.activeContext?.userId).toBe('actor-b')
    expect(actorB.activeContext?.companyId).toBe('fixture-company-b')
    expect(needsBootstrap(actorB)).toBe(false)
    // namespace never collides with actor A's entries
    expect(actorB.committedContextRevision).toBe(ready.committedContextRevision + 1)
    expect(contextKey(actorB)).not.toEqual(contextKey(ready))
  })

  it('CAPABILITY_REVOKED fails capability closed and re-fences, keeping company identity', () => {
    const revoked = run(ready, { type: 'CAPABILITY_REVOKED' })
    expect(revoked.activeContext?.companyId).toBe('fixture-company-a')
    expect(revoked.activeContext?.capabilityGeneration).toBeNull()
    expect(capabilityFailClosed(revoked)).toBe(true)
    expect(revoked.requestFenceGeneration).toBe(ready.requestFenceGeneration + 1)
  })
})

describe('contextKey (INV-NS) — Codex #133 P1-2', () => {
  it('is a structural tuple, not a delimiter-joined string', () => {
    const key = contextKey(ready)
    expect(Array.isArray(key)).toBe(true)
    expect(key).toHaveLength(5)
  })

  it('does NOT collide on delimiter-ambiguous field values', () => {
    const a = run(initialShellState, {
      type: 'SESSION_READY',
      context: { userId: 'u|single', scopeMode: 'single', companyId: 'c', capabilityGeneration: 'g' },
    })
    const b = run(initialShellState, {
      type: 'SESSION_READY',
      context: { userId: 'u', scopeMode: 'single', companyId: 'single|c', capabilityGeneration: 'g' },
    })
    const ka = contextKey(a)
    const kb = contextKey(b)
    expect(ka).not.toEqual(kb)
    expect(contextKeyString(ka!)).not.toEqual(contextKeyString(kb!))
  })

  it('differs across companies and committed revisions; stable across a failed switch', () => {
    const b = switchTo(ready, 'fixture-company-b')
    expect(contextKey(ready)).not.toEqual(contextKey(b))

    const afterFailedSwitch = run(
      ready,
      { type: 'REQUEST_SWITCH', target: { scopeMode: 'single', companyId: 'fixture-company-b' } },
      { type: 'REAUTH_FAIL', forGeneration: ready.requestFenceGeneration + 1 },
    )
    // former context restored unchanged → namespace identical to `ready` (fence not in the key)
    expect(contextKey(afterFailedSwitch)).toEqual(contextKey(ready))
  })
})
