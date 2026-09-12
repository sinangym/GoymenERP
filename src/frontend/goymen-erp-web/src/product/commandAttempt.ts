/**
 * The logical U-02 write attempt — framework-independent, shared by Draft create
 * and Draft basic edit.
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
import type { CommandOutcome } from './api'

/** A definite, non-success answer. */
export type AttemptFailure =
  | 'validation'
  | 'metadata'
  | 'unauthenticated'
  | 'forbidden'
  | 'idempotency-conflict'
  /**
   * §19.5: the `expectedVersion` we held is no longer current. Kept apart from
   * `idempotency-conflict` because the user-facing rule differs — a stale edit shows
   * the current record and waits for the person to review it, and must never be
   * rebased or overwritten on their behalf.
   */
  | 'stale-version'
  /** Basic edit was aimed at a record that is no longer a Draft. */
  | 'lifecycle-conflict'
  | 'not-found'
  | 'unexpected'

/** Why an outcome is unknown. Every one of these is retryable with the SAME key. */
export type AttemptUnknownReason =
  | 'offline'
  | 'network-error'
  | 'service-unavailable'
  | 'malformed-response'

export interface AttemptSnapshot<P> {
  /** The single Idempotency-Key for this snapshot. */
  readonly key: string
  /** The normalized payload exactly as sent. */
  readonly payload: P
  /**
   * The shell context the attempt was authorized under.
   *
   * §19.4: "Origin değişirse eski girişim otomatik taşınmaz." An attempt belongs to
   * the actor, company context and session it started in; carrying it into a new one
   * would replay a write under authority that was never granted there.
   */
  readonly namespace: string
}

export type AttemptState<P, R> =
  | { readonly phase: 'idle' }
  | { readonly phase: 'submitting'; readonly snapshot: AttemptSnapshot<P> }
  | {
      readonly phase: 'unknown'
      readonly snapshot: AttemptSnapshot<P>
      readonly reason: AttemptUnknownReason
    }
  | {
      readonly phase: 'failed'
      readonly snapshot: AttemptSnapshot<P>
      readonly failure: AttemptFailure
      /** The server's field errors, when it sent any. */
      readonly fieldErrors: Readonly<Record<string, string>>
      /**
       * Set when this refusal answered a RETRY of an attempt whose outcome was
       * already unknown.
       *
       * It is the difference between "your change was not applied" and "we still do
       * not know whether your change was applied, and this retry was refused". The
       * second is the truth when a write committed, its response was lost, and the
       * caller's authority was then revoked: the backend must re-authorize before it
       * can read a stored result, so the refusal is a denial of REPLAY ACCESS, never
       * evidence that the first write rolled back.
       *
       * Carrying it also keeps the snapshot alive, so resubmitting the SAME payload
       * reuses the key instead of minting a second one against a record that may
       * already exist.
       */
      readonly unresolvedSince?: AttemptUnknownReason
    }
  | {
      readonly phase: 'completed'
      readonly snapshot: AttemptSnapshot<P>
      readonly result: R
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
export function isSamePayload<P>(a: P, b: P): boolean {
  return stableStringify(a) === stableStringify(b)
}

/**
 * JSON with object keys sorted at every depth.
 *
 * `JSON.stringify` preserves insertion order, so two payloads carrying identical
 * values would compare as different simply because one object literal was written
 * with its fields in another order. That difference would mint a second idempotency
 * key for the same business attempt — which is exactly how one click becomes two
 * products.
 */
function stableStringify(value: unknown): string {
  if (value === null || typeof value !== 'object') return JSON.stringify(value) ?? 'null'
  if (Array.isArray(value)) return `[${value.map(stableStringify).join(',')}]`
  const entries = Object.entries(value as Record<string, unknown>)
    .filter(([, v]) => v !== undefined)
    .sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0))
  return `{${entries.map(([k, v]) => `${JSON.stringify(k)}:${stableStringify(v)}`).join(',')}}`
}

/**
 * Classify a transport outcome into the attempt's next state.
 *
 * The split that matters: `http` is a DEFINITE answer and closes the attempt,
 * while everything else leaves the record's existence unknown and keeps the key.
 * A malformed 201 is deliberately on the unknown side — the server said it created
 * something, we just could not read the description.
 */
