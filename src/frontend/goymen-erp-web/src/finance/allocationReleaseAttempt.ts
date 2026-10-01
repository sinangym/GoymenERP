import type { FinanceAllocationWireSnapshot } from './allocationHttp'
import type { AttemptBinding } from './receiptCommand'

export interface AllocationReleaseAttempt {
  readonly phase: 'submitting' | 'unknown'
  readonly snapshot: FinanceAllocationWireSnapshot
}

const attempts = new Map<string, AllocationReleaseAttempt>()

export function allocationReleaseAttemptSlot(binding: AttemptBinding, allocationId: string): string {
  return JSON.stringify([
    binding.sessionEpoch,
    binding.actorId,
    binding.companyId,
    allocationId,
  ])
}

export function recallAllocationReleaseAttempt(slot: string): AllocationReleaseAttempt | null {
  return attempts.get(slot) ?? null
}

export function recoverAllocationReleaseAttempt(slot: string): AllocationReleaseAttempt | null {
  const current = recallAllocationReleaseAttempt(slot)
  if (current === null || current.phase === 'unknown') return current
  const recovered: AllocationReleaseAttempt = { phase: 'unknown', snapshot: current.snapshot }
  attempts.set(slot, recovered)
  return recovered
}

export function reserveAllocationReleaseAttempt(
  slot: string,
  snapshot: FinanceAllocationWireSnapshot,
): AllocationReleaseAttempt | null {
  if (attempts.has(slot)) return null
  const attempt: AllocationReleaseAttempt = { phase: 'submitting', snapshot }
  attempts.set(slot, attempt)
  return attempt
}

export function resendAllocationReleaseAttempt(slot: string, key: string): AllocationReleaseAttempt | null {
  const current = attempts.get(slot)
  if (current === undefined || current.phase !== 'unknown' || current.snapshot.key !== key) return null
  const attempt: AllocationReleaseAttempt = { phase: 'submitting', snapshot: current.snapshot }
  attempts.set(slot, attempt)
  return attempt
}

export function markAllocationReleaseAttemptUnknown(slot: string, key: string): AllocationReleaseAttempt | null {
  const current = attempts.get(slot)
  if (current === undefined || current.snapshot.key !== key) return null
  const attempt: AllocationReleaseAttempt = { phase: 'unknown', snapshot: current.snapshot }
  attempts.set(slot, attempt)
  return attempt
}

export function finishAllocationReleaseAttempt(slot: string, key: string): void {
  if (attempts.get(slot)?.snapshot.key === key) attempts.delete(slot)
}

/** Test-only. */
export function clearAllocationReleaseAttemptStore(): void {
  attempts.clear()
}
