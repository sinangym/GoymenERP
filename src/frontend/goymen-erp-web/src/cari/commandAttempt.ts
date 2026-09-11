/**
 * Logical command attempt — the framework-independent state machine behind
 * C-02 create and CARI-LINK submit (spec CARI_C02_LINK_JIT_SPEC_v1 §4.3, §4.4,
 * §5, §8; handoff §6).
 *
 * THE RULE, in one line: one key per submitted SNAPSHOT, never per click.
 *
 *  - No key exists at mount, while typing, or on a click that fails client-side
 *    validation. The first client-valid submit freezes an immutable snapshot
 *    (payload + operation + context binding) and mints ONE random key for it.
 *  - A double click, a timeout, a dropped connection, a 5xx or a malformed 201
 *    leaves the outcome UNKNOWN: the record may exist. The user retries the same
 *    snapshot with the SAME key — the server replays instead of duplicating — or
 *    explicitly abandons it. Nothing here ever resends on its own, and nothing
 *    here ever rotates the key under an unchanged payload.
 *  - An ABANDONED attempt is still unresolved: the record may exist. Its snapshot
 *    and key are kept (`abandoned`) while the form is unfrozen. The same
 *    operation with the same payload is THAT attempt — it may be retried under
 *    its own key, but it is never re-sent under a new one. Only a CHANGED payload
 *    (or a different Party in the link flow) starts a new attempt (§4.3,
 *    C02-AC-011; C-004-P1-001).
 *  - A DEFINITE answer that is not success (400, 401, 403, 404, 409, 415, …)
 *    closes the attempt. A later submit is a NEW business attempt with a new
 *    snapshot and a new key; editing the payload is what makes it new.
 *  - Success, replayed or not, completes the attempt exactly once.
 *
 * The raw key lives in component memory only: not in storage, the URL, a log or
 * telemetry.
 */

import type { CommandOutcome } from './commandApi'
import { PROBLEM_CODE } from './contract'

/** A definite, non-success answer from the server, by stable code. */
export type AttemptFailure =
  /**
   * Compatibility was not proven in this context, so nothing was sent. Retryable once
   * a fresh acknowledged read succeeds here — under the SAME key, because the attempt
   * never left the client.
   */
  | 'contract-not-ready'
  | 'validation'
  | 'metadata'
  | 'unauthenticated'
  | 'forbidden'
  | 'not-found'
  | 'already-linked'
  | 'idempotency-conflict'
  | 'unexpected'

/** Why an outcome is unknown. Every one of these is retryable with the SAME key. */
export type AttemptUnknownReason =
  /** A response arrived, but without the acknowledgment that says which contract answered. */
  | 'unacknowledged'
  | 'offline'
  | 'network-error'
  | 'service-unavailable'
  | 'malformed-response'

export interface AttemptSnapshot<P> {
  /** The single Idempotency-Key for this snapshot. */
  readonly key: string
  /** e.g. `counterparty.create`, `counterparty.link:<partyId>`. */
  readonly operation: string
  /** Actor / company / revision / session the attempt was authorized under. */
  readonly binding: string
  /** The normalized payload exactly as sent. */
  readonly payload: P
}

export type AttemptState<P, R> =
  | { readonly phase: 'idle' }
  | { readonly phase: 'submitting'; readonly snapshot: AttemptSnapshot<P>; readonly resends: number }
  | {
      readonly phase: 'unknown'
      readonly snapshot: AttemptSnapshot<P>
      readonly reason: AttemptUnknownReason
      readonly resends: number
    }
  /**
   * Explicitly abandoned while UNKNOWN. Not frozen — the form is editable and a
   * different payload may start a new attempt — but the unresolved identity is
   * kept so that the unchanged payload cannot be re-sent under a new key.
   */
  | {
      readonly phase: 'abandoned'
      readonly snapshot: AttemptSnapshot<P>
      readonly reason: AttemptUnknownReason
      readonly resends: number
    }
  | { readonly phase: 'failed'; readonly snapshot: AttemptSnapshot<P>; readonly failure: AttemptFailure }
  | {
      readonly phase: 'completed'
      readonly snapshot: AttemptSnapshot<P>
      readonly result: R
      readonly replayed: boolean
    }

