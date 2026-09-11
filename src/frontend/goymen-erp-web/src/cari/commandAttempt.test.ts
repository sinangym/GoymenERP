/**
 * The logical command attempt (spec CARI_C02_LINK_JIT_SPEC_v1 §4.3–§4.4, §5;
 * handoff §6), framework-free. Covers G-002 check 7 (double submit, same-key
 * retry, no automatic resend or key rotation, replay completes once,
 * idempotency_conflict) at the state-machine level; the component tests prove
 * the same rules through the real form.
 */

import { describe, expect, it } from 'vitest'
import {
  MAX_ATTEMPT_KEY_LENGTH,
  abandonAttempt,
  beginAttempt,
  canBeginAttempt,
  classifyCommandOutcome,
  generateAttemptKey,
  isAttemptFrozen,
  isValidAttemptKey,
  repeatsUnresolvedAttempt,
  retryAttempt,
  samePayload,
  settleAttempt,
  type AttemptSnapshot,
  type AttemptState,
} from './commandAttempt'
import type { CommandOutcome } from './commandApi'
import { PROBLEM_CODE, type ProblemCode } from './contract'

type Payload = { readonly roles: number }

const SNAPSHOT: AttemptSnapshot<Payload> = {
  key: 'a'.repeat(32),
  operation: 'counterparty.create',
  binding: '["user","company","gen","1","0"]',
  payload: { roles: 3 },
}

function created(replayed = false): CommandOutcome<string> {
  return { type: 'created', data: 'record', replayed, originalCorrelationId: replayed ? 'corr-1' : null }
}

function http(status: number, code: ProblemCode | null = null): CommandOutcome<string> {
  return { type: 'http', status, code, body: null }
}

describe('attempt key — one cryptographically random key per snapshot (§4.3)', () => {
  it('is 128 random bits as 32 lowercase hex characters, drawn from the given source', () => {
    const source = {
      getRandomValues<T extends ArrayBufferView | null>(array: T): T {
        if (array instanceof Uint8Array) array.fill(0xab)
        return array
      },
    }
    expect(generateAttemptKey(source)).toBe('ab'.repeat(16))
    expect(generateAttemptKey()).toMatch(/^[0-9a-f]{32}$/)
    expect(generateAttemptKey()).not.toBe(generateAttemptKey())
    expect(isValidAttemptKey(generateAttemptKey())).toBe(true)
  })

  it('validates the contract rule: trimmed, 1–128 characters, no comma', () => {
    expect(isValidAttemptKey('')).toBe(false)
    expect(isValidAttemptKey(' key')).toBe(false)
    expect(isValidAttemptKey('key ')).toBe(false)
    expect(isValidAttemptKey('a,b')).toBe(false)
    expect(isValidAttemptKey('x'.repeat(MAX_ATTEMPT_KEY_LENGTH))).toBe(true)
    expect(isValidAttemptKey('x'.repeat(MAX_ATTEMPT_KEY_LENGTH + 1))).toBe(false)
  })
})

