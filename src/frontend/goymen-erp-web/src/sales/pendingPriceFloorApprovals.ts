import type { DraftMutation, DraftPriceFloorExceptionApprovalRequest } from './contract'
import { isOutcomeStillUnknown, type OrderAttemptState } from './orderAttempt'

export type PriceFloorApprovalAttempt = OrderAttemptState<DraftPriceFloorExceptionApprovalRequest, DraftMutation>

const slots = new Map<string, Exclude<PriceFloorApprovalAttempt, { phase: 'idle' }>>()
const settled = new Set<string>()

export function rememberPriceFloorApproval(binding: string, attempt: PriceFloorApprovalAttempt): void {
  if (attempt.phase === 'idle') return
  const token = `${binding}|${attempt.snapshot.key}`
  if (settled.has(token)) return
  if (isOutcomeStillUnknown(attempt)) {
    const current = slots.get(binding)
    if (current !== undefined && current.snapshot.key !== attempt.snapshot.key) settled.add(`${binding}|${current.snapshot.key}`)
    slots.set(binding, attempt)
    return
  }
  settled.add(token)
  if (slots.get(binding)?.snapshot.key === attempt.snapshot.key) slots.delete(binding)
}

export function recallPriceFloorApproval(binding: string | null): PriceFloorApprovalAttempt | null {
  if (binding === null) return null
  const held = slots.get(binding)
  if (held === undefined) return null
  return held.phase === 'submitting'
    ? { phase: 'unknown', snapshot: held.snapshot, reason: 'network-error' }
    : held
}

export function resetPendingPriceFloorApprovals(): void {
  slots.clear()
  settled.clear()
}
