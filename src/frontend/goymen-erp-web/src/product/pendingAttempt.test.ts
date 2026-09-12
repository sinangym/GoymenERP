import { afterEach, describe, expect, it } from 'vitest'
import {
  armDiscard,
  attemptBindingFor,
  createScope,
  editScope,
  forgetAttempt,
  recallAttempt,
  reconcileArmedDiscards,
  rememberAttempt,
  resetPendingAttempts,
  type AttemptBinding,
} from './pendingAttempt'
import type { AttemptSnapshot, AttemptState } from './commandAttempt'
import type { ShellState } from '../shell/shellState'

/**
 * Bindings are derived from a shell state the way the screens derive them, so the
 * key derivation under test is the real one. Only the fields the binding reads are
 * supplied; the capability generation and the committed revision are deliberately
 * absent, because the binding must not depend on them.
 */
function shellWith(over: {
  readonly userId?: string
  readonly companyId?: string | null
  readonly scopeMode?: 'single' | 'group'
  readonly sessionEpoch?: number
  readonly revision?: number
}): ShellState {
  return {
    activeContext: {
      userId: over.userId ?? 'actor-a',
      scopeMode: over.scopeMode ?? 'single',
      companyId: over.companyId === undefined ? 'company-a' : over.companyId,
    },
    sessionEpoch: over.sessionEpoch ?? 0,
    committedContextRevision: over.revision ?? 1,
  } as unknown as ShellState
}

const A = attemptBindingFor(shellWith({}), createScope()) as AttemptBinding

type S = AttemptState<{ name: string }, unknown>

function snapshotOf(key: string): AttemptSnapshot<{ name: string }> {
  return { key, payload: { name: 'x' }, origin: 'company-a', namespace: A.key }
}
const snapshot = snapshotOf('k1')

const inFlight: S = { phase: 'submitting', snapshot }
const unknown: S = { phase: 'unknown', snapshot, reason: 'network-error' }
const refusedRetry: S = {
  phase: 'failed',
  snapshot,
  failure: 'forbidden',
  fieldErrors: {},
  unresolvedSince: 'network-error',
}
const definite: S = { phase: 'failed', snapshot, failure: 'validation', fieldErrors: {} }
const completed: S = { phase: 'completed', snapshot, result: {} }

afterEach(() => {
  resetPendingAttempts()
})

describe('the binding', () => {
  it('is the same across a capability re-verification of the same context', () => {
    /**
     * The whole point. A real 403 nulls the generation and the fresh authorized
     * entry carries a new one; the shell's committed revision may move too. Neither
     * is read here, so the same actor, session, origin and context find their own
     * attempt on the way back.
     */
    const before = attemptBindingFor(
      { ...shellWith({}), committedContextRevision: 3 } as ShellState,
      createScope(),
    )
    const after = attemptBindingFor(
      { ...shellWith({}), committedContextRevision: 4 } as ShellState,
      createScope(),
    )
    expect(after?.key).toBe(before?.key)
  })

  it('differs for another actor, another session, another context, another record', () => {
    const others = [
      attemptBindingFor(shellWith({ userId: 'actor-b' }), createScope()),
      attemptBindingFor(shellWith({ sessionEpoch: 1 }), createScope()),
      attemptBindingFor(shellWith({ companyId: 'company-b' }), createScope()),
      attemptBindingFor(shellWith({ scopeMode: 'group', companyId: null }), createScope()),
      attemptBindingFor(shellWith({}), editScope('p-1')),
    ]
    for (const other of others) expect(other?.key).not.toBe(A.key)
    expect(attemptBindingFor(shellWith({}), editScope('p-1'))?.key).not.toBe(
      attemptBindingFor(shellWith({}), editScope('p-2'))?.key,
    )
  })

  it('is null while no context is committed', () => {
    expect(attemptBindingFor({ activeContext: null, sessionEpoch: 0 } as ShellState, createScope()))
      .toBeNull()
  })
})

describe('what is kept', () => {
  it('keeps an in-flight send, and hands it back as UNKNOWN', () => {
    /**
     * A send whose screen went away mid-flight is unresolved work: the screen that
     * comes back cannot receive that answer, so to it the outcome is unknown — and
     * like every unknown, retryable only with the same key.
     */
    rememberAttempt(A, inFlight)
    const recalled = recallAttempt<{ name: string }, unknown>(A)
    expect(recalled?.phase).toBe('unknown')
    expect(recalled && 'snapshot' in recalled ? recalled.snapshot.key : null).toBe('k1')
    expect(recalled && 'reason' in recalled ? recalled.reason : null).toBe('interrupted')
  })

  it('keeps an unknown attempt and a refusal that left an earlier send unresolved', () => {
    rememberAttempt(A, unknown)
    expect(recallAttempt<{ name: string }, unknown>(A)?.phase).toBe('unknown')
    rememberAttempt(A, refusedRetry)
    expect(recallAttempt<{ name: string }, unknown>(A)?.phase).toBe('failed')
  })

  it('keeps nothing once the attempt is resolved AND seen', () => {
    rememberAttempt(A, unknown)
    rememberAttempt(A, definite)
    expect(recallAttempt(A)).toBeNull()
    rememberAttempt(A, unknown)
    rememberAttempt(A, completed)
    expect(recallAttempt(A)).toBeNull()
  })

  it('keeps a success nobody saw, until a screen renders it', () => {
    /**
     * The canonical answer to a send whose screen was already torn down is the
     * only record that the write succeeded. Settling it in a continuation nobody
     * can see would leave the next screen empty — and inviting a second key for a
     * product that exists.
     */
    rememberAttempt(A, inFlight)
    rememberAttempt(A, completed, false)
    expect(recallAttempt<{ name: string }, unknown>(A)?.phase).toBe('completed')
    // Shown: now it is settled.
    rememberAttempt(A, completed, true)
    expect(recallAttempt(A)).toBeNull()
  })

  it('does NOT keep a definite refusal nobody saw — nothing was created', () => {
    rememberAttempt(A, inFlight)
    rememberAttempt(A, definite, false)
    expect(recallAttempt(A)).toBeNull()
  })

  it('does nothing for an idle state or without a binding', () => {
    rememberAttempt(A, unknown)
    rememberAttempt(A, { phase: 'idle' })
    expect(recallAttempt(A)).not.toBeNull()
    rememberAttempt(null, unknown)
    expect(recallAttempt(null)).toBeNull()
  })
})

