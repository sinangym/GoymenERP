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
 * `session + actor + company + location`.
 *
 * The shell namespace is deliberately NOT used: it carries the capability generation,
 * and a 403 refence is exactly what changes that. Keying on it would throw the attempt
 * away in the very case we are trying to survive.
 *
 * `sessionEpoch` IS included, and the two are not the same dimension — an earlier
 * version of this comment wrongly lumped them together. In `shellState.ts` the 403
 * branch returns without touching `sessionEpoch`; only the 401 branch, `SESSION_RESET`
 * and `SESSION_INVALIDATED` advance it. So including it draws precisely the line that
 * is wanted:
 *
 *   - a 403 inside the SAME session keeps the attempt (the capability was revoked, the
 *     session was not), and
 *   - a logout, an actor change or a 401 starts a new epoch, so the same human signing
 *     back in never inherits the previous session's snapshot or key.
 *
 * What the key guarantees on top of that is the boundary the coordinator drew: an
 * attempt never travels to another user or another company. A different actor, company
 * or session simply does not match, so it reads as absent.
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

/** `session + actor + company + location`. Structural, so no delimiter can collide. */
export function attemptStoreKey(
  sessionEpoch: number,
  actorId: string | null,
  companyId: string | null,
  locationId: string,
): string | null {
  if (actorId === null || companyId === null || locationId.length === 0) return null
  return JSON.stringify([sessionEpoch, actorId, companyId, locationId])
}

const store = new Map<string, StoredAttempt>()

/**
 * Reserve the key SYNCHRONOUSLY, before the request leaves.
 *
 * This must not be done from an effect. An effect runs after the render commit, while
 * `send()` is called in the same tick as the dispatch — so between the request going
 * out and the effect running there was a window in which a real Idempotency-Key was
 * already in flight with nothing recorded anywhere. Unmounting inside that window (the
 * card re-read failing, the boundary closing the mount) lost exactly the key this store
 * exists to keep. Reserving first closes the window: the record exists before anything
 * can be uncertain about it.
 *
 * Unconditional on purpose — a submit is a deliberate act by the current owner, and it
 * is the one moment a new attempt legitimately replaces whatever was there.
 */
export function reserveAttempt(key: string | null, attempt: StoredAttempt): void {
  if (key === null) return
  store.set(key, attempt)
}

/**
 * Mirror a later phase change of an attempt ALREADY reserved.
 *
 * Identity-guarded: a component instance that is unmounting, or a stale one still
 * holding an older attempt, must not write over a newer reservation made by whoever
 * owns the screen now. Only the attempt that is actually stored may update itself.
 */
export function updateAttempt(key: string | null, attempt: StoredAttempt): void {
  if (key === null) return
  const current = store.get(key)
  if (current !== undefined && current.snapshot.key !== attempt.snapshot.key) return
  store.set(key, attempt)
}

export function recallAttempt(key: string | null): StoredAttempt | null {
  if (key === null) return null
  return store.get(key) ?? null
}

/**
 * Release on a terminal answer — and ONLY for the attempt that actually settled.
 *
 * A settlement closes one attempt, not "whatever is stored". Deleting unconditionally
 * let an obsolete or idle owner erase a newer outstanding command; the reducer already
 * refuses a settle whose key does not match, and this is the same rule at the store.
 */
export function releaseAttempt(key: string | null, attemptKey: string): void {
  if (key === null) return
  const current = store.get(key)
  if (current === undefined || current.snapshot.key !== attemptKey) return
  store.delete(key)
}

/** Test-only: the store is module state, so each case must start empty. */
export function clearAttemptStore(): void {
  store.clear()
}
