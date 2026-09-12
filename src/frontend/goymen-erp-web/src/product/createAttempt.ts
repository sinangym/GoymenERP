/**
 * The logical U-02 create attempt — framework-independent.
 *
 * THE RULE, in one line: one idempotency key per submitted SNAPSHOT, never per click.
 *
 *  - No key exists at mount, while typing, or on a click that fails client-side
 *    validation. The first client-valid submit freezes the normalized payload and
 *    mints ONE key for it.
 *  - A double click, a timeout, a dropped connection, a 5xx or a malformed 201
 *    leaves the outcome UNKNOWN: the Draft may exist. The user retries the SAME
 *    snapshot with the SAME key — the server replays instead of creating a second
 *    product — or edits the form, which is what makes it a new attempt. Nothing
 *    here ever resends on its own and nothing here ever rotates the key under an
 *    unchanged payload.
 *  - A DEFINITE non-success answer (400/401/403/409/…) closes the attempt. A later
 *    submit is a new business attempt with a new key.
 *  - Success, replayed or not, completes the attempt exactly once.
 *
 * This is a smaller sibling of the Cari `commandAttempt` state machine and follows
 * the same rule for the same reason. It is a separate module rather than an import
 * because `src/cari/**` has its own single writer, and because the Cari machine
 * carries link-flow and client-contract states this slice has no server for.
 *
 * The raw key lives in component memory only: never in storage, a URL, a log or
 * telemetry.
 */

import { PLATFORM_PROBLEM_CODE, PRODUCT_PROBLEM_CODE } from './contract'
import type { CreateOutcome, CreateProductDraftPayload } from './api'
import type { ProductDraftCreated } from './contract'

/** A definite, non-success answer. */
export type AttemptFailure =
  | 'validation'
  | 'code-collision'
  | 'metadata'
  | 'unauthenticated'
  | 'forbidden'
  | 'idempotency-conflict'
  | 'unexpected'

/** Why an outcome is unknown. Every one of these is retryable with the SAME key. */
export type AttemptUnknownReason =
  | 'offline'
  | 'network-error'
  | 'service-unavailable'
  | 'malformed-response'

export interface AttemptSnapshot {
  /** The single Idempotency-Key for this snapshot. */
  readonly key: string
  /** The normalized payload exactly as sent. */
  readonly payload: CreateProductDraftPayload
}

export type AttemptState =
  | { readonly phase: 'idle' }
  | { readonly phase: 'submitting'; readonly snapshot: AttemptSnapshot }
  | {
      readonly phase: 'unknown'
      readonly snapshot: AttemptSnapshot
      readonly reason: AttemptUnknownReason
    }
  | {
      readonly phase: 'failed'
      readonly snapshot: AttemptSnapshot
      readonly failure: AttemptFailure
      /** The server's field errors, when it sent any. */
      readonly fieldErrors: Readonly<Record<string, string>>
    }
  | {
      readonly phase: 'completed'
      readonly snapshot: AttemptSnapshot
      readonly result: ProductDraftCreated
      readonly replayed: boolean
    }

/** The contract bound: trim → 1..128 characters, not blank. */
export const MAX_ATTEMPT_KEY_LENGTH = 128

/**
 * 128 random bits as 32 lowercase hex characters.
 *
 * `getRandomValues` rather than `randomUUID`: the latter is only defined in a
 * secure context, and a plain-HTTP intranet host must not silently fall back to
 * something weaker for a value whose whole job is to be unguessable and unique.
 */
export function generateAttemptKey(
  source: Pick<Crypto, 'getRandomValues'> = globalThis.crypto,
): string {
  const bytes = new Uint8Array(16)
  source.getRandomValues(bytes)
  let key = ''
  for (const byte of bytes) key += byte.toString(16).padStart(2, '0')
  return key
}

/**
 * Whether two payloads are the same submitted snapshot.
 *
 * Compared on the NORMALIZED payload — the exact object the transport would send —
 * so re-typing the same name with different surrounding whitespace is the same
 * attempt, not a new one that would consume a second key and risk a duplicate.
 */
export function isSamePayload(
  a: CreateProductDraftPayload,
  b: CreateProductDraftPayload,
): boolean {
  return JSON.stringify(normalizeForCompare(a)) === JSON.stringify(normalizeForCompare(b))
}