describe('classifyCommandOutcome — branches on status + stable code only (§5)', () => {
  it('201 is the only success; replay is still a completion', () => {
    expect(classifyCommandOutcome(created())).toEqual({ kind: 'completed', result: 'record', replayed: false })
    expect(classifyCommandOutcome(created(true))).toEqual({ kind: 'completed', result: 'record', replayed: true })
  })

  it('an outcome the server never confirmed is UNKNOWN, never a failure', () => {
    expect(classifyCommandOutcome<string>({ type: 'offline' })).toEqual({ kind: 'unknown', reason: 'offline' })
    expect(classifyCommandOutcome<string>({ type: 'network-error' })).toEqual({ kind: 'unknown', reason: 'network-error' })
    expect(classifyCommandOutcome<string>({ type: 'aborted' })).toEqual({ kind: 'unknown', reason: 'network-error' })
    expect(classifyCommandOutcome<string>({ type: 'malformed' })).toEqual({ kind: 'unknown', reason: 'malformed-response' })
    expect(classifyCommandOutcome(http(500, PROBLEM_CODE.internalError))).toEqual({ kind: 'unknown', reason: 'service-unavailable' })
    expect(classifyCommandOutcome(http(503, PROBLEM_CODE.serviceUnavailable))).toEqual({ kind: 'unknown', reason: 'service-unavailable' })
    expect(classifyCommandOutcome(http(502))).toEqual({ kind: 'unknown', reason: 'service-unavailable' })
    // A 2xx that is not 201 is outside the contract: the record may exist.
    expect(classifyCommandOutcome(http(200))).toEqual({ kind: 'unknown', reason: 'malformed-response' })
    expect(classifyCommandOutcome(http(202))).toEqual({ kind: 'unknown', reason: 'malformed-response' })
  })

  it('400 splits on the stable code: validation vs metadata vs unexpected', () => {
    expect(classifyCommandOutcome(http(400, PROBLEM_CODE.validationFailed))).toEqual({ kind: 'failed', failure: 'validation' })
    expect(classifyCommandOutcome(http(400, PROBLEM_CODE.commandMetadataInvalid))).toEqual({ kind: 'failed', failure: 'metadata' })
    expect(classifyCommandOutcome(http(400, PROBLEM_CODE.correlationMetadataInvalid))).toEqual({ kind: 'failed', failure: 'metadata' })
    expect(classifyCommandOutcome(http(400, PROBLEM_CODE.companyContextInvalid))).toEqual({ kind: 'failed', failure: 'metadata' })
    // `x-erp-empty-body-possible`: no code at all is still a definite rejection.
    expect(classifyCommandOutcome(http(400, null))).toEqual({ kind: 'failed', failure: 'unexpected' })
    // invalid_cursor belongs to the candidate READ; on a write it is not validation.
    expect(classifyCommandOutcome(http(400, PROBLEM_CODE.invalidCursor))).toEqual({ kind: 'failed', failure: 'unexpected' })
  })

  it('401/403/404 are definite; 409 splits on already_linked vs idempotency_conflict', () => {
    expect(classifyCommandOutcome(http(401, PROBLEM_CODE.authenticationRequired))).toEqual({ kind: 'failed', failure: 'unauthenticated' })
    expect(classifyCommandOutcome(http(403, PROBLEM_CODE.forbidden))).toEqual({ kind: 'failed', failure: 'forbidden' })
    expect(classifyCommandOutcome(http(404, PROBLEM_CODE.notFound))).toEqual({ kind: 'failed', failure: 'not-found' })
    expect(classifyCommandOutcome(http(409, PROBLEM_CODE.alreadyLinked))).toEqual({ kind: 'failed', failure: 'already-linked' })
    expect(classifyCommandOutcome(http(409, PROBLEM_CODE.idempotencyConflict))).toEqual({ kind: 'failed', failure: 'idempotency-conflict' })
    // version_conflict is the versioned-update flow's code, not create/link's.
    expect(classifyCommandOutcome(http(409, 'version_conflict' as unknown as ProblemCode))).toEqual({ kind: 'failed', failure: 'unexpected' })
    expect(classifyCommandOutcome(http(409, null))).toEqual({ kind: 'failed', failure: 'unexpected' })
    expect(classifyCommandOutcome(http(415))).toEqual({ kind: 'failed', failure: 'unexpected' })
  })
})

describe('attempt state machine — one key per snapshot, retry only from unknown', () => {
  it('begins frozen, settles an unknown outcome and retries the SAME snapshot and key', () => {
    let state: AttemptState<Payload, string> = beginAttempt(SNAPSHOT)
    expect(state).toEqual({ phase: 'submitting', snapshot: SNAPSHOT, resends: 0 })
    expect(isAttemptFrozen(state)).toBe(true)

    state = settleAttempt(state, { type: 'network-error' })
    expect(state).toEqual({ phase: 'unknown', snapshot: SNAPSHOT, reason: 'network-error', resends: 0 })
    expect(isAttemptFrozen(state)).toBe(true)

    state = retryAttempt(state)
    expect(state.phase).toBe('submitting')
    expect(state.phase === 'submitting' && state.snapshot.key).toBe(SNAPSHOT.key)
    expect(state.phase === 'submitting' && state.resends).toBe(1)

    // The replay completes the very same logical attempt, exactly once.
    state = settleAttempt(state, created(true))
    expect(state).toEqual({ phase: 'completed', snapshot: SNAPSHOT, result: 'record', replayed: true })
    expect(isAttemptFrozen(state)).toBe(false)
  })

  it('a definite failure closes the attempt: no retry, no resend, no key reuse', () => {
    const failed = settleAttempt(beginAttempt<Payload, string>(SNAPSHOT), http(400, PROBLEM_CODE.validationFailed))
    expect(failed).toEqual({ phase: 'failed', snapshot: SNAPSHOT, failure: 'validation' })
    expect(isAttemptFrozen(failed)).toBe(false)
    // Retry is not a way out of a failure — only a new submit (new snapshot) is.
    expect(retryAttempt(failed)).toBe(failed)
    expect(retryAttempt({ phase: 'idle' })).toEqual({ phase: 'idle' })
  })

  it('only a submitting attempt can settle; a late outcome cannot re-complete or re-fail', () => {
    const completed = settleAttempt(beginAttempt<Payload, string>(SNAPSHOT), created())
    expect(settleAttempt(completed, created(true))).toBe(completed)
    expect(settleAttempt(completed, http(409, PROBLEM_CODE.idempotencyConflict))).toBe(completed)
    const unknown = settleAttempt(beginAttempt<Payload, string>(SNAPSHOT), { type: 'offline' })
    expect(settleAttempt(unknown, created())).toBe(unknown)
    expect(settleAttempt({ phase: 'idle' }, created())).toEqual({ phase: 'idle' })
  })
})