/** Contract: trim → 1..128 characters, no comma, not blank. */
export const MAX_ATTEMPT_KEY_LENGTH = 128

/**
 * 128 random bits as 32 lowercase hex characters. `getRandomValues` rather than
 * `randomUUID` because the latter is only defined in a secure context, and a
 * plain-HTTP intranet host must not fall back to something weaker silently.
 */
export function generateAttemptKey(source: Pick<Crypto, 'getRandomValues'> = globalThis.crypto): string {
  const bytes = new Uint8Array(16)
  source.getRandomValues(bytes)
  let key = ''
  for (const byte of bytes) key += byte.toString(16).padStart(2, '0')
  return key
}

export function isValidAttemptKey(key: string): boolean {
  return (
    key === key.trim() &&
    key.length >= 1 &&
    key.length <= MAX_ATTEMPT_KEY_LENGTH &&
    !key.includes(',')
  )
}

export type CommandResolution<R> =
  | { readonly kind: 'completed'; readonly result: R; readonly replayed: boolean }
  | { readonly kind: 'unknown'; readonly reason: AttemptUnknownReason }
  | { readonly kind: 'failed'; readonly failure: AttemptFailure }

/**
 * Map one transport outcome to what it means for the attempt. Branches on the
 * HTTP status and the STABLE code only — never on localized title/detail.
 */
export function classifyCommandOutcome<R>(outcome: CommandOutcome<R>): CommandResolution<R> {
  switch (outcome.type) {
    case 'contract-not-ready':
      // Nothing was sent, so this is definite: not an unknown outcome.
      return { kind: 'failed', failure: 'contract-not-ready' }
    case 'unacknowledged':
      return { kind: 'unknown', reason: 'unacknowledged' }
    case 'created':
      return { kind: 'completed', result: outcome.data, replayed: outcome.replayed }
    case 'offline':
      return { kind: 'unknown', reason: 'offline' }
    case 'network-error':
    case 'aborted':
      return { kind: 'unknown', reason: 'network-error' }
    case 'malformed':
      return { kind: 'unknown', reason: 'malformed-response' }
    case 'http':
      return classifyHttp(outcome.status, outcome.code)
    default:
      return { kind: 'unknown', reason: 'malformed-response' }
  }
}

function classifyHttp<R>(status: number, code: string | null): CommandResolution<R> {
  // The platform did not answer for the command. Same key on retry.
  if (status >= 500) return { kind: 'unknown', reason: 'service-unavailable' }
  // A 2xx that is not 201 is outside the contract; the record may exist.
  if (status >= 200 && status < 300) return { kind: 'unknown', reason: 'malformed-response' }

  switch (status) {
    case 400:
      if (code === PROBLEM_CODE.validationFailed) return { kind: 'failed', failure: 'validation' }
      if (
        code === PROBLEM_CODE.commandMetadataInvalid ||
        code === PROBLEM_CODE.correlationMetadataInvalid ||
        code === PROBLEM_CODE.companyContextInvalid
      ) {
        return { kind: 'failed', failure: 'metadata' }
      }
      return { kind: 'failed', failure: 'unexpected' }
    case 401:
      return { kind: 'failed', failure: 'unauthenticated' }
    case 403:
      return { kind: 'failed', failure: 'forbidden' }
    case 404:
      return { kind: 'failed', failure: 'not-found' }
    case 409:
      if (code === PROBLEM_CODE.alreadyLinked) return { kind: 'failed', failure: 'already-linked' }
      if (code === PROBLEM_CODE.idempotencyConflict) return { kind: 'failed', failure: 'idempotency-conflict' }
      // `version_conflict` belongs to the versioned update flow, not to create/link.
      return { kind: 'failed', failure: 'unexpected' }
    default:
      // 415 and any other definite rejection: safe technical error, no business effect.
      return { kind: 'failed', failure: 'unexpected' }
  }
}

export function beginAttempt<P, R>(snapshot: AttemptSnapshot<P>): AttemptState<P, R> {
  return { phase: 'submitting', snapshot, resends: 0 }
}

