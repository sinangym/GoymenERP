import { afterEach, describe, expect, it } from 'vitest'
import {
  attemptStoreKey,
  clearAttemptStore,
  forgetAttempt,
  recallAttempt,
  rememberAttempt,
  type StoredAttempt,
} from './unresolvedAttemptStore'

const ACTOR = '99999999-9999-9999-9999-999999999999'
const OTHER_ACTOR = '88888888-8888-8888-8888-888888888888'
const COMPANY_A = 'aaaaaaaa-0000-0000-0000-000000000001'
const COMPANY_B = 'bbbbbbbb-0000-0000-0000-000000000002'
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
    expect(attemptStoreKey(null, COMPANY_A, LOCATION)).toBeNull()
    expect(attemptStoreKey(ACTOR, null, LOCATION)).toBeNull()
    expect(attemptStoreKey(ACTOR, COMPANY_A, '')).toBeNull()
  })

  it('is structural, so no delimiter can make two identities collide', () => {
    expect(attemptStoreKey('a', 'b|c', LOCATION)).not.toBe(attemptStoreKey('a|b', 'c', LOCATION))
  })
})

describe('survival within one identity', () => {
  it('recalls what was remembered', () => {
    const key = attemptStoreKey(ACTOR, COMPANY_A, LOCATION)
    rememberAttempt(key, ATTEMPT)
    expect(recallAttempt(key)?.snapshot.key).toBe('key-1')
  })

  it('forgets on resolution', () => {
    const key = attemptStoreKey(ACTOR, COMPANY_A, LOCATION)
    rememberAttempt(key, ATTEMPT)
    forgetAttempt(key)
    expect(recallAttempt(key)).toBeNull()
  })

  it('ignores a null key rather than throwing', () => {
    rememberAttempt(null, ATTEMPT)
    expect(recallAttempt(null)).toBeNull()
    forgetAttempt(null)
  })
})

describe('an attempt never travels', () => {
  it.each([
    ['another company', () => attemptStoreKey(ACTOR, COMPANY_B, LOCATION)],
    ['another actor', () => attemptStoreKey(OTHER_ACTOR, COMPANY_A, LOCATION)],
    ['another location', () => attemptStoreKey(ACTOR, COMPANY_A, OTHER_LOCATION)],
  ])('is not visible to %s', (_label, otherKey) => {
    /**
     * The boundary the coordinator drew: an outstanding command survives the component
     * lifecycle, but it must never surface for a different user or company. It simply
     * does not match, so it reads as absent.
     */
    rememberAttempt(attemptStoreKey(ACTOR, COMPANY_A, LOCATION), ATTEMPT)
    expect(recallAttempt(otherKey())).toBeNull()
  })

  it('is still there when the original identity comes back', () => {
    // Switching away and back is not abandonment — the command is still outstanding.
    const key = attemptStoreKey(ACTOR, COMPANY_A, LOCATION)
    rememberAttempt(key, ATTEMPT)
    expect(recallAttempt(attemptStoreKey(ACTOR, COMPANY_B, LOCATION))).toBeNull()
    expect(recallAttempt(attemptStoreKey(ACTOR, COMPANY_A, LOCATION))?.snapshot.key).toBe('key-1')
  })
})