describe('abandoned attempt — the unresolved identity is kept (§4.3, C02-AC-011, C-004-P1-001)', () => {
  function unknownAttempt(): AttemptState<Payload, string> {
    return settleAttempt(beginAttempt<Payload, string>(SNAPSHOT), { type: 'network-error' })
  }

  it('"İşlemi bırak" from UNKNOWN keeps the snapshot and its key, unfrozen; a new attempt may begin', () => {
    const abandoned = abandonAttempt(unknownAttempt())
    expect(abandoned).toEqual({ phase: 'abandoned', snapshot: SNAPSHOT, reason: 'network-error', resends: 0 })
    expect(isAttemptFrozen(abandoned)).toBe(false)
    expect(canBeginAttempt(abandoned)).toBe(true)
    // Asking twice does not drop the identity; a request in flight cannot be abandoned.
    expect(abandonAttempt(abandoned)).toBe(abandoned)
    const submitting = beginAttempt<Payload, string>(SNAPSHOT)
    expect(abandonAttempt(submitting)).toBe(submitting)
  })

  it('a definite failure or a completion is closed for good: acknowledge/reset goes to idle', () => {
    const failed = settleAttempt(beginAttempt<Payload, string>(SNAPSHOT), http(409, PROBLEM_CODE.idempotencyConflict))
    const completed = settleAttempt(beginAttempt<Payload, string>(SNAPSHOT), created())
    expect(abandonAttempt(failed)).toEqual({ phase: 'idle' })
    expect(abandonAttempt(completed)).toEqual({ phase: 'idle' })
    expect(abandonAttempt({ phase: 'idle' })).toEqual({ phase: 'idle' })
    expect(canBeginAttempt({ phase: 'idle' })).toBe(true)
    expect(canBeginAttempt(failed)).toBe(false)
    expect(canBeginAttempt(completed)).toBe(false)
    expect(canBeginAttempt(unknownAttempt())).toBe(false)
    expect(canBeginAttempt(beginAttempt(SNAPSHOT))).toBe(false)
  })

  it('the abandoned snapshot can still be retried under its OWN key and completes exactly once', () => {
    const abandoned = abandonAttempt(settleAttempt(beginAttempt<Payload, string>(SNAPSHOT), { type: 'offline' }))
    const retried = retryAttempt(abandoned)
    // The re-send carries the original reason so a rejection of THIS request cannot
    // be mistaken for an answer about the first one.
    expect(retried).toEqual({
      phase: 'submitting',
      snapshot: SNAPSHOT,
      resends: 1,
      unresolvedSince: 'offline',
    })
    expect(retried.phase === 'submitting' && retried.snapshot.key).toBe(SNAPSHOT.key)
    const completed = settleAttempt(retried, created(true))
    expect(completed).toEqual({ phase: 'completed', snapshot: SNAPSHOT, result: 'record', replayed: true })
    expect(retryAttempt(completed)).toBe(completed)
  })

  it('the unchanged payload for the same operation repeats the unresolved attempt; anything else is a new one', () => {
    const abandoned = abandonAttempt(unknownAttempt())
    expect(repeatsUnresolvedAttempt(abandoned, 'counterparty.create', { roles: 3 })).toBe(true)
    expect(repeatsUnresolvedAttempt(abandoned, 'counterparty.create', { roles: 1 })).toBe(false)
    expect(repeatsUnresolvedAttempt(abandoned, 'counterparty.link:other-party', { roles: 3 })).toBe(false)
    // Only an ABANDONED attempt binds the payload: unknown is frozen anyway, and
    // failed / completed / idle are closed — a new key there is correct.
    expect(repeatsUnresolvedAttempt(unknownAttempt(), 'counterparty.create', { roles: 3 })).toBe(false)
    expect(repeatsUnresolvedAttempt({ phase: 'idle' }, 'counterparty.create', { roles: 3 })).toBe(false)
    const completed = settleAttempt(beginAttempt<Payload, string>(SNAPSHOT), created())
    expect(repeatsUnresolvedAttempt(completed, 'counterparty.create', { roles: 3 })).toBe(false)
    const failed = settleAttempt(beginAttempt<Payload, string>(SNAPSHOT), http(400, PROBLEM_CODE.validationFailed))
    expect(repeatsUnresolvedAttempt(failed, 'counterparty.create', { roles: 3 })).toBe(false)
  })

  it('samePayload is structural: key order is irrelevant, values and shapes are not', () => {
    const create = { kind: 1, legalName: 'Örnek', tradeName: null, roles: 3 }
    expect(samePayload(create, { roles: 3, tradeName: null, legalName: 'Örnek', kind: 1 })).toBe(true)
    expect(samePayload(create, { ...create, legalName: 'Örnek ' })).toBe(false)
    expect(samePayload(create, { ...create, roles: 1 })).toBe(false)
    expect(samePayload(create, { ...create, tradeName: 'Örnek' })).toBe(false)
    expect(samePayload({ tradeName: null }, { tradeName: undefined })).toBe(false)
    expect(samePayload({ tradeName: null }, {})).toBe(false)
    expect(samePayload({ roles: 3 }, { roles: 3, extra: true })).toBe(false)
    expect(samePayload([1, 2], [1, 2])).toBe(true)
    expect(samePayload([1, 2], [2, 1])).toBe(false)
    expect(samePayload([1], { 0: 1 })).toBe(false)
    expect(samePayload(null, null)).toBe(true)
    expect(samePayload(null, {})).toBe(false)
    expect(samePayload('3', 3)).toBe(false)
  })
})

