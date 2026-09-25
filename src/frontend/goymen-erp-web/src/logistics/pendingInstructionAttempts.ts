/**
 * Keeps an unresolved Logistics command alive while its route is torn down.
 * Slots are actor/session/company/record scoped by `attemptBinding`; raw keys and
 * payloads remain in memory and never reach storage, a URL or a log.
 */
import type { InstructionMutation } from './contract'
import { isOutcomeStillUnknown, type AttemptState } from './instructionAttempt'

interface HeldInstructionAttempt {
  readonly attempt: Exclude<AttemptState<unknown, InstructionMutation>, { phase: 'idle' }>
  readonly values: unknown
}

const slots = new Map<string, HeldInstructionAttempt>()
const settled = new Set<string>()

export function rememberInstructionAttempt<P, V>(
  slot: string,
  attempt: AttemptState<P, InstructionMutation>,
  values: V,
): void {
  if (attempt.phase === 'idle') return
  const token = `${slot}|${attempt.snapshot.key}`
  if (settled.has(token)) return

  if (isOutcomeStillUnknown(attempt)) {
    const current = slots.get(slot)
    if (current !== undefined && current.attempt.snapshot.key !== attempt.snapshot.key) {
      settled.add(`${slot}|${current.attempt.snapshot.key}`)
    }
    slots.set(slot, {
      attempt: attempt as Exclude<AttemptState<unknown, InstructionMutation>, { phase: 'idle' }>,
      values,
    })
    return
  }

  settled.add(token)
  if (slots.get(slot)?.attempt.snapshot.key === attempt.snapshot.key) slots.delete(slot)
}

export function recallInstructionAttempt<P, V>(
  slot: string | null,
): { readonly attempt: AttemptState<P, InstructionMutation>; readonly values: V } | null {
  if (slot === null) return null
  const held = slots.get(slot)
  if (held === undefined) return null
  const attempt = held.attempt.phase === 'submitting'
    ? { phase: 'unknown' as const, snapshot: held.attempt.snapshot, reason: 'network-error' as const }
    : held.attempt
  return {
    attempt: attempt as AttemptState<P, InstructionMutation>,
    values: held.values as V,
  }
}

/** Tests call this between cases; slots intentionally outlive a component render. */
export function resetPendingInstructionAttempts(): void {
  slots.clear()
  settled.clear()
}
