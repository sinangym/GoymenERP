import { describe, expect, it } from 'vitest'
import {
  boundaryEffectKey,
  captureSecuritySnapshot,
  decideBoundaryAction,
  isSnapshotCurrent,
} from './authBoundary'
import { initialShellState, shellReducer, type ShellState } from './shellState'

const ACTOR_A = 'aaaa1111-0000-0000-0000-000000000001'
const ACTOR_B = 'bbbb2222-0000-0000-0000-000000000002'
const COMPANY_A = 'cccc0000-0000-0000-0000-000000000001'
const COMPANY_B = 'dddd0000-0000-0000-0000-000000000002'

function ready(actor = ACTOR_A, company = COMPANY_A, gen = 'gen-1'): ShellState {
  return shellReducer(initialShellState, {
    type: 'SESSION_READY',
    forSessionEpoch: initialShellState.sessionEpoch,
    context: {
      userId: actor,
      scopeMode: 'single',
      companyId: company,
      capabilityGeneration: gen,
      capabilities: ['cari.parties.list'],
    },
  })
}

function switchTo(state: ShellState, company: string, gen = 'gen-2'): ShellState {
  const switching = shellReducer(state, {
    type: 'REQUEST_SWITCH',
    target: { scopeMode: 'single', companyId: company },
  })
  return shellReducer(switching, {
    type: 'REAUTH_OK',
    forGeneration: switching.requestFenceGeneration,
    context: {
      userId: state.activeContext!.userId,
      scopeMode: 'single',
      companyId: company,
      capabilityGeneration: gen,
      capabilities: ['cari.parties.list'],
    },
  })
}

describe('SAB-001 — the snapshot is taken at request start', () => {
  it('captures the bindings a security decision needs', () => {
    const s = ready()
    const snap = captureSecuritySnapshot(s, ACTOR_A, 'ns-1')
    expect(snap).toEqual({
      actorId: ACTOR_A,
      sessionEpoch: s.sessionEpoch,
      companyId: COMPANY_A,
      committedContextRevision: s.committedContextRevision,
      requestFenceGeneration: s.requestFenceGeneration,
      capabilityGeneration: 'gen-1',
      namespace: 'ns-1',
    })
  })

  it('is a value, so later state movement cannot rewrite it', () => {
    const s = ready()
    const snap = captureSecuritySnapshot(s, ACTOR_A, 'ns-1')
    const moved = switchTo(s, COMPANY_B)
    expect(snap.companyId).toBe(COMPANY_A)
    expect(moved.activeContext?.companyId).toBe(COMPANY_B)
    expect(isSnapshotCurrent(snap, moved, ACTOR_A)).toBe(false)
  })
})

describe('SAB-002 / SAB-004 — a CURRENT outcome produces the central effect', () => {
  it('401 asks for a session reset', () => {
    const s = ready()
    const snap = captureSecuritySnapshot(s, ACTOR_A, 'ns-1')
    expect(decideBoundaryAction(401, snap, s, ACTOR_A)).toEqual({ kind: 'session-reset' })
  })

  it('403 asks for a capability re-fence of that namespace', () => {
    const s = ready()
    const snap = captureSecuritySnapshot(s, ACTOR_A, 'ns-1')
    expect(decideBoundaryAction(403, snap, s, ACTOR_A)).toEqual({
      kind: 'capability-refence',
      namespace: 'ns-1',
    })
  })

  it('leaves every other status to the caller', () => {
    const s = ready()
    const snap = captureSecuritySnapshot(s, ACTOR_A, 'ns-1')
    for (const status of [200, 400, 404, 409, 500, 503]) {
      expect(decideBoundaryAction(status, snap, s, ACTOR_A)).toEqual({ kind: 'none' })
    }
  })
})

describe('SAB-008 — a late 401 from actor A cannot reset actor B', () => {
  it('discards it silently instead', () => {
    const a = ready(ACTOR_A)
    const snap = captureSecuritySnapshot(a, ACTOR_A, 'ns-a')
    const reset = shellReducer(a, { type: 'SESSION_RESET' })
    const b = shellReducer(reset, {
      type: 'SESSION_READY',
      forSessionEpoch: reset.sessionEpoch,
      context: {
        userId: ACTOR_B,
        scopeMode: 'single',
        companyId: COMPANY_A,
        capabilityGeneration: 'gen-b',
        capabilities: ['cari.parties.list'],
      },
    })

    // The late 401 from A finally lands, while B is current.
    const action = decideBoundaryAction(401, snap, b, ACTOR_B)
    expect(action).toEqual({ kind: 'stale-discard' })
    // No effect at all — not even an idempotency entry.
    expect(boundaryEffectKey(action, snap)).toBeNull()
  })
})

