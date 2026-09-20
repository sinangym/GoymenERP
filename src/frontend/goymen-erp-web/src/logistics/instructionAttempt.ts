/**
 * One logical command attempt on a loading instruction — pure, framework-independent. The Sales
 * lane's `orderAttempt` rules (SALES_ORDER_DRAFT_JIT_SPEC_v1 §3, ADR-011), copied into this module
 * on purpose: the module boundary forbids importing `src/sales/**`, and a company-scoped loading
 * instruction carries neither the Cari ACK protocol nor the Product global-master policy.
 *
 * THE RULES:
 *  - one Idempotency-Key per submitted SNAPSHOT (normalized payload + path + binding), minted when
 *    the first client-valid submit freezes it — never per click;
 *  - no response, a 5xx, a 2xx that is not the pinned success, or a success body the decoder
 *    refused: the record MAY exist. The outcome is unknown and the key is kept;
 *  - while an outcome is unknown, a DIFFERENT payload does not quietly open a second attempt with
 *    a second key. The person retries the same snapshot, or explicitly leaves it (`abandon`);
 *  - an abandoned snapshot is still unresolved: submitting the SAME payload again reuses its key,
 *    a changed payload is the person's explicit new attempt;
 *  - a definite 4xx closes the attempt — unless it answered a retry of an unknown outcome, in which
 *    case it says nothing about the first send, and the snapshot stays live;
 *  - an outcome is applied only to the attempt AND binding that sent it. A late answer from a
 *    company the person has left writes nothing.
 *
 * Memory only: the raw key and payload never reach storage, a URL or a log.
 */

import type { ShellState } from '../shell/shellState'
import type { LogisticsProblem } from './contract'

export type AttemptFailure = 'validation' | 'unauthenticated' | 'forbidden' | 'not-found' | 'conflict' | 'unexpected'

export type AttemptUnknownReason = 'offline' | 'network-error' | 'service-unavailable' | 'malformed-response'

/** What the transport made of one send. `succeeded` means the pinned success status AND a body the decoder accepted. */
export type CommandOutcome<R> =
  | { readonly type: 'succeeded'; readonly data: R }
  | { readonly type: 'http'; readonly status: number; readonly problem: LogisticsProblem | null }
  | { readonly type: 'offline' }
  | { readonly type: 'network-error' }
  | { readonly type: 'malformed' }
  | { readonly type: 'aborted' }

export interface AttemptSnapshot<P> {
  readonly key: string
  /** The request path exactly as sent. */
  readonly path: string
  /** Canonical binding string (see `attemptBinding`). */
  readonly binding: string
  /** The company the attempt was made for; travels as request context, never in `payload`. */
  readonly companyId: string
  readonly payload: P
}

export type AttemptState<P, R> =
  | { readonly phase: 'idle' }
  | { readonly phase: 'submitting'; readonly snapshot: AttemptSnapshot<P> }
  | { readonly phase: 'unknown'; readonly snapshot: AttemptSnapshot<P>; readonly reason: AttemptUnknownReason }
  | {
      readonly phase: 'failed'
      readonly snapshot: AttemptSnapshot<P>
      readonly failure: AttemptFailure
      readonly problem: LogisticsProblem | null
      /** Set when this refusal answered a retry of an outcome that was already unknown. */
      readonly unresolvedSince?: AttemptUnknownReason
    }
  | { readonly phase: 'abandoned'; readonly snapshot: AttemptSnapshot<P>; readonly reason: AttemptUnknownReason }
  | { readonly phase: 'completed'; readonly snapshot: AttemptSnapshot<P>; readonly result: R }

/**
 * Who and where an attempt belongs to: actor, session, company, screen scope. Null when there is
 * no committed SINGLE-company context: a group view is not a write target.
 */
export function attemptBinding(shell: ShellState, scope: string): string | null {
  const context = shell.activeContext
  if (context === null || context.scopeMode !== 'single' || context.companyId === null) return null
  return JSON.stringify([context.userId, String(shell.sessionEpoch), context.companyId, scope])
}

export type SubmitDecision<P> =
  | { readonly type: 'send'; readonly snapshot: AttemptSnapshot<P>; readonly reusedKey: boolean }
  | { readonly type: 'blocked'; readonly reason: 'in-flight' | 'completed' | 'unresolved-other-payload' | 'context-changed' }

/** Decide what a submit of `payload` may do. Sends nothing itself; `newKey` runs only when a new attempt really starts. */
export function decideSubmit<P, R>(
  state: AttemptState<P, R>,
  request: { readonly path: string; readonly binding: string; readonly companyId: string; readonly payload: P },
  newKey: () => string,
): SubmitDecision<P> {
  if (state.phase === 'submitting') return { type: 'blocked', reason: 'in-flight' }
  if (state.phase === 'completed') return { type: 'blocked', reason: 'completed' }

  const live = liveSnapshot(state)
  if (live !== null) {
    if (live.binding !== request.binding || live.companyId !== request.companyId) {
      return { type: 'blocked', reason: 'context-changed' }
    }
    if (live.path === request.path && isSamePayload(live.payload, request.payload)) {
      return { type: 'send', snapshot: live, reusedKey: true }
    }
    if (state.phase !== 'abandoned') return { type: 'blocked', reason: 'unresolved-other-payload' }
  }

  return { type: 'send', snapshot: { ...request, key: newKey() }, reusedKey: false }
}

