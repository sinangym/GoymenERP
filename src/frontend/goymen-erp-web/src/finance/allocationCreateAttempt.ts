import type { FinanceAllocationWireSnapshot } from './allocationHttp'
import type { AttemptBinding } from './receiptCommand'

export interface AllocationCreateAttempt {
  readonly phase: 'submitting' | 'unknown'
  readonly snapshot: FinanceAllocationWireSnapshot
}

const attempts = new Map<string, AllocationCreateAttempt>()

export function allocationCreateAttemptSlot(binding: AttemptBinding): string {
  return JSON.stringify([
    binding.sessionEpoch,
    binding.actorId,
    binding.companyId,
    'finance-allocation-create',
  ])
}

export function recallAllocationCreateAttempt(slot: string): AllocationCreateAttempt | null {
  return attempts.get(slot) ?? null
}

export function recoverAllocationCreateAttempt(slot: string): AllocationCreateAttempt | null {
  const current = recallAllocationCreateAttempt(slot)
  if (current === null || current.phase === 'unknown') return current
  const recovered: AllocationCreateAttempt = { phase: 'unknown', snapshot: current.snapshot }
  attempts.set(slot, recovered)
  return recovered
}

export function reserveAllocationCreateAttempt(
  slot: string,
  snapshot: FinanceAllocationWireSnapshot,
): AllocationCreateAttempt | null {
  if (snapshot.request.operation !== 'allocation-create' || attempts.has(slot)) return null
  const attempt: AllocationCreateAttempt = { phase: 'submitting', snapshot }
  attempts.set(slot, attempt)
  return attempt
}

export function resendAllocationCreateAttempt(slot: string, key: string): AllocationCreateAttempt | null {
  const current = attempts.get(slot)
  if (current === undefined || current.phase !== 'unknown' || current.snapshot.key !== key) return null
  const attempt: AllocationCreateAttempt = { phase: 'submitting', snapshot: current.snapshot }
  attempts.set(slot, attempt)
  return attempt
}

export function markAllocationCreateAttemptUnknown(slot: string, key: string): AllocationCreateAttempt | null {
  const current = attempts.get(slot)
  if (current === undefined || current.snapshot.key !== key) return null
  const attempt: AllocationCreateAttempt = { phase: 'unknown', snapshot: current.snapshot }
  attempts.set(slot, attempt)
  return attempt
}

export function finishAllocationCreateAttempt(slot: string, key: string): void {
  if (attempts.get(slot)?.snapshot.key === key) attempts.delete(slot)
}

/** Test-only. Production intentionally has no abandon/reset operation for an unresolved money attempt. */
export function clearAllocationCreateAttemptStore(): void {
  attempts.clear()
}