describe('who may see or write it', () => {
  it('is unreachable from another binding, and a foreign read does not destroy it', () => {
    const B = attemptBindingFor(shellWith({ userId: 'actor-b' }), createScope())
    rememberAttempt(A, unknown)
    expect(recallAttempt(B)).toBeNull()
    expect(recallAttempt(A)).not.toBeNull()
  })

  it('cannot be deleted by a write under another binding', () => {
    // The third cause on #238: a scope-only slot let a screen mounted under a
    // different context settle someone else's attempt on its way to idle.
    const B = attemptBindingFor(shellWith({ sessionEpoch: 1 }), createScope())
    rememberAttempt(A, unknown)
    rememberAttempt(B, completed)
    rememberAttempt(B, definite)
    forgetAttempt(B)
    expect(recallAttempt(A)).not.toBeNull()
  })

  it('cannot be deleted by resolving a DIFFERENT attempt under the same binding', () => {
    rememberAttempt(A, { phase: 'unknown', snapshot: snapshotOf('k2'), reason: 'offline' })
    rememberAttempt(A, completed) // k1, which is not what the slot holds
    expect(recallAttempt<{ name: string }, unknown>(A)).toMatchObject({ snapshot: { key: 'k2' } })
  })
})

describe('an armed discard', () => {
  /**
   * A teardown while the switch question is open is not the person's answer: the
   * shell drops the outlet for a failed directory read too. The teardown arms;
   * the committed revision — which only a new context commit advances — decides.
   */
  it('is disarmed when the same context is seen again at the same revision', () => {
    rememberAttempt(A, unknown)
    armDiscard(A, 1)
    reconcileArmedDiscards(shellWith({ revision: 1 }), createScope())
    expect(recallAttempt(A)).not.toBeNull()
    // Disarmed for good: a later switch is a new question, not this one.
    reconcileArmedDiscards(shellWith({ companyId: 'company-b', revision: 2 }), createScope())
    expect(recallAttempt(A)).not.toBeNull()
  })

  it('is confirmed by a higher revision in the same session, wherever it is seen', () => {
    rememberAttempt(A, unknown)
    armDiscard(A, 1)
    // Seen from the context the person switched TO.
    reconcileArmedDiscards(shellWith({ companyId: 'company-b', revision: 2 }), createScope())
    expect(recallAttempt(A)).toBeNull()

    rememberAttempt(A, { ...unknown, snapshot: snapshotOf('k2') })
    armDiscard(A, 2)
    // Or on the way back, after switching there and back again.
    reconcileArmedDiscards(shellWith({ revision: 4 }), createScope())
    expect(recallAttempt(A)).toBeNull()
  })

  it('is not confirmed by another actor, another session or another screen', () => {
    rememberAttempt(A, unknown)
    armDiscard(A, 1)
    reconcileArmedDiscards(shellWith({ userId: 'actor-b', revision: 5 }), createScope())
    reconcileArmedDiscards(shellWith({ sessionEpoch: 1, revision: 5 }), createScope())
    reconcileArmedDiscards(shellWith({ revision: 5 }), editScope('p-1'))
    expect(recallAttempt(A)).not.toBeNull()
  })
})

describe('a late outcome', () => {
  it('cannot resurrect an attempt the user abandoned', () => {
    // The continuation of a send whose screen is gone may still write. If the user
    // was asked and chose to leave in the meantime, that answer changes nothing.
    rememberAttempt(A, inFlight)
    forgetAttempt(A)
    rememberAttempt(A, unknown)
    expect(recallAttempt(A)).toBeNull()
  })

  it('cannot resurrect an attempt that already reached a final state', () => {
    rememberAttempt(A, inFlight)
    rememberAttempt(A, completed)
    rememberAttempt(A, unknown)
    expect(recallAttempt(A)).toBeNull()
  })

  it('forgetting without a binding, or with nothing held, is a no-op', () => {
    rememberAttempt(A, unknown)
    forgetAttempt(null)
    forgetAttempt(attemptBindingFor(shellWith({ userId: 'actor-b' }), createScope()))
    expect(recallAttempt(A)).not.toBeNull()
  })
})
