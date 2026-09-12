import { describe, expect, it } from 'vitest'
import {
  MAX_ATTEMPT_KEY_LENGTH,
  generateAttemptKey,
  hasUnresolvedWork,
  isFormFrozen,
  isSamePayload,
  nextState,
  type AttemptSnapshot,
} from './createAttempt'
import type { CreateProductDraftPayload } from './api'

function payload(over: Partial<CreateProductDraftPayload> = {}): CreateProductDraftPayload {
  return {
    name: 'Siyah sac',
    classification: { class: 'Yassı ürün', group: null, family: null },
    roles: { purchasable: true },
    trackingPolicy: null,
    ...over,
  }
}

const snapshot: AttemptSnapshot = { key: 'k1', payload: payload() }

const created = {
  id: '11111111-1111-1111-1111-111111111111',
  code: 'SAC-42',
  name: 'Siyah sac',
  lifecycle: 'Draft' as const,
  version: '1',
}

describe('generateAttemptKey', () => {
  it('produces 32 hex characters inside the contract bound', () => {
    const key = generateAttemptKey()
    expect(key).toMatch(/^[0-9a-f]{32}$/)
    expect(key.length).toBeLessThanOrEqual(MAX_ATTEMPT_KEY_LENGTH)
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

  it('treats a changed field as a new attempt', () => {
    expect(isSamePayload(payload(), payload({ name: 'Galvaniz sac' }))).toBe(false)
    expect(
      isSamePayload(payload(), payload({ classification: { class: 'Boru', group: null, family: null } })),
    ).toBe(false)
  })

  it('treats an added role answer as a new attempt', () => {
    // The roles object is what a tri-state picker writes, so a newly answered role
    // genuinely changes what would be persisted.
    expect(isSamePayload(payload(), payload({ roles: { purchasable: true, sellable: false } }))).toBe(
      false,
    )
  })
})

describe('nextState', () => {
  it('completes on a created outcome and carries the replay flag', () => {
    const state = nextState(snapshot, { type: 'created', data: created, replayed: true })
    expect(state.phase).toBe('completed')
    if (state.phase !== 'completed') return
    expect(state.replayed).toBe(true)
    expect(state.result.code).toBe('SAC-42')
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
    // The key survives: re-sending it replays instead of creating a second product.
    expect(state.snapshot.key).toBe('k1')
  })

  it('treats a 5xx as unknown, not as a definite failure', () => {
    // A 5xx is the server failing to answer, not an answer. The Draft may exist.
    const state = nextState(snapshot, { type: 'http', status: 503, code: null, body: null })
    expect(state.phase).toBe('unknown')
  })

  it('closes the attempt on a definite refusal', () => {
    const state = nextState(snapshot, {
      type: 'http',
      status: 409,
      code: 'product_code_collision',
      body: null,
    })
    expect(state.phase).toBe('failed')
    if (state.phase !== 'failed') return
    expect(state.failure).toBe('code-collision')
  })

  it.each([
    [401, 'unauthenticated'],
    [403, 'forbidden'],
    [400, 'validation'],
    [409, 'idempotency-conflict'],
    [418, 'unexpected'],
  ] as const)('falls back on status %i when the code is unknown', (status, failure) => {
    /**
     * The Product problem codes are proposed, not accepted, so a server answering
     * with a code this build has never heard of is expected. Falling back keeps the
     * screen correct instead of showing "unexpected error" for an ordinary refusal.
     */
    const state = nextState(snapshot, { type: 'http', status, code: null, body: null })
    expect(state.phase).toBe('failed')
    if (state.phase !== 'failed') return
    expect(state.failure).toBe(failure)
  })
})

describe('isFormFrozen', () => {
  it('freezes while submitting and after completion, never on a failure', () => {
    expect(isFormFrozen({ phase: 'submitting', snapshot })).toBe(true)
    expect(isFormFrozen({ phase: 'completed', snapshot, result: created, replayed: false })).toBe(true)
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
    // The Draft may exist and nothing has confirmed it; letting the user walk away
    // silently is how an unresolved write is forgotten.
    expect(hasUnresolvedWork({ phase: 'unknown', snapshot, reason: 'offline' }, false)).toBe(true)
  })

  it('is clear once the attempt completed', () => {
    expect(
      hasUnresolvedWork({ phase: 'completed', snapshot, result: created, replayed: false }, true),
    ).toBe(false)
  })

  it('follows the form when idle', () => {
    expect(hasUnresolvedWork({ phase: 'idle' }, false)).toBe(false)
    expect(hasUnresolvedWork({ phase: 'idle' }, true)).toBe(true)
  })
})