describe('a later rejection cannot answer an earlier unknown (spec §4.5)', () => {
  const contractRejection = { type: 'http', status: 409, code: 'client_contract_unsupported', body: null } as const

  function unknownThenRetry() {
    // First send processed, response lost.
    const unknown = settleAttempt(beginAttempt<Payload, string>(SNAPSHOT), { type: 'network-error' })
    expect(unknown.phase).toBe('unknown')
    return retryAttempt(unknown)
  }

  it('a compatibility 409 on the RE-SEND leaves the attempt unresolved', () => {
    // The 409 describes the second request. The first may already have been
    // written, and closing the attempt here would let the same payload go out
    // again under a NEW key.
    const settled = settleAttempt(unknownThenRetry(), contractRejection)
    expect(settled.phase).toBe('unknown')
    expect(settled.phase === 'unknown' && settled.snapshot.key).toBe(SNAPSHOT.key)
    expect(settled.phase === 'unknown' && settled.reason).toBe('network-error')
  })

  it('a local contract-not-ready on the RE-SEND leaves it unresolved too', () => {
    // Nothing left the client this time — which says nothing at all about the
    // request that did.
    const settled = settleAttempt(unknownThenRetry(), { type: 'contract-not-ready' })
    expect(settled.phase).toBe('unknown')
    expect(settled.phase === 'unknown' && settled.snapshot.key).toBe(SNAPSHOT.key)
  })

  it('an acknowledged 500, a timeout and a malformed body on the RE-SEND all leave it unresolved', () => {
    for (const outcome of [
      { type: 'http', status: 500, code: null, body: null },
      { type: 'network-error' },
      { type: 'malformed' },
      { type: 'unacknowledged', status: 201 },
    ] as const) {
      const settled = settleAttempt(unknownThenRetry(), outcome)
      expect(settled.phase, JSON.stringify(outcome)).toBe('unknown')
      expect(settled.phase === 'unknown' && settled.snapshot.key).toBe(SNAPSHOT.key)
    }
  })

  it('only a validated definite result closes it — and a replay counts', () => {
    const settled = settleAttempt(unknownThenRetry(), created(true))
    expect(settled.phase).toBe('completed')
    expect(settled.phase === 'completed' && settled.replayed).toBe(true)
  })

  it('a definite failure on a FIRST send is still a failure', () => {
    // The rule is about re-sends. A first attempt refused before anything happened
    // has no earlier uncertainty to protect.
    const settled = settleAttempt(beginAttempt<Payload, string>(SNAPSHOT), {
      type: 'http',
      status: 400,
      code: 'validation_failed',
      body: null,
    })
    expect(settled.phase).toBe('failed')
  })
})
