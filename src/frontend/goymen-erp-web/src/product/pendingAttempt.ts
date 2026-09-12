/**
 * Keeps an UNRESOLVED write attempt alive across a component unmount.
 *
 * WHY THIS EXISTS. An attempt whose outcome is unknown owns an idempotency key,
 * and that key is the only thing standing between one committed record and two.
 * The screen, however, does not survive everything the shell can do to it: a 401
 * or 403 re-fences the capability, the gated route closes, the form unmounts — and
 * the key went with it. The next authorized visit then minted a fresh key for a
 * payload the server may already have applied.
 *
 * The user's decision to preserve unknown/same-key retries is already made; keeping
 * that key across a remount is the mechanism of that decision, not a new one.
 *
 * WHAT IS DELIBERATELY NARROW:
 *
 *  - keyed by the shell's CONTEXT NAMESPACE (actor + scope mode + company +
 *    capability generation + committed revision) AND by a scope string. A read
 *    under any other namespace returns nothing, so another actor, another company
 *    or a new session cannot see — let alone resume — someone else's pending write.
 *    That is the same boundary the list view state uses, for the same reason;
 *  - nothing is ever retried automatically. Restoring shows the unresolved state
 *    and waits for a person. An automatic replay after an authorization failure is
 *    precisely the thing the auth boundary just refused;
 *  - memory only. A pending write is not written to storage, a URL or a log: it
 *    carries a payload that can name a real product, and a page reload is a
 *    legitimate end to a session's in-flight work.
 *
 * Only the UNRESOLVED phases are held. A completed or definitively failed attempt
 * has nothing left to protect, and holding it would resurrect a closed decision.
 */

import type { AttemptState } from './commandAttempt'
import { isOutcomeStillUnknown } from './commandAttempt'

/** One slot per scope. A scope is a screen plus, for an edit, the record it edits. */
const slots = new Map<string, { readonly namespace: string; readonly state: unknown }>()

/** `product.create`, or `product.edit:<productId>`. */
export function createScope(): string {
  return 'product.create'
}

export function editScope(productId: string): string {
  return `product.edit:${productId}`
}

/**
 * Remember an attempt, or forget it.
 *
 * Called on every state change rather than only on unmount: an unmount caused by a
 * route being closed underneath the screen does not always run a cleanup path we
 * control, and a key that is only saved on the way out is a key that is lost
 * exactly when it matters.
 */
export function rememberAttempt<P, R>(
  namespace: string | null,
  scope: string,
  state: AttemptState<P, R>,
): void {
  if (namespace === null) return
  if (!isOutcomeStillUnknown(state) || state.phase === 'submitting') {
    slots.delete(scope)
    return
  }
  slots.set(scope, { namespace, state })
}

/**
 * The attempt to resume, or null.
 *
 * Returns nothing for a different namespace — and does NOT delete the slot in that
 * case. The pending write belongs to the context that started it; a colleague
 * opening the same screen must neither see it nor destroy it.
 */
export function recallAttempt<P, R>(
  namespace: string | null,
  scope: string,
): AttemptState<P, R> | null {
  if (namespace === null) return null
  const slot = slots.get(scope)
  if (slot === undefined || slot.namespace !== namespace) return null
  return slot.state as AttemptState<P, R>
}

/** Explicitly drop a pending attempt — the user resolved or abandoned it. */
export function forgetAttempt(scope: string): void {
  slots.delete(scope)
}

/** Tests call this between cases; the slots outlive a single render by design. */
export function resetPendingAttempts(): void {
  slots.clear()
}
