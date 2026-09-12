import { describe, expect, it } from 'vitest'
import {
  MAX_ATTEMPT_KEY_LENGTH,
  generateAttemptKey,
  hasUnresolvedWork,
  isFormFrozen,
  isOutcomeStillUnknown,
  isSamePayload,
  liveSnapshot,
  nextState,
  type AttemptSnapshot,
} from './commandAttempt'
import type { CreateProductDraftPayload } from './api'
import type { ProductMutationResult } from './contract'

const GROUP_A = 'aaaaaaa1-0000-0000-0000-000000000001'
const GROUP_B = 'aaaaaaa1-0000-0000-0000-000000000002'

function payload(over: Partial<CreateProductDraftPayload> = {}): CreateProductDraftPayload {
  return {
    name: 'Siyah sac',
    groupId: GROUP_A,
    roles: { purchasable: { value: true, provenance: 'manual' } },
    ...over,
  }
}

const snapshot: AttemptSnapshot<CreateProductDraftPayload> = {
  key: 'k1',
  payload: payload(),
  namespace: 'ns-a',
}

const result = { isReplay: false } as unknown as ProductMutationResult

describe('generateAttemptKey', () => {
  it('meets the contract’s key rule', () => {
    const key = generateAttemptKey()
    // 1..128 printable ASCII, no whitespace, no comma.
    expect(key).toMatch(/^[0-9a-f]{32}$/)
    expect(key.length).toBeLessThanOrEqual(MAX_ATTEMPT_KEY_LENGTH)
    expect(key).not.toMatch(/[\s,]/)
  })

  it('does not repeat across attempts', () => {
    const keys = new Set(Array.from({ length: 50 }, () => generateAttemptKey()))
    expect(keys.size).toBe(50)
  })
})

describe('isSamePayload', () => {
  it('treats the same normalized payload as the same attempt', () => {
    expect(isSamePayload(payload(), payload())).toBe(true)
  })

  it('ignores key ORDER, so one attempt does not become two', () => {
    /**
     * `JSON.stringify` preserves insertion order, so two payloads with identical
     * values would otherwise compare as different purely because an object literal
     * was written with its fields in another order — and that difference would mint
     * a second idempotency key for the same business attempt.
     */
    const a = { name: 'x', groupId: GROUP_A } as CreateProductDraftPayload
    const b = { groupId: GROUP_A, name: 'x' } as CreateProductDraftPayload
    expect(isSamePayload(a, b)).toBe(true)
  })

  it('treats a changed field or role as a new attempt', () => {
    expect(isSamePayload(payload(), payload({ name: 'Galvaniz sac' }))).toBe(false)
    expect(isSamePayload(payload(), payload({ groupId: GROUP_B }))).toBe(false)
    expect(
      isSamePayload(
        payload(),
        payload({
          roles: {
            purchasable: { value: true, provenance: 'manual' },
            sellable: { value: false, provenance: 'manual' },
          },
        }),
      ),
    ).toBe(false)
  })
})

describe('nextState', () => {
  it('completes on a successful outcome', () => {
    const state = nextState(snapshot, { type: 'succeeded', data: result })
    expect(state.phase).toBe('completed')
    if (state.phase !== 'completed') return
    // `isReplay` lives inside the result, so the attempt carries no second copy
    // that could disagree with it.
    expect(state.result.isReplay).toBe(false)
  })

  it.each([
    ['offline', 'offline'],
    ['network-error', 'network-error'],
    ['malformed', 'malformed-response'],
    ['aborted', 'network-error'],
  ] as const)('leaves %s unresolved so the same key can be retried', (type, reason) => {
    const state = nextState(snapshot, { type } as never)
    expect(state.phase).toBe('unknown')
    if (state.phase !== 'unknown') return
    expect(state.reason).toBe(reason)
    // The key survives: "An unknown outcome must not be retried with a new key."
    expect(state.snapshot.key).toBe('k1')
  })

  it('treats a 5xx as unknown, not as a definite failure', () => {
    // A 5xx is the server failing to answer, not an answer. The write may have applied.
    const state = nextState(snapshot, { type: 'http', status: 503, code: null, body: null })
    expect(state.phase).toBe('unknown')
  })

  it.each([
    ['version_conflict', 'stale-version'],
    ['lifecycle_conflict', 'lifecycle-conflict'],
    ['not_found', 'not-found'],
    ['validation_failed', 'validation'],
    ['idempotency_conflict', 'idempotency-conflict'],
  ] as const)('classifies the contract code %s', (code, failure) => {
    const state = nextState(snapshot, { type: 'http', status: 409, code, body: null })
    expect(state.phase).toBe('failed')
    if (state.phase !== 'failed') return
    expect(state.failure).toBe(failure)
  })

  it('keeps stale-version apart from idempotency-conflict', () => {
    /**
     * Both are 409 and the user-facing rule differs: a stale edit shows the current
     * record and waits for the person to review it, and must never be rebased.
     */
    const stale = nextState(snapshot, {
      type: 'http',
      status: 409,
      code: 'version_conflict',
      body: null,
    })
    const idem = nextState(snapshot, {
      type: 'http',
      status: 409,
      code: 'idempotency_conflict',
      body: null,
    })
    expect(stale.phase === 'failed' && stale.failure).toBe('stale-version')
    expect(idem.phase === 'failed' && idem.failure).toBe('idempotency-conflict')
  })

  it.each([
    [401, 'unauthenticated'],
    [403, 'forbidden'],
    [404, 'not-found'],
    [400, 'validation'],
    [409, 'idempotency-conflict'],
    [418, 'unexpected'],
  ] as const)('falls back on status %i when the code is unknown', (status, failure) => {
    /**
     * The endpoints defer 401/403/500/503 to the host's canonical handling, so a
     * refusal without a Product code is expected rather than exceptional. Falling
     * back keeps the screen correct instead of showing "unexpected error" for an
     * ordinary refusal.
     */
    const state = nextState(snapshot, { type: 'http', status, code: null, body: null })
    expect(state.phase).toBe('failed')
    if (state.phase !== 'failed') return
    expect(state.failure).toBe(failure)
  })
})

