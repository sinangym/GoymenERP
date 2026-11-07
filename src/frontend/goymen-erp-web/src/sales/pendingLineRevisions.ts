/**
 * SAT-D167 (EK24): keeps an unresolved line-revision attempt alive while the detail route is torn down (the quantity
 * revision's slot, one per binding). The binding already contains actor, session, company and draft scope; nothing is
 * persisted to storage, a URL or a log.
 */
import type { DraftLineRevisionRequest, DraftMutation } from './contract'
import type { LineRevisionValues } from './lineRevision'
import { isOutcomeStillUnknown, type OrderAttemptState } from './orderAttempt'

export type LineRevisionAttempt = OrderAttemptState<DraftLineRevisionRequest, DraftMutation>

interface HeldLineRevision {
  readonly attempt: Exclude<LineRevisionAttempt, { phase: 'idle' }>
  readonly values: LineRevisionValues
}

const slots = new Map<string, HeldLineRevision>()
const settled = new Set<string>()

export function rememberLineRevisionAttempt(binding: string, attempt: LineRevisionAttempt, values: LineRevisionValues): void {
  if (attempt.phase === 'idle') return
  const token = `${binding}|${attempt.snapshot.key}`
  if (settled.has(token)) return

  if (isOutcomeStillUnknown(attempt)) {
    const current = slots.get(binding)
    if (current !== undefined && current.attempt.snapshot.key !== attempt.snapshot.key) {
      settled.add(`${binding}|${current.attempt.snapshot.key}`)
    }
    slots.set(binding, { attempt, values })
    return
  }

  settled.add(token)
  if (slots.get(binding)?.attempt.snapshot.key === attempt.snapshot.key) slots.delete(binding)
}

export function recallLineRevisionAttempt(
  binding: string | null,
): { readonly attempt: LineRevisionAttempt; readonly values: LineRevisionValues } | null {
  if (binding === null) return null
  const held = slots.get(binding)
  if (held === undefined) return null
  if (held.attempt.phase === 'submitting') {
    return { attempt: { phase: 'unknown', snapshot: held.attempt.snapshot, reason: 'network-error' }, values: held.values }
  }
  return held
}

/** Tests call this between cases; slots intentionally outlive a component render. */
export function resetPendingLineRevisions(): void {
  slots.clear()
  settled.clear()
}
