import { describe, expect, it } from 'vitest'
import {
  boundaryEffectKey,
  captureSecuritySnapshot,
  decideBoundaryAction,
  isSnapshotCurrent,
} from './authBoundary'
import {
  capabilityFailClosed,
  initialShellState,
  needsBootstrap,
  shellReducer,
  type ShellState,
} from './shellState'

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

describe('ChatGPT #169 P1-1 — a reset must not resurrect a rejected session', () => {
  it('SESSION_RESET after SESSION_INVALIDATED keeps the terminal flag', () => {
    const invalid = shellReducer(ready(), { type: 'SESSION_INVALIDATED' })
    expect(invalid.sessionInvalid).toBe(true)
    // The switch-401 path used to dispatch both, and the second rebuilt
    // initialShellState — undoing the terminal state one dispatch later.
    const afterReset = shellReducer(invalid, { type: 'SESSION_RESET' })
    expect(afterReset.sessionInvalid).toBe(true)
    expect(needsBootstrap(afterReset)).toBe(false)
  })
})

describe('ChatGPT #169 P1-2 / P1-3 — the decision and the ledger live in the reducer', () => {
  function snapshotOf(s: ShellState, namespace = 'ns-1') {
    return {
      actorId: s.activeContext?.userId ?? null,
      sessionEpoch: s.sessionEpoch,
      companyId: s.activeContext?.companyId ?? null,
      committedContextRevision: s.committedContextRevision,
      capabilityGeneration: s.activeContext?.capabilityGeneration ?? null,
      namespace,
    }
  }

  it('a 403 captured under a SUPERSEDED capability generation is ignored', () => {
    // The scenario: a read is authorized under generation g1, the capability is
    // re-granted as g2 in the meantime (which is what a successful mid-switch
    // reauth does), and only then does g1's 403 arrive. Nothing else has moved —
    // same actor, same session epoch, same company, same committed revision — so
    // every other currentness term still matches and only the generation can tell
    // the two apart.
    //
    // Without that term the stale outcome revokes an authority the server never
    // refused. This test exists because removing the term left all 189 other tests
    // green: an unproven clause in a security reducer is not something to ship.
    const s = ready()
    const underG1 = snapshotOf(s)
    const regranted: ShellState = {
      ...s,
      activeContext: s.activeContext
        ? { ...s.activeContext, capabilityGeneration: 'gen-2' }
        : null,
    }
    expect(underG1.capabilityGeneration).not.toBe('gen-2')

    const out = shellReducer(regranted, {
      type: 'BUSINESS_AUTH_OUTCOME',
      status: 403,
      authenticatedActorId: ACTOR_A,
      snapshot: underG1,
    })

    expect(out).toBe(regranted)
    expect(out.activeContext?.capabilityGeneration).toBe('gen-2')
    expect(out.requestFenceGeneration).toBe(regranted.requestFenceGeneration)
    expect(out.lastSecurityEffect).toBeNull()
  })

  it('a current 401 is terminal and records the effect', () => {
    const s = ready()
    const out = shellReducer(s, {
      type: 'BUSINESS_AUTH_OUTCOME',
      status: 401,
      authenticatedActorId: ACTOR_A,
      snapshot: snapshotOf(s),
    })
    expect(out.sessionInvalid).toBe(true)
    expect(out.activeContext).toBeNull()
    expect(out.sessionEpoch).toBe(s.sessionEpoch + 1)
    expect(out.lastSecurityEffect?.kind).toBe('session-reset')
  })

  it('P1-3: the SAME outcome applied twice moves nothing the second time', () => {
    // A 403 is the case that actually exercises the LEDGER. After a 401 the
    // context is gone, so the second report is already rejected as stale and the
    // ledger never gets a turn — testing it with a 401 proves nothing. After a
    // 403 the actor, company and revision are unchanged, so the same snapshot is
    // still current and only the ledger can stop the second application.
    const s = ready()
    const snap = snapshotOf(s)
    const once = shellReducer(s, { type: 'BUSINESS_AUTH_OUTCOME', status: 403, authenticatedActorId: ACTOR_A, snapshot: snap })
    expect(once.requestFenceGeneration).toBe(s.requestFenceGeneration + 1)

    // A SECOND reader reporting the same rejection. Per-hook Sets could not see
    // each other; the reducer ledger can.
    const twice = shellReducer(once, { type: 'BUSINESS_AUTH_OUTCOME', status: 403, authenticatedActorId: ACTOR_A, snapshot: snap })
    expect(twice).toBe(once)
    expect(twice.requestFenceGeneration).toBe(s.requestFenceGeneration + 1)
  })

  it('P1-2: a late 401 from a departed actor cannot invalidate the current one', () => {
    const a = ready(ACTOR_A)
    const snapA = snapshotOf(a)
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
    // The A consumer is long unmounted; only its snapshot survives.
    const out = shellReducer(b, { type: 'BUSINESS_AUTH_OUTCOME', status: 401, authenticatedActorId: ACTOR_B, snapshot: snapA })
    expect(out).toBe(b)
    expect(out.sessionInvalid).toBe(false)
    expect(out.activeContext?.userId).toBe(ACTOR_B)
  })

  it('P1-2: a late 403 from a company we left cannot revoke the current one', () => {
    const a = ready(ACTOR_A, COMPANY_A)
    const snapA = snapshotOf(a)
    const b = switchTo(a, COMPANY_B)
    const out = shellReducer(b, { type: 'BUSINESS_AUTH_OUTCOME', status: 403, authenticatedActorId: ACTOR_A, snapshot: snapA })
    expect(out).toBe(b)
    expect(out.activeContext?.capabilityGeneration).toBe('gen-2')
    expect(out.activeContext?.capabilities).toEqual(['cari.parties.list'])
  })

  it('a current 403 re-fences the capability without ending the session', () => {
    const s = ready()
    const out = shellReducer(s, {
      type: 'BUSINESS_AUTH_OUTCOME',
      status: 403,
      authenticatedActorId: ACTOR_A,
      snapshot: snapshotOf(s),
    })
    expect(out.sessionInvalid).toBe(false)
    expect(out.activeContext?.capabilityGeneration).toBeNull()
    expect(out.activeContext?.capabilities).toEqual([])
    expect(out.activeContext?.companyId).toBe(COMPANY_A)
    expect(out.requestFenceGeneration).toBe(s.requestFenceGeneration + 1)
    expect(capabilityFailClosed(out)).toBe(true)
  })
})