/**
 * Same snapshot, same key. Only an unresolved attempt — UNKNOWN, or ABANDONED
 * while unknown — can be retried; a definite failure or a completion cannot.
 */
export function retryAttempt<P, R>(state: AttemptState<P, R>): AttemptState<P, R> {
  if (state.phase !== 'unknown' && state.phase !== 'abandoned') return state
  return { phase: 'submitting', snapshot: state.snapshot, resends: state.resends + 1 }
}

/**
 * Explicit abandonment / acknowledgement / reset:
 *  - UNKNOWN → `abandoned`: the outcome is still unresolved, so the snapshot and
 *    its key are kept and the unchanged payload stays bound to them;
 *  - `abandoned` stays as it is (the identity is not dropped by asking twice);
 *  - a request in flight cannot be abandoned (the hook also refuses);
 *  - a definite failure or a completion is closed for good → `idle`.
 */
export function abandonAttempt<P, R>(state: AttemptState<P, R>): AttemptState<P, R> {
  switch (state.phase) {
    case 'unknown':
      return { phase: 'abandoned', snapshot: state.snapshot, reason: state.reason, resends: state.resends }
    case 'abandoned':
    case 'submitting':
      return state
    default:
      return { phase: 'idle' }
  }
}

/**
 * True when a NEW attempt may be started: nothing in flight, nothing frozen,
 * nothing unacknowledged. An abandoned attempt does not block a new one — a
 * changed payload is a different business attempt — but the unchanged payload
 * is caught by `repeatsUnresolvedAttempt`.
 */
export function canBeginAttempt<P, R>(state: AttemptState<P, R>): boolean {
  return state.phase === 'idle' || state.phase === 'abandoned'
}

/**
 * Structural equality of two normalized payloads (primitives, null, arrays and
 * plain objects; key order is irrelevant). This is what "unchanged" means: the
 * server fingerprints the body, not the object identity.
 */
export function samePayload(a: unknown, b: unknown): boolean {
  if (Object.is(a, b)) return true
  if (typeof a !== 'object' || typeof b !== 'object' || a === null || b === null) return false
  if (Array.isArray(a) || Array.isArray(b)) {
    if (!Array.isArray(a) || !Array.isArray(b) || a.length !== b.length) return false
    return a.every((item, index) => samePayload(item, b[index]))
  }
  const left = a as Record<string, unknown>
  const right = b as Record<string, unknown>
  const keys = Object.keys(left)
  if (keys.length !== Object.keys(right).length) return false
  return keys.every((key) => Object.hasOwn(right, key) && samePayload(left[key], right[key]))
}

/**
 * True when submitting `payload` for `operation` would repeat an abandoned
 * attempt whose outcome is still unknown. That submit must not mint a new key:
 * the user either retries the kept snapshot under its own key or changes the
 * payload (§4.3: "değiştirilmiş payload için yeni attempt").
 */
export function repeatsUnresolvedAttempt<P, R>(state: AttemptState<P, R>, operation: string, payload: P): boolean {
  return (
    state.phase === 'abandoned' &&
    state.snapshot.operation === operation &&
    samePayload(state.snapshot.payload, payload)
  )
}

export function settleAttempt<P, R>(state: AttemptState<P, R>, outcome: CommandOutcome<R>): AttemptState<P, R> {
  if (state.phase !== 'submitting') return state
  const resolution = classifyCommandOutcome(outcome)
  switch (resolution.kind) {
    case 'completed':
      return {
        phase: 'completed',
        snapshot: state.snapshot,
        result: resolution.result,
        replayed: resolution.replayed,
      }
    case 'unknown':
      return { phase: 'unknown', snapshot: state.snapshot, reason: resolution.reason, resends: state.resends }
    case 'failed':
      return { phase: 'failed', snapshot: state.snapshot, failure: resolution.failure }
    default:
      return state
  }
}

/** True while the snapshot is frozen: a request is in flight or its outcome is unknown. */
export function isAttemptFrozen<P, R>(state: AttemptState<P, R>): boolean {
  return state.phase === 'submitting' || state.phase === 'unknown'
}