/** The attempt that is now in flight. */
export function beginSend<P, R>(snapshot: AttemptSnapshot<P>): AttemptState<P, R> {
  return { phase: 'submitting', snapshot }
}

/**
 * Apply one transport outcome. `previous` is the state BEFORE this send; it is what lets a refused
 * retry keep saying "still unknown" about the send before it. Returns `current` untouched when the
 * outcome is not for the attempt in flight under this binding.
 */
export function applyOutcome<P, R>(
  current: AttemptState<P, R>,
  sent: { readonly key: string; readonly binding: string },
  currentBinding: string | null,
  outcome: CommandOutcome<R>,
  previous: AttemptState<P, R>,
): AttemptState<P, R> {
  if (current.phase !== 'submitting') return current
  if (current.snapshot.key !== sent.key || current.snapshot.binding !== sent.binding) return current
  if (currentBinding !== sent.binding) return current

  const snapshot = current.snapshot
  const unresolvedSince =
    previous.phase === 'unknown' || previous.phase === 'abandoned'
      ? previous.reason
      : previous.phase === 'failed'
        ? previous.unresolvedSince
        : undefined
  const unknown = (reason: AttemptUnknownReason): AttemptState<P, R> => ({
    phase: 'unknown',
    snapshot,
    reason: unresolvedSince ?? reason,
  })

  switch (outcome.type) {
    case 'succeeded':
      return { phase: 'completed', snapshot, result: outcome.data }
    case 'offline':
      return unknown('offline')
    case 'network-error':
    case 'aborted':
      return unknown('network-error')
    case 'malformed':
      return unknown('malformed-response')
    case 'http':
      if (outcome.status >= 500) return unknown('service-unavailable')
      if (outcome.status >= 200 && outcome.status < 300) return unknown('malformed-response')
      return {
        phase: 'failed',
        snapshot,
        failure: failureForStatus(outcome.status),
        problem: outcome.problem,
        ...(unresolvedSince !== undefined ? { unresolvedSince } : {}),
      }
    default:
      return unknown('malformed-response')
  }
}

/** The class of a definite refusal. The stable `code` refines the MESSAGE, never the class. */
function failureForStatus(status: number): AttemptFailure {
  if (status === 400 || status === 422) return 'validation'
  if (status === 401) return 'unauthenticated'
  if (status === 403) return 'forbidden'
  if (status === 404) return 'not-found'
  if (status === 409) return 'conflict'
  return 'unexpected'
}

/** Explicitly leave the attempt. An unresolved snapshot is kept, never forgotten. */
export function abandon<P, R>(state: AttemptState<P, R>): AttemptState<P, R> {
  if (state.phase === 'submitting') return state
  if (state.phase === 'unknown') return { phase: 'abandoned', snapshot: state.snapshot, reason: state.reason }
  if (state.phase === 'failed' && state.unresolvedSince !== undefined) {
    return { phase: 'abandoned', snapshot: state.snapshot, reason: state.unresolvedSince }
  }
  if (state.phase === 'abandoned') return state
  return { phase: 'idle' }
}

/** The snapshot a resubmission of the same payload must reuse, or null. */
export function liveSnapshot<P, R>(state: AttemptState<P, R>): AttemptSnapshot<P> | null {
  if (state.phase === 'unknown' || state.phase === 'abandoned') return state.snapshot
  if (state.phase === 'failed' && state.unresolvedSince !== undefined) return state.snapshot
  return null
}

/** A screen must not say "not saved" while this holds. */
export function isOutcomeStillUnknown<P, R>(state: AttemptState<P, R>): boolean {
  return state.phase === 'submitting' || liveSnapshot(state) !== null
}

/** 128 random bits as 32 hex characters; `getRandomValues` works outside a secure context too. */
export function generateAttemptKey(source: Pick<Crypto, 'getRandomValues'> = globalThis.crypto): string {
  const bytes = new Uint8Array(16)
  source.getRandomValues(bytes)
  return Array.from(bytes, (byte) => byte.toString(16).padStart(2, '0')).join('')
}

/** A client-owned record id (a line id, a load id) in canonical lowercase UUID form; `randomUUID` needs a secure context, so it is built from bytes. */
export function generateClientId(source: Pick<Crypto, 'getRandomValues'> = globalThis.crypto): string {
  const bytes = new Uint8Array(16)
  source.getRandomValues(bytes)
  bytes[6] = (bytes[6] & 0x0f) | 0x40
  bytes[8] = (bytes[8] & 0x3f) | 0x80
  const hex = Array.from(bytes, (byte) => byte.toString(16).padStart(2, '0')).join('')
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`
}

/** Same business snapshot regardless of object key order. */
export function isSamePayload(a: unknown, b: unknown): boolean {
  return stableStringify(a) === stableStringify(b)
}

function stableStringify(value: unknown): string {
  if (value === null || typeof value !== 'object') return JSON.stringify(value) ?? 'null'
  if (Array.isArray(value)) return `[${value.map(stableStringify).join(',')}]`
  const entries = Object.entries(value as Record<string, unknown>)
    .filter(([, v]) => v !== undefined)
    .sort(([x], [y]) => (x < y ? -1 : x > y ? 1 : 0))
  return `{${entries.map(([k, v]) => `${JSON.stringify(k)}:${stableStringify(v)}`).join(',')}}`
}
