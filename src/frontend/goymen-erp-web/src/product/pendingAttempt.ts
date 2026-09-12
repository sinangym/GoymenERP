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
 * WHAT THE ATTEMPT IS BOUND TO — and, just as deliberately, what it is NOT.
 *
 * The first version keyed the slot by the shell's cache namespace, which carries
 * the capability generation and the committed revision. Both are TEMPORARY: a
 * real 403 nulls the generation, and the fresh authorized snapshot the re-entry
 * gate then fetches carries a new one. The same person, in the same session,
 * against the same server, came back authorized — and could not find their own
 * attempt, so the form minted a second key. The namespace fences DATA ACCESS; it
 * is not the identity of a pending write.
 *
 * The binding is therefore the DURABLE identity of the attempt:
 *
 *  - the API origin — §19.4: an attempt is not carried to another server;
 *  - the actor — never another person's write;
 *  - the shell's real `sessionEpoch` — a logout, an actor change or a server-side
 *    session invalidation advances it, so a new session cannot reach the old one;
 *  - the committed company context — the shell's own context boundary, and the
 *    one the switch dialog speaks for ("bekleyen işlem bırakılır");
 *  - the screen, plus the record for an edit.
 *
 * Fresh ACCESS is a separate fence and stays where it is: the form only mounts
 * behind `CapabilityRoute`, which re-verifies the capability on every entry, and a
 * retry goes through the same auth boundary as the first send. Restoring shows the
 * unresolved state and waits for a person; nothing is ever retried on its own — an
 * automatic replay after an authorization failure is precisely the thing the
 * boundary just refused.
 *
 * WHO MAY WRITE. Every write and every delete names the exact binding AND the
 * attempt key it is about. A screen mounted under another binding can neither see
 * this attempt nor delete it; a late outcome for an attempt the user has already
 * resolved or abandoned is ignored rather than resurrecting it.
 *
 * WHEN IT IS WRITTEN. Synchronously, before the send awaits — never only from an
 * effect. An effect runs after the commit, and a 403 answering a retry closes the
 * route before that commit happens: the outcome that mattered most was the one
 * that never got written. An in-flight send is unresolved work and is kept too.
 *
 * Memory only. A pending write is not written to storage, a URL or a log: it
 * carries a payload that can name a real product, and a page reload is a
 * legitimate end to a session's in-flight work.
 */

import type { AttemptState } from './commandAttempt'
import { isOutcomeStillUnknown } from './commandAttempt'
import { API_BASE } from './api'
import type { ShellState } from '../shell/shellState'

/** The durable identity of a pending attempt. See the module comment. */
export interface AttemptBinding {
  readonly origin: string
  readonly actorId: string
  readonly sessionEpoch: number
  readonly scopeMode: 'single' | 'group'
  readonly companyId: string | null
  /** `product.create`, or `product.edit:<productId>`. */
  readonly scope: string
  /** Canonical, collision-free string of the fields above (JSON of a string array). */
  readonly key: string
}

export function createScope(): string {
  return 'product.create'
}

export function editScope(productId: string): string {
  return `product.edit:${productId}`
}

/**
 * The binding for a screen under the shell's current context, or null while no
 * context is committed. The capability generation and the committed revision are
 * read NOWHERE here — that is the point.
 */
export function attemptBindingFor(shell: ShellState, scope: string): AttemptBinding | null {
  const context = shell.activeContext
  if (context === null) return null
  const fields = {
    origin: API_BASE,
    actorId: context.userId,
    sessionEpoch: shell.sessionEpoch,
    scopeMode: context.scopeMode,
    companyId: context.companyId,
    scope,
  }
  return {
    ...fields,
    key: JSON.stringify([
      fields.origin,
      fields.actorId,
      String(fields.sessionEpoch),
      fields.scopeMode,
      fields.companyId ?? 'group',
      fields.scope,
    ]),
  }
}

type Held = Exclude<AttemptState<unknown, unknown>, { phase: 'idle' }>

/** One slot per binding. */
const slots = new Map<string, Held>()
/**
 * Attempt keys (per binding) that reached a final state or were abandoned. A late
 * write for one of these — the continuation of a send whose screen is gone — must
 * not bring the attempt back.
 */
const settled = new Set<string>()

function settledKey(binding: AttemptBinding, attemptKey: string): string {
  return `${binding.key}|${attemptKey}`
}

/**
 * Record the attempt's current state under its binding.
 *
 * Unresolved (in flight, unknown, or refused with an unresolved history) is kept.
 * Resolved (completed, or a definite refusal) settles the key and clears the slot
 * — but only if the slot still holds THIS attempt; a newer attempt under the same
 * binding is someone else's business.
 */
export function rememberAttempt<P, R>(
  binding: AttemptBinding | null,
  state: AttemptState<P, R>,
): void {
  if (binding === null || state.phase === 'idle') return
  const attemptKey = state.snapshot.key
  if (settled.has(settledKey(binding, attemptKey))) return

  if (isOutcomeStillUnknown(state)) {
    slots.set(binding.key, state as Held)
    return
  }
  settled.add(settledKey(binding, attemptKey))
  if (slots.get(binding.key)?.snapshot.key === attemptKey) slots.delete(binding.key)
}

/**
 * The attempt to resume, or null.
 *
 * A send that was still in flight when its screen went away comes back as UNKNOWN:
 * the new screen has no way to receive that answer, so to it the outcome is
 * unknown — and, like every unknown, retryable only with the same key.
 *
 * Reading never deletes. The attempt belongs to the binding that started it.
 */
export function recallAttempt<P, R>(binding: AttemptBinding | null): AttemptState<P, R> | null {
  if (binding === null) return null
  const held = slots.get(binding.key)
  if (held === undefined) return null
  if (held.phase === 'submitting') {
    return { phase: 'unknown', snapshot: held.snapshot, reason: 'interrupted' } as AttemptState<P, R>
  }
  return held as AttemptState<P, R>
}

/**
 * Explicitly drop a pending attempt — the user was asked and chose to abandon it.
 * The key is settled so a late outcome cannot resurrect a decision just made.
 */
export function forgetAttempt(binding: AttemptBinding | null): void {
  if (binding === null) return
  const held = slots.get(binding.key)
  if (held === undefined) return
  settled.add(settledKey(binding, held.snapshot.key))
  slots.delete(binding.key)
}

/** Tests call this between cases; the slots outlive a single render by design. */
export function resetPendingAttempts(): void {
  slots.clear()
  settled.clear()
}
