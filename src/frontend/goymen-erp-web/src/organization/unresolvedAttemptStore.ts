/**
 * Survival store for an UNRESOLVED activate/deactivate attempt.
 *
 * WHY THIS EXISTS AT ALL. The attempt lived in a `useReducer` inside the dialog's
 * section, and that section unmounts on perfectly ordinary events:
 *
 *   - the record re-read fails (503 / malformed), so the card renders a state block
 *     instead of the section — and a LATER successful read then mounted a fresh,
 *     `idle` section;
 *   - a command 401/403 is reported to the shared auth boundary, which closes every
 *     gated mount until a fresh capability snapshot lands.
 *
 * Both paths silently dropped the Idempotency-Key of a command that MAY ALREADY BE
 * RECORDED. That is the same loss P1-1 closed for the user-driven paths, arriving
 * through the component lifecycle instead — and it is worse, because nothing asked the
 * user. The next submit would then mint a second key against a freshly-read state,
 * i.e. possibly the opposite activation.
 *
 * WHAT THE KEY IS, AND WHY IT IS NOT THE SHELL NAMESPACE. Identity here is
 * `actor + company + location` and deliberately NOTHING else. The shell namespace
 * includes the capability generation, which is exactly what a 403 refence changes — so
 * keying on it would throw the attempt away in the very case we are trying to survive.
 * Session epoch is left out for the same reason.
 *
 * What the key DOES guarantee is the boundary the coordinator drew: an attempt never
 * travels to another user or another company. A different actor or a different company
 * simply does not match, so it reads as absent.
 *
 * This does NOT weaken the shell's safe close. The boundary still ends the session,
 * still drops the capability and still unmounts the feature; the only thing that
 * survives is our own record that a command of ours is outstanding — which is
 * information the user needs precisely BECAUSE the screen went away underneath it.
 *
 * In memory only: never storage, never the URL, never a log. A reload legitimately
 * loses it, exactly as the raw key must not outlive the tab.
 */

import type { CommandFailure, CommandUnknown } from './commandApi'
import type { AttemptSnapshot } from './activeStateAttempt'

export interface StoredAttempt {
  readonly snapshot: AttemptSnapshot
  readonly reason: CommandUnknown
  readonly resends: number
  readonly lastRefusal?: CommandFailure
}

/** `actor + company + location`. Structural, so no delimiter can collide. */
export function attemptStoreKey(
  actorId: string | null,
  companyId: string | null,
  locationId: string,
): string | null {
  if (actorId === null || companyId === null || locationId.length === 0) return null
  return JSON.stringify([actorId, companyId, locationId])
}

const store = new Map<string, StoredAttempt>()

export function rememberAttempt(key: string | null, attempt: StoredAttempt): void {
  if (key === null) return
  store.set(key, attempt)
}

export function recallAttempt(key: string | null): StoredAttempt | null {
  if (key === null) return null
  return store.get(key) ?? null
}

/** Called when the attempt reaches a terminal answer, or the user abandons it. */
export function forgetAttempt(key: string | null): void {
  if (key === null) return
  store.delete(key)
}

/** Test-only: the store is module state, so each case must start empty. */
export function clearAttemptStore(): void {
  store.clear()
}
