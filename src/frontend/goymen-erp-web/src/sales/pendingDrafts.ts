/**
 * Keeps an unresolved "save draft" attempt alive when its screen is torn down.
 *
 * The screen does not survive everything the shell does: a 401/403 re-fences the
 * capability and closes the route, a failed directory read removes the outlet. The
 * attempt's key went with it, and the next visit would mint a new key for an order
 * that may already exist. So the attempt — and the form it was sent from — is held
 * here under its binding (actor, session, company, scope; see `orderAttemptBinding`),
 * written synchronously BEFORE the send awaits.
 *
 * Only the exact binding can read it back: another company, actor or session sees
 * nothing. A late outcome for an attempt that was already settled (completed and
 * shown, or refused) cannot bring it back. Memory only — never storage, URL or log.
 */

import type { OrderAttemptState } from './orderAttempt'
import { isOutcomeStillUnknown } from './orderAttempt'
import type { DraftCreateRequest, DraftMutation } from './contract'
import type { OrderDraftFormValues } from './orderDraftForm'

export type DraftAttempt = OrderAttemptState<DraftCreateRequest, DraftMutation>

interface Held {
  readonly attempt: Exclude<DraftAttempt, { phase: 'idle' }>
  readonly values: OrderDraftFormValues
}

const slots = new Map<string, Held>()
const settled = new Set<string>()

/**
 * Record the attempt's state. Unresolved work, and a success no screen has shown yet
 * (`seen === false`), is kept; a definite refusal or a shown success settles it.
 */
export function rememberDraftAttempt(
  binding: string,
  attempt: DraftAttempt,
  values: OrderDraftFormValues,
  seen: boolean,
): void {
  if (attempt.phase === 'idle') return
  const token = `${binding}|${attempt.snapshot.key}`
  if (settled.has(token)) return
  if (isOutcomeStillUnknown(attempt) || (attempt.phase === 'completed' && !seen)) {
    slots.set(binding, { attempt, values })
    return
  }
  settled.add(token)
  if (slots.get(binding)?.attempt.snapshot.key === attempt.snapshot.key) slots.delete(binding)
}

/**
 * The attempt to resume under this binding. A send still in flight when its screen
 * went away comes back UNKNOWN: the new screen can never receive that answer.
 */
export function recallDraftAttempt(binding: string | null): { attempt: DraftAttempt; values: OrderDraftFormValues } | null {
  if (binding === null) return null
  const held = slots.get(binding)
  if (held === undefined) return null
  if (held.attempt.phase === 'submitting') {
    return { attempt: { phase: 'unknown', snapshot: held.attempt.snapshot, reason: 'network-error' }, values: held.values }
  }
  return { attempt: held.attempt, values: held.values }
}

/** Tests call this between cases; slots outlive a render by design. */
export function resetPendingDrafts(): void {
  slots.clear()
  settled.clear()
}