function normalizeForCompare(payload: CreateProductDraftPayload) {
  // Key order is part of `JSON.stringify`, and the roles object is built by
  // iterating a constant array — but the classification object is written by hand
  // in two places, so the comparison pins its shape rather than trusting that.
  return {
    name: payload.name,
    class: payload.classification.class,
    group: payload.classification.group,
    family: payload.classification.family,
    roles: payload.roles,
    trackingPolicy: payload.trackingPolicy,
  }
}

/**
 * Classify a transport outcome into the attempt's next state.
 *
 * The split that matters: `http` is a DEFINITE answer and closes the attempt,
 * while everything else leaves the record's existence unknown and keeps the key.
 * A malformed 201 is deliberately on the unknown side — the server said it created
 * something, we just could not read the description.
 */
export function nextState(snapshot: AttemptSnapshot, outcome: CreateOutcome): AttemptState {
  switch (outcome.type) {
    case 'created':
      return {
        phase: 'completed',
        snapshot,
        result: outcome.data,
        replayed: outcome.replayed,
      }
    case 'offline':
      return { phase: 'unknown', snapshot, reason: 'offline' }
    case 'network-error':
      return { phase: 'unknown', snapshot, reason: 'network-error' }
    case 'malformed':
      return { phase: 'unknown', snapshot, reason: 'malformed-response' }
    case 'aborted':
      // Our own cancellation. The request may still have been processed, so the
      // attempt stays unresolved rather than silently disappearing.
      return { phase: 'unknown', snapshot, reason: 'network-error' }
    case 'http': {
      // A 5xx is the server failing to answer, not an answer. The key is kept.
      if (outcome.status >= 500) {
        return { phase: 'unknown', snapshot, reason: 'service-unavailable' }
      }
      return {
        phase: 'failed',
        snapshot,
        failure: classifyFailure(outcome.status, outcome.code),
        fieldErrors: {},
      }
    }
    default:
      return { phase: 'unknown', snapshot, reason: 'malformed-response' }
  }
}

function classifyFailure(status: number, code: string | null): AttemptFailure {
  if (code === PRODUCT_PROBLEM_CODE.productCodeCollision) return 'code-collision'
  if (code === PRODUCT_PROBLEM_CODE.validationFailed) return 'validation'
  if (code === PRODUCT_PROBLEM_CODE.idempotencyConflict) return 'idempotency-conflict'
  if (code === PRODUCT_PROBLEM_CODE.commandMetadataInvalid) return 'metadata'
  if (code === PLATFORM_PROBLEM_CODE.correlationMetadataInvalid) return 'metadata'
  if (code === PLATFORM_PROBLEM_CODE.authenticationRequired) return 'unauthenticated'
  if (code === PLATFORM_PROBLEM_CODE.forbidden) return 'forbidden'
  /**
   * Fall back on the STATUS when the code is missing or not one we know.
   *
   * The Product problem codes are proposed, not accepted (see ./contract.ts), so a
   * server answering 409 with a code this build has never heard of is expected
   * rather than exceptional. Falling back keeps the screen correct — the user still
   * learns they are not authorized, or that the request was rejected — instead of
   * showing "unexpected error" for a perfectly ordinary refusal.
   */
  if (status === 401) return 'unauthenticated'
  if (status === 403) return 'forbidden'
  if (status === 400) return 'validation'
  if (status === 409) return 'idempotency-conflict'
  return 'unexpected'
}

/** Whether a state leaves the form frozen. */
export function isFormFrozen(state: AttemptState): boolean {
  return state.phase === 'submitting' || state.phase === 'completed'
}

/**
 * Whether the user still has unsaved work.
 *
 * `completed` is the only phase that is NOT unsaved. `unknown` deliberately counts
 * as unsaved: the Draft may exist, but nothing has confirmed it, and letting the
 * user walk away silently is how an unresolved write is forgotten.
 */
export function hasUnresolvedWork(state: AttemptState, formDirty: boolean): boolean {
  if (state.phase === 'completed') return false
  if (state.phase === 'unknown' || state.phase === 'submitting') return true
  return formDirty
}