export function nextState<P, R>(
  snapshot: AttemptSnapshot<P>,
  outcome: CommandOutcome<R>,
  /**
   * What was known BEFORE this send. Passing it is what lets a refusal of a retry
   * stop short of claiming the earlier write did not happen.
   */
  previous?: AttemptState<P, R>,
): AttemptState<P, R> {
  const unresolvedSince =
    previous !== undefined && previous.phase === 'unknown'
      ? previous.reason
      : previous !== undefined && previous.phase === 'failed'
        ? previous.unresolvedSince
        : undefined

  switch (outcome.type) {
    case 'succeeded':
      // `isReplay` lives INSIDE the result in this contract, not beside it, so the
      // attempt state does not carry a second copy that could disagree with it.
      return { phase: 'completed', snapshot, result: outcome.data }
    case 'offline':
      return { phase: 'unknown', snapshot, reason: unresolvedSince ?? 'offline' }
    case 'network-error':
      return { phase: 'unknown', snapshot, reason: unresolvedSince ?? 'network-error' }
    case 'malformed':
      return { phase: 'unknown', snapshot, reason: unresolvedSince ?? 'malformed-response' }
    case 'aborted':
      // Our own cancellation. The request may still have been processed, so the
      // attempt stays unresolved rather than silently disappearing.
      return { phase: 'unknown', snapshot, reason: unresolvedSince ?? 'network-error' }
    case 'http': {
      // A 5xx is the server failing to answer, not an answer. The key is kept.
      if (outcome.status >= 500) {
        return { phase: 'unknown', snapshot, reason: unresolvedSince ?? 'service-unavailable' }
      }
      /**
       * A 2xx that is NOT the expected success status is uncertainty, not refusal.
       *
       * The server reported success, so calling it "failed" would tell the user
       * their change did not apply when it very likely did. A `204` is the live
       * case: §19.4 allows it and then requires a fresh GET for the new value and
       * version, and this client has neither — "Gönderilen taslak server sonucu gibi
       * boyanmaz". Treating it as unknown is honest and safe: the same key replays
       * rather than writing twice.
       */
      if (outcome.status >= 200 && outcome.status < 300) {
        return { phase: 'unknown', snapshot, reason: unresolvedSince ?? 'malformed-response' }
      }
      return {
        phase: 'failed',
        snapshot,
        failure: classifyFailure(outcome.status, outcome.code),
        fieldErrors: {},
        // A definite answer to THIS request. Whether it also settles the previous
        // one is a different question, and the answer is no.
        ...(unresolvedSince !== undefined ? { unresolvedSince } : {}),
      }
    }
    default:
      return { phase: 'unknown', snapshot, reason: 'malformed-response' }
  }
}

function classifyFailure(status: number, code: string | null): AttemptFailure {
  if (code === PRODUCT_PROBLEM_CODE.versionConflict) return 'stale-version'
  if (code === PRODUCT_PROBLEM_CODE.lifecycleConflict) return 'lifecycle-conflict'
  if (code === PRODUCT_PROBLEM_CODE.notFound) return 'not-found'
  if (code === PRODUCT_PROBLEM_CODE.validationFailed) return 'validation'
  if (code === PRODUCT_PROBLEM_CODE.idempotencyConflict) return 'idempotency-conflict'
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
  if (status === 404) return 'not-found'
  if (status === 400) return 'validation'
  if (status === 409) return 'idempotency-conflict'
  return 'unexpected'
}

/**
 * Whether the outcome of an earlier send is STILL unknown.
 *
 * True while the attempt is unknown, and true after a refusal that only answered a
 * retry. A screen must not say "not applied" while this holds.
 */
export function isOutcomeStillUnknown<P, R>(state: AttemptState<P, R>): boolean {
  if (state.phase === 'unknown' || state.phase === 'submitting') return true
  return state.phase === 'failed' && state.unresolvedSince !== undefined
}

/**
 * The snapshot a resubmission of the same payload must reuse.
 *
 * A plain failure closes its attempt — editing and resubmitting is a NEW business
 * attempt with a new key. But a failure that left an earlier write unresolved keeps
 * its snapshot: minting a second key there is exactly how one committed write
 * becomes two.
 */
export function liveSnapshot<P, R>(state: AttemptState<P, R>): AttemptSnapshot<P> | null {
  if (state.phase === 'unknown') return state.snapshot
  if (state.phase === 'failed' && state.unresolvedSince !== undefined) return state.snapshot
  return null
}

/** Whether a state leaves the form frozen. */
export function isFormFrozen<P, R>(state: AttemptState<P, R>): boolean {
  return state.phase === 'submitting' || state.phase === 'completed'
}

/**
 * Whether the user still has unsaved work.
 *
 * `completed` is the only phase that is NOT unsaved. `unknown` deliberately counts
 * as unsaved: the Draft may exist, but nothing has confirmed it, and letting the
 * user walk away silently is how an unresolved write is forgotten.
 */
export function hasUnresolvedWork<P, R>(
  state: AttemptState<P, R>,
  formDirty: boolean,
): boolean {
  if (state.phase === 'completed') return false
  // A refused retry still leaves the first write unaccounted for, so leaving the
  // screen silently would forget it just as surely as leaving during the unknown.
  if (isOutcomeStillUnknown(state)) return true
  return formDirty
}