describe('ChatGPT #169 P1-4 — identity moved to B, context still says A', () => {
  function snapOf(s: ShellState, ns = 'ns') {
    return {
      actorId: s.activeContext?.userId ?? null,
      sessionEpoch: s.sessionEpoch,
      companyId: s.activeContext?.companyId ?? null,
      committedContextRevision: s.committedContextRevision,
      capabilityGeneration: s.activeContext?.capabilityGeneration ?? null,
      namespace: ns,
    }
  }

  it("a late A 401 in the pre-reset window cannot terminate actor B", () => {
    const a = ready(ACTOR_A)
    const snapA = snapOf(a)
    // Identity resolved as B while the committed context still says A. This is
    // the window, and it no longer depends on any effect having run: the actor is
    // read at REPORT time and carried on the event.
    const window = a
    expect(window.activeContext?.userId).toBe(ACTOR_A)

    const out = shellReducer(window, {
      type: 'BUSINESS_AUTH_OUTCOME',
      status: 401,
      authenticatedActorId: ACTOR_B,
      snapshot: snapA,
    })
    expect(out).toBe(window)
    expect(out.sessionInvalid).toBe(false)
  })

  it('a late A 403 in the same window cannot revoke anything either', () => {
    const a = ready(ACTOR_A)
    const snapA = snapOf(a)
    const window = a
    const out = shellReducer(window, {
      type: 'BUSINESS_AUTH_OUTCOME',
      status: 403,
      // Identity already resolved as B when this outcome was reported.
      authenticatedActorId: ACTOR_B,
      snapshot: snapA,
    })
    expect(out).toBe(window)
    expect(out.activeContext?.capabilityGeneration).toBe('gen-1')
  })

  it('the SAME actor still produces the effect — the guard is not a blanket refusal', () => {
    const a = ready(ACTOR_A)
    const observed = a
    const out = shellReducer(observed, {
      type: 'BUSINESS_AUTH_OUTCOME',
      status: 401,
      authenticatedActorId: ACTOR_A,
      snapshot: snapOf(observed),
    })
    expect(out.sessionInvalid).toBe(true)
  })
})

