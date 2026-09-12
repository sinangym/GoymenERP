import { afterEach, describe, expect, it } from 'vitest'
import {
  createScope,
  editScope,
  forgetAttempt,
  recallAttempt,
  rememberAttempt,
  resetPendingAttempts,
} from './pendingAttempt'
import type { AttemptSnapshot, AttemptState } from './commandAttempt'

const NS_A = '["actor-a","single","company-a","gen-1","3"]'
const NS_B = '["actor-b","single","company-b","gen-2","4"]'

const snapshot: AttemptSnapshot<{ name: string }> = {
  key: 'k1',
  payload: { name: 'x' },
  namespace: NS_A,
}

type S = AttemptState<{ name: string }, unknown>

const unknown: S = { phase: 'unknown', snapshot, reason: 'network-error' }
const refusedRetry: S = {
  phase: 'failed',
  snapshot,
  failure: 'forbidden',
  fieldErrors: {},
  unresolvedSince: 'network-error',
}

afterEach(() => {
  resetPendingAttempts()
})

describe('what is kept', () => {
  it('keeps an unknown attempt so its key survives an unmount', () => {
    /**
     * The key is the only thing standing between one committed record and two, and
     * a capability re-fence unmounts the screen without warning.
     */
    rememberAttempt(NS_A, createScope(), unknown)
    expect(recallAttempt<{ name: string }, unknown>(NS_A, createScope())?.phase).toBe('unknown')
  })

  it('keeps a refusal that left an earlier send unresolved', () => {
    rememberAttempt(NS_A, createScope(), refusedRetry)
    const recalled = recallAttempt<{ name: string }, unknown>(NS_A, createScope())
    expect(recalled?.phase).toBe('failed')
  })

  it('keeps nothing once the attempt is resolved', () => {
    // A completed or definitively failed attempt has no key left to protect, and
    // holding it would resurrect a closed decision.
    rememberAttempt(NS_A, createScope(), unknown)
    rememberAttempt(NS_A, createScope(), {
      phase: 'failed',
      snapshot,
      failure: 'validation',
      fieldErrors: {},
    })
    expect(recallAttempt(NS_A, createScope())).toBeNull()
  })

  it('keeps nothing while a send is in flight', () => {
    rememberAttempt(NS_A, createScope(), { phase: 'submitting', snapshot })
    expect(recallAttempt(NS_A, createScope())).toBeNull()
  })
})

describe('who can see it', () => {
  it('is unreachable from another namespace', () => {
    /**
     * Another actor, another company or a new session must not see — let alone
     * resume — someone else's pending write. Same boundary the list view uses.
     */
    rememberAttempt(NS_A, createScope(), unknown)
    expect(recallAttempt(NS_B, createScope())).toBeNull()
  })

  it('is not destroyed by a foreign read', () => {
    // The pending write belongs to the context that started it; a colleague opening
    // the same screen must not throw it away either.
    rememberAttempt(NS_A, createScope(), unknown)
    expect(recallAttempt(NS_B, createScope())).toBeNull()
    expect(recallAttempt(NS_A, createScope())).not.toBeNull()
  })

  it('keeps one record’s edit apart from another’s', () => {
    rememberAttempt(NS_A, editScope('p-1'), unknown)
    expect(recallAttempt(NS_A, editScope('p-2'))).toBeNull()
    expect(recallAttempt(NS_A, editScope('p-1'))).not.toBeNull()
  })

  it('returns nothing while the shell holds no bound context', () => {
    rememberAttempt(null, createScope(), unknown)
    expect(recallAttempt(null, createScope())).toBeNull()
  })
})

describe('forgetAttempt', () => {
  it('drops a pending attempt the user resolved or abandoned', () => {
    rememberAttempt(NS_A, createScope(), unknown)
    forgetAttempt(createScope())
    expect(recallAttempt(NS_A, createScope())).toBeNull()
  })
})
