/**
 * Keeps an unresolved delivery-schedule attempt alive while the detail route is torn down (the commercial revision's
 * pattern). The binding already contains actor, session, company and draft scope; nothing is persisted to storage, a
 * URL or a log.
 */
import type { DraftDeliveryScheduleRequest, DraftMutation } from './contract'
import type { DeliveryScheduleRows } from './deliverySchedule'
import { isOutcomeStillUnknown, type OrderAttemptState } from './orderAttempt'

export type DeliveryScheduleAttempt = OrderAttemptState<DraftDeliveryScheduleRequest, DraftMutation>

interface HeldDeliverySchedule {
  readonly attempt: Exclude<DeliveryScheduleAttempt, { phase: 'idle' }>
  readonly rows: DeliveryScheduleRows
}

const slots = new Map<string, HeldDeliverySchedule>()
const settled = new Set<string>()

export function rememberDeliveryScheduleAttempt(binding: string, attempt: DeliveryScheduleAttempt, rows: DeliveryScheduleRows): void {
  if (attempt.phase === 'idle') return
  const token = `${binding}|${attempt.snapshot.key}`
  if (settled.has(token)) return

  if (isOutcomeStillUnknown(attempt)) {
    const current = slots.get(binding)
    if (current !== undefined && current.attempt.snapshot.key !== attempt.snapshot.key) {
      settled.add(`${binding}|${current.attempt.snapshot.key}`)
    }
    slots.set(binding, { attempt, rows })
    return
  }

  settled.add(token)
  if (slots.get(binding)?.attempt.snapshot.key === attempt.snapshot.key) slots.delete(binding)
}

export function recallDeliveryScheduleAttempt(
  binding: string | null,
): { readonly attempt: DeliveryScheduleAttempt; readonly rows: DeliveryScheduleRows } | null {
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
export function resetPendingDeliverySchedules(): void {
  slots.clear()
  settled.clear()
}
