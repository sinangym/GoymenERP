import { afterEach, describe, expect, it } from 'vitest'
import {
  attemptStoreKey,
  clearAttemptStore,
  recallAttempt,
  releaseAttempt,
  reserveAttempt,
  updateAttempt,
  type StoredAttempt,
} from './unresolvedAttemptStore'

const ACTOR = '99999999-9999-9999-9999-999999999999'
const OTHER_ACTOR = '88888888-8888-8888-8888-888888888888'
const COMPANY_A = 'aaaaaaaa-0000-0000-0000-000000000001'
const COMPANY_B = 'bbbbbbbb-0000-0000-0000-000000000002'
const SESSION = 0
const LOCATION = '33333333-3333-3333-3333-333333333333'
const OTHER_LOCATION = '44444444-4444-4444-4444-444444444444'

const ATTEMPT: StoredAttempt = {
  snapshot: {
    key: 'key-1',
    binding: 'ctx-a',
    payload: { isActive: false, expectedVersion: 3, reason: 'Bakım' },
  },
  reason: { kind: 'network-error' },
  resends: 0,
}

afterEach(() => {
  clearAttemptStore()
})

describe('key', () => {
  it('is null when any part of the identity is missing', () => {
    // Without a full identity there is no context to bind an outstanding command to,
    // and a partial key could collide across users.
    expect(attemptStoreKey(SESSION, null, COMPANY_A, LOCATION)).toBeNull()
    expect(attemptStoreKey(SESSION, ACTOR, null, LOCATION)).toBeNull()
    expect(attemptStoreKey(SESSION, ACTOR, COMPANY_A, '')).toBeNull()
  })

  it('is structural, so no delimiter can make two identities collide', () => {
    expect(attemptStoreKey(SESSION, 'a', 'b|c', LOCATION)).not.toBe(
      attemptStoreKey(SESSION, 'a|b', 'c', LOCATION),
    )
  })
})

describe('survival within one identity', () => {
  it('recalls what was remembered', () => {
    const key = attemptStoreKey(SESSION, ACTOR, COMPANY_A, LOCATION)
    reserveAttempt(key, ATTEMPT)
    expect(recallAttempt(key)?.snapshot.key).toBe('key-1')
  })

  it('releases on resolution of THAT attempt', () => {
    const key = attemptStoreKey(SESSION, ACTOR, COMPANY_A, LOCATION)
    reserveAttempt(key, ATTEMPT)
    releaseAttempt(key, 'key-1')
    expect(recallAttempt(key)).toBeNull()
  })

  it('ignores a null key rather than throwing', () => {
    reserveAttempt(null, ATTEMPT)
    expect(recallAttempt(null)).toBeNull()
    releaseAttempt(null, 'key-1')
    updateAttempt(null, ATTEMPT)
  })
})

describe('an obsolete owner cannot clobber a newer attempt', () => {
  const KEY = attemptStoreKey(SESSION, ACTOR, COMPANY_A, LOCATION)
  const NEWER: StoredAttempt = {
    ...ATTEMPT,
    snapshot: { ...ATTEMPT.snapshot, key: 'key-2' },
  }

  it('does not release an attempt it does not own', () => {
    /**
     * A settlement closes ONE command, not "whatever is in the slot". An unmounting or
     * stale owner settling its own older attempt must not erase the outstanding command
     * belonging to whoever owns the screen now.
     */
    reserveAttempt(KEY, NEWER)
    releaseAttempt(KEY, 'key-1')
    expect(recallAttempt(KEY)?.snapshot.key).toBe('key-2')
  })

  it('does not overwrite a newer reservation from a stale phase update', () => {
    reserveAttempt(KEY, NEWER)
    updateAttempt(KEY, { ...ATTEMPT, reason: { kind: 'offline' } })
    expect(recallAttempt(KEY)?.snapshot.key).toBe('key-2')
    expect(recallAttempt(KEY)?.reason).toEqual(ATTEMPT.reason)
  })

  it('lets the OWNING attempt update itself', () => {
    reserveAttempt(KEY, ATTEMPT)
    updateAttempt(KEY, { ...ATTEMPT, reason: { kind: 'offline' }, resends: 2 })
    expect(recallAttempt(KEY)).toMatchObject({ reason: { kind: 'offline' }, resends: 2 })
  })

  it('lets a deliberate new submit replace the slot', () => {
    // A reservation is the one moment a new attempt legitimately takes over.
    reserveAttempt(KEY, ATTEMPT)
    reserveAttempt(KEY, NEWER)
    expect(recallAttempt(KEY)?.snapshot.key).toBe('key-2')
  })
})

describe('an attempt never travels', () => {
  it.each([
    ['another company', () => attemptStoreKey(SESSION, ACTOR, COMPANY_B, LOCATION)],
    ['another actor', () => attemptStoreKey(SESSION, OTHER_ACTOR, COMPANY_A, LOCATION)],
    ['another location', () => attemptStoreKey(SESSION, ACTOR, COMPANY_A, OTHER_LOCATION)],
    ['a NEW session for the same human', () => attemptStoreKey(SESSION + 1, ACTOR, COMPANY_A, LOCATION)],
  ])('is not visible to %s', (_label, otherKey) => {
    /**
     * The boundary the coordinator drew: an outstanding command survives the component
     * lifecycle, but it must never surface for a different user or company. It simply
     * does not match, so it reads as absent.
     */
    reserveAttempt(attemptStoreKey(SESSION, ACTOR, COMPANY_A, LOCATION), ATTEMPT)
    expect(recallAttempt(otherKey())).toBeNull()
  })

  it('survives a capability refence, which does not change the session epoch', () => {
    /**
     * The distinction that matters: in `shellState.ts` the 403 branch returns without
     * touching `sessionEpoch` — only 401 / SESSION_RESET / SESSION_INVALIDATED advance
     * it. So a revoked capability keeps the outstanding command (the session is still
     * the user's), while signing back in starts a new epoch and inherits nothing.
     */
    const key = attemptStoreKey(SESSION, ACTOR, COMPANY_A, LOCATION)
    reserveAttempt(key, ATTEMPT)
    // Same epoch, whatever happened to the capability generation.
    expect(recallAttempt(attemptStoreKey(SESSION, ACTOR, COMPANY_A, LOCATION))?.snapshot.key).toBe(
      'key-1',
    )
    // A new session for the same human starts clean.
    expect(recallAttempt(attemptStoreKey(SESSION + 1, ACTOR, COMPANY_A, LOCATION))).toBeNull()
  })

  it('is still there when the original identity comes back', () => {
    // Switching away and back is not abandonment — the command is still outstanding.
    const key = attemptStoreKey(SESSION, ACTOR, COMPANY_A, LOCATION)
    reserveAttempt(key, ATTEMPT)
    expect(recallAttempt(attemptStoreKey(SESSION, ACTOR, COMPANY_B, LOCATION))).toBeNull()
    expect(recallAttempt(attemptStoreKey(SESSION, ACTOR, COMPANY_A, LOCATION))?.snapshot.key).toBe('key-1')
  })
})
