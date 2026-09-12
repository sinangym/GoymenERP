/**
 * The logical attempt behind one reasoned activate/deactivate.
 *
 * THE RULE, in one line: one Idempotency-Key per submitted SNAPSHOT, never per click.
 *
 * This is the same discipline `../cari/commandAttempt.ts` applies to C-02, written
 * small for a single command — NOT a port of it. The Cari machine carries
 * `contract-not-ready`, `already-linked` and an ACK-driven write gate, none of which
 * exist on this surface.
 *
 * Why a key at all: a double click, a dropped connection or a 5xx leaves the outcome
 * UNKNOWN — the change may already be recorded. Re-sending the SAME key lets the
 * server replay its terminal answer instead of writing a second time; minting a new
 * one would ask for a second command. So:
 *
 *  - no key exists at mount, while typing, or on a submit that fails client-side
 *    validation. The first valid submit freezes a snapshot and mints ONE key for it;
 *  - an unknown outcome keeps the snapshot AND the key, and only an explicit user
 *    action re-sends. Nothing here re-sends on its own;
 *  - a DEFINITE answer (204, 400, 401, 403, 404, 409, 415) closes the attempt. A
 *    later submit is a new attempt with a new key — which is correct, because after a
 *    version conflict the user must re-read and submit a DIFFERENT `expectedVersion`,
 *    and that is a different command;
 *  - the raw key lives in component memory only: never in storage, the URL or a log.
 */

import type {
  CommandFailure,
  CommandOutcome,
  CommandUnknown,
  SetActiveStatePayload,
} from './commandApi'

export interface AttemptSnapshot {
  /** The single Idempotency-Key for this snapshot. */
  readonly key: string
  /**
   * Actor / company / location / shell-context this attempt was authorized under.
   * A snapshot is only ever re-sent under the binding it was created in — readiness
   * in one company context is not readiness in another.
   */
  readonly binding: string
  /** The normalized payload exactly as it will be sent. */
  readonly payload: SetActiveStatePayload
}

export type AttemptState =
  | { readonly phase: 'idle' }
  | {
      readonly phase: 'submitting'
      readonly snapshot: AttemptSnapshot
      readonly resends: number
      /** Set when this send is a RE-send of an already-unknown attempt. */
      readonly unresolvedSince?: CommandUnknown
    }
  | {
      readonly phase: 'unknown'
      readonly snapshot: AttemptSnapshot
      readonly reason: CommandUnknown
      readonly resends: number
      /**
       * A later request was refused while THIS attempt is still unresolved. Shown, but
       * it does not close the attempt: a retry turned away at the auth or validation
       * gate never ran the command, so it is no evidence about the first request that
       * did. The key and snapshot stay.
       */
      readonly lastRefusal?: CommandFailure
    }
  | {
      readonly phase: 'failed'
      readonly snapshot: AttemptSnapshot
      readonly failure: CommandFailure
    }
  | {
      readonly phase: 'applied'
      readonly snapshot: AttemptSnapshot
      readonly replayed: boolean
      readonly originalCorrelationId: string | null
    }

export type AttemptEvent =
  /** A client-valid submit of a frozen snapshot. */
  | { readonly type: 'SUBMIT'; readonly snapshot: AttemptSnapshot }
  /** An explicit user re-send of the SAME unresolved snapshot. */
  | { readonly type: 'RESEND' }
  | { readonly type: 'SETTLE'; readonly outcome: CommandOutcome; readonly key: string }
  /** The user abandoned/closed the form. */
  | { readonly type: 'RESET' }

export const initialAttemptState: AttemptState = { phase: 'idle' }

/**
 * Identity of an attempt's business content. Two submits with the same fingerprint in
 * the same binding are the SAME attempt and must not get a second key.
 *
 * `reason` is part of it: the reason is stored on the audit record, so changing the
 * justification changes what is being recorded, and that is a different command.
 */