describe('ChatGPT #169 P1-5 — a rejection that lands mid-switch', () => {
  function snapOf(s: ShellState, ns = 'ns') {
    return {
      actorId: s.activeContext?.userId ?? null,
      sessionEpoch: s.sessionEpoch,
      companyId: s.activeContext?.companyId ?? null,
      committedContextRevision: s.committedContextRevision,
      capabilityGeneration: s.activeContext?.capabilityGeneration ?? null,
      namespace: ns,
    }
  }

  it('a 403 for company A during a switch is NOT ignored', () => {
    const a = ready(ACTOR_A, COMPANY_A)
    const snapA = snapOf(a)
    const switching = shellReducer(a, {
      type: 'REQUEST_SWITCH',
      target: { scopeMode: 'single', companyId: COMPANY_B },
    })
    // activeContext is null here — judging against it alone made this stale.
    expect(switching.activeContext).toBeNull()
    expect(switching.verifiedFormerContext?.companyId).toBe(COMPANY_A)

    const out = shellReducer(switching, {
      type: 'BUSINESS_AUTH_OUTCOME',
      status: 403,
      authenticatedActorId: ACTOR_A,
      snapshot: snapA,
    })
    expect(out).not.toBe(switching)
    expect(out.lastSecurityEffect?.kind).toBe('capability-refence')
  })

  it('...and a FAILED switch cannot restore the rejected authority', () => {
    const a = ready(ACTOR_A, COMPANY_A)
    const snapA = snapOf(a)
    const switching = shellReducer(a, {
      type: 'REQUEST_SWITCH',
      target: { scopeMode: 'single', companyId: COMPANY_B },
    })
    const rejected = shellReducer(switching, {
      type: 'BUSINESS_AUTH_OUTCOME',
      status: 403,
      authenticatedActorId: ACTOR_A,
      snapshot: snapA,
    })
    // The switch to B now fails, and REAUTH_FAIL restores the former context.
    const restored = shellReducer(rejected, {
      type: 'REAUTH_FAIL',
      forGeneration: rejected.requestFenceGeneration,
    })
    expect(restored.phase).toBe('idle')
    expect(restored.activeContext?.companyId).toBe(COMPANY_A)
    // It must come back FAIL-CLOSED, not carrying the capability the server refused.
    expect(restored.activeContext?.capabilityGeneration).toBeNull()
    expect(restored.activeContext?.capabilities).toEqual([])
    expect(capabilityFailClosed(restored)).toBe(true)
  })

  it('a 401 for company A during a switch is HELD, not applied immediately', () => {
    // This case used to assert immediate termination. Codex's #169 P1-1 produced
    // the counter-example and it is decisive: if the switch then succeeds, the
    // server has just authenticated the session one call later, so the former
    // 401 cannot have been about the session — and terminating on it killed a
    // live session. Issue #165 calls for the success/failure target distinction.
    const a = ready(ACTOR_A, COMPANY_A)
    const snapA = snapOf(a)
    const switching = shellReducer(a, {
      type: 'REQUEST_SWITCH',
      target: { scopeMode: 'single', companyId: COMPANY_B },
    })
    const held = shellReducer(switching, {
      type: 'BUSINESS_AUTH_OUTCOME',
      status: 401,
      authenticatedActorId: ACTOR_A,
      snapshot: snapA,
    })

    // Held: nothing is torn down while the target is still being verified.
    expect(held.sessionInvalid).toBe(false)
    expect(held.pendingFormerSessionFailure).not.toBeNull()
    expect(held.phase).toBe('switching')
    expect(held.verifiedFormerContext).not.toBeNull()
    // The ledger still records it, so a second observer cannot re-apply it.
    expect(held.lastSecurityEffect).not.toBeNull()
  })

  it('a successful switch DISCARDS the held former 401', () => {
    const a = ready(ACTOR_A, COMPANY_A)
    const snapA = snapOf(a)
    const switching = shellReducer(a, {
      type: 'REQUEST_SWITCH',
      target: { scopeMode: 'single', companyId: COMPANY_B },
    })
    const held = shellReducer(switching, {
      type: 'BUSINESS_AUTH_OUTCOME',
      status: 401,
      authenticatedActorId: ACTOR_A,
      snapshot: snapA,
    })
    const out = shellReducer(held, {
      type: 'REAUTH_OK',
      forGeneration: held.requestFenceGeneration,
      context: {
        userId: ACTOR_A,
        companyId: COMPANY_B,
        scopeMode: 'single',
        capabilityGeneration: 'gen-b',
        capabilities: [],
      },
    })

    // The server authenticated the target, so the session is demonstrably alive.
    expect(out.sessionInvalid).toBe(false)
    expect(out.pendingFormerSessionFailure).toBeNull()
    expect(out.phase).toBe('idle')
    expect(out.activeContext?.companyId).toBe(COMPANY_B)
  })

  it('a failed switch APPLIES the held former 401 instead of restoring', () => {
    const a = ready(ACTOR_A, COMPANY_A)
    const snapA = snapOf(a)
    const switching = shellReducer(a, {
      type: 'REQUEST_SWITCH',
      target: { scopeMode: 'single', companyId: COMPANY_B },
    })
    const held = shellReducer(switching, {
      type: 'BUSINESS_AUTH_OUTCOME',
      status: 401,
      authenticatedActorId: ACTOR_A,
      snapshot: snapA,
    })
    const out = shellReducer(held, {
      type: 'REAUTH_FAIL',
      forGeneration: held.requestFenceGeneration,
    })

    // Nothing contradicted the 401, so it stands — and the former context is NOT
    // restored, because restoring it would carry an authority nothing confirmed.
    expect(out.sessionInvalid).toBe(true)
    expect(out.activeContext).toBeNull()
    expect(out.verifiedFormerContext).toBeNull()
    expect(out.pendingFormerSessionFailure).toBeNull()
  })

  it('a rejection for a DIFFERENT company during a switch is still stale', () => {
    const a = ready(ACTOR_A, COMPANY_A)
    const foreign = { ...snapOf(a), companyId: 'ffff0000-0000-0000-0000-00000000000f' }
    const switching = shellReducer(a, {
      type: 'REQUEST_SWITCH',
      target: { scopeMode: 'single', companyId: COMPANY_B },
    })
    const out = shellReducer(switching, {
      type: 'BUSINESS_AUTH_OUTCOME',
      status: 403,
      authenticatedActorId: ACTOR_A,
      snapshot: foreign,
    })
    expect(out).toBe(switching)
  })
})

