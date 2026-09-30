/**
 * Keeps an unresolved quantity-revision attempt alive while the detail route is torn down (the commercial revision's
 * slot, one per binding). The binding already contains actor, session, company and draft scope; nothing is persisted to
 * storage, a URL or a log.
 */
import type { DraftMutation, DraftQuantityRevisionRequest } from './contract'
import type { QuantityRevisionRow } from './quantityRevision'
import { isOutcomeStillUnknown, type OrderAttemptState } from './orderAttempt'

export type QuantityRevisionAttempt = OrderAttemptState<DraftQuantityRevisionRequest, DraftMutation>

interface HeldQuantityRevision {
  readonly attempt: Exclude<QuantityRevisionAttempt, { phase: 'idle' }>
  readonly rows: readonly QuantityRevisionRow[]
  readonly reason: string
}

const slots = new Map<string, HeldQuantityRevision>()
const settled = new Set<string>()

export function rememberQuantityRevisionAttempt(
  binding: string,
  attempt: QuantityRevisionAttempt,
  rows: readonly QuantityRevisionRow[],
  reason: string,
): void {
  if (attempt.phase === 'idle') return
  const token = `${binding}|${attempt.snapshot.key}`
  if (settled.has(token)) return

  if (isOutcomeStillUnknown(attempt)) {
    const current = slots.get(binding)
    if (current !== undefined && current.attempt.snapshot.key !== attempt.snapshot.key) {
      settled.add(`${binding}|${current.attempt.snapshot.key}`)
    }
    slots.set(binding, { attempt, rows: rows.map((row) => ({ ...row })), reason })
    return
  }

  settled.add(token)
  if (slots.get(binding)?.attempt.snapshot.key === attempt.snapshot.key) slots.delete(binding)
}

export function recallQuantityRevisionAttempt(
  binding: string | null,
): { readonly attempt: QuantityRevisionAttempt; readonly rows: readonly QuantityRevisionRow[]; readonly reason: string } | null {
  if (binding === null) return null
  const held = slots.get(binding)
  if (held === undefined) return null
  if (held.attempt.phase === 'submitting') {
    return {
      attempt: { phase: 'unknown', snapshot: held.attempt.snapshot, reason: 'network-error' },
      rows: held.rows,
      reason: held.reason,
    }
  }
  return held
}

/** Tests call this between cases; slots intentionally outlive a component render. */
export function resetPendingQuantityRevisions(): void {
  slots.clear()
  settled.clear()
}