describe('SAB-009 — a late 403 from company A cannot revoke company B', () => {
  it('discards it silently instead', () => {
    const a = ready(ACTOR_A, COMPANY_A)
    const snap = captureSecuritySnapshot(a, ACTOR_A, 'ns-a')
    const b = switchTo(a, COMPANY_B)
    expect(b.activeContext?.companyId).toBe(COMPANY_B)

    const action = decideBoundaryAction(403, snap, b, ACTOR_A)
    expect(action).toEqual({ kind: 'stale-discard' })
    expect(boundaryEffectKey(action, snap)).toBeNull()
  })

  it('a 403 for the company still bound IS current, not stale', () => {
    const a = ready(ACTOR_A, COMPANY_A)
    const snap = captureSecuritySnapshot(a, ACTOR_A, 'ns-a')
    expect(decideBoundaryAction(403, snap, a, ACTOR_A).kind).toBe('capability-refence')
  })
})

describe('SAB-010 — the same outcome has one identity', () => {
  it('two observers of the same 401 produce the same effect key', () => {
    const s = ready()
    const one = captureSecuritySnapshot(s, ACTOR_A, 'ns-1')
    const two = captureSecuritySnapshot(s, ACTOR_A, 'ns-1')
    const action = decideBoundaryAction(401, one, s, ACTOR_A)
    expect(boundaryEffectKey(action, one)).toBe(boundaryEffectKey(action, two))
  })

  it('a 401 and a 403 on the same context are DIFFERENT effects', () => {
    const s = ready()
    const snap = captureSecuritySnapshot(s, ACTOR_A, 'ns-1')
    const k401 = boundaryEffectKey(decideBoundaryAction(401, snap, s, ACTOR_A), snap)
    const k403 = boundaryEffectKey(decideBoundaryAction(403, snap, s, ACTOR_A), snap)
    expect(k401).not.toBe(k403)
  })

  it('the same status under a NEW capability generation is a new effect', () => {
    // Otherwise a re-fence would apply once and never again, leaving a second
    // revocation silently unhandled.
    const s1 = ready(ACTOR_A, COMPANY_A, 'gen-1')
    const s2 = ready(ACTOR_A, COMPANY_A, 'gen-2')
    const a1 = captureSecuritySnapshot(s1, ACTOR_A, 'ns-1')
    const a2 = captureSecuritySnapshot(s2, ACTOR_A, 'ns-1')
    const act = { kind: 'capability-refence', namespace: 'ns-1' } as const
    expect(boundaryEffectKey(act, a1)).not.toBe(boundaryEffectKey(act, a2))
  })
})

describe('isSnapshotCurrent — what counts as identity', () => {
  it('a different actor, session or company is stale', () => {
    const s = ready()
    const snap = captureSecuritySnapshot(s, ACTOR_A, 'ns')
    expect(isSnapshotCurrent(snap, s, ACTOR_B)).toBe(false)
    expect(isSnapshotCurrent(snap, shellReducer(s, { type: 'SESSION_RESET' }), ACTOR_A)).toBe(false)
    expect(isSnapshotCurrent(snap, switchTo(s, COMPANY_B), ACTOR_A)).toBe(false)
  })

  it('a moved request fence alone does NOT make a result stale', () => {
    // The fence advances on every transition attempt, including a failed switch
    // that restored the same context. Treating that as an identity change would
    // discard genuinely current security outcomes.
    const s = ready()
    const snap = captureSecuritySnapshot(s, ACTOR_A, 'ns')
    const switching = shellReducer(s, {
      type: 'REQUEST_SWITCH',
      target: { scopeMode: 'single', companyId: COMPANY_B },
    })
    const failed = shellReducer(switching, {
      type: 'REAUTH_FAIL',
      forGeneration: switching.requestFenceGeneration,
    })
    expect(failed.requestFenceGeneration).toBeGreaterThan(snap.requestFenceGeneration)
    expect(failed.activeContext?.companyId).toBe(COMPANY_A)
    expect(isSnapshotCurrent(snap, failed, ACTOR_A)).toBe(true)
  })
})