export function attemptFingerprint(
  binding: string,
  payload: SetActiveStatePayload,
): string {
  return JSON.stringify([
    binding,
    payload.isActive,
    payload.expectedVersion,
    payload.reason.trim(),
  ])
}

/** The snapshot currently carrying an unresolved key, if any. */
export function unresolvedSnapshot(state: AttemptState): AttemptSnapshot | null {
  return state.phase === 'submitting' || state.phase === 'unknown' ? state.snapshot : null
}

/** True while a request is in flight. */
export function isBusy(state: AttemptState): boolean {
  return state.phase === 'submitting'
}

/**
 * True while an attempt holds a key whose outcome is not settled.
 *
 * The form MUST stay frozen in this state. Editing the justification would change the
 * payload fingerprint, and a fingerprint change is what mints a new key — which is
 * exactly how a committed-but-unacknowledged command becomes a SECOND command (and,
 * against a freshly-read state, possibly the opposite activation). The only ways out
 * are the same-key resend or an explicit abandon.
 */
export function isUnresolved(state: AttemptState): boolean {
  return state.phase === 'submitting' || state.phase === 'unknown'
}

export function attemptReducer(state: AttemptState, event: AttemptEvent): AttemptState {
  switch (event.type) {
    case 'SUBMIT': {
      // A submit while one is in flight is a double click. Ignored — it must never
      // start a second request or mint a second key.
      if (state.phase === 'submitting') return state
      return { phase: 'submitting', snapshot: event.snapshot, resends: 0 }
    }

    case 'RESEND': {
      // Only an UNKNOWN attempt may be re-sent, and only under its own key.
      if (state.phase !== 'unknown') return state
      return {
        phase: 'submitting',
        snapshot: state.snapshot,
        resends: state.resends + 1,
        unresolvedSince: state.reason,
      }
    }

    case 'SETTLE': {
      if (state.phase !== 'submitting') return state
      /**
       * A settle for a key we are no longer sending is a late response from a
       * superseded attempt. Dropping it is the same fence rule the read path uses:
       * a stale result never becomes UI state.
       */
      if (state.snapshot.key !== event.key) return state

      if (event.outcome.type === 'applied') {
        return {
          phase: 'applied',
          snapshot: state.snapshot,
          replayed: event.outcome.replayed,
          originalCorrelationId: event.outcome.originalCorrelationId,
        }
      }

      if (event.outcome.type === 'failed') {
        /**
         * A refusal only CLOSES this attempt when it is the attempt's own outcome.
         *
         * If nothing was unresolved yet, this response is the answer to the only
         * request we made — definite. But once an earlier send has gone unresolved, the
         * first PUT may already be recorded, and a later 401/403/400/415 is the auth or
         * validation gate turning away THAT request before it ran. It proves nothing
         * about the first one, so treating it as "uygulanmadı" would be a false
         * reassurance AND would free the form to mint a second key.
         *
         * The exception is a REPLAY: the server replays a stored terminal answer for a
         * repeated key, so a replayed 404/409 IS the original command's own outcome and
         * does resolve it.
         */
        const unresolvedSince = state.unresolvedSince
        if (unresolvedSince === undefined || event.outcome.replayed) {
          return { phase: 'failed', snapshot: state.snapshot, failure: event.outcome.failure }
        }
        return {
          phase: 'unknown',
          snapshot: state.snapshot,
          reason: unresolvedSince,
          resends: state.resends,
          lastRefusal: event.outcome.failure,
        }
      }

      /**
       * Unknown. The FIRST uncertainty is the one that matters: if a re-send of an
       * already-unknown attempt itself fails, the original uncertainty still stands
       * and is what the user is told about.
       */
      return {
        phase: 'unknown',
        snapshot: state.snapshot,
        reason: state.unresolvedSince ?? event.outcome.unknown,
        resends: state.resends,
      }
    }

    case 'RESET':
      return initialAttemptState

    default:
      return state
  }
}