describe('ChatGPT/Codex #169 P1-2 — a mid-switch 403 must not orphan the switch', () => {
  function snapOf(s: ShellState, ns = 'ns') {
    return {
      actorId: s.activeContext?.userId ?? null,
      sessionEpoch: s.sessionEpoch,
      companyId: s.activeContext?.companyId ?? null,
      committedContextRevision: s.committedContextRevision,
      capabilityGeneration: s.activeContext?.capabilityGeneration ?? null,
      namespace: ns,
    }
  }

  it('a REAUTH_OK carrying the ORIGINAL generation still commits', () => {
    const a = ready(ACTOR_A, COMPANY_A)
    const snapA = snapOf(a)
    const switching = shellReducer(a, {
      type: 'REQUEST_SWITCH',
      target: { scopeMode: 'single', companyId: COMPANY_B },
    })
    // This is what the in-flight request captured when it started. It cannot
    // learn about anything that happens afterwards.
    const g1 = switching.requestFenceGeneration

    const refenced = shellReducer(switching, {
      type: 'BUSINESS_AUTH_OUTCOME',
      status: 403,
      authenticatedActorId: ACTOR_A,
      snapshot: snapA,
    })
    // The 403 was handled — but the attempt's identity must be untouched, or the
    // reauth below can never be recognised and the shell hangs in `switching`.
    expect(refenced.lastSecurityEffect?.kind).toBe('capability-refence')
    expect(refenced.requestFenceGeneration).toBe(g1)

    const committed = shellReducer(refenced, {
      type: 'REAUTH_OK',
      forGeneration: g1,
      context: {
        userId: ACTOR_A,
        scopeMode: 'single',
        companyId: COMPANY_B,
        capabilityGeneration: 'gen-b',
        capabilities: ['cari.parties.list'],
      },
    })
    expect(committed.phase).toBe('idle')
    expect(committed.activeContext?.companyId).toBe(COMPANY_B)
  })

  it('a REAUTH_FAIL carrying the ORIGINAL generation still restores — fail-closed', () => {
    const a = ready(ACTOR_A, COMPANY_A)
    const snapA = snapOf(a)
    const switching = shellReducer(a, {
      type: 'REQUEST_SWITCH',
      target: { scopeMode: 'single', companyId: COMPANY_B },
    })
    const g1 = switching.requestFenceGeneration

    const refenced = shellReducer(switching, {
      type: 'BUSINESS_AUTH_OUTCOME',
      status: 403,
      authenticatedActorId: ACTOR_A,
      snapshot: snapA,
    })
    const restored = shellReducer(refenced, { type: 'REAUTH_FAIL', forGeneration: g1 })

    // Recognised, so no permanent `switching`...
    expect(restored.phase).toBe('idle')
    expect(restored.activeContext?.companyId).toBe(COMPANY_A)
    // ...and still without the authority the server refused.
    expect(restored.activeContext?.capabilityGeneration).toBeNull()
    expect(capabilityFailClosed(restored)).toBe(true)
  })

  it('outside a switch, a 403 DOES advance the fence — nothing is in flight to break', () => {
    const s = ready()
    const out = shellReducer(s, {
      type: 'BUSINESS_AUTH_OUTCOME',
      status: 403,
      authenticatedActorId: ACTOR_A,
      snapshot: snapOf(s),
    })
    expect(out.requestFenceGeneration).toBe(s.requestFenceGeneration + 1)
  })
})