describe('a refusal that only answers a RETRY', () => {
  const unknown = nextState(snapshot, { type: 'network-error' } as never)

  it('does not settle the earlier send', () => {
    /**
     * The scenario this exists for: the first write COMMITS, its response is lost,
     * the caller's authority is then revoked, and the same-key retry gets 403. The
     * backend must re-authorize before it can read a stored result, so that 403 is a
     * denial of REPLAY ACCESS — never evidence the first write rolled back.
     */
    const refused = nextState(
      snapshot,
      { type: 'http', status: 403, code: 'forbidden', body: null },
      unknown,
    )
    expect(refused.phase).toBe('failed')
    if (refused.phase !== 'failed') return
    expect(refused.failure).toBe('forbidden')
    // ...and the earlier outcome is still open.
    expect(refused.unresolvedSince).toBe('network-error')
    expect(isOutcomeStillUnknown(refused)).toBe(true)
  })

  it('keeps the snapshot alive so the same payload reuses the key', () => {
    // Minting a second key here is how one committed record becomes two.
    const refused = nextState(
      snapshot,
      { type: 'http', status: 403, code: 'forbidden', body: null },
      unknown,
    )
    expect(liveSnapshot(refused)?.key).toBe('k1')
  })

  it('keeps the ORIGINAL unknown reason across a further transport failure', () => {
    const again = nextState(snapshot, { type: 'offline' }, unknown)
    expect(again.phase).toBe('unknown')
    if (again.phase !== 'unknown') return
    expect(again.reason).toBe('network-error')
  })

  it('leaves a FIRST-send refusal definite, with nothing carried', () => {
    // No prior unknown: this really is "it did not happen", and the screen may
    // say so.
    const first = nextState(snapshot, { type: 'http', status: 403, code: 'forbidden', body: null })
    expect(first.phase).toBe('failed')
    if (first.phase !== 'failed') return
    expect(first.unresolvedSince).toBeUndefined()
    expect(isOutcomeStillUnknown(first)).toBe(false)
    // A closed attempt is not resubmitted under its old key.
    expect(liveSnapshot(first)).toBeNull()
  })

  it('counts as unsaved work even with an untouched form', () => {
    const refused = nextState(
      snapshot,
      { type: 'http', status: 403, code: 'forbidden', body: null },
      unknown,
    )
    expect(hasUnresolvedWork(refused, false)).toBe(true)
  })
})

describe('isFormFrozen', () => {
  it('freezes while submitting and after completion, never on a failure', () => {
    expect(isFormFrozen({ phase: 'submitting', snapshot })).toBe(true)
    expect(isFormFrozen({ phase: 'completed', snapshot, result })).toBe(true)
    expect(isFormFrozen({ phase: 'failed', snapshot, failure: 'validation', fieldErrors: {} })).toBe(
      false,
    )
    // An unknown outcome leaves the form editable: changing the payload is what
    // makes a new attempt legitimate.
    expect(isFormFrozen({ phase: 'unknown', snapshot, reason: 'offline' })).toBe(false)
  })
})

describe('hasUnresolvedWork', () => {
  it('counts an unknown outcome as unsaved even when the form is untouched', () => {
    // The write may have applied and nothing has confirmed it; letting the user walk
    // away silently is how an unresolved write is forgotten.
    expect(hasUnresolvedWork({ phase: 'unknown', snapshot, reason: 'offline' }, false)).toBe(true)
  })

  it('is clear once the attempt completed', () => {
    expect(hasUnresolvedWork({ phase: 'completed', snapshot, result }, true)).toBe(false)
  })

  it('follows the form when idle', () => {
    expect(hasUnresolvedWork({ phase: 'idle' }, false)).toBe(false)
    expect(hasUnresolvedWork({ phase: 'idle' }, true)).toBe(true)
  })
})
