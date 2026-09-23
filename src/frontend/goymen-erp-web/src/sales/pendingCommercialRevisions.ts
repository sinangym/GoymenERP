/**
 * Keeps an unresolved commercial-revision attempt alive while the detail route is
 * torn down. The binding already contains actor, session, company and draft scope;
 * nothing is persisted to storage, a URL or a log.
 */
import type { DraftCommercialRevisionRequest, DraftMutation } from './contract'
import type { CommercialRevisionRow } from './commercialRevision'
import { isOutcomeStillUnknown, type OrderAttemptState } from './orderAttempt'

export type CommercialRevisionAttempt = OrderAttemptState<DraftCommercialRevisionRequest, DraftMutation>

interface HeldCommercialRevision {
  readonly attempt: Exclude<CommercialRevisionAttempt, { phase: 'idle' }>
  readonly rows: readonly CommercialRevisionRow[]
}

const slots = new Map<string, HeldCommercialRevision>()
const settled = new Set<string>()

export function rememberCommercialRevisionAttempt(
  binding: string,
  attempt: CommercialRevisionAttempt,
  rows: readonly CommercialRevisionRow[],
): void {
  if (attempt.phase === 'idle') return
  const token = `${binding}|${attempt.snapshot.key}`
  if (settled.has(token)) return

  if (isOutcomeStillUnknown(attempt)) {
    slots.set(binding, { attempt, rows: rows.map((row) => ({ ...row })) })
    return
  }

  settled.add(token)
  if (slots.get(binding)?.attempt.snapshot.key === attempt.snapshot.key) slots.delete(binding)
}

export function recallCommercialRevisionAttempt(
  binding: string | null,
): { readonly attempt: CommercialRevisionAttempt; readonly rows: readonly CommercialRevisionRow[] } | null {
  if (binding === null) return null
  const held = slots.get(binding)
  if (held === undefined) return null
  if (held.attempt.phase === 'submitting') {
    return {
      attempt: { phase: 'unknown', snapshot: held.attempt.snapshot, reason: 'network-error' },
      rows: held.rows,
    }
  }
  return held
}

/** Tests call this between cases; slots intentionally outlive a component render. */
export function resetPendingCommercialRevisions(): void {
  slots.clear()
  settled.clear()
}
