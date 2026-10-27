/**
 * Keeps an unresolved S-01 create attempt (a sales request or a sales quote) alive when its screen is torn down — the
 * order create's rule (./pendingDrafts.ts), the same pattern and the same limits.
 *
 * A 401/403 re-fence closes the route, the person leaves through the menu or back, a company switch is confirmed: the
 * screen goes and its key with it, and the next visit would mint a new key for a request or quote that may already
 * exist. So the attempt — and the form values it was sent from — is held here under its binding (actor, session,
 * company, scope; see `orderAttemptBinding`), written synchronously BEFORE the send awaits.
 *
 * Only the exact binding can read it back: another company, actor or session sees nothing. A late outcome for an
 * attempt that was already settled (completed and shown, or refused) cannot bring it back. Memory only — never storage,
 * URL or log; nothing expires but the page itself.
 */

import { isOutcomeStillUnknown, type OrderAttemptState } from './orderAttempt'

type AnyAttempt = OrderAttemptState<unknown, unknown>

interface Held {
  readonly attempt: Exclude<AnyAttempt, { phase: 'idle' }>
  readonly values: unknown
}

const slots = new Map<string, Held>()
const settled = new Set<string>()

/**
 * Record the attempt's state. Unresolved work, and a success no screen has shown yet (`seen === false`), is kept; a
 * definite refusal or a shown success settles it.
 */
export function rememberPoolAttempt<P, R, V>(binding: string, attempt: OrderAttemptState<P, R>, values: V, seen: boolean): void {
  if (attempt.phase === 'idle') return
  const token = `${binding}|${attempt.snapshot.key}`
  if (settled.has(token)) return
  if (isOutcomeStillUnknown(attempt) || (attempt.phase === 'completed' && !seen)) {
    slots.set(binding, { attempt: attempt as Held['attempt'], values })
    return
  }
  settled.add(token)
  if (slots.get(binding)?.attempt.snapshot.key === attempt.snapshot.key) slots.delete(binding)
}

/**
 * The attempt to resume under this binding, with the values it was sent from. A send still in flight when its screen
 * went away comes back UNKNOWN: the new screen can never receive that answer.
 */
export function recallPoolAttempt<P, R, V>(binding: string | null): { readonly attempt: OrderAttemptState<P, R>; readonly values: V } | null {
  if (binding === null) return null
  const held = slots.get(binding)
  if (held === undefined) return null
  const attempt: AnyAttempt =
    held.attempt.phase === 'submitting' ? { phase: 'unknown', snapshot: held.attempt.snapshot, reason: 'network-error' } : held.attempt
  return { attempt: attempt as OrderAttemptState<P, R>, values: held.values as V }
}

/** Tests call this between cases; slots outlive a render by design. */
export function resetPendingPoolAttempts(): void {
  slots.clear()
  settled.clear()
}
